use serde::Deserialize;

fn durable_default() -> bool {
    true
}

#[derive(Deserialize)]
pub(crate) struct CreateTaskReq {
    pub(crate) to: String,
    pub(crate) body: String,
}

#[derive(Deserialize)]
pub(crate) struct SpawnWorkerReq {
    /// The project context for the new worker. Scoped callers may use only their own project.
    #[serde(default)]
    pub(crate) project: String,
    /// Qualified identity from the daemon's spawnable-template catalog.
    pub(crate) template: String,
    pub(crate) body: String,
    #[serde(default = "durable_default")]
    pub(crate) durable: bool,
    #[serde(default)]
    pub(crate) worktree: String,
    #[serde(default)]
    pub(crate) new_worktree: bool,
    #[serde(default)]
    pub(crate) base: String,
    #[serde(default)]
    pub(crate) worktree_name: String,
}

#[derive(Debug, Deserialize, Default)]
pub(crate) struct SpawnableTemplatesQuery {
    /// Root callers may select a project context; agents default to their own project.
    #[serde(default)]
    pub(crate) project: String,
}

#[derive(Deserialize)]
pub(crate) struct UpdateTaskReq {
    pub(crate) status: crate::tasks::Status,
    #[serde(default)]
    pub(crate) note: Option<String>,
}

#[derive(Deserialize)]
pub(crate) struct ListTasksQuery {
    /// Root callers may ask for the complete task board. The default remains the caller's
    /// mailbox so agent and CLI inboxes do not learn about unrelated work.
    #[serde(default)]
    pub(crate) all: bool,
}

#[derive(Deserialize)]
pub(crate) struct PruneTasksQuery {
    /// Every finished task in the store rather than the caller's own. Root-only: it reaches
    /// mailboxes the caller is not party to.
    #[serde(default)]
    pub(crate) all: bool,
}

#[derive(Deserialize)]
pub(crate) struct RemoveTasksReq {
    /// Removes the listed task ids in one store transaction. An empty list is a no-op.
    pub(crate) ids: Vec<String>,
}

#[derive(Debug, Deserialize)]
pub(crate) struct LabelReq {
    pub(crate) label: String,
}

#[derive(Debug, Deserialize)]
#[serde(deny_unknown_fields)]
pub(crate) struct SaveAgentTemplateReq {
    pub(crate) name: String,
    #[serde(default)]
    pub(crate) description: String,
    /// Existing configured agent to capture. Kept for the editor's "Save as template" action.
    #[serde(default)]
    pub(crate) source: String,
    /// Existing template to duplicate under `name`.
    #[serde(default)]
    pub(crate) duplicate: String,
}

#[derive(Debug, Deserialize)]
pub(crate) struct TemplateVersionQuery {
    pub(crate) version: Option<u64>,
}

#[derive(Debug, Deserialize)]
pub(crate) struct CreateAgentTemplateReq {
    pub(crate) name: String,
    pub(crate) project: String,
    /// A complete form snapshot. The daemon copies only the documented portable fields.
    #[serde(default)]
    pub(crate) overrides: Option<serde_json::Value>,
    /// When present, explicitly controls whether the resulting session starts. Omitted keeps
    /// the template/override autostart value for the in-game editor.
    #[serde(default)]
    pub(crate) start: Option<bool>,
}

/// An errand nobody wrote down: the same temporary agent `/api/library/NAME/run` makes,
/// spelled out in the body instead of looked up. `text` is optional here where it is
/// required of an entry - `less` on a file is a command with nothing to type after it.
#[derive(Deserialize)]
pub(crate) struct RunReq {
    #[serde(default)]
    pub(crate) worktree: String,
    pub(crate) cols: Option<u16>,
    pub(crate) rows: Option<u16>,
    #[serde(default)]
    pub(crate) project: String,
    #[serde(default)]
    pub(crate) kind: crate::config::LibraryItemKind,
    /// A preset name or a command line, read exactly as a library item's is.
    #[serde(default)]
    pub(crate) command: String,
    /// Raw selected Files-sidebar path. When present, the daemon expands it and replaces the
    /// quoted raw path in `command`, so interactive and captured file actions agree.
    #[serde(default)]
    pub(crate) path: String,
    /// Keep a file-action terminal open after its command exits so short output remains visible.
    #[serde(default)]
    pub(crate) hold: bool,
    #[serde(default)]
    pub(crate) text: String,
    /// Names the session and, through `slug`, the tmux session behind it. The errand's own
    /// word for itself, since there is no entry to take one from. Empty with `host` set is
    /// the one case the daemon answers instead - see `sandbox::host_session_name`.
    #[serde(default)]
    pub(crate) label: String,
    #[serde(default)]
    pub(crate) temp: bool,
    /// The game supplies loading-screen tips for `{{ random_tip }}`, already distinct: one is
    /// spent per mention, so a text with five bullets gets five different lines.
    #[serde(default)]
    pub(crate) random_tips: Vec<String>,
    /// Explicit unsandboxed errand execution.
    #[serde(default)]
    pub(crate) host: bool,
    #[serde(default)]
    pub(crate) agent_template: String,
    /// Clone agent-owned process settings (presets, persistent /tmp, network, DNS, and limits)
    /// from this session. The selected project supplies mounts.
    #[serde(default)]
    pub(crate) like: String,
}

#[derive(Deserialize)]
pub(crate) struct FileActionReq {
    #[serde(default)]
    pub(crate) project: String,
    pub(crate) path: String,
    pub(crate) command: String,
    #[serde(default)]
    pub(crate) host: bool,
}

#[derive(Deserialize)]
pub(crate) struct OpenAppsQuery {
    pub(crate) path: String,
}

#[derive(Deserialize)]
pub(crate) struct GrantReq {
    pub(crate) grantor: String,
    #[serde(default)]
    pub(crate) sessions: Vec<String>,
    /// `ro` to watch, `rw` to drive.
    pub(crate) level: String,
}

#[derive(Deserialize)]
pub(crate) struct ConfigReq {
    pub(crate) text: String,
}

#[derive(Deserialize)]
pub(crate) struct ProjectPreviewReq {
    pub(crate) name: String,
    #[serde(default)]
    pub(crate) temp: bool,
}

#[derive(Deserialize)]
pub(crate) struct ClipReq {
    #[serde(default)]
    pub(crate) text: String,
}

/// `?files=1` and `?files=true` are the same answer. serde's own bool takes only the
/// second, and half of what this endpoint is for is being asked by hand from a shell.
/// A word that is neither is refused rather than read as "on": a typo silently turning
/// a switch on is worse than a 400 saying so.
fn flag<'de, D: serde::Deserializer<'de>>(d: D) -> Result<bool, D::Error> {
    use serde::de::Error;
    let s = String::deserialize(d)?;
    match s.trim() {
        "" | "0" | "false" | "no" | "off" => Ok(false),
        "1" | "true" | "yes" | "on" => Ok(true),
        other => Err(D::Error::custom(format!(
            "expected a yes or a no, got {other:?}"
        ))),
    }
}

#[derive(Deserialize)]
pub(crate) struct BrowseReq {
    #[serde(default)]
    pub(crate) path: String,
    /// Opt-in, so the project-dir picker - which wants directories and nothing else -
    /// pays neither the read nor the wire for a directory full of files.
    #[serde(default, deserialize_with = "flag")]
    pub(crate) files: bool,
    #[serde(default, deserialize_with = "flag")]
    pub(crate) hidden: bool,
    #[serde(default, deserialize_with = "flag")]
    pub(crate) gitignore: bool,
    #[serde(default)]
    pub(crate) limit: Option<usize>,
}

#[derive(Deserialize)]
pub(crate) struct ReadReq {
    pub(crate) path: String,
}

#[derive(Deserialize)]
pub(crate) struct HighlightReq {
    pub(crate) text: String,
    #[serde(default)]
    pub(crate) language: String,
}

#[derive(Deserialize)]
pub(crate) struct FileReq {
    pub(crate) path: String,
    #[serde(default)]
    pub(crate) name: String,
    #[serde(default)]
    pub(crate) kind: String,
}

#[derive(Deserialize)]
pub(crate) struct SearchReq {
    #[serde(default)]
    pub(crate) path: String,
    #[serde(default)]
    pub(crate) q: String,
    #[serde(default, deserialize_with = "flag")]
    pub(crate) regex: bool,
    #[serde(default, deserialize_with = "flag")]
    pub(crate) case: bool,
    #[serde(default, deserialize_with = "flag")]
    pub(crate) word: bool,
    #[serde(default, deserialize_with = "flag")]
    pub(crate) hidden: bool,
    #[serde(default, deserialize_with = "flag")]
    pub(crate) gitignore: bool,
    #[serde(default)]
    pub(crate) limit: Option<usize>,
}

#[derive(Deserialize)]
pub(crate) struct GitReq {
    #[serde(default)]
    pub(crate) path: String,
    pub(crate) counts: Option<bool>,
}

pub(crate) enum ClientMsg {
    Redraw {
        cols: Option<u16>,
        rows: Option<u16>,
    },
    Sub {
        name: String,
    },
    Unsub {
        name: String,
    },
    Keys(KeysReq),
    Resize(ResizeReq),
    Scroll(ScrollReq),
    Mouse(MouseReq),
    Paste(PasteReq),
    Breadcrumb(BreadcrumbReq),
    Audio(AudioReq),
}

/// The jukebox. `selection` is absent for a volume-only update, null for silence, a station
/// id/stream key for a catalog entry, or a file/directory path for the mod's OST.
#[derive(Deserialize)]
#[serde(deny_unknown_fields)]
pub(crate) struct AudioReq {
    #[serde(default, deserialize_with = "some_option")]
    pub(crate) selection: Option<Option<AudioSelection>>,
    pub(crate) volume: f32,
}

#[derive(Deserialize)]
pub(crate) struct AudioSelection {
    #[serde(default)]
    pub(crate) station: Option<String>,
    #[serde(default)]
    pub(crate) stream: Option<String>,
    #[serde(default)]
    pub(crate) file: Option<String>,
    #[serde(default)]
    pub(crate) ncspot: bool,
}

/// Tells "the key was absent" from "the key was null", which is the difference between a
/// volume change and a stop.
fn some_option<'de, D, T>(d: D) -> Result<Option<Option<T>>, D::Error>
where
    D: serde::Deserializer<'de>,
    T: Deserialize<'de>,
{
    Option::deserialize(d).map(Some)
}

#[derive(Deserialize)]
pub(crate) struct PasteReq {
    pub(crate) name: String,
    pub(crate) text: String,
}

#[derive(Deserialize)]
pub(crate) struct BreadcrumbReq {
    pub(crate) name: String,
    pub(crate) breadcrumb: String,
    #[serde(default)]
    pub(crate) random_tips: Vec<String>,
}

#[derive(Deserialize)]
pub(crate) struct MouseReq {
    pub(crate) name: String,
    /// press | release | drag | wheelup | wheeldown
    pub(crate) action: String,
    /// 0/1/2 = left/middle/right; ignored for the wheel.
    #[serde(default)]
    pub(crate) button: u8,
    pub(crate) col: u16,
    pub(crate) row: u16,
    /// How many times to repeat the report. The mod batches wheel events so a single
    /// gesture notch does not spawn one tmux process per scrolled line.
    #[serde(default)]
    pub(crate) count: u8,
}

#[derive(Deserialize)]
pub(crate) struct ScrollReq {
    pub(crate) name: String,
    /// Lines scrolled up into scrollback; 0 returns to the live bottom.
    pub(crate) off: u32,
    /// Echoed back in the response so the mod can reject a stale reply to an older request.
    #[serde(default)]
    pub(crate) request_id: u64,
}

#[derive(Deserialize)]
pub(crate) struct KeysReq {
    pub(crate) name: String,
    /// tmux key names (Enter, C-c, Up) unless `literal`, in which case raw text.
    pub(crate) keys: Vec<String>,
    #[serde(default)]
    pub(crate) literal: bool,
    /// The game supplies loading-screen tips for `{{ random_tip }}`, already distinct: one is
    /// spent per mention, so a text with five bullets gets five different lines.
    #[serde(default)]
    pub(crate) random_tips: Vec<String>,
}

#[derive(Deserialize)]
pub(crate) struct ResizeReq {
    pub(crate) name: String,
    pub(crate) cols: u16,
    pub(crate) rows: u16,
}

#[cfg(test)]
#[path = "types_tests.rs"]
mod tests;
