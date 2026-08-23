use super::State;
use crate::config::{DnsConfig, Limits, NetworkMode};
use crate::emu::Frame;
use serde::Serialize;

#[derive(Debug, Clone, Serialize)]
pub struct SessionView {
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
    pub breadcrumbs: Vec<String>,
    pub breadcrumb_yolo: bool,
    // Lets the client attach tips only to the Enter that will consume breadcrumbs.
    pub breadcrumbs_pending: bool,
    pub agent: String,
    pub state: State,
    pub alive: bool,
    pub cols: u16,
    pub rows: u16,
    /// The effective project ceiling or agent reduction.
    pub network: NetworkMode,
    /// Null means the agent inherits the project setting.
    pub network_override: Option<NetworkMode>,
    /// The effective DNS source, resolved from the project and agent settings.
    pub dns: DnsConfig,
    /// Null means the agent inherits the project's DNS setting.
    pub dns_override: Option<DnsConfig>,
    /// The caps this agent runs under, its own merged over its project's.
    pub limits: Limits,
    /// This agent's own caps before project inheritance - what the editor edits.
    pub limits_override: Limits,
    pub autostart: bool,
    pub auto_resume: bool,
    // Temporary sessions have no editable config entry. Durable host tabs also use the
    // ghost-row presentation, but are identified separately by `host`.
    pub ephemeral: bool,
    // Host terminals are sidebar tabs backed by host-terminal records, not sandboxed agents.
    pub host: bool,
    pub last_change: u64,
    // Separate from output activity so a continuously-redrawing worker can still age.
    pub state_since: u64,
    pub title: String,
    // Sticky until someone subscribes, rather than tied to the frame that rang.
    pub bell: bool,
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
    pub cursor_shape: u8,
    #[serde(default)]
    pub cursor_blink: bool,
    #[serde(default)]
    pub app_mouse: bool,
    // Distinguishes application mouse drags from terminal text selection.
    #[serde(default)]
    pub app_drag: bool,
    // Alternate screens have no scrollback; clients route wheel input differently.
    #[serde(default)]
    pub alt_screen: bool,
    #[serde(default)]
    pub title: String,
    // Echoes a one-off scroll request; live broadcasts use zero.
    #[serde(default)]
    pub request_id: u64,
    pub lines: Vec<String>,
}

impl ScreenView {
    pub(crate) fn from_frame(
        name: &str,
        seq: u64,
        cols: u16,
        rows: u16,
        off: u32,
        frame: Frame,
        request_id: u64,
    ) -> Self {
        Self {
            name: name.to_string(),
            seq,
            cols,
            rows,
            cx: frame.cx,
            cy: frame.cy,
            off,
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
