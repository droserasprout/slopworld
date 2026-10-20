//! Session manager state and focused implementation modules.
//! Start at init.rs for construction, boundary.rs for concurrency, and lifecycle/start.rs for launch.
//! session_state.rs owns classification; views.rs projects live state for clients.

mod agent_templates;
mod boundary;
mod caps;
mod capture;
mod catalog_state;
mod config;
mod delivery;
mod desktop;
mod directories;
mod errands;
mod host_metadata;
mod identity;
mod init;
mod launch;
mod library;
mod lifecycle;
mod maintenance;
mod music;
mod projects;
mod session_state;
mod sessions;
mod signals;
mod task_summary;
mod tasks;
mod views;
mod workers;
pub(crate) use workers::{WorkerCheckout, WorkerRequest};
mod worktrees;
pub(crate) use worktrees::WorktreeRequest;
pub(crate) use worktrees::WorktreeState;

use crate::music::Music;
pub(crate) use agent_templates::TemplatePersistence;
pub(crate) use agent_templates::TemplateStore;
pub(crate) use caps::Authorization;
use capture::CachedScroll;
pub(crate) use catalog_state::CatalogState;
pub(crate) use host_metadata::HostMetadataPoll;
pub(crate) use signals::Signals;
pub use signals::WatchGuard;

use super::*;
use std::collections::HashMap;
use std::path::PathBuf;
use std::sync::{Arc, Mutex};
use tokio::sync::RwLock;

/// Shared daemon coordinator; each subsystem keeps its own state and locks.
pub struct Manager {
    // Configuration and saved definitions.
    pub cfg_path: PathBuf,
    pub(super) endpoint_path: PathBuf,
    // Accepted configuration and auth publish together; configuration owns transactions.
    pub(super) cfg: RwLock<Config>,
    pub(super) catalog_state: CatalogState,
    pub(super) configuration: crate::config::transactions::Transactions,
    pub(super) templates: TemplateStore,

    // Live sessions, terminal capture, and activity.
    pub tmux: Tmux,
    // Includes stopped sessions as well as attached processes.
    pub(super) live: RwLock<HashMap<String, Live>>,
    pub(super) library_snapshot: Mutex<Vec<LibraryItemCfg>>,
    pub(super) temp: RwLock<HashMap<String, ProjectCfg>>,
    pub(super) host_metadata: HostMetadataPoll,
    pub(super) scroll_cache: Mutex<HashMap<String, CachedScroll>>,
    pub(super) activity_mutation: tokio::sync::Mutex<()>,
    pub(super) activity_cache: crate::activity::ActivityCache,
    pub(super) title_cache: crate::title::SummaryCache,

    // Client communication and authorization.
    pub(crate) application: crate::application::Services,
    pub(super) signals: Signals,
    pub(super) auth: Authorization,

    // Supporting services.
    pub(super) worktrees: WorktreeState,
    music: Music,

    // Operation locks spanning multiple owners.
    // Shared for ordinary requests, exclusive for session identity and lifecycle changes.
    pub(super) session_boundary: Arc<tokio::sync::RwLock<()>>,
    // Per-name input guards exclude only the terminal being changed by lifecycle work.
    pub(super) terminal_boundaries:
        Mutex<HashMap<String, std::sync::Weak<tokio::sync::RwLock<()>>>>,
    // Keep tmux resize acceptance and published dimensions in request order.
    pub(super) terminal_resizes: Mutex<HashMap<String, std::sync::Weak<tokio::sync::Mutex<()>>>>,
    // Prevent child-name collisions and interleaved task/session writes.
    pub(super) worker_spawn: tokio::sync::Mutex<()>,
    #[cfg(test)]
    pub(super) frame_commit_pause:
        Mutex<Option<(Arc<tokio::sync::Notify>, Arc<tokio::sync::Notify>)>>,
    #[cfg(test)]
    pub(super) scroll_capture_pause:
        Mutex<Option<(Arc<tokio::sync::Notify>, Arc<tokio::sync::Notify>)>>,
    #[cfg(test)]
    pub(super) reader_attach_pause:
        Mutex<Option<(Arc<tokio::sync::Notify>, Arc<tokio::sync::Notify>)>>,
    #[cfg(test)]
    pub(super) input_sink: Mutex<Option<tokio::sync::mpsc::UnboundedSender<Input>>>,
    #[cfg(test)]
    _test_directory: Option<tests::TestDirectory>,
}

impl Manager {
    /// Capture the session authorization lease before task work can outlive its requester.
    pub(crate) fn task_store(&self) -> crate::tasks::io::TaskIo {
        self.application.tasks(boundary::task_io_guards())
    }

    /// Publish one event with encoding shared across subscribers.
    pub(crate) fn emit(&self, event: Event) {
        self.application.emit(event);
    }
}

#[cfg(test)]
#[path = "tests.rs"]
mod tests;
#[cfg(test)]
pub(crate) use tests::{test_manager, test_manager_with_socket};
