use super::*;
use std::collections::HashMap;
use std::path::PathBuf;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex};
use tokio::sync::{broadcast, RwLock};

pub struct Manager {
    pub tmux: Tmux,
    pub cfg_path: PathBuf,
    pub(super) endpoint_path: PathBuf,
    pub(super) cfg: RwLock<Config>,
    pub(super) templates: RwLock<AgentTemplateStore>,
    pub(super) live: RwLock<HashMap<String, Live>>,
    pub(super) temp: RwLock<HashMap<String, ProjectCfg>>,
    pub(super) rules: RwLock<Vec<(State, Regex)>>,
    pub(super) rules_revision: AtomicU64,
    pub(super) config_state: super::manager::ConfigState,
    /// Refresh host pane directories and processes together, less often than the one-second state classification interval.
    /// The timestamp also limits refresh frequency for any additional maintenance callers.
    pub(super) host_metadata_checked: AtomicU64,
    /// A slow tmux listing must not block classification or overlap the next listing.
    pub(super) host_metadata_poll: tokio::sync::Mutex<Option<JoinHandle<()>>>,
    pub(super) signals: super::manager::Signals,
    pub(super) scroll_cache: Mutex<HashMap<String, CachedScroll>>,
    pub(super) activity_cache: crate::activity::ActivityCache,
    pub audio: crate::audio::Audio,
    pub(crate) music_transition: tokio::sync::Mutex<()>,
    pub(crate) ncspot: tokio::sync::Mutex<super::manager::ncspot::Player>,
    pub events: broadcast::Sender<Arc<EventMessage>>,
    pub(super) auth_generation: AtomicU64,
    pub(super) auth_changes: broadcast::Sender<AuthChange>,
    pub(super) grants: RwLock<crate::grant::Grants>,
    pub(super) session_boundary: tokio::sync::RwLock<()>,
    pub(super) resize_mutation: tokio::sync::Mutex<()>,
    /// Serialize template transactions through reading, comparison, writing, and publication.
    /// Checking versions outside this lock could let two editors pass and overwrite one draft.
    pub(super) template_mutation: tokio::sync::Mutex<()>,
    pub(crate) tasks: super::manager::TaskStore,
    /// Serialize worker creation by the daemon.
    /// This prevents root requests from reserving the same child name or interleaving task and session writes.
    pub(super) worker_spawn: tokio::sync::Mutex<()>,
    pub(super) worktree_mutation: tokio::sync::Mutex<()>,
    pub(super) worktree_views: tokio::sync::Mutex<super::manager::WorktreeViewCache>,
    pub(super) title_cache: crate::title::SummaryCache,
}

pub(crate) struct CachedScroll {
    pub(super) live_seq: u64,
    pub(super) off: u32,
    pub(super) cols: u16,
    pub(super) rows: u16,
    pub(super) view: ScreenView,
}

pub struct ClientGuard(pub(super) Arc<Manager>);

impl Drop for ClientGuard {
    fn drop(&mut self) {
        self.0.signals.clients.fetch_sub(1, Ordering::Relaxed);
    }
}

pub struct WatchGuard(pub(super) Arc<Manager>, pub(super) String);

impl Drop for WatchGuard {
    fn drop(&mut self) {
        let Ok(mut w) = self.0.signals.watchers.lock() else {
            return;
        };
        if let Some(n) = w.get_mut(&self.1) {
            *n -= 1;
            if *n == 0 {
                w.remove(&self.1);
            }
        }
        self.0.signals.watchers_changed.send_replace(());
    }
}

impl Manager {
    pub(crate) fn emit(&self, event: Event) {
        let _ = self.events.send(EventMessage::new(event));
    }

    pub(crate) fn auth_generation(&self) -> u64 {
        self.auth_generation.load(Ordering::Acquire)
    }

    pub(crate) fn auth_changes(&self) -> broadcast::Receiver<AuthChange> {
        self.auth_changes.subscribe()
    }

    pub(crate) fn invalidate_auth(&self, change: AuthChange) {
        self.auth_generation.fetch_add(1, Ordering::AcqRel);
        let _ = self.auth_changes.send(change);
    }
}

#[cfg(test)]
#[path = "ctrl_tests.rs"]
mod tests;
#[cfg(test)]
pub(crate) use tests::{test_manager, test_manager_with_socket};
