//! Live session runtime and focused application adapters.
//! init.rs constructs state; application/startup.rs orders recovery.
//! boundary.rs owns concurrency and lifecycle/start.rs owns process startup.
//! Application feature entry points coordinate launches, workers, projects and worktrees.
//! session_state.rs owns classification; views.rs projects live state for clients.

use super::*;
use std::collections::HashMap;
#[cfg(test)]
use std::path::PathBuf;
use std::sync::{Arc, Mutex};
use tokio::sync::RwLock;

/// Live session coordinator with injected application services and workspace storage.
pub struct SessionRuntime {
    // Configuration and saved definitions.
    pub(crate) workspace: Arc<crate::application::Workspace>,
    // Accepted configuration and auth publish together; configuration owns transactions.
    pub(crate) config_authority: Arc<crate::application::ConfigAuthority>,

    pub(super) host_records: crate::application::HostRecords,

    // Live sessions, terminal capture, and activity.
    pub tmux: Tmux,
    // Includes stopped sessions as well as attached processes.
    pub(super) live: RwLock<HashMap<String, Live>>,
    pub(super) temp: RwLock<HashMap<String, ProjectCfg>>,
    pub(super) host_metadata: HostMetadataPoll,
    pub(super) scroll_cache: Mutex<HashMap<String, CachedScroll>>,
    pub(super) activity_mutation: tokio::sync::Mutex<()>,
    pub(super) activity_cache: crate::activity::ActivityCache,

    // Client communication and authorization.
    pub(super) tasks: crate::tasks::io::TaskStore,
    pub(super) events: crate::application::EventPublisher,
    pub(super) title_cache: Arc<crate::title::SummaryCache>,
    pub(super) templates: crate::application::TemplateCatalog,
    // Fixture composition only; production runtime cannot access the service container.
    #[cfg(test)]
    pub(crate) application: Arc<crate::application::Services>,
    pub(super) signals: Signals,

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
    pub(super) _test_directory: Option<tests::TestDirectory>,
}

impl SessionRuntime {
    #[cfg(test)]
    pub(crate) fn cfg_path(&self) -> &std::path::Path {
        self.workspace.settings_path()
    }

    /// Capture the session authorization lease before task work can outlive its requester.
    pub(crate) fn task_store(&self) -> crate::tasks::io::TaskIo {
        self.tasks.access(boundary::task_io_guards())
    }

    /// Publish one event with encoding shared across subscribers.
    pub(crate) fn emit(&self, event: Event) {
        self.events.emit(event);
    }
}

#[cfg(test)]
#[path = "runtime_tests.rs"]
mod tests;
#[cfg(test)]
pub(crate) use tests::{test_manager, test_manager_with_socket};
