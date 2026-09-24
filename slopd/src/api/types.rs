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
    /// Root callers may select a project context. Agents default to their own project.
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
    /// If present, control whether the resulting session starts.
    /// Otherwise, keep the template or override autostart value for the game editor.
    #[serde(default)]
    pub(crate) start: Option<bool>,
}

/// An errand defined in the request body instead of a saved library entry.
/// It creates the same temporary agent as `/api/library/NAME/run`.
/// `text` is optional because commands such as `less` can run without additional input.
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
    /// A preset name or command line, interpreted as in a library entry.
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
    /// The session label. `slug` converts it to the tmux session name.
    /// If empty with `host` set, the daemon generates a name. See `sandbox::host_session_name`.
    #[serde(default)]
    pub(crate) label: String,
    #[serde(default)]
    pub(crate) temp: bool,
    /// Distinct loading-screen tips supplied by the game for `{{ random_tip }}`.
    /// Each occurrence consumes one tip, so five occurrences receive five different tips.
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
    pub(crate) worktree: String,
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

/// Accept numeric and textual Boolean flags for manual shell requests.
/// For example, `?files=1` and `?files=true` have the same meaning.
/// Reject unknown values with 400 to prevent typing errors from enabling a flag.
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
    /// Include files only when requested.
    /// Directory selectors do not need file entries or their transfer costs.
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

/// A jukebox request. Omit `selection` to change only the volume. Use null to stop playback.
/// Select a catalog entry with a station ID and stream key, or select soundtrack files with a file or directory path.
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

/// Distinguish an absent selection from a null selection.
/// An absent selection preserves playback. A null selection stops playback.
fn some_option<'de, D, T>(d: D) -> Result<Option<Option<T>>, D::Error>
where
    D: serde::Deserializer<'de>,
    T: Deserialize<'de>,
{
    Option::deserialize(d).map(Some)
}

#[derive(Deserialize)]
pub(crate) struct PasteReq {
    #[serde(default)]
    pub(crate) trace_id: String,
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
    #[serde(default)]
    pub(crate) trace_id: String,
    pub(crate) name: String,
    /// press | release | drag | wheelup | wheeldown
    pub(crate) action: String,
    /// 0/1/2 = left/middle/right. Ignored for the wheel.
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
    /// Lines scrolled up into scrollback. 0 returns to the live bottom.
    pub(crate) off: u32,
    /// Echoed back in the response so the mod can reject a stale reply to an older request.
    #[serde(default)]
    pub(crate) request_id: u64,
}

#[derive(Deserialize)]
pub(crate) struct KeysReq {
    #[serde(default)]
    pub(crate) trace_id: String,
    pub(crate) name: String,
    /// tmux key names (Enter, C-c, Up) unless `literal`, in which case raw text.
    pub(crate) keys: Vec<String>,
    #[serde(default)]
    pub(crate) literal: bool,
    /// Distinct loading-screen tips supplied by the game for `{{ random_tip }}`.
    /// Each occurrence consumes one tip, so five occurrences receive five different tips.
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
