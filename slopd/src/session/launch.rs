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
    pub(crate) fn persistent_host(&self) -> bool {
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
    pub(crate) fn validate(&self) -> anyhow::Result<()> {
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
