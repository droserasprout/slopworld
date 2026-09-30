//! Client snapshots. Reader, runtime, launch, and worker snapshots are nested.

use super::State;
use crate::config::{DnsConfig, Limits, Mount, NetworkMode};
use crate::emu::Frame;
use serde::Serialize;
use std::sync::Arc;

/// Editable project configuration alongside its daemon-expanded directory.
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

/// Session identity, effective launch settings, and current runtime status.
#[derive(Debug, Clone, Serialize)]
pub struct SessionView {
    // Identity and workspace.
    pub worktree: String,
    pub worktree_name: String,
    pub name: String,
    pub label: String,
    pub intent: String,
    pub project: String,
    pub dir: String,
    // Launch policy and task ownership.
    pub launch: SessionLaunchView,
    pub worker: SessionWorkerView,
    // No editable config entry; host tabs are identified separately by `host`.
    pub ephemeral: bool,
    // Backed by a host-terminal record rather than a sandboxed agent.
    pub host: bool,
    pub reader: SessionReaderView,
    pub runtime: SessionRuntimeView,
}

/// Source location and pin state for a temporary terminal reader.
#[derive(Debug, Clone, Serialize)]
pub struct SessionReaderView {
    pub path: String,
    pub key: String,
    pub scope: String,
    pub pinned: bool,
    pub line: u32,
}

/// Current process, activity, and terminal status.
#[derive(Debug, Clone, Serialize)]
pub struct SessionRuntimeView {
    // Runtime status. Delay user input while startup auto-resume is pending.
    pub auto_resume_pending: bool,
    pub state: State,
    pub alive: bool,
    pub cols: u16,
    pub rows: u16,
    // Foreground job in a host shell; false for ordinary agents.
    pub process_running: bool,
    // Activity and state-transition times are independent epoch milliseconds.
    pub last_change: u64,
    pub state_since: u64,
    pub title: String,
    // Preserve the bell notification until a client subscribes.
    pub bell: bool,
    // Process identity invalidates cached history; seq tracks terminal revisions.
    pub run_id: u64,
    pub seq: u64,
}

/// Command selection and effective settings for launching the session.
#[derive(Debug, Clone, Serialize)]
pub struct SessionLaunchView {
    // Configured command selection and resolved preset.
    pub command: String,
    /// Resolved preset; empty for a custom command without a preset sandbox.
    pub command_preset: String,
    pub cmd: Option<String>,
    pub args: Option<String>,
    pub sandbox: Vec<String>,
    pub persistent_tmp: bool,
    /// Resolved command line.
    pub agent: String,
    // Agent-owned isolation settings.
    pub network: NetworkMode,
    pub dns: DnsConfig,
    pub limits: Limits,
    /// Project-owned mounts applied when this agent next starts.
    pub mounts: Vec<Mount>,
    // Effective startup policy; task-owned workers disable both flags.
    pub autostart: bool,
    pub auto_resume: bool,
}

/// Explicit task hierarchy and worker persistence.
#[derive(Debug, Clone, Serialize)]
pub struct SessionWorkerView {
    /// Task-owned child; clients must not infer this from names or presets.
    pub enabled: bool,
    pub parent: String,
    pub task_id: String,
    /// Configured worker retained after exit; derived from live session persistence.
    pub durable: bool,
}

/// Terminal snapshot for a live update or a scrollback reply.
#[derive(Debug, Clone, Serialize)]
pub struct ScreenView {
    pub input_timings: Vec<crate::shared::wire::InputTiming>,
    pub name: String,
    pub seq: u64,
    // Viewport dimensions, cursor position, and scrollback extent.
    pub cols: u16,
    pub rows: u16,
    pub cx: u16,
    pub cy: u16,
    #[serde(default)]
    pub off: u32,
    #[serde(default)]
    pub history: u32,
    // Cursor appearance and application input modes.
    #[serde(default)]
    pub cursor_shape: u8,
    #[serde(default)]
    pub cursor_blink: bool,
    #[serde(default)]
    pub app_mouse: bool,
    // Distinguishes application mouse drags from terminal text selection.
    #[serde(default)]
    pub app_drag: bool,
    // Alternate screens have no scrollback; this also controls wheel routing.
    #[serde(default)]
    pub alt_screen: bool,
    #[serde(default)]
    pub title: String,
    // Echo the scroll request ID. Live broadcasts use zero.
    #[serde(default)]
    pub request_id: u64,
    // Styled terminal rows shared across snapshot clones.
    pub lines: Vec<Arc<str>>,
}

/// Session and viewport metadata supplied alongside an emulator frame.
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
    /// Attach viewport metadata; the capture path adds input timings separately.
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
            input_timings: Vec::new(),
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
