//! Manager construction and startup recovery.

use super::super::*;
use super::session_state::compile_rules;
use super::{
    ActivityRules, Authorization, ConfigState, HostMetadataPoll, MusicState, Signals,
    TemplateStore, WorktreeState,
};
use crate::paths::disk_mtime;

impl Manager {
    /// Load stores, recover worktrees, and reconcile configured sessions.
    pub async fn new(cfg: Config, cfg_path: PathBuf) -> Arc<Self> {
        let (events, _) = broadcast::channel(256);

        // Catalog stamps seed the maintenance reload checks.
        let mtime = disk_mtime(&cfg_path).await;
        let library_mtime = Config::library_stamp_for(&cfg_path);
        let presets_mtime = crate::presets::Table::stamp();
        let presets_loaded = crate::presets::reload();
        let jukebox_mtime = crate::paths::dir_stamp(&crate::jukebox::Catalog::dir());

        // Load persisted stores before constructing shared state.
        let grants = crate::grant::Grants::load(&cfg_path)
            .unwrap_or_else(|error| panic!("grant store for {}: {error:#}", cfg_path.display()));
        let tasks = crate::tasks::Tasks::load(&cfg_path)
            .unwrap_or_else(|e| panic!("task store {}: {e:#}", cfg_path.display()));
        let title_cache = crate::title::SummaryCache::load(crate::title::cache_path(&cfg_path));
        let template_path = crate::session::AgentTemplateStore::path_for(&cfg_path);
        let templates = crate::session::AgentTemplateStore::load(&template_path)
            .await
            .unwrap_or_else(|e| panic!("agent template store {}: {e:#}", template_path.display()));
        let activity_cache =
            crate::activity::ActivityCache::load(crate::activity::cache_path(&cfg_path));

        // Refresh after main's catalog read; record stamps only on success so failures retry.
        let jukebox_loaded = crate::jukebox::reload();

        let m = Arc::new(Self {
            tmux: Tmux::new(crate::tmux::tmux_socket()),
            cfg_path,
            endpoint_path: crate::endpoint::path(),
            rules: ActivityRules::new(compile_rules(&cfg)),
            live: RwLock::new(HashMap::new()),
            temp: RwLock::new(HashMap::new()),
            cfg: RwLock::new(cfg),
            templates: TemplateStore::new(templates),
            config_state: ConfigState::new(
                mtime,
                library_mtime,
                presets_loaded.then_some(presets_mtime).flatten(),
                jukebox_loaded.then_some(jukebox_mtime).flatten(),
            ),
            host_metadata: HostMetadataPoll::default(),
            signals: Signals::new(),
            scroll_cache: Mutex::new(HashMap::new()),
            activity_cache,
            music: MusicState::new(),
            events,
            auth: Authorization::new(grants),
            session_boundary: tokio::sync::RwLock::new(()),
            resize_mutation: tokio::sync::Mutex::new(()),
            tasks: crate::session::manager::TaskStore::new(tasks),
            worker_spawn: tokio::sync::Mutex::new(()),
            worktrees: WorktreeState::default(),
            title_cache,
        });

        // Recover filesystem state before reconciling sessions.
        if let Ok(n) = crate::sandbox::purge_trash() {
            if n > 0 {
                tracing::info!("purged {n} expired private-state trash entries");
            }
        }
        if let Err(error) = m.recover_worktrees().await {
            tracing::error!("Worktree recovery failed. Records remain: {error:#}");
        }

        m.tmux.ensure_server().await;
        m.sync_from_config().await;

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

        if let Ok(root) = super::ncspot::runtime() {
            m.recover_ncspot(&root).await;
        }

        m
    }
}

#[cfg(test)]
#[path = "init_tests.rs"]
mod tests;
