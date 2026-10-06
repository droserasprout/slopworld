//! Shared session state; manager/ owns lifecycle and events.rs owns publication.

use crate::clock::unix_ms;
mod protobuf;
use std::collections::HashMap;
use std::path::{Component, Path, PathBuf};
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex};
use std::time::Duration;

use anyhow::{Context, Result, bail};
use serde::Deserialize;
use serde_json::Value;
use tokio::sync::{RwLock, broadcast, mpsc};
use tokio::task::JoinHandle;

mod agent_templates;
mod events;
mod input;
mod template;
mod text;
mod title;
mod validation;
mod view;

mod manager;
mod prompt;
#[cfg(test)]
pub(crate) use manager::AudioSelection;
pub(crate) use manager::TemplatePersistence;
pub(crate) use manager::{AudioReq, WorktreeRequest};

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

use input::{Input, merge_input};
use prompt::{PromptVars, render_prompt, render_prompt_with};
pub use text::strip_sgr;
use title::{TitleAction, TitleCapture, TitleRequest, TitleSettings};
use validation::{
    absolute_path, check_belongs, check_name, check_project, free_name, free_project_name,
    json_to_toml, merge_toml, normalize_action_command, project_action_path, settle, slug,
};
pub(crate) use validation::{check_library_item, hold_action_command, validate_config};

#[cfg(test)]
use input::INPUT_BATCH;
#[cfg(test)]
use validation::normalize_path;

use crate::config::{
    Config, LibraryItemCfg, LibraryItemKind, LibraryItemLink, NetworkMode, ProjectCfg, SessionCfg,
    TitlePolicy, expand,
};
use crate::emu::{Frame, SessionEmu};
use crate::tmux::Tmux;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum State {
    Down,
    Working,
    Waiting,
    Idle,
}

crate::wire_enum!(State, {
    State::Down => crate::shared::protocol::enums::agent_state::DOWN,
    State::Working => crate::shared::protocol::enums::agent_state::WORKING,
    State::Waiting => crate::shared::protocol::enums::agent_state::WAITING,
    State::Idle => crate::shared::protocol::enums::agent_state::IDLE,
});

/// Launch overrides supplied by the caller.
#[derive(Debug, Clone, Default, Deserialize)]
pub struct RunWhere {
    #[serde(default)]
    pub worktree: String,
    #[serde(default)]
    pub intent: String,
    #[serde(default)]
    pub reader_path: String,
    #[serde(default)]
    pub reader_key: String,
    #[serde(default)]
    pub reader_scope: String,
    #[serde(default)]
    pub reader_pinned: bool,
    #[serde(default)]
    pub reader_line: u32,
    pub cols: Option<u16>,
    pub rows: Option<u16>,
    #[serde(default)]
    pub project: Option<String>,
    #[serde(default)]
    pub temp: bool,
    #[serde(default)]
    pub random_tips: Vec<String>,
}

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
    ephemeral: bool,
    // Recovered from a private tmux option after daemon restart; never saved in config.
    host: bool,
    // Preserve only cataloged host shell records after exit. Remove viewer and action records.
    persistent_host: bool,
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
            ephemeral: false,
            host: false,
            persistent_host: false,
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
