//! Manager construction and startup recovery.

use super::super::*;
use super::{
    Authorization, ConfigState, HostMetadataPoll, MusicState, Signals, TemplateStore, WorktreeState,
};

impl Manager {
    /// Load stores, recover worktrees, and reconcile configured sessions.
    pub(crate) async fn new(loaded: crate::storage::layout::Loaded) -> Result<Arc<Self>> {
        let crate::storage::layout::Loaded {
            config: cfg,
            binding,
            stores,
            library_revision,
        } = loaded;
        let cfg_path = binding.settings.clone();
        let (events, _) = broadcast::channel(256);

        // Catalog stamps seed the maintenance reload checks.
        let presets_mtime = crate::presets::Table::stamp();
        let presets_loaded = crate::presets::reload();
        let jukebox_mtime = crate::paths::dir_stamp(&crate::jukebox::Catalog::dir());

        // Load persisted stores before constructing shared state.
        let grants = crate::grant::Grants::load_data(&cfg_path, &binding.data)
            .with_context(|| format!("loading grant store for {}", cfg_path.display()))?;
        let task_path = binding.data.clone();
        let tasks =
            tokio::task::spawn_blocking(move || crate::tasks::Tasks::load_records(&task_path))
                .await
                .context("task startup owner panicked")?
                .with_context(|| format!("loading task store for {}", cfg_path.display()))?;
        let title_cache = crate::title::SummaryCache::load(crate::title::cache_path(&cfg_path));
        let template_path = binding.config.join("agent_templates");
        let templates = crate::session::AgentTemplateStore::load(&template_path)
            .await
            .with_context(|| format!("loading agent template store {}", template_path.display()))?;
        let activity_cache =
            crate::activity::ActivityCache::load(crate::activity::cache_path(&cfg_path));

        // Refresh after main's catalog read; record stamps only on success so failures retry.
        let jukebox_loaded = crate::jukebox::reload();

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
            tmux: Tmux::new(crate::tmux::tmux_socket()),
            cfg_path,
            endpoint_path: crate::endpoint::path(),
            live: RwLock::new(HashMap::new()),
            temp: RwLock::new(HashMap::new()),
            library_snapshot: Mutex::new(cfg.library_items_all()),
            cfg: RwLock::new(cfg),
            templates: TemplateStore::new(templates),
            config_state: ConfigState::new(
                super::config::backend::records::Records::new(binding, stores),
                library_revision,
                presets_loaded.then_some(presets_mtime).flatten(),
                jukebox_loaded.then_some(jukebox_mtime).flatten(),
            ),
            host_metadata: HostMetadataPoll::default(),
            signals: Signals::new(),
            scroll_cache: Mutex::new(HashMap::new()),
            activity_mutation: tokio::sync::Mutex::new(()),
            activity_cache,
            music: MusicState::new(),
            events,
            auth: Authorization::new(grants),
            session_boundary: Arc::new(tokio::sync::RwLock::new(())),
            terminal_boundaries: Mutex::new(HashMap::new()),
            terminal_resizes: Mutex::new(HashMap::new()),
            tasks: crate::session::manager::TaskStore::new(tasks),
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
                if session.host {
                    host_sessions.insert(name.clone());
                } else {
                    state_ids.insert(name.clone(), session.cfg.state_id.clone());
                }
            }
            (state_ids, host_sessions)
        };
        if let Err(error) = m
            .auth
            .grants
            .write()
            .await
            .prune_stale(&state_ids, &host_sessions)
        {
            tracing::error!("could not prune stale persisted grants: {error:#}");
        }

        m.recover_music().await;

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
