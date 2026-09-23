//! Configuration data types and their field-level defaults.

use std::collections::{BTreeMap, BTreeSet};
use std::net::Ipv4Addr;
use std::sync::OnceLock;

use anyhow::{bail, Result};
use serde::{Deserialize, Serialize};

use crate::presets::{CommandPreset, SandboxPreset};

// These constants define daemon policy. Keep them beside the Rust modules that apply them.
// Clients obtain effective values and factory defaults through API read models.
pub const DEFAULT_BIND: &str = "127.0.0.1:7717";
pub const DEFAULT_USAGE_POLL_SECS: u64 = 60;
pub const DEFAULT_CLAUDE_CREDENTIALS: &str = "~/.claude/.credentials.json";
pub const DEFAULT_OPENAI_CREDENTIALS: &str = "~/.codex/auth.json";
pub const DEFAULT_TITLE_MODEL: &str = "google/gemini-3.1-flash-lite";
pub const DEFAULT_TITLE_MIN_CHARS: usize = 0;
pub const DEFAULT_SUMMARY_PROMPT: &str = "Summarise this prompt in at most 6 words for a session title. Reply with only the title in sentence case, without quotes, punctuation, or commentary. If prompt is too short to summarize - return it verbatim.";

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct Config {
    #[serde(default)]
    pub daemon: Daemon,
    #[serde(default)]
    pub defaults: Defaults,
    /// Commands the client uses for file viewers, editors, syntax highlighting and links.
    #[serde(default)]
    pub commands: CommandDefaults,
    /// Projects supply each agent session's directory and shared mounts.
    /// The agent owns its process settings.
    #[serde(default, rename = "project")]
    pub projects: Vec<ProjectCfg>,
    #[serde(default, rename = "session")]
    pub sessions: Vec<SessionCfg>,
    /// Library entries include errands that temporary agents run for the duration of a process.
    /// The in-memory configuration model keeps this field for library resolution.
    /// Separate catalog files under `prompts/`, `breadcrumbs/`, `file_actions/`, and `shell_scripts/` store the entries.
    /// The main configuration document does not store them.
    #[serde(default, rename = "library", skip_serializing)]
    pub library: Vec<LibraryItemCfg>,
    /// Host shells opened from a project heading. These records are separate from `session`.
    /// Only the explicit host-shell route permits a host terminal.
    /// A configuration edit cannot convert a host terminal to an agent.
    #[serde(
        default,
        rename = "host_terminal",
        skip_serializing_if = "Vec::is_empty"
    )]
    pub host_terminals: Vec<HostTerminalCfg>,
    #[serde(default, rename = "state_rule")]
    pub state_rules: Vec<StateRule>,
}

/// Protocol sentinel for a redacted token. Writing this value preserves the token.
/// Writing a new value or an empty string changes the token.
pub const TOKEN_REDACTED: &str = "<redacted>";

// These are daemon identity and scheduling policy, not user configuration. The private tmux
// name is part of the sandbox/debug contract.
pub const SCROLLBACK_LINES: u32 = 10_000;

/// The private tmux socket name (`tmux -L <name>`). The default is `slopworld`.
/// `SLOPD_TMUX_SOCKET` lets a temporary daemon use a separate tmux server.
/// Production leaves this variable unset. Read and cache the name once because it identifies the daemon.
pub fn tmux_socket() -> &'static str {
    static SOCKET: OnceLock<String> = OnceLock::new();
    SOCKET.get_or_init(|| {
        std::env::var("SLOPD_TMUX_SOCKET")
            .ok()
            .map(|s| s.trim().to_string())
            .filter(|s| !s.is_empty())
            .unwrap_or_else(|| "slopworld".to_string())
    })
}

/// Redact a nonempty `[daemon] token` in configuration text. Preserve comments and blank lines.
/// `Manager::replace_config` restores the real value when a client writes the sentinel.
pub fn redact_token_text(text: &str) -> String {
    let mut out = String::with_capacity(text.len() + TOKEN_REDACTED.len());
    let mut in_daemon = false;
    for line in text.lines() {
        let t = line.trim_start();
        if t.starts_with('[') {
            // A different table, such as `[daemon.x]`, ends the daemon section.
            in_daemon = t.starts_with("[daemon]");
        }
        let redacted = in_daemon
            .then(|| {
                t.strip_prefix("token")
                    .and_then(|r| r.trim_start().strip_prefix('='))
            })
            .flatten()
            .map(str::trim)
            .filter(|v| !v.is_empty() && *v != "\"\"")
            .map(|_| {
                let indent = &line[..line.len() - t.len()];
                format!("{indent}token = \"{TOKEN_REDACTED}\"")
            });
        out.push_str(redacted.as_deref().unwrap_or(line));
        out.push('\n');
    }
    out
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Daemon {
    pub bind: String,
    /// An empty token disables authentication.
    /// `GET /api/config` replaces a nonempty token with `TOKEN_REDACTED`.
    /// Writing the sentinel preserves the token. See `redact_token_text` and `Manager::replace_config`.
    #[serde(default)]
    pub token: String,
    /// Poll interval for usage windows that change over minutes.
    /// Most providers have a minimum interval of 10 seconds.
    /// Anthropic has a minimum interval of five minutes because its OAuth usage endpoint limits requests per account.
    #[serde(default = "default_usage_poll")]
    pub usage_poll_secs: u64,
    /// Usage settings for each window. An explicit `interval_secs` overrides only the global interval.
    /// Source defaults apply until the source has configured rows.
    #[serde(default)]
    pub usage_items: BTreeMap<String, UsageItem>,
    /// Read the file each time to receive credential updates. Do not copy it.
    #[serde(default = "default_credentials")]
    pub claude_credentials: String,
    /// An empty path selects `OPENROUTER_API_KEY` from the slopd environment.
    /// Otherwise, read the file for each request and trim whitespace from its contents.
    /// Do not log or write either secret.
    #[serde(default)]
    pub openrouter_key_file: String,
    /// Codex stores its ChatGPT access token here.
    /// Read the file each time, as for Claude credentials.
    /// Do not copy the credentials or send them through the daemon API.
    #[serde(default = "default_openai_credentials")]
    pub openai_credentials: String,
    /// Automatic task titles are opt-in because a title request sends part of a prompt to
    /// OpenRouter. `once` names the first real prompt in each Codex conversation.
    #[serde(default)]
    pub agent_titles: TitlePolicy,
    /// The OpenRouter model used for automatic agent prompt titles.
    #[serde(default = "default_title_model")]
    pub title_model: String,
    /// Instruction prepended to prompts sent to OpenRouter for session and task summaries.
    #[serde(default = "default_summary_prompt")]
    pub summary_prompt: String,
    /// Do not request an external title for prompts shorter than this limit.
    /// Count Unicode characters so the setting does not depend on UTF-8 byte width.
    #[serde(default = "default_title_min_chars")]
    pub title_min_chars: usize,
    /// Pi uses daemon title generation. Its default remains one title update for each prompt.
    #[serde(default = "default_pi_title_policy")]
    pub pi_titles: TitlePolicy,
    /// Each delegated task is a separate conversation.
    /// `once` summarizes each task body at most once. `never` uses the local sidebar preview.
    #[serde(default)]
    pub task_summaries: TitlePolicy,
    /// Prompt delivered to a newly spawned task worker.
    #[serde(default)]
    pub instructions: InstructionsCfg,
    /// Agent templates permitted as task worker sources.
    /// Keep this policy separate from the template catalog.
    /// Editing or deleting a template definition does not change an existing worker.
    #[serde(default, skip_serializing_if = "BTreeSet::is_empty")]
    pub worker_templates: BTreeSet<String>,
}

/// Settings for the prompt delivered to a new task worker.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct InstructionsCfg {
    /// Prompt submitted to a newly spawned task worker before it retrieves its mailbox task.
    #[serde(default = "default_worker_prompt")]
    pub worker_prompt: String,
}

pub const DEFAULT_WORKER_PROMPT: &str = "You are a SlopWorld worker. Run `slopctl task show` once, then `slopctl task accept`. Use `slopctl task progress` while working and conclude with `slopctl task finish` or `slopctl task fail`. Do not search the task list or poll task status.\n\nWorker task: use `slopctl task show`, then `task accept`, `task progress`, and finally `task finish` or `task fail`. Do not search the task list or poll task status.";

fn default_worker_prompt() -> String {
    DEFAULT_WORKER_PROMPT.into()
}

impl Default for InstructionsCfg {
    fn default() -> Self {
        Self {
            worker_prompt: default_worker_prompt(),
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
pub struct UsageItem {
    /// Whether this individual usage window should be fetched and displayed.
    #[serde(default = "yes")]
    pub poll: bool,
    /// Empty in the UI is represented by None (or zero from a patch), meaning
    /// `daemon.usage_poll_secs`, subject to the provider's minimum poll interval.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub interval_secs: Option<u64>,
}

#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum TitlePolicy {
    #[default]
    Never,
    Once,
    Always,
}

fn default_usage_poll() -> u64 {
    DEFAULT_USAGE_POLL_SECS
}

fn default_credentials() -> String {
    DEFAULT_CLAUDE_CREDENTIALS.into()
}

fn default_openai_credentials() -> String {
    DEFAULT_OPENAI_CREDENTIALS.into()
}

fn default_title_model() -> String {
    DEFAULT_TITLE_MODEL.into()
}

fn default_summary_prompt() -> String {
    DEFAULT_SUMMARY_PROMPT.into()
}

fn default_title_min_chars() -> usize {
    DEFAULT_TITLE_MIN_CHARS
}

fn default_pi_title_policy() -> TitlePolicy {
    TitlePolicy::Always
}

impl Default for Daemon {
    fn default() -> Self {
        Self {
            bind: DEFAULT_BIND.into(),
            token: String::new(),
            usage_poll_secs: default_usage_poll(),
            usage_items: BTreeMap::new(),
            claude_credentials: default_credentials(),
            openrouter_key_file: String::new(),
            openai_credentials: default_openai_credentials(),
            agent_titles: TitlePolicy::Never,
            title_model: default_title_model(),
            summary_prompt: default_summary_prompt(),
            title_min_chars: default_title_min_chars(),
            pi_titles: default_pi_title_policy(),
            task_summaries: TitlePolicy::Never,
            instructions: InstructionsCfg::default(),
            worker_templates: BTreeSet::new(),
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Defaults {
    /// The default command preset for agents without an explicit command.
    pub agent: String,
    /// The shell agents should advertise to tools that run commands inside the sandbox.
    #[serde(default = "default_agent_shell")]
    pub agent_shell: String,
    /// The default shell for shell errands.
    /// Store this machine setting here so each library entry does not need to specify it.
    #[serde(default = "default_shell")]
    pub shell: String,
}

pub(crate) fn default_agent() -> String {
    "claude".into()
}

fn default_agent_shell() -> String {
    "bash".into()
}

fn default_shell() -> String {
    "bash".into()
}

impl Default for Defaults {
    fn default() -> Self {
        Self {
            agent: default_agent(),
            agent_shell: default_agent_shell(),
            shell: default_shell(),
        }
    }
}

/// Host applications that the mod uses for temporary file actions.
/// Split command templates into arguments without a shell.
/// The client expands `{file}` and `{line}` where supported.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct CommandDefaults {
    /// Pager command. The client appends the file unless `{file}` is present.
    #[serde(default = "default_pager")]
    pub pager: String,
    /// Editor command. The client appends the file unless `{file}` is present.
    #[serde(default = "default_editor")]
    pub editor: String,
    /// Command used by less's LESSOPEN hook. `%s` is replaced by less with the file path.
    #[serde(default = "default_highlighter")]
    pub highlighter: String,
}

fn default_pager() -> String {
    "less".into()
}

fn default_editor() -> String {
    "micro".into()
}

fn default_highlighter() -> String {
    "highlight --out-format=xterm256".into()
}

impl Default for CommandDefaults {
    fn default() -> Self {
        Self {
            pager: default_pager(),
            editor: default_editor(),
            highlighter: default_highlighter(),
        }
    }
}

/// Store temporary work under `/tmp` so the host controls its removal.
pub const TEMP_ROOT: &str = "/tmp/slopworld";

/// Generate the temporary directory path from the project name.
pub fn temp_dir(name: &str) -> String {
    format!("{TEMP_ROOT}/{}", temp_slug(name))
}

/// The daemon uses the same normalized temporary project path for previews and creation.
/// Keep normalization here for consistent project creation, renaming, and temporary errands.
pub fn temp_slug(name: &str) -> String {
    let mut out = String::with_capacity(name.len());
    for ch in name.trim().chars() {
        if ch.is_whitespace() || matches!(ch, ':' | '.' | '/') {
            if !out.ends_with('-') {
                out.push('-');
            }
        } else {
            out.push(ch);
        }
    }
    let out = out.trim_matches('-');
    if out.is_empty() {
        "library".into()
    } else {
        out.to_string()
    }
}

/// The network an agent sandbox may use.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub enum NetworkMode {
    None,
    #[default]
    Private,
    Host,
}

crate::wire_enum!(NetworkMode, {
    NetworkMode::None => crate::shared::protocol::enums::network_mode::NONE,
    NetworkMode::Private => crate::shared::protocol::enums::network_mode::PRIVATE,
    NetworkMode::Host => crate::shared::protocol::enums::network_mode::HOST,
});

/// Access and storage policy for a project mount.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub enum MountMode {
    Ro,
    #[default]
    Rw,
    Cache,
}

crate::wire_enum!(MountMode, {
    MountMode::Ro => crate::shared::protocol::enums::mount_mode::RO,
    MountMode::Rw => crate::shared::protocol::enums::mount_mode::RW,
    MountMode::Cache => crate::shared::protocol::enums::mount_mode::CACHE,
});

/// A literal host path bound at an explicit sandbox path. Project shortcuts copy these values.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct Mount {
    #[serde(default)]
    pub from: String,
    pub to: String,
    #[serde(default)]
    pub mode: MountMode,
}

/// DNS resolution for private and host-mode sandboxes.
/// The default uses the daemon's current `/etc/resolv.conf`, including the Docker resolver in slopcar.
/// Explicit servers override the system resolver for a machine or project.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub enum DnsConfig {
    #[default]
    Resolved,
    Servers {
        servers: Vec<Ipv4Addr>,
    },
}

#[derive(Deserialize)]
struct DnsWire {
    mode: String,
    #[serde(default)]
    servers: Vec<Ipv4Addr>,
}

impl Serialize for DnsConfig {
    fn serialize<S>(&self, serializer: S) -> std::result::Result<S::Ok, S::Error>
    where
        S: serde::Serializer,
    {
        use serde::ser::SerializeStruct;

        match self {
            Self::Resolved => {
                let mut out = serializer.serialize_struct("DnsConfig", 1)?;
                out.serialize_field("mode", crate::shared::protocol::enums::dns_mode::RESOLVED)?;
                out.end()
            }
            Self::Servers { servers } => {
                let mut out = serializer.serialize_struct("DnsConfig", 2)?;
                out.serialize_field("mode", crate::shared::protocol::enums::dns_mode::SERVERS)?;
                out.serialize_field("servers", servers)?;
                out.end()
            }
        }
    }
}

impl<'de> Deserialize<'de> for DnsConfig {
    fn deserialize<D>(deserializer: D) -> std::result::Result<Self, D::Error>
    where
        D: serde::Deserializer<'de>,
    {
        use serde::de::Error;

        let wire = DnsWire::deserialize(deserializer)?;
        match wire.mode.as_str() {
            crate::shared::protocol::enums::dns_mode::RESOLVED => Ok(Self::Resolved),
            crate::shared::protocol::enums::dns_mode::SERVERS => Ok(Self::Servers {
                servers: wire.servers,
            }),
            other => Err(D::Error::unknown_variant(
                other,
                &[
                    crate::shared::protocol::enums::dns_mode::RESOLVED,
                    crate::shared::protocol::enums::dns_mode::SERVERS,
                ],
            )),
        }
    }
}

impl DnsConfig {
    pub fn validate(&self, owner: &str) -> Result<()> {
        let Self::Servers { servers } = self else {
            return Ok(());
        };
        if servers.is_empty() {
            bail!("The {owner} DNS server list must not be empty. Set `mode = \"resolved\"`.");
        }
        if servers.len() > 2 {
            bail!("{owner} may configure at most two DNS servers");
        }
        if servers
            .iter()
            .any(|server| server.is_unspecified() || server.is_multicast())
        {
            bail!("{owner} DNS servers must be unicast IPv4 addresses");
        }
        if servers.windows(2).any(|pair| pair[0] == pair[1]) {
            bail!("{owner} DNS servers must be unique");
        }
        Ok(())
    }

    pub fn servers(&self) -> Vec<String> {
        match self {
            Self::Resolved => system_resolvers(),
            Self::Servers { servers } => servers.iter().map(ToString::to_string).collect(),
        }
    }
}

fn system_resolvers() -> Vec<String> {
    std::fs::read_to_string("/etc/resolv.conf")
        .ok()
        .map(|text| resolvers_from(&text))
        .filter(|servers| !servers.is_empty())
        .unwrap_or_else(|| vec!["127.0.0.53".into()])
}

pub(crate) fn resolvers_from(text: &str) -> Vec<String> {
    let mut out = Vec::new();
    for line in text.lines() {
        let mut fields = line.split_whitespace();
        if fields.next() != Some("nameserver") {
            continue;
        }
        let Some(value) = fields.next() else { continue };
        let Ok(server) = value.parse::<Ipv4Addr>() else {
            continue;
        };
        if server.is_unspecified() || server.is_multicast() {
            continue;
        }
        let server = server.to_string();
        if !out.contains(&server) {
            out.push(server);
        }
        if out.len() == 2 {
            break;
        }
    }
    out
}

/// The systemd scope created by `build_argv` enforces these agent resource limits.
/// Each field is optional. An unset field means no limit.
/// These settings limit resource use. The sandbox controls access.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize, Deserialize)]
pub struct Limits {
    /// Maximum memory in MiB (systemd `MemoryMax`). The kernel enforces this limit through OOM termination.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub memory_mb: Option<u32>,
    /// Maximum number of tasks in the agent's process tree (`TasksMax`). Tasks include processes and threads.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub pids: Option<u32>,
    /// Open-file-descriptor ceiling for each process in the tree (`LimitNOFILE`).
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub nofile: Option<u32>,
    /// CPU as a percentage of one core: 100 is a whole core, 50 a half, 200 two (`CPUQuota`).
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub cpu_pct: Option<u32>,
}

impl Limits {
    pub fn is_empty(&self) -> bool {
        self.memory_mb.is_none()
            && self.pids.is_none()
            && self.nofile.is_none()
            && self.cpu_pct.is_none()
    }

    /// Reject zero limits when saving configuration.
    /// These limits can prevent session startup through OOM termination or failure to create a process.
    pub fn validate(&self) -> Result<()> {
        for (what, value) in [
            ("memory_mb", self.memory_mb),
            ("pids", self.pids),
            ("nofile", self.nofile),
            ("cpu_pct", self.cpu_pct),
        ] {
            if value == Some(0) {
                bail!("resource limit {what} must be at least 1, or unset for no cap");
            }
        }
        Ok(())
    }
}

/// A project owns the paths it exposes to its agents. It includes the primary directory implicitly.
/// A mount with that directory as both source and destination changes its mode.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct ProjectCfg {
    #[serde(default, skip_serializing_if = "String::is_empty")]
    pub id: String,
    #[serde(default, skip_serializing_if = "String::is_empty")]
    #[serde(alias = "workspace_root")]
    pub worktree_root: String,
    pub name: String,
    /// Temporary projects can omit this directory.
    /// `check_project` requires a directory for other project types.
    #[serde(default)]
    pub dir: String,
    /// Generate the directory path `TEMP_ROOT/<name>` when saving the entry.
    /// Create the directory when the first agent starts. The directory is temporary, but the entry persists.
    /// Library errands use a separate temporary workspace without a stored project entry. See `LibraryItemLink::Temp`.
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
    /// Empty selects the original project checkout.
    #[serde(default, skip_serializing_if = "String::is_empty")]
    #[serde(alias = "workspace")]
    pub worktree: String,
    pub name: String,
    /// A non-empty manual sidebar label disables automatic title summaries for this agent.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub label: Option<String>,
    /// The daemon owns this stable UUID for the agent's private state.
    /// UI and tmux names can change or identify a different agent later. This UUID cannot.
    #[serde(default, skip_serializing_if = "String::is_empty")]
    pub state_id: String,
    /// The project supplies the workspace and shared mounts.
    #[serde(default)]
    pub project: String,
    /// The command preset name. An empty name selects `[defaults] agent` unless this entry has an explicit `cmd`.
    #[serde(default)]
    pub command: String,
    /// An explicit command line that overrides the preset command.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub cmd: Option<String>,
    /// A command definition that the daemon captures when it creates this agent from a template.
    /// Manual session requests cannot set it. The manager preserves it during edits.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub(crate) command_snapshot: Option<CommandPreset>,
    /// Sandbox presets this agent adds to its command's presets.
    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    pub sandbox: Vec<String>,
    /// Preset definitions that the daemon captures for this agent.
    /// Names in `sandbox` use these definitions before the live preset catalog.
    /// This preserves the template definitions after agent creation.
    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    pub(crate) sandbox_snapshots: Vec<SandboxPreset>,
    /// Give this agent a durable, private `/tmp` instead of the sandbox's per-run tmpfs.
    #[serde(default, skip_serializing_if = "is_false")]
    pub persistent_tmp: bool,
    /// The explicit network mode for this agent.
    /// If a template does not specify a mode, copy the daemon's documented agent default.
    #[serde(default)]
    pub network: NetworkMode,
    /// DNS for this agent. `resolved` retains the daemon's current resolver at launch.
    #[serde(default)]
    pub dns: DnsConfig,
    /// This agent's final resource caps. An unset field means no configured cap.
    #[serde(default, skip_serializing_if = "Limits::is_empty")]
    pub limits: Limits,
    #[serde(default)]
    pub autostart: bool,
    /// After a fresh process reaches its first settled prompt, select its latest conversation.
    #[serde(default)]
    pub auto_resume: bool,
    /// Metadata that the daemon owns for a task's child session.
    /// Ordinary session creation clears these fields. Only worker creation sets them.
    #[serde(default, skip_serializing_if = "is_false")]
    pub worker: bool,
    #[serde(default, skip_serializing_if = "String::is_empty")]
    pub parent: String,
    #[serde(default, skip_serializing_if = "String::is_empty")]
    pub task_id: String,
    /// A scoped credential for a task worker that exists only in memory.
    /// The daemon does not save it or expose it in session views.
    /// Each worker run receives a new grant.
    #[serde(skip)]
    pub(crate) worker_token: Option<String>,
}

/// A persistent host terminal tab. The daemon owns this record.
/// It restores the shell in the sidebar after a game or daemon restart.
/// Host execution remains separate from agent settings.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct HostTerminalCfg {
    pub name: String,
    /// A fixed sidebar label. Empty means the terminal application's title is shown.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub label: Option<String>,
    #[serde(default)]
    pub project: String,
    /// The last directory reported by tmux, separate from the project root.
    /// This preserves shell directory changes after a daemon restart.
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

#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum LibraryItemKind {
    #[default]
    Prompt,
    /// Handed to an interactive shell inside the project's sandbox.
    Shell,
    /// A named piece of guidance pasted into an agent's first prompt.
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
    /// Use the configured project. This default preserves the behavior of older library entries.
    #[default]
    Project,
    /// Create a temporary workspace for each run. Do not save it in this file.
    /// If the entry names a project, copy that project's sandbox settings.
    Temp,
    /// Let the caller select the project for each run.
    Ask,
}

crate::wire_enum!(LibraryItemLink, {
    LibraryItemLink::Project => crate::shared::protocol::enums::library_link::PROJECT,
    LibraryItemLink::Temp => crate::shared::protocol::enums::library_link::TEMP,
    LibraryItemLink::Ask => crate::shared::protocol::enums::library_link::ASK,
});

/// What the Files sidebar does after a file action is selected. `Ask` is the compatibility
/// default for entries written before file actions had a saved mode.
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

/// A session template with attached text.
/// Store the definition directly so deletion of an existing session cannot invalidate it.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct LibraryItemCfg {
    /// The button label and initial colonist name.
    /// `slug` converts it to a valid session name.
    pub name: String,
    #[serde(default)]
    pub kind: LibraryItemKind,
    #[serde(default)]
    pub link: LibraryItemLink,
    /// The project to use when `link` is `project`.
    /// Temporary links create a new workspace. Ask links let the caller select the destination.
    #[serde(default)]
    pub project: String,
    /// A prompt for the agent, a command line for the shell.
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
    /// What a file action does after selection. `ask` keeps the old per-invocation menu.
    #[serde(default, skip_serializing_if = "is_file_action_mode_default")]
    pub mode: FileActionMode,
    /// Identifies entries supplied with the daemon. `config.toml` does not contain these entries.
    /// The protocol includes this flag so the GUI can exclude them from the library table and prevent edits.
    /// Breadcrumb lists still include them. Serialization omits a false value to preserve the TOML format of ordinary entries.
    #[serde(default, skip_serializing_if = "not_set")]
    pub builtin: bool,
}

pub fn builtin_library_items() -> &'static [LibraryItemCfg] {
    static BUILTIN: OnceLock<Vec<LibraryItemCfg>> = OnceLock::new();
    BUILTIN.get_or_init(|| {
        vec![LibraryItemCfg {
            name: "Useful tips".into(),
            kind: LibraryItemKind::Breadcrumb,
            text: "___\n\nUseful tips:\n\n- {{ random_tip }}\n- {{ random_tip }}\n\
                   - {{ random_tip }}\n- {{ random_tip }}\n- {{ random_tip }}"
                .into(),
            builtin: true,
            ..Default::default()
        }]
    })
}

fn not_set(b: &bool) -> bool {
    !*b
}

fn is_file_action_mode_default(mode: &FileActionMode) -> bool {
    *mode == FileActionMode::Ask
}

fn yes() -> bool {
    true
}

fn is_true(b: &bool) -> bool {
    *b
}

fn is_false(b: &bool) -> bool {
    !*b
}

/// Ordered: first match wins. Shipped defaults target Claude Code's TUI.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct StateRule {
    pub state: String,
    pub pattern: String,
}
