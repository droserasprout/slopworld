//! Manager construction and startup recovery.

use super::super::*;
use super::{CatalogState, HostMetadataPoll, Signals, WorktreeState};
use crate::config::transactions::{Transactions, records::Records};

impl Manager {
    /// Load stores, recover worktrees, and reconcile configured sessions.
    pub(crate) async fn new(
        storage: crate::storage::layout::LoadedStorage,
        application: Arc<crate::application::Services>,
        tmux: Tmux,
        config_authority: Arc<crate::application::ConfigAuthority>,
    ) -> Result<Arc<Self>> {
        let crate::storage::layout::LoadedStorage {
            binding,
            stores,
            library_revision,
        } = storage;
        let cfg_path = binding.settings.clone();
        let library_snapshot = config_authority.read_config().await.library_items_all();

        // Catalog stamps seed the maintenance reload checks.
        let presets_mtime = crate::presets::Table::stamp();
        let presets_loaded = crate::presets::reload();
        let jukebox_mtime = crate::paths::dir_stamp(&crate::jukebox::Catalog::dir());

        // Load persisted stores before constructing shared state.
        let title_cache = crate::title::SummaryCache::load(crate::title::cache_path(&cfg_path));
        let activity_cache =
            crate::activity::ActivityCache::load(crate::activity::cache_path(&cfg_path));

        // Refresh after main's catalog read; record stamps only on success so failures retry.
        let jukebox_loaded = crate::jukebox::reload()
            .inspect_err(|error| {
                tracing::warn!("jukebox definitions are not reloadable: {error:#}")
            })
            .is_ok();

        let m = Arc::new(Self {
            #[cfg(test)]
            frame_commit_pause: Mutex::new(None),
            #[cfg(test)]
            scroll_capture_pause: Mutex::new(None),
            #[cfg(test)]
            reader_attach_pause: Mutex::new(None),
            #[cfg(test)]
            input_sink: Mutex::new(None),
            #[cfg(test)]
            _test_directory: None,
            tmux,
            cfg_path,
            endpoint_path: crate::endpoint::path(),
            live: RwLock::new(HashMap::new()),
            temp: RwLock::new(HashMap::new()),
            library_snapshot: Mutex::new(library_snapshot),
            config_authority,
            configuration: Transactions::new(Records::new(binding, stores), library_revision),
            catalog_state: CatalogState::new(
                presets_loaded.then_some(presets_mtime).flatten(),
                jukebox_loaded.then_some(jukebox_mtime).flatten(),
            ),
            host_metadata: HostMetadataPoll::default(),
            signals: Signals::new(),
            scroll_cache: Mutex::new(HashMap::new()),
            activity_mutation: tokio::sync::Mutex::new(()),
            activity_cache,
            application,
            session_boundary: Arc::new(tokio::sync::RwLock::new(())),
            terminal_boundaries: Mutex::new(HashMap::new()),
            terminal_resizes: Mutex::new(HashMap::new()),
            worker_spawn: tokio::sync::Mutex::new(()),
            worktrees: WorktreeState::default(),
            title_cache,
        });

        m.recover_runtime().await;

        // Prune credentials against the reconciled session identities.
        let (state_ids, host_sessions) = {
            let live = m.live.read().await;
            let mut state_ids = HashMap::new();
            let mut host_sessions = std::collections::HashSet::new();
            for (name, session) in live.iter() {
                if session.role.is_host() {
                    host_sessions.insert(name.clone());
                } else {
                    state_ids.insert(name.clone(), session.cfg.state_id.clone());
                }
            }
            (state_ids, host_sessions)
        };
        if let Err(error) = m
            .config_authority
            .prune_stale_grants(&state_ids, &host_sessions)
            .await
        {
            tracing::error!("could not prune stale persisted grants: {error:#}");
        }

        Ok(m)
    }
    async fn recover_runtime(self: &Arc<Self>) {
        // Recover filesystem state before reconciling sessions.
        match crate::sandbox::purge_trash() {
            Ok(n) if n > 0 => tracing::info!("purged {n} expired private-state trash entries"),
            Ok(_) => {}
            Err(error) => tracing::warn!("could not purge private-state trash: {error:#}"),
        }
        if let Err(error) = self.recover_worktrees().await {
            tracing::error!("Worktree recovery failed. Records remain: {error:#}");
        }

        if let Err(error) = self.tmux.ensure_server().await {
            tracing::error!("could not prepare tmux server: {error:#}");
        }
        self.sync_from_config().await;
    }
}

#[cfg(test)]
#[path = "init_tests.rs"]
mod tests;
