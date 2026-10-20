//! Library entries, supplied content, and catalog lookups.

use super::{Config, is_false};
use anyhow::{Result, bail};
use serde::{Deserialize, Serialize};
use std::sync::OnceLock;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum LibraryItemKind {
    #[default]
    Prompt,
    /// Handed to an interactive shell inside the project's sandbox.
    Shell,
    /// Reusable guidance inserted into a prompt by the user.
    Breadcrumb,
    /// A command offered for the selected path in the Files sidebar.
    FileAction,
}

impl LibraryItemKind {
    pub fn config_dir(self) -> &'static str {
        match self {
            Self::Prompt => "prompts",
            Self::Shell => "shell_scripts",
            Self::Breadcrumb => "breadcrumbs",
            Self::FileAction => "file_actions",
        }
    }
}

crate::wire_enum!(LibraryItemKind, {
    LibraryItemKind::Prompt => crate::shared::protocol::enums::library_kind::PROMPT,
    LibraryItemKind::Shell => crate::shared::protocol::enums::library_kind::SHELL,
    LibraryItemKind::Breadcrumb => crate::shared::protocol::enums::library_kind::BREADCRUMB,
    LibraryItemKind::FileAction => crate::shared::protocol::enums::library_kind::FA,
});

/// How a library item selects its project for each run.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum LibraryItemLink {
    /// Use the configured project when an entry does not specify a link mode.
    #[default]
    Project,
    /// Create a temporary workspace, optionally based on the named project.
    Temp,
    /// Let the caller select the project for each run.
    Ask,
}

crate::wire_enum!(LibraryItemLink, {
    LibraryItemLink::Project => crate::shared::protocol::enums::library_link::PROJECT,
    LibraryItemLink::Temp => crate::shared::protocol::enums::library_link::TEMP,
    LibraryItemLink::Ask => crate::shared::protocol::enums::library_link::ASK,
});

/// What the Files sidebar does after a file action is selected. `Ask` is the default mode.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum FileActionMode {
    #[default]
    Ask,
    ShowResult,
    OpenTerminal,
    Nothing,
}

crate::wire_enum!(FileActionMode, {
    FileActionMode::Ask => crate::shared::protocol::enums::file_action_mode::ASK,
    FileActionMode::ShowResult => crate::shared::protocol::enums::file_action_mode::SHOW_RESULT,
    FileActionMode::OpenTerminal => crate::shared::protocol::enums::file_action_mode::OPEN_TERMINAL,
    FileActionMode::Nothing => crate::shared::protocol::enums::file_action_mode::NOTHING,
});

/// Reusable prompt, breadcrumb, shell command, or file action.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct LibraryItemCfg {
    // Display identity and workspace selection.
    /// Button label, also used to derive a new session name.
    pub name: String,
    #[serde(default)]
    pub kind: LibraryItemKind,
    pub link: LibraryItemLink,
    /// Selected project, or the base project for a temporary workspace.
    #[serde(default)]
    pub project: String,

    // Content and execution target.
    /// Prompt text or a shell command line.
    #[serde(default)]
    pub text: String,
    /// Empty means `[defaults] agent` or `[defaults] shell`.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub command: Option<String>,
    /// Runnable entries explicitly choose host execution or a portable agent template.
    /// An entry without either selection cannot run.
    #[serde(default, skip_serializing_if = "is_false")]
    pub host: bool,
    #[serde(default, skip_serializing_if = "String::is_empty")]
    pub agent_template: String,

    // File-action behavior and catalog ownership.
    /// `ask` prompts for an action on each invocation.
    #[serde(default, skip_serializing_if = "is_file_action_mode_default")]
    pub mode: FileActionMode,
    /// Supplied by the daemon; clients cannot edit these entries.
    #[serde(default, skip_serializing_if = "is_false")]
    pub builtin: bool,
}

/// Supplied entries are merged after user entries, which may override them by name.
pub fn builtin_library_items() -> &'static [LibraryItemCfg] {
    static BUILTIN: OnceLock<Vec<LibraryItemCfg>> = OnceLock::new();
    BUILTIN.get_or_init(|| {
        vec![
            LibraryItemCfg {
                name: "Useful tips".into(),
                kind: LibraryItemKind::Breadcrumb,
                text: "{{ random_tip }} {{ random_tip }} {{ random_tip }} \
                       {{ random_tip }} {{ random_tip }}"
                    .into(),
                builtin: true,
                ..Default::default()
            },
            LibraryItemCfg {
                name: "Magic 8 Ball".into(),
                kind: LibraryItemKind::Breadcrumb,
                text: "{{ magic_8_ball }}".into(),
                builtin: true,
                ..Default::default()
            },
        ]
    })
}

fn is_file_action_mode_default(mode: &FileActionMode) -> bool {
    *mode == FileActionMode::Ask
}

impl Config {
    /// File entries take precedence over built-in entries with the same name.
    pub fn library_item(&self, name: &str) -> Option<LibraryItemCfg> {
        self.library
            .iter()
            .chain(builtin_library_items())
            .find(|s| s.name == name)
            .cloned()
    }

    /// Return file entries, then built-in entries whose names do not occur in the file.
    pub fn library_items_all(&self) -> Vec<LibraryItemCfg> {
        let mut all = self.library.clone();
        all.extend(
            builtin_library_items()
                .iter()
                .filter(|b| !self.library.iter().any(|s| s.name == b.name))
                .cloned(),
        );
        all
    }

    /// Check whether this name identifies a built-in entry without a file override.
    /// Clients cannot edit or delete these entries.
    pub fn is_builtin_library_item(&self, name: &str) -> bool {
        !self.library.iter().any(|s| s.name == name)
            && builtin_library_items().iter().any(|b| b.name == name)
    }
}

pub(crate) fn check_library_item(cfg: &Config, sc: &LibraryItemCfg) -> Result<()> {
    if sc.name.trim().is_empty() {
        bail!("Enter a name for the library item.");
    }
    if sc.kind == LibraryItemKind::Breadcrumb {
        if sc.text.trim().is_empty() {
            bail!("Enter text for breadcrumb {:?}.", sc.name);
        }
        return Ok(());
    }
    if sc.host && !sc.agent_template.trim().is_empty() {
        bail!(
            "Choose either Host or an agent template for library item {:?}.",
            sc.name
        );
    }
    if sc.kind == LibraryItemKind::FileAction {
        if sc
            .command
            .as_deref()
            .map(str::trim)
            .unwrap_or("")
            .is_empty()
        {
            bail!("Enter a command for file action {:?}.", sc.name);
        }
        if !sc.text.trim().is_empty() {
            bail!("Remove text from file action {:?}.", sc.name);
        }
        return Ok(());
    }
    if sc.link == LibraryItemLink::Project && sc.project.trim().is_empty() {
        bail!("Choose a project for library item {:?}.", sc.name);
    }
    if !sc.project.trim().is_empty() && cfg.project(&sc.project).is_none() {
        bail!("Project {:?} does not exist.", sc.project);
    }
    if sc.text.trim().is_empty() {
        bail!("Enter text for library item {:?}.", sc.name);
    }
    Ok(())
}

#[cfg(test)]
#[path = "library_tests.rs"]
mod tests;
