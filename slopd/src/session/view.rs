use super::State;
use crate::config::{DnsConfig, Limits, Mount, NetworkMode};
use crate::emu::Frame;
use serde::Serialize;
use std::sync::Arc;

// Project config remains editable while filesystem consumers use daemon-local expansion.
#[derive(Debug, Clone, Serialize)]
pub struct ProjectView {
    #[serde(flatten)]
    pub config: crate::config::ProjectCfg,
    pub expanded_dir: String,
}

impl From<crate::config::ProjectCfg> for ProjectView {
    fn from(config: crate::config::ProjectCfg) -> Self {
        Self {
            expanded_dir: crate::config::expand(&config.dir),
            config,
        }
    }
}

#[derive(Debug, Clone, Serialize)]
pub struct SessionView {
    pub worktree: String,
    pub worktree_name: String,
    pub name: String,
    pub label: String,
    pub project: String,
    pub dir: String,
    pub command: String,
    /// The command preset after `[defaults] agent` is resolved. Empty means the session
    /// supplies its own command line, so it has no command preset's sandbox.
    pub command_preset: String,
    pub cmd: Option<String>,
    pub sandbox: Vec<String>,
    pub persistent_tmp: bool,
    // Startup auto-resume is waiting or queued. Clients must delay user input until it completes.
    pub auto_resume_pending: bool,
    pub agent: String,
    pub state: State,
    pub alive: bool,
    pub cols: u16,
    pub rows: u16,
    /// The network mode owned by this agent.
    pub network: NetworkMode,
    /// The DNS configuration owned by this agent.
    pub dns: DnsConfig,
    /// The resource limits owned by this agent.
    pub limits: Limits,
    /// Project-owned mounts applied when this agent next starts.
    pub mounts: Vec<Mount>,
    pub autostart: bool,
    pub auto_resume: bool,
    /// Task-owned children carry explicit hierarchy metadata. The client must not infer this
    /// from names, projects or command presets.
    pub worker: bool,
    pub parent: String,
    pub task_id: String,
    /// A worker in config.toml persists after its process exits. A temporary worker does not.
    /// Derive this value from the live session, without a separate configuration flag.
    pub durable: bool,
    // Temporary sessions have no editable config entry. Durable host tabs also use the
    // ghost-row presentation, but are identified separately by `host`.
    pub ephemeral: bool,
    // Host terminals are sidebar tabs backed by host-terminal records, not sandboxed agents.
    pub host: bool,
    // Host-only foreground-job state. False for an idle shell prompt and for ordinary agents.
    pub process_running: bool,
    pub last_change: u64,
    // Separate from output activity so a continuously-redrawing worker can still age.
    pub state_since: u64,
    pub title: String,
    // Preserve the bell notification until a client subscribes.
    pub bell: bool,
    // Distinguishes successive processes under one durable session name. Clients use it to
    // reject cached terminal history from a previous run.
    pub run_id: u64,
    pub seq: u64,
}

#[derive(Debug, Clone, Serialize)]
pub struct ScreenView {
    pub name: String,
    pub seq: u64,
    pub cols: u16,
    pub rows: u16,
    pub cx: u16,
    pub cy: u16,
    #[serde(default)]
    pub off: u32,
    #[serde(default)]
    pub history: u32,
    #[serde(default)]
    pub cursor_shape: u8,
    #[serde(default)]
    pub cursor_blink: bool,
    #[serde(default)]
    pub app_mouse: bool,
    // Distinguishes application mouse drags from terminal text selection.
    #[serde(default)]
    pub app_drag: bool,
    // Alternate screens have no scrollback. Clients route wheel input according to this flag.
    #[serde(default)]
    pub alt_screen: bool,
    #[serde(default)]
    pub title: String,
    // Echo the scroll request ID. Live broadcasts use zero.
    #[serde(default)]
    pub request_id: u64,
    pub lines: Vec<Arc<str>>,
}

pub(crate) struct FrameViewArgs<'a> {
    pub(crate) name: &'a str,
    pub(crate) seq: u64,
    pub(crate) cols: u16,
    pub(crate) rows: u16,
    pub(crate) off: u32,
    pub(crate) history: u32,
    pub(crate) request_id: u64,
}

impl ScreenView {
    pub(crate) fn from_frame(args: FrameViewArgs<'_>, frame: Frame) -> Self {
        let FrameViewArgs {
            name,
            seq,
            cols,
            rows,
            off,
            history,
            request_id,
        } = args;
        Self {
            name: name.to_string(),
            seq,
            cols,
            rows,
            cx: frame.cx,
            cy: frame.cy,
            off,
            history,
            request_id,
            cursor_shape: frame.cursor_shape,
            cursor_blink: frame.cursor_blink,
            app_mouse: frame.app_mouse,
            app_drag: frame.app_drag,
            alt_screen: frame.alt_screen,
            title: frame.title,
            lines: frame.lines,
        }
    }
}

#[cfg(test)]
#[path = "view_tests.rs"]
mod tests;
