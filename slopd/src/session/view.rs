use super::State;
use crate::config::{DnsConfig, Limits, Mount, NetworkMode};
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
    pub slopworld_md: bool,
    pub breadcrumb_yolo: bool,
    // Lets the client attach tips only to the Enter that will consume breadcrumbs.
    pub breadcrumbs_pending: bool,
    // Startup auto-resume is waiting or queued; clients must keep user input behind it.
    pub auto_resume_pending: bool,
    pub agent: String,
    pub state: State,
    pub alive: bool,
    pub cols: u16,
    pub rows: u16,
    /// The effective project default or agent override.
    pub network: NetworkMode,
    /// Null means the agent inherits the project default.
    pub network_override: Option<NetworkMode>,
    /// The effective DNS source, resolved from the project and agent settings.
    pub dns: DnsConfig,
    /// Null means the agent inherits the project's DNS setting.
    pub dns_override: Option<DnsConfig>,
    /// The caps this agent runs under, its own merged over its project's.
    pub limits: Limits,
    /// This agent's own caps before project inheritance - what the editor edits.
    pub limits_override: Limits,
    pub mounts: Vec<Mount>,
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
mod tests {
    use super::*;

    #[test]
    fn frame_metadata_is_preserved_on_the_wire_view() {
        let frame = Frame {
            lines: vec!["one".into(), "two".into()],
            cx: 7,
            cy: 3,
            cursor_shape: 2,
            cursor_blink: true,
            app_mouse: true,
            app_drag: true,
            alt_screen: true,
            title: "editor".into(),
            bell: true,
        };

        let view = ScreenView::from_frame(
            FrameViewArgs {
                name: "agent",
                seq: 11,
                cols: 120,
                rows: 40,
                off: 9,
                history: 91,
                request_id: 23,
            },
            frame,
        );
        assert_eq!(view.name, "agent");
        assert_eq!((view.seq, view.cols, view.rows), (11, 120, 40));
        assert_eq!((view.cx, view.cy, view.off), (7, 3, 9));
        assert_eq!(view.history, 91);
        assert_eq!(view.request_id, 23);
        assert_eq!(view.cursor_shape, 2);
        assert!(view.cursor_blink && view.app_mouse && view.app_drag && view.alt_screen);
        assert_eq!(view.title, "editor");
        assert_eq!(view.lines, ["one", "two"]);
    }
}
