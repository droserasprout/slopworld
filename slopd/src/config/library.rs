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
    /// A command offered for the selected path in the Files sidebar.
    FileAction,
    /// Scheduled Prompt delivery to a durable agent.
    Timer,
}

impl LibraryItemKind {
    pub(crate) const ALL: [Self; 4] = [Self::Prompt, Self::FileAction, Self::Timer, Self::Shell];

    pub(crate) fn from_config_dir(directory: &str) -> Option<Self> {
        Self::ALL
            .into_iter()
            .find(|kind| kind.config_dir() == directory)
    }

    pub fn config_dir(self) -> &'static str {
        match self {
            Self::Prompt => "prompts",
            Self::Shell => "shell_scripts",
            Self::FileAction => "file_actions",
            Self::Timer => "timers",
        }
    }
}

crate::wire_enum!(LibraryItemKind, {
    LibraryItemKind::Timer => crate::shared::protocol::enums::library_kind::TIMER,
    LibraryItemKind::Prompt => crate::shared::protocol::enums::library_kind::PROMPT,
    LibraryItemKind::Shell => crate::shared::protocol::enums::library_kind::SHELL,
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

/// Reusable prompt, shell command, or file action.
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
    /// Supplied default; editing creates a personal override.
    #[serde(default, skip_serializing_if = "is_false")]
    pub builtin: bool,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub timer: Option<TimerCfg>,
    #[serde(default, skip_deserializing, skip_serializing_if = "Option::is_none")]
    pub timer_status: Option<TimerStatus>,
}

#[derive(Debug, Clone, Serialize)]
pub struct TimerStatus {
    pub last_at: u64,
    pub next_at: u64,
    pub outcome: String,
    pub state: String,
}

/// Trigger policy is evaluated by the daemon, independently of game time.
#[derive(Debug, Clone, Default, PartialEq, Eq, Serialize, Deserialize)]
pub struct TimerCfg {
    #[serde(default)]
    pub enabled: bool,
    pub schedule: String,
    pub trigger: String,
    #[serde(default)]
    pub timezone: String,
    /// Stable identity of the selected durable agent.
    #[serde(default)]
    pub target: String,
    #[serde(default)]
    pub prompt: String,
    pub action: String,
    #[serde(default)]
    pub template: String,
    #[serde(default)]
    pub project: String,
    #[serde(default)]
    pub shell: String,
}

/// Supplied entries are merged after user entries, which may override them by name.
pub fn builtin_library_items() -> &'static [LibraryItemCfg] {
    static BUILTIN: OnceLock<Vec<LibraryItemCfg>> = OnceLock::new();
    BUILTIN.get_or_init(|| {
        vec![
            LibraryItemCfg {
                name: "Useful tips".into(),
                kind: LibraryItemKind::Prompt,
                text: "{{ random_tip }} {{ random_tip }} {{ random_tip }} \
                       {{ random_tip }} {{ random_tip }}"
                    .into(),
                builtin: true,
                ..Default::default()
            },
            LibraryItemCfg {
                name: "Magic 8 Ball".into(),
                kind: LibraryItemKind::Prompt,
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
    /// Editing creates an override; deleting an override reveals the default.
    pub fn is_builtin_library_item(&self, name: &str) -> bool {
        !self.library.iter().any(|s| s.name == name)
            && builtin_library_items().iter().any(|b| b.name == name)
    }
}

pub(crate) fn check_library_item(cfg: &Config, sc: &LibraryItemCfg) -> Result<()> {
    if sc.name.trim().is_empty() {
        bail!("Enter a name for the library item.");
    }
    if sc.kind == LibraryItemKind::Timer {
        let timer = sc
            .timer
            .as_ref()
            .ok_or_else(|| anyhow::anyhow!("Timer settings are required."))?;
        super::timer_trigger::Trigger::parse(timer)?;
        match timer.action.as_str() {
            "prompt" => {
                anyhow::ensure!(!timer.target.is_empty(), "Choose a target agent.");
                super::validate_state_id(&timer.target)?;
                check_timer_prompt(cfg, timer)?;
            }
            "worker" => {
                anyhow::ensure!(
                    !timer.template.trim().is_empty(),
                    "Choose an agent template."
                );
                anyhow::ensure!(
                    cfg.project(&timer.project).is_some(),
                    "Choose an existing project."
                );
                check_timer_prompt(cfg, timer)?;
            }
            "shell" => {
                let shell = cfg
                    .library_item(&timer.shell)
                    .filter(|item| item.kind == LibraryItemKind::Shell)
                    .ok_or_else(|| anyhow::anyhow!("Choose an existing Library shell script."))?;
                anyhow::ensure!(
                    shell.link != LibraryItemLink::Ask,
                    "Choose a saved location for the shell script."
                );
                check_library_item(cfg, &shell)?;
                anyhow::ensure!(
                    shell.host || !shell.agent_template.trim().is_empty(),
                    "Choose Host or an agent template for the shell script."
                );
            }
            _ => bail!("Choose a Timer action: Prompt, Worker, or Shell script."),
        }
        return Ok(());
    }
    anyhow::ensure!(
        sc.timer.is_none(),
        "Only Timer items may contain timer settings."
    );
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
    let reusable_prompt =
        sc.kind == LibraryItemKind::Prompt && !sc.host && sc.agent_template.trim().is_empty();
    if !reusable_prompt && sc.link == LibraryItemLink::Project && sc.project.trim().is_empty() {
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

fn check_timer_prompt(cfg: &Config, timer: &TimerCfg) -> Result<()> {
    anyhow::ensure!(
        cfg.library_item(&timer.prompt)
            .is_some_and(|p| p.kind == LibraryItemKind::Prompt),
        "Choose an existing Library Prompt."
    );
    Ok(())
}

#[cfg(test)]
#[path = "library_tests.rs"]
mod tests;
