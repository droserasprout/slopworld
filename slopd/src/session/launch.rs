//! Launch requests, execution choices and reader context, separate from saved definitions.
//! Live sessions own this context; StartPlan snapshots it and tmux metadata restores it.
use serde::Deserialize;

#[derive(Debug, Clone, Default, PartialEq, Eq, Deserialize)]
pub struct ReaderContext {
    #[serde(default, rename = "reader_path")]
    pub path: String,
    #[serde(default, rename = "reader_key")]
    pub key: String,
    #[serde(default, rename = "reader_scope")]
    pub scope: String,
    #[serde(default, rename = "reader_pinned")]
    pub pinned: bool,
    #[serde(default, rename = "reader_line")]
    pub line: u32,
}

#[derive(Debug, Clone, Default, PartialEq, Eq, Deserialize)]
pub struct LaunchContext {
    #[serde(default)]
    pub intent: String,
    #[serde(flatten)]
    pub reader: ReaderContext,
    // Derived from the action label, never accepted from a launch request.
    #[serde(skip)]
    pub original_label: String,
}

impl LaunchContext {
    pub(super) fn metadata(
        &self,
        session: &crate::config::SessionCfg,
    ) -> Option<crate::tmux::ReaderMetadata> {
        if self.intent.is_empty() {
            return None;
        }
        Some(crate::tmux::ReaderMetadata {
            intent: self.intent.clone(),
            label: session.label.clone().unwrap_or_default(),
            original_label: self.original_label.clone(),
            project: session.project.clone(),
            worktree: session.worktree.clone(),
            path: self.reader.path.clone(),
            key: self.reader.key.clone(),
            scope: self.reader.scope.clone(),
            pinned: self.reader.pinned,
            line: self.reader.line,
        })
    }
}

impl From<&crate::tmux::ReaderMetadata> for LaunchContext {
    fn from(value: &crate::tmux::ReaderMetadata) -> Self {
        Self {
            intent: value.intent.clone(),
            original_label: value.original_label.clone(),
            reader: ReaderContext {
                path: value.path.clone(),
                key: value.key.clone(),
                scope: value.scope.clone(),
                pinned: value.pinned,
                line: value.line,
            },
        }
    }
}

/// Launch overrides supplied by the caller.
#[derive(Debug, Clone, Default, Deserialize)]
pub struct RunWhere {
    #[serde(default)]
    pub worktree: String,
    #[serde(flatten)]
    pub context: LaunchContext,
    pub cols: Option<u16>,
    pub rows: Option<u16>,
    #[serde(default)]
    pub project: Option<String>,
    #[serde(default)]
    pub temp: bool,
    #[serde(default)]
    pub random_tips: Vec<String>,
}

#[cfg(test)]
#[path = "launch_tests.rs"]
mod tests;

/// Execution authority is chosen once, before allocating a terminal or workspace.
#[derive(Debug, Clone, PartialEq, Eq)]
pub(crate) enum ExecutionSource {
    Host(HostLifetime),
    Template(String),
    CloneAgent(String),
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub(crate) enum HostLifetime {
    Disposable,
    Persistent,
}

impl ExecutionSource {
    pub(super) fn select(
        host: bool,
        persistent: bool,
        template: &str,
        like: &str,
    ) -> anyhow::Result<Self> {
        let template = template.trim();
        if host && (!template.is_empty() || !like.is_empty()) {
            anyhow::bail!("choose host execution or agent settings, not both");
        }
        if !template.is_empty() && !like.is_empty() {
            anyhow::bail!("choose an agent template or a source agent, not both");
        }
        Ok(if host {
            Self::Host(if persistent {
                HostLifetime::Persistent
            } else {
                HostLifetime::Disposable
            })
        } else if !template.is_empty() {
            Self::Template(template.to_owned())
        } else if !like.is_empty() {
            Self::CloneAgent(like.to_owned())
        } else {
            anyhow::bail!("choose Host or an agent template for this errand before running it");
        })
    }
    pub(super) fn is_host(&self) -> bool {
        matches!(self, Self::Host(_))
    }
    pub(super) fn persistent_host(&self) -> bool {
        matches!(self, Self::Host(HostLifetime::Persistent))
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub(crate) enum LaunchDestination {
    Temporary,
    Project(String),
}

/// Transient content has no catalog identity, file-action mode or storage ownership.
#[derive(Debug, Clone, Default)]
pub(crate) struct LaunchContent {
    pub name: String,
    pub kind: crate::config::LibraryItemKind,
    pub command: Option<String>,
    pub text: String,
}

impl LaunchContent {
    pub(super) fn validate(&self) -> anyhow::Result<()> {
        if matches!(
            self.kind,
            crate::config::LibraryItemKind::Breadcrumb | crate::config::LibraryItemKind::FileAction
        ) {
            anyhow::bail!(
                "library item {} is not runnable as an agent errand",
                self.name
            );
        }
        Ok(())
    }
}

#[derive(Debug, Clone, Default)]
pub(crate) struct LaunchOptions {
    pub worktree: String,
    pub context: LaunchContext,
    pub cols: Option<u16>,
    pub rows: Option<u16>,
    pub random_tips: Vec<String>,
}

impl From<RunWhere> for LaunchOptions {
    fn from(value: RunWhere) -> Self {
        Self {
            worktree: value.worktree,
            context: value.context,
            cols: value.cols,
            rows: value.rows,
            random_tips: value.random_tips,
        }
    }
}

/// Common input to guarded session creation. Saved entries and explicit runs adapt here.
#[derive(Debug, Clone)]
pub(crate) struct LaunchRequest {
    pub content: LaunchContent,
    pub execution: ExecutionSource,
    pub destination: LaunchDestination,
    pub options: LaunchOptions,
}

impl LaunchRequest {
    pub(crate) fn library(
        item: crate::config::LibraryItemCfg,
        want: RunWhere,
    ) -> anyhow::Result<Self> {
        let execution = ExecutionSource::select(item.host, false, &item.agent_template, "")?;
        Self::from_library(item, want, execution)
    }

    pub(super) fn from_library(
        item: crate::config::LibraryItemCfg,
        want: RunWhere,
        execution: ExecutionSource,
    ) -> anyhow::Result<Self> {
        use crate::config::LibraryItemLink;
        let content = LaunchContent {
            name: item.name,
            kind: item.kind,
            command: item.command,
            text: item.text,
        };
        content.validate()?;
        let asked = want
            .project
            .as_deref()
            .map(str::trim)
            .filter(|s| !s.is_empty());
        let destination = if want.temp || (asked.is_none() && item.link == LibraryItemLink::Temp) {
            LaunchDestination::Temporary
        } else if let Some(project) = asked {
            LaunchDestination::Project(project.to_owned())
        } else if item.link == LibraryItemLink::Ask {
            anyhow::bail!(
                "Choose a project or request a temporary project for library item {:?}.",
                content.name
            );
        } else {
            LaunchDestination::Project(item.project)
        };
        Ok(Self {
            content,
            execution,
            destination,
            options: want.into(),
        })
    }
}

/// An errand defined in the request body instead of a saved library entry.
/// It creates the same temporary agent as `/api/library/NAME/run`.
/// `text` is optional because commands such as `less` can run without additional input.
#[derive(Deserialize)]
pub(crate) struct ExplicitLaunch {
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
    pub(crate) intent: String,
    #[serde(default)]
    pub(crate) reader: ExplicitReader,
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

/// Optional source metadata for a terminal reader.
#[derive(Default, Deserialize)]
#[serde(default)]
pub(crate) struct ExplicitReader {
    pub(crate) path: String,
    pub(crate) key: String,
    pub(crate) scope: String,
    pub(crate) line: u32,
    pub(crate) pinned: bool,
}
