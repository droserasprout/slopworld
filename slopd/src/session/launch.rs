//! Per-launch UI context, separate from persisted agent definitions.
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
