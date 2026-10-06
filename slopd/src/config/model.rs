//! Assembled configuration read model and workspace record types.
//! settings owns machine settings; legacy owns temporary inline persistence.

use super::{Settings, is_false, is_true, library::LibraryItemCfg, sandbox::*, yes};
use crate::presets::{CommandPreset, SandboxPreset};
use serde::{Deserialize, Serialize};

/// Loaded configuration, including the separately stored library catalog.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct Config {
    #[serde(flatten)]
    pub settings: Settings,
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
}

// Field access remains convenient for effective configuration consumers. Persistence
// chooses Settings explicitly; the assembled view does not own a root document.
impl std::ops::Deref for Config {
    type Target = Settings;
    fn deref(&self) -> &Settings {
        &self.settings
    }
}

impl std::ops::DerefMut for Config {
    fn deref_mut(&mut self) -> &mut Settings {
        &mut self.settings
    }
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
#[derive(Clone, Serialize, Deserialize)]
pub struct SessionCfg {
    // Identity and workspace.
    /// Empty selects the original project checkout.
    #[serde(default, skip_serializing_if = "String::is_empty")]
    pub worktree: String,
    pub name: String,
    /// A non-empty manual sidebar label disables automatic title summaries for this agent.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub label: Option<String>,
    /// UI purpose and reader identity survive while a temporary tmux session is adopted.
    #[serde(skip)]
    pub intent: String,
    #[serde(skip)]
    pub reader_label: String,
    #[serde(skip)]
    pub reader_path: String,
    #[serde(skip)]
    pub reader_key: String,
    #[serde(skip)]
    pub reader_scope: String,
    #[serde(skip)]
    pub reader_pinned: bool,
    #[serde(skip)]
    pub reader_line: u32,
    /// Stable private-state identity, independent of renames or reused session names.
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
    /// Extra shell-quoted arguments appended to the resolved command line.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub args: Option<String>,
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
    /// Stable storage identity; legacy inline shells receive one during migration.
    #[serde(default, skip_serializing_if = "String::is_empty")]
    pub id: String,
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
            id: String::new(),
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
            intent: String::new(),
            reader_label: String::new(),
            reader_path: String::new(),
            reader_key: String::new(),
            reader_scope: String::new(),
            reader_pinned: false,
            reader_line: 0,
            state_id: uuid::Uuid::new_v4().to_string(),
            project: String::new(),
            command: String::new(),
            cmd: None,
            args: None,
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

impl std::fmt::Debug for SessionCfg {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("SessionCfg")
            .field("worktree", &self.worktree)
            .field("name", &self.name)
            .field("label", &self.label)
            .field("intent", &self.intent)
            .field("reader_label", &self.reader_label)
            .field("reader_path", &self.reader_path)
            .field("reader_key", &self.reader_key)
            .field("reader_scope", &self.reader_scope)
            .field("reader_pinned", &self.reader_pinned)
            .field("reader_line", &self.reader_line)
            .field("state_id", &self.state_id)
            .field("project", &self.project)
            .field("command", &self.command)
            .field("cmd", &self.cmd)
            .field("args", &self.args)
            .field("command_snapshot", &self.command_snapshot)
            .field("sandbox", &self.sandbox)
            .field("sandbox_snapshots", &self.sandbox_snapshots)
            .field("persistent_tmp", &self.persistent_tmp)
            .field("network", &self.network)
            .field("dns", &self.dns)
            .field("limits", &self.limits)
            .field("autostart", &self.autostart)
            .field("auto_resume", &self.auto_resume)
            .field("worker", &self.worker)
            .field("parent", &self.parent)
            .field("task_id", &self.task_id)
            .field("worker_token", &super::TOKEN_REDACTED)
            .finish()
    }
}
