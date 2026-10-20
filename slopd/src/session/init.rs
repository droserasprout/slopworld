//! Construct session state and adapt reconciled identities for credential pruning.
//! Application startup owns recovery ordering.

use super::*;
use super::{HostMetadataPoll, Signals};

/// Construction-only handoff. SessionRuntime stores each capability independently.
/// Cloned handles share accepted state with application services.
pub(crate) struct RuntimeServices {
    pub(crate) tasks: crate::tasks::io::TaskStore,
    pub(crate) events: crate::application::EventPublisher,
    pub(crate) title_cache: Arc<crate::title::SummaryCache>,
    #[cfg(test)]
    pub(crate) application: Arc<crate::application::Services>,
}

/// Construction-only storage handoff; application keeps transaction and endpoint ownership.
pub(crate) struct RuntimeStorage {
    pub(crate) identities: crate::application::WorkspaceIdentities,
    pub(crate) worktrees: crate::config::transactions::records::WorktreeCatalog,
    pub(crate) activity_cache: crate::activity::ActivityCache,
    #[cfg(test)]
    pub(crate) workspace: Arc<crate::application::Workspace>,
}

impl SessionRuntime {
    /// Borrow application owners and construct an empty session runtime.
    pub(crate) async fn new(
        storage: RuntimeStorage,
        services: RuntimeServices,
        tmux: Tmux,
        config_authority: Arc<crate::application::ConfigAuthority>,
        host_records: crate::application::HostRecords,
    ) -> Result<Arc<Self>> {
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
            host_records,
            tmux,
            identities: storage.identities,
            worktrees: storage.worktrees,
            #[cfg(test)]
            workspace: storage.workspace,
            live: RwLock::new(HashMap::new()),
            temp: RwLock::new(HashMap::new()),
            config_authority,
            host_metadata: HostMetadataPoll::default(),
            signals: Signals::new(),
            scroll_cache: Mutex::new(HashMap::new()),
            activity_mutation: tokio::sync::Mutex::new(()),
            activity_cache: storage.activity_cache,
            tasks: services.tasks,
            events: services.events,
            title_cache: services.title_cache,
            #[cfg(test)]
            application: services.application,
            session_boundary: Arc::new(tokio::sync::RwLock::new(())),
            terminal_boundaries: Mutex::new(HashMap::new()),
            terminal_resizes: Mutex::new(HashMap::new()),
            worker_spawn: tokio::sync::Mutex::new(()),
        });

        Ok(m)
    }

    pub(crate) async fn prune_stale_grants(&self) {
        // Prune credentials against the reconciled session identities.
        let (state_ids, host_sessions) = {
            let live = self.live.read().await;
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
        if let Err(error) = self
            .config_authority
            .prune_stale_grants(&state_ids, &host_sessions)
            .await
        {
            tracing::error!("could not prune stale persisted grants: {error:#}");
        }
    }
}

#[cfg(test)]
#[path = "init_tests.rs"]
mod tests;
