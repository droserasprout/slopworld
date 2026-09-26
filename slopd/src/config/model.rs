//! Root configuration and persistent project, agent, and host-terminal records.

use super::{daemon::*, is_false, is_true, library::LibraryItemCfg, sandbox::*, yes};
use crate::presets::{CommandPreset, SandboxPreset};
use serde::{Deserialize, Serialize};

/// Loaded configuration, including the separately stored library catalog.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct Config {
    #[serde(default)]
    pub daemon: Daemon,
    #[serde(default)]
    pub defaults: Defaults,
    /// Host applications used by file actions.
    #[serde(default)]
    pub commands: CommandDefaults,
    // Persistent workspace and process records.
    #[serde(default, rename = "project")]
    pub projects: Vec<ProjectCfg>,
    #[serde(default, rename = "session")]
    pub sessions: Vec<SessionCfg>,
    /// Loaded from per-kind catalog files; omitted from the main configuration document.
    #[serde(default, rename = "library", skip_serializing)]
    pub library: Vec<LibraryItemCfg>,
    /// Host shells have a separate creation route and cannot become agent sessions.
    #[serde(
        default,
        rename = "host_terminal",
        skip_serializing_if = "Vec::is_empty"
    )]
    pub host_terminals: Vec<HostTerminalCfg>,
    #[serde(default, rename = "state_rule")]
    pub state_rules: Vec<StateRule>,
}

/// A project owns the paths it exposes to its agents. It includes the primary directory implicitly.
/// A mount with that directory as both source and destination changes its mode.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct ProjectCfg {
    #[serde(default, skip_serializing_if = "String::is_empty")]
    pub id: String,
    /// Empty uses `<dir>/.worktrees`; explicit roots contain project-name subdirectories.
    #[serde(default, skip_serializing_if = "String::is_empty")]
    pub worktree_root: String,
    pub name: String,
    /// Required unless this is a temporary project.
    #[serde(default)]
    pub dir: String,
    /// Derive a temporary path from the name; create it on first launch. The record persists.
    /// Library errands instead use temporary workspaces without stored project records.
    #[serde(default)]
    pub temp: bool,
    /// Literal host/sandbox path pairs. Project shortcuts copy paths once, never references.
    #[serde(default)]
    pub mounts: Vec<Mount>,
}

/// An agent is a command preset plus its process settings. Workspace mounts belong to the project
/// so every agent in the same project starts from the same directory view.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct SessionCfg {
    // Identity and workspace.
    /// Empty selects the original project checkout.
    #[serde(default, skip_serializing_if = "String::is_empty")]
    pub worktree: String,
    pub name: String,
    /// A non-empty manual sidebar label disables automatic title summaries for this agent.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub label: Option<String>,
    /// Stable private-state UUID, independent of renames or reused session names.
    #[serde(default, skip_serializing_if = "String::is_empty")]
    pub state_id: String,
    /// The project supplies the workspace and shared mounts.
    #[serde(default)]
    pub project: String,

    // Launch command and captured template definition.
    /// Empty selects `[defaults] agent` unless `cmd` overrides it.
    #[serde(default)]
    pub command: String,
    /// An explicit command line that overrides the preset command.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub cmd: Option<String>,
    /// Captured from the template at creation; preserved during ordinary edits.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub(crate) command_snapshot: Option<CommandPreset>,

    // Sandbox additions, storage, and resource policy.
    /// Sandbox presets added to the command's presets.
    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    pub sandbox: Vec<String>,
    /// Captured template presets take precedence over the live catalog.
    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    pub(crate) sandbox_snapshots: Vec<SandboxPreset>,
    /// Give this agent a durable, private `/tmp` instead of the sandbox's per-run tmpfs.
    #[serde(default, skip_serializing_if = "is_false")]
    pub persistent_tmp: bool,
    /// Explicit network policy; omitted values use the agent default.
    #[serde(default)]
    pub network: NetworkMode,
    /// DNS for this agent. `resolved` retains the daemon's current resolver at launch.
    #[serde(default)]
    pub dns: DnsConfig,
    /// This agent's final resource caps. An unset field means no configured cap.
    #[serde(default, skip_serializing_if = "Limits::is_empty")]
    pub limits: Limits,

    // Startup behavior.
    #[serde(default)]
    pub autostart: bool,
    /// After a fresh process reaches its first settled prompt, select its latest conversation.
    #[serde(default)]
    pub auto_resume: bool,

    // Worker identity, set only by delegated-task creation.
    /// Ordinary session creation clears worker metadata.
    #[serde(default, skip_serializing_if = "is_false")]
    pub worker: bool,
    #[serde(default, skip_serializing_if = "String::is_empty")]
    pub parent: String,
    #[serde(default, skip_serializing_if = "String::is_empty")]
    pub task_id: String,
    /// Per-run worker credential; never persisted or exposed in session views.
    #[serde(skip)]
    pub(crate) worker_token: Option<String>,
}

/// Persistent host shell, restored independently of agent sessions.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct HostTerminalCfg {
    pub name: String,
    /// A fixed sidebar label. Empty means the terminal application's title is shown.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub label: Option<String>,
    #[serde(default)]
    pub project: String,
    /// Last directory reported by tmux; preserves navigation across daemon restarts.
    #[serde(default)]
    pub path: String,
    /// Recreate the tmux shell when slopd starts after the machine has rebooted.
    #[serde(default = "yes", skip_serializing_if = "is_true")]
    pub autostart: bool,
}

impl Default for HostTerminalCfg {
    fn default() -> Self {
        Self {
            name: String::new(),
            label: None,
            project: String::new(),
            path: String::new(),
            autostart: true,
        }
    }
}

impl Default for SessionCfg {
    fn default() -> Self {
        Self {
            worktree: String::new(),
            name: String::new(),
            label: None,
            state_id: uuid::Uuid::new_v4().to_string(),
            project: String::new(),
            command: String::new(),
            cmd: None,
            command_snapshot: None,
            sandbox: Vec::new(),
            sandbox_snapshots: Vec::new(),
            persistent_tmp: false,
            network: NetworkMode::default(),
            dns: DnsConfig::default(),
            limits: Limits::default(),
            autostart: false,
            auto_resume: false,
            worker: false,
            parent: String::new(),
            task_id: String::new(),
            worker_token: None,
        }
    }
}

/// Ordered: first match wins. Shipped defaults target Claude Code's TUI.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct StateRule {
    pub state: String,
    pub pattern: String,
}
