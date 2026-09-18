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
    // Startup auto-resume is waiting or queued; clients must keep user input behind it.
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
    /// A worker in config.toml is durable; an ephemeral worker exists only until its process
    /// exits. This is derived from the live session rather than another mutable config flag.
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
    // Sticky until someone subscribes, rather than tied to the frame that rang.
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
    // Alternate screens have no scrollback; clients route wheel input differently.
    #[serde(default)]
    pub alt_screen: bool,
    #[serde(default)]
    pub title: String,
    // Echoes a one-off scroll request; live broadcasts use zero.
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
mod tests {
    use super::*;

    #[test]
    fn project_paths_keep_config_and_daemon_expansion_separate() {
        let config = crate::config::ProjectCfg {
            name: "repo".into(),
            dir: "~/repo".into(),
            ..Default::default()
        };
        let expected = dirs::home_dir().unwrap().join("repo");
        let view = ProjectView::from(config.clone());
        let wire = serde_json::to_value(&view).unwrap();
        assert_eq!(wire["name"], "repo");
        assert_eq!(wire["dir"], "~/repo");
        assert_eq!(wire["expanded_dir"], expected.to_string_lossy().as_ref());
        assert!(wire.get("config").is_none());
        assert!(serde_json::to_value(config)
            .unwrap()
            .get("expanded_dir")
            .is_none());
        let event = serde_json::to_value(super::super::Event::Projects {
            projects: vec![view],
        })
        .unwrap();
        assert_eq!(event["projects"][0], wire);
    }

    #[test]
    fn frame_metadata_is_preserved_on_the_wire_view() {
        let frame = Frame {
            lines: vec!["one".into(), "two".into()],
            content_hash: 0,
            activity_hash: 0,
            history: 0,
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
        assert_eq!(
            view.lines.iter().map(AsRef::as_ref).collect::<Vec<&str>>(),
            ["one", "two"]
        );
    }

    #[test]
    fn shared_rows_keep_the_existing_json_shape() {
        let frame = Frame {
            lines: vec!["one".into(), "two".into()],
            content_hash: 0,
            activity_hash: 0,
            history: 0,
            cx: 0,
            cy: 0,
            cursor_shape: 0,
            cursor_blink: false,
            app_mouse: false,
            app_drag: false,
            alt_screen: false,
            title: String::new(),
            bell: false,
        };
        let view = ScreenView::from_frame(
            FrameViewArgs {
                name: "agent",
                seq: 1,
                cols: 20,
                rows: 2,
                off: 0,
                history: 0,
                request_id: 0,
            },
            frame,
        );

        let json = serde_json::to_value(view).unwrap();
        assert_eq!(json["lines"], serde_json::json!(["one", "two"]));
        assert!(json.get("content_hash").is_none());
    }
}
