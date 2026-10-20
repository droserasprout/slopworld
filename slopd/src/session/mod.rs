//! Shared session state; manager/ owns lifecycle and events.rs owns publication.

use crate::clock::unix_ms;
mod protobuf;
use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex};
use std::time::Duration;

use anyhow::{Context, Result, bail};
use serde_json::Value;
use tokio::sync::{RwLock, broadcast, mpsc};
use tokio::task::JoinHandle;

mod agent_templates;
mod events;
mod input;
mod launch;
pub use launch::RunWhere;
use launch::{
    ExecutionSource, HostLifetime, LaunchContent, LaunchDestination, LaunchOptions, LaunchRequest,
};
pub(crate) use launch::{ExplicitLaunch, LaunchContext, ReaderContext};
mod template;
mod text;
mod title;
mod validation;
mod view;

mod manager;
mod prompt;
mod role;
pub(crate) use manager::TemplatePersistence;
pub(crate) use manager::WorktreeRequest;
use role::{AgentLifetime, SessionRole};

#[cfg(test)]
pub(crate) use agent_templates::validate_definition as validate_template_definition;
pub(crate) use agent_templates::{AgentTemplate, AgentTemplateError, AgentTemplateStore};
pub use events::Event;
pub(crate) use events::EventMessage;
pub use manager::{Manager, WatchGuard};
#[cfg(test)]
pub(crate) use manager::{test_manager, test_manager_with_socket};
pub(crate) use view::FrameViewArgs;
pub use view::{
    ProjectView, ScreenView, SessionLaunchView, SessionReaderView, SessionRuntimeView, SessionView,
    SessionWorkerView,
};

use crate::config::patch::json_to_toml;
use crate::config::{check_library_item, check_project};
use crate::files::actions::{absolute_path, normalize_action_command, project_action_path};
pub(crate) use input::Input;
use input::merge_input;
use prompt::{PromptVars, render_prompt, render_prompt_with};
pub use text::strip_sgr;
use title::{TitleAction, TitleCapture, TitleRequest, TitleSettings};
pub(crate) use validation::name_slug;
use validation::{check_belongs, check_name, free_name, free_project_name, settle, slug};

#[cfg(test)]
use input::INPUT_BATCH;

use crate::config::{
    Config, LibraryItemCfg, LibraryItemKind, NetworkMode, ProjectCfg, SessionCfg, TitlePolicy,
    expand,
};
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

/// Internal notifications for rechecking connected clients’ credentials.
#[derive(Debug, Clone)]
pub(crate) enum AuthChange {
    GrantsRevoked,
    RootTokenChanged,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum Ready {
    Settled,
    // Startup continues in the background. Only Gone cancels queued typing.
    Timeout,
    Gone,
}

/// Mutable runtime state for one session, owned by Manager.
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
use crate::config::LibraryItemLink;
