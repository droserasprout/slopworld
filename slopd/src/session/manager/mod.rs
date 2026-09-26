//! Session manager state and focused implementation modules.
//! Start at init.rs for construction, boundary.rs for concurrency, and start.rs for launch.

mod adoption;
mod agent_templates;
mod boundary;
mod caps;
mod capture;
mod config;
mod config_cache;
mod config_state;
mod desktop;
mod errands;
mod init;
mod library;
mod maintenance;
mod music;
pub(crate) mod ncspot;
mod reconcile;
mod session_lifecycle;
mod session_state;
mod sessions;
mod signals;
mod start;
mod task_summary;
mod tasks;
mod workers;
mod worktrees;
pub(crate) use worktrees::WorktreeRequest;
pub(crate) use worktrees::WorktreeState;

pub(crate) use agent_templates::TemplateStore;
pub(crate) use caps::Authorization;
use capture::CachedScroll;
pub(crate) use config_state::ConfigState;
pub(crate) use music::MusicState;
pub(crate) use session_state::ActivityRules;
pub(crate) use sessions::HostMetadataPoll;
pub(crate) use signals::Signals;
pub use signals::WatchGuard;
pub(crate) use tasks::TaskStore;

use super::*;
use std::collections::HashMap;
use std::path::PathBuf;
use std::sync::{Arc, Mutex};
use tokio::sync::{broadcast, RwLock};

/// Shared daemon coordinator; each subsystem keeps its own state and locks.
pub struct Manager {
    // Configuration and saved definitions.
    pub cfg_path: PathBuf,
    pub(super) endpoint_path: PathBuf,
    // Accepted configuration; config.rs serializes persistence and publication.
    pub(super) cfg: RwLock<Config>,
    pub(super) config_state: ConfigState,
    pub(super) templates: TemplateStore,

    // Live sessions, terminal capture, and activity.
    pub tmux: Tmux,
    // Includes stopped sessions as well as attached processes.
    pub(super) live: RwLock<HashMap<String, Live>>,
    pub(super) temp: RwLock<HashMap<String, ProjectCfg>>,
    pub(super) rules: ActivityRules,
    pub(super) host_metadata: HostMetadataPoll,
    pub(super) scroll_cache: Mutex<HashMap<String, CachedScroll>>,
    pub(super) activity_cache: crate::activity::ActivityCache,
    pub(super) title_cache: crate::title::SummaryCache,

    // Client communication and authorization.
    pub events: broadcast::Sender<Arc<EventMessage>>,
    pub(super) signals: Signals,
    pub(super) auth: Authorization,

    // Supporting services.
    pub(crate) tasks: TaskStore,
    pub(super) worktrees: WorktreeState,
    pub(crate) music: MusicState,

    // Operation locks spanning multiple owners.
    // Shared for ordinary requests, exclusive for session identity and lifecycle changes.
    pub(super) session_boundary: tokio::sync::RwLock<()>,
    // Keep tmux resize acceptance and published dimensions in request order.
    pub(super) resize_mutation: tokio::sync::Mutex<()>,
    // Prevent child-name collisions and interleaved task/session writes.
    pub(super) worker_spawn: tokio::sync::Mutex<()>,
}

impl Manager {
    /// Publish one event with encoding shared across subscribers.
    pub(crate) fn emit(&self, event: Event) {
        let _ = self.events.send(EventMessage::new(event));
    }
}

#[cfg(test)]
#[path = "tests.rs"]
mod tests;
#[cfg(test)]
pub(crate) use tests::{test_manager, test_manager_with_socket};
