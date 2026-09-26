//! Shared session state; manager/ owns lifecycle and events.rs owns publication.

mod protobuf;
use std::collections::HashMap;
use std::path::{Component, Path, PathBuf};
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex};
use std::time::{Duration, SystemTime, UNIX_EPOCH};

use anyhow::{bail, Context, Result};
use regex::Regex;
use serde::Deserialize;
use serde_json::Value;
use tokio::sync::{broadcast, mpsc, RwLock};
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
pub(crate) use manager::WorktreeRequest;

#[cfg(test)]
pub(crate) use agent_templates::validate_definition as validate_template_definition;
pub(crate) use agent_templates::{AgentTemplate, AgentTemplateError, AgentTemplateStore};
pub use events::Event;
pub(crate) use events::EventMessage;
#[cfg(test)]
pub(crate) use manager::{test_manager, test_manager_with_socket};
pub use manager::{Manager, WatchGuard};
pub(crate) use view::FrameViewArgs;
pub use view::{ProjectView, ScreenView, SessionLaunchView, SessionView, SessionWorkerView};

use input::{merge_input, Input};
use template::{render_template, render_template_with, TemplateVars};
pub use text::strip_sgr;
pub(crate) use text::strip_sgr_tail;
use title::{
    begin_title_request, is_dialog_answer, prompt_is_long_enough, title_settings, Composer,
    Submission, TitleCapture, TitleRequest,
};
use validation::{
    absolute_path, check_belongs, check_name, check_project, free_name, free_project_name,
    json_to_toml, merge_toml, normalize_action_command, project_action_path, settle, slug,
};
pub(crate) use validation::{check_library_item, hold_action_command, validate_config};

#[cfg(test)]
use input::INPUT_BATCH;
#[cfg(test)]
use title::{title_agent, TitleAgent};
#[cfg(test)]
use validation::normalize_path;

use crate::config::{
    expand, Config, LibraryItemCfg, LibraryItemKind, LibraryItemLink, NetworkMode, ProjectCfg,
    SessionCfg, TitlePolicy,
};
use crate::emu::{Frame, SessionEmu};
use crate::tmux::Tmux;

// Limit classification to the tail ending at the last nonblank terminal line.
const TAIL_LINES: usize = 12;

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
    // State time is independent of pane redraws, which can continue several times a second.
    state_since: u64,
    // Preserve the bell notification until a client subscribes. Its original frame is temporary.
    bell: bool,
    // Terminal snapshot.
    cols: u16,
    rows: u16,
    plain: Arc<String>,
    // Cache regex results by visible text and rules revision; idle decay is separate.
    rule_cache: Option<RuleCache>,
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
    // Insert immediately before the first Enter after process startup.
    breadcrumbs: Vec<u8>,
    breadcrumbs_pending: bool,
    // Keep user keystrokes behind the pending startup auto-resume sequence.
    auto_resume_pending: bool,
}

#[derive(Clone)]
struct RuleCache {
    revision: u64,
    text: Arc<String>,
    matched: Option<State>,
}

struct Classification {
    state: State,
    rules_revision: u64,
    matched: Option<State>,
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
            rule_cache: None,
            state_since: 0,
            bell: false,
            cols: Live::BOOT_COLS,
            rows: Live::BOOT_ROWS,
            plain: Arc::new(String::new()),
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
        self.state_since = now_ms();
        true
    }
}

async fn disk_mtime(path: &std::path::Path) -> Option<SystemTime> {
    tokio::fs::metadata(path).await.ok()?.modified().ok()
}

fn now_ms() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_millis() as u64)
        .unwrap_or(0)
}

fn match_rules(rules: &[(State, Regex)], text: &str) -> Option<State> {
    // Use the lowest matching line. Configuration order resolves ties on that line.
    // Skip only trailing blank rows when locating the screen's end.
    // Each blank row within the tail counts toward TAIL_LINES.
    let mut lines = text.lines().rev();
    let mut line = lines.find(|line| !line.trim().is_empty())?;
    for index in 0..TAIL_LINES {
        for (state, re) in rules {
            if re.is_match(line) {
                return Some(*state);
            }
        }
        if index + 1 == TAIL_LINES {
            break;
        }
        let Some(next) = lines.next() else {
            break;
        };
        line = next;
    }
    None
}

fn compile_rules(cfg: &Config) -> Vec<(State, Regex)> {
    cfg.state_rules
        .iter()
        .filter_map(|r| {
            let state = match r.state.as_str() {
                "waiting" => State::Waiting,
                "working" => State::Working,
                "idle" => State::Idle,
                other => {
                    tracing::warn!("state_rule has unknown state {other:?}, ignoring");
                    return None;
                }
            };
            match Regex::new(&r.pattern) {
                Ok(re) => Some((state, re)),
                Err(e) => {
                    tracing::warn!("bad state_rule pattern {:?}: {e}", r.pattern);
                    None
                }
            }
        })
        .collect()
}

#[cfg(test)]
#[path = "tests.rs"]
mod tests;
