//! Construct session state and adapt reconciled identities for credential pruning.
//! Application startup owns recovery ordering.

use super::super::*;
use super::{HostMetadataPoll, Signals};

impl Manager {
    /// Borrow application owners and construct an empty session runtime.
    pub(crate) async fn new(
        workspace: Arc<crate::application::Workspace>,
        application: Arc<crate::application::Services>,
        tmux: Tmux,
        config_authority: Arc<crate::application::ConfigAuthority>,
    ) -> Result<Arc<Self>> {
        let activity_cache = crate::activity::ActivityCache::load(crate::activity::cache_path(
            workspace.settings_path(),
        ));

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
            workspace,
            live: RwLock::new(HashMap::new()),
            temp: RwLock::new(HashMap::new()),
            config_authority,
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
