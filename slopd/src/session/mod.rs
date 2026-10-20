//! Live session state, lifecycle, operation guards and terminal capture.
//! Application owns user-operation coordination, configuration edits and daemon publication.

use crate::clock::unix_ms;
use std::collections::HashMap;
use std::path::Path;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex};
use std::time::Duration;

use anyhow::{Context, Result, bail};
use tokio::sync::{RwLock, mpsc};
use tokio::task::JoinHandle;

mod input;
mod launch;
pub use launch::RunWhere;
pub(crate) use launch::{
    ExecutionSource, HostLifetime, LaunchContent, LaunchDestination, LaunchOptions, LaunchRequest,
};
pub(crate) use launch::{LaunchContext, ReaderContext};
mod title;
mod validation;
mod view;

mod boundary;
mod caps;
mod capture;
mod config;
mod delivery;
mod errands;
pub(crate) use errands::{ErrandAllocation, PreparedErrand};
mod host_metadata;
mod identity;
mod init;
pub(crate) use init::{RuntimeServices, RuntimeStorage};
mod lifecycle;
mod maintenance;
mod runtime;
mod session_state;
mod sessions;
mod signals;
mod tasks;
mod views;
mod workers;
mod worktrees;

use capture::CachedScroll;
pub(crate) use host_metadata::HostMetadataPoll;
pub(crate) use signals::Signals;
pub use signals::WatchGuard;

mod role;
use role::AgentLifetime;
pub(crate) use role::SessionRole;

#[cfg(test)]
use crate::agent_templates::AgentTemplateStore;
use crate::application::Event;
pub use runtime::SessionRuntime;
#[cfg(test)]
pub(crate) use runtime::{test_manager, test_manager_with_socket};
pub(crate) use view::FrameViewArgs;
pub use view::{
    ScreenView, SessionLaunchView, SessionReaderView, SessionRuntimeView, SessionView,
    SessionWorkerView,
};

use crate::emu::strip_sgr;
pub(crate) use input::Input;
use input::merge_input;
use title::{TitleAction, TitleCapture, TitleRequest, TitleSettings};
pub(crate) use validation::check_name;
pub(crate) use validation::name_slug;
use validation::{free_name, free_project_name, slug};

#[cfg(test)]
use input::INPUT_BATCH;

use crate::config::{Config, NetworkMode, ProjectCfg, SessionCfg, TitlePolicy, expand};
use crate::emu::{Frame, SessionEmu};
use crate::tmux::Tmux;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum State {
    Down,
    Working,
    Idle,
}

crate::wire_enum!(State, {
    State::Down => crate::shared::protocol::enums::agent_state::DOWN,
    State::Working => crate::shared::protocol::enums::agent_state::WORKING,
    State::Idle => crate::shared::protocol::enums::agent_state::IDLE,
});

/// Mutable runtime state for one session, owned by SessionRuntime.
struct Live {
    // Configuration and host-shell identity.
    cfg: SessionCfg,
    launch: LaunchContext,
    role: SessionRole,
    // Host shells keep their last tmux cwd separately from the project's configured root.
    host_path: String,
    // Activity classification and terminal revisions.
    state: State,
    // Host-only: whether tmux currently has a foreground command other than the login shell.
    process_running: bool,
    seq: u64,
    // Last classified sequence; unchanged output only needs idle-time decay.
    retick_seq: u64,
    hash: u64,
    activity_hash: u64,
    last_change: u64,
    // Runtime decay uses monotonic time; last_change remains an epoch wire timestamp.
    activity_at: Option<tokio::time::Instant>,
    // State time is independent of pane redraws, which can continue several times a second.
    state_since: u64,
    // Preserve the bell notification until a client subscribes. Its original frame is temporary.
    bell: bool,
    // Terminal snapshot.
    cols: u16,
    rows: u16,
    screen: Option<ScreenView>,
    capture: LiveCapture,
    input: LiveInput,
    // Prevent stale startup input from reaching a replacement process with the same name.
    run_id: u64,
    title: TitleCapture,
}

/// Emulator and the reader task that owns it.
#[derive(Default)]
struct LiveCapture {
    // Serialize rendering through frame commit for this capture owner.
    render: Arc<tokio::sync::Mutex<()>>,
    emu: Option<Arc<Mutex<SessionEmu>>>,
    reader: Option<JoinHandle<()>>,
    // Prevent a replaced reader from clearing its successor’s emulator.
    reader_token: Option<Arc<()>>,
    // Maintenance retries failed attachment/status inspection without config edits.
    retry_at: Option<tokio::time::Instant>,
}

/// Queued input and startup sequencing for the current process.
#[derive(Default)]
struct LiveInput {
    sender: Option<mpsc::UnboundedSender<Input>>,
    traces: crate::latency::Pending,
    // Keep user keystrokes behind the pending startup auto-resume sequence.
    auto_resume_pending: bool,
}

impl Live {
    /// A configured session before any process or terminal reader is attached.
    fn new(cfg: SessionCfg, title: TitleCapture) -> Self {
        Self {
            cfg,
            launch: LaunchContext::default(),
            role: SessionRole::Agent(AgentLifetime::Durable),
            host_path: String::new(),
            state: State::Down,
            process_running: false,
            seq: 0,
            retick_seq: 0,
            hash: 0,
            activity_hash: 0,
            last_change: 0,
            activity_at: None,
            state_since: 0,
            bell: false,
            cols: Live::BOOT_COLS,
            rows: Live::BOOT_ROWS,
            screen: None,
            capture: LiveCapture::default(),
            input: LiveInput::default(),
            run_id: 0,
            title,
        }
    }
    // Initial grid until a client supplies its panel dimensions.
    const BOOT_COLS: u16 = 120;
    // Initial row count until a client supplies its panel height.
    const BOOT_ROWS: u16 = 34;

    fn set_state(&mut self, s: State) -> bool {
        if self.state == s {
            return false;
        }
        self.state = s;
        self.state_since = unix_ms();
        true
    }
}

#[cfg(test)]
#[path = "tests.rs"]
mod tests;

#[cfg(test)]
use crate::config::{LibraryItemCfg, LibraryItemKind, LibraryItemLink};
