//! Configuration data types and their field-level defaults.

use std::collections::BTreeMap;
use std::net::Ipv4Addr;
use std::path::{Component, Path};
use std::sync::OnceLock;

use anyhow::{bail, Result};
use serde::{Deserialize, Serialize};

const DEFAULT_BIND: &str = crate::wire::DEFAULT_BIND;
const DEFAULT_USAGE_POLL_SECS: u64 = crate::wire::USAGE_POLL_SECS;
const DEFAULT_CLAUDE_CREDENTIALS: &str = crate::wire::DEFAULT_CLAUDE_CREDENTIALS;
const DEFAULT_OPENAI_CREDENTIALS: &str = crate::wire::DEFAULT_OPENAI_CREDENTIALS;
const DEFAULT_TITLE_MODEL: &str = crate::wire::DEFAULT_TITLE_MODEL;
const DEFAULT_TITLE_MIN_CHARS: usize = crate::wire::DEFAULT_TITLE_MIN_CHARS as usize;

/// One TOML file, which the mod reads and writes back verbatim, so hand-edits and
/// in-game edits use the same format.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct Config {
    #[serde(default)]
    pub daemon: Daemon,
    #[serde(default)]
    pub defaults: Defaults,
    /// Commands the client uses for file viewers, editors, syntax highlighting and links.
    #[serde(default)]
    pub commands: CommandDefaults,
    /// A session is an agent *in* one of these, and takes its directory and sandbox
    /// from it rather than carrying either.
    #[serde(default, rename = "project")]
    pub projects: Vec<ProjectCfg>,
    #[serde(default, rename = "session")]
    pub sessions: Vec<SessionCfg>,
    /// One-shot errands, run by a temporary agent that exists only as long as its
    /// process does.
    #[serde(default, rename = "library")]
    pub library: Vec<LibraryItemCfg>,
    /// Host shells opened from a project heading. These are deliberately separate from
    /// `session`: a host terminal is allowed only through the explicit host-shell route and
    /// never becomes an agent merely because a config entry was edited.
    #[serde(
        default,
        rename = "host_terminal",
        skip_serializing_if = "Vec::is_empty"
    )]
    pub host_terminals: Vec<HostTerminalCfg>,
    #[serde(default, rename = "state_rule")]
    pub state_rules: Vec<StateRule>,
}

/// Wire sentinel for a redacted token and for writes meaning "unchanged"; a new value or empty
/// string is an explicit change.
pub const TOKEN_REDACTED: &str = "<redacted>";

// These are daemon identity and scheduling policy, not user configuration. The private tmux
// name is part of the sandbox/debug contract; the state tick only drives idle reclassification.
pub const STATE_TICK_MS: u64 = 1_000;
pub const SCROLLBACK_LINES: u32 = crate::wire::SCROLLBACK_LINES;

/// The private tmux socket name (`tmux -L <name>`). `SLOPD_TMUX_SOCKET` overrides it so a
/// throwaway daemon can run beside the real one without sharing its tmux server; production
/// leaves it unset and gets `slopworld`. Read once and cached, since it is daemon identity.
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

/// Redacts only a non-empty `[daemon] token` in raw config text, preserving comments and blanks;
/// `Manager::replace_config` restores the real value when the sentinel is written back.
pub fn redact_token_text(text: &str) -> String {
    let mut out = String::with_capacity(text.len() + TOKEN_REDACTED.len());
    let mut in_daemon = false;
    for line in text.lines() {
        let t = line.trim_start();
        if t.starts_with('[') {
            // `[daemon.x]` is a different table and leaves the section, which is what we want.
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
    /// Empty means no auth, which is fine on a loopback bind. Never leaves the daemon as
    /// written: `GET /api/config` swaps it for `TOKEN_REDACTED`, and a write of the sentinel
    /// restores it - see `redact_token_text` and `Manager::replace_config`.
    #[serde(default)]
    pub token: String,
    /// The windows it reports move in minutes; ordinary providers are floored at 10 seconds,
    /// while Anthropic is floored at five minutes because its OAuth usage endpoint is
    /// account-rate-limited.
    #[serde(default = "default_usage_poll")]
    pub usage_poll_secs: u64,
    /// Per-window usage settings. An explicit `interval_secs` overrides only the global
    /// interval; source defaults apply until a source's rows are configured.
    #[serde(default)]
    pub usage_items: BTreeMap<String, UsageItem>,
    /// Read fresh each time and never copied, so a refresh behind us is picked up.
    #[serde(default = "default_credentials")]
    pub claude_credentials: String,
    /// Blank reads `OPENROUTER_API_KEY` out of slopd's own environment. A path here is read
    /// fresh per request and trimmed, the way the credentials file is, and neither is ever
    /// logged or written back.
    #[serde(default)]
    pub openrouter_key_file: String,
    /// Codex signs in with ChatGPT and keeps the short-lived access token here. Like the
    /// Claude credentials this is read fresh, never copied or sent over the wire.
    #[serde(default = "default_openai_credentials")]
    pub openai_credentials: String,
    /// Automatic task titles are opt-in because a title request sends part of a prompt to
    /// OpenRouter. `once` names the first real prompt in each Codex conversation.
    #[serde(default)]
    pub agent_titles: TitlePolicy,
    /// The OpenRouter model used for automatic agent prompt titles.
    #[serde(default = "default_title_model")]
    pub title_model: String,
    /// Prompts shorter than this are not worth an external title request.
    /// Count Unicode characters so the setting does not depend on UTF-8 byte width.
    #[serde(default = "default_title_min_chars")]
    pub title_min_chars: usize,
    /// Pi follows the daemon title path. It historically renamed on every prompt, so that
    /// remains its default.
    #[serde(default = "default_pi_title_policy")]
    pub pi_titles: TitlePolicy,
    /// The generated SLOPWORLD.md template and the discovery settings for opted-in agents.
    #[serde(default)]
    pub instructions: InstructionsCfg,
}

/// Settings for generated runtime context and the prompt delivered to a new task worker.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct InstructionsCfg {
    /// Markdown body template. `{{ runtime_context }}` expands to SlopWorld's generated snapshot.
    #[serde(default = "default_instructions_template")]
    pub template: String,
    /// Relative to the primary project directory inside the sandbox.
    #[serde(default = "default_instructions_mount_path")]
    pub mount_path: String,
    /// Prompt text added before the first prompt when the generated manifest is mounted.
    /// `{{ project }}`, `{{ mount_path }}` and `{{ file }}` are available.
    #[serde(default = "default_instructions_breadcrumb")]
    pub breadcrumb: String,
    /// Add the manifest discovery breadcrumb to agents that mount the file.
    #[serde(default = "default_instructions_breadcrumb_enabled")]
    pub breadcrumb_enabled: bool,
    /// Prompt submitted to a newly spawned task worker before it retrieves its mailbox task.
    #[serde(default = "default_worker_prompt")]
    pub worker_prompt: String,
}

pub const DEFAULT_INSTRUCTIONS_TEMPLATE: &str = "\
# SlopWorld agent context

Read the project's `README.md` and any applicable `AGENTS.md` files for project instructions. This generated `{{ file }}` is mounted at `{{ mount_path }}` and is runtime context, not a replacement for them.

{{ runtime_context }}
";
pub const DEFAULT_INSTRUCTIONS_MOUNT_PATH: &str = crate::wire::DEFAULT_INSTRUCTIONS_MOUNT_PATH;
pub const DEFAULT_INSTRUCTIONS_BREADCRUMB: &str = "Read `{{ mount_path }}` for SlopWorld runtime context. It is a generated snapshot, not project instructions. When delegating, send work once and use `slopctl wait ID` for the result; do not poll `task`, `inbox`, or `status`.";
pub const DEFAULT_WORKER_PROMPT: &str = "You are a SlopWorld worker. Your assigned task ID is $SLOPWORLD_TASK_ID. Run `slopctl task \"$SLOPWORLD_TASK_ID\"` once, then `slopctl accept \"$SLOPWORLD_TASK_ID\"`. Use `slopctl progress \"$SLOPWORLD_TASK_ID\" \"note\"` while working and conclude with `slopctl finish \"$SLOPWORLD_TASK_ID\" \"result\"` or `slopctl fail \"$SLOPWORLD_TASK_ID\" \"reason\"`. Do not search the inbox or poll task status.";

fn default_instructions_template() -> String {
    DEFAULT_INSTRUCTIONS_TEMPLATE.into()
}

fn default_instructions_mount_path() -> String {
    DEFAULT_INSTRUCTIONS_MOUNT_PATH.into()
}

fn default_instructions_breadcrumb_enabled() -> bool {
    crate::wire::DEFAULT_INSTRUCTIONS_BREADCRUMB_ENABLED
}

fn default_instructions_breadcrumb() -> String {
    DEFAULT_INSTRUCTIONS_BREADCRUMB.into()
}

fn default_worker_prompt() -> String {
    DEFAULT_WORKER_PROMPT.into()
}

impl Default for InstructionsCfg {
    fn default() -> Self {
        Self {
            template: default_instructions_template(),
            mount_path: default_instructions_mount_path(),
            breadcrumb: default_instructions_breadcrumb(),
            breadcrumb_enabled: default_instructions_breadcrumb_enabled(),
            worker_prompt: default_worker_prompt(),
        }
    }
}

impl InstructionsCfg {
    pub fn validate(&self) -> Result<()> {
        let raw = self.mount_path.trim();
        if raw.is_empty() {
            bail!("instructions mount path must not be empty");
        }
        if raw != self.mount_path {
            bail!("instructions mount path must not start or end with whitespace");
        }
        if raw.ends_with('/') || raw.ends_with('\\') {
            bail!("instructions mount path must name a file, not a directory");
        }
        let path = Path::new(raw);
        if !path.is_relative()
            || path
                .components()
                .any(|part| matches!(part, Component::CurDir | Component::ParentDir))
        {
            bail!("instructions mount path must be a relative path inside the project directory");
        }
        Ok(())
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
            title_min_chars: default_title_min_chars(),
            pi_titles: default_pi_title_policy(),
            instructions: InstructionsCfg::default(),
        }
    }
}

/// Both fields name a *command preset*. What one runs is that preset's file, so this
/// section says which agent is meant rather than what it is.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Defaults {
    /// What an agent that names no command of its own runs.
    pub agent: String,
    /// The shell agents should advertise to tools that run commands inside the sandbox.
    #[serde(default = "default_agent_shell")]
    pub agent_shell: String,
    /// What a shell errand runs. Here rather than in every library item: which shell this
    /// machine has is the machine's answer.
    #[serde(default = "default_shell")]
    pub shell: String,
}

pub(crate) fn default_agent() -> String {
    crate::wire::DEFAULT_AGENT.into()
}

fn default_agent_shell() -> String {
    crate::wire::DEFAULT_AGENT_SHELL.into()
}

fn default_shell() -> String {
    crate::wire::DEFAULT_SHELL.into()
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

/// Host applications used by the mod for transient file actions. These are command templates,
/// split into argv without a shell; the client expands `{file}` and `{line}` where supported.
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
    crate::wire::DEFAULT_PAGER.into()
}

fn default_editor() -> String {
    crate::wire::DEFAULT_EDITOR.into()
}

fn default_highlighter() -> String {
    crate::wire::DEFAULT_HIGHLIGHTER.into()
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

/// Under `/tmp` deliberately: the machine clears it, so nothing here has to decide
/// when scratch work has outlived its use.
pub const TEMP_ROOT: &str = crate::wire::TEMP_ROOT;

/// Coined rather than typed, which is the whole point of the flag.
pub fn temp_dir(name: &str) -> String {
    format!("{TEMP_ROOT}/{name}")
}

/// The network a sandbox may use. A project supplies the default and an agent may
/// override it with any of the three modes.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub enum NetworkMode {
    None,
    #[default]
    Private,
    Host,
}

crate::wire_enum!(NetworkMode, {
    NetworkMode::None => crate::wire::enums::network_mode::NONE,
    NetworkMode::Private => crate::wire::enums::network_mode::PRIVATE,
    NetworkMode::Host => crate::wire::enums::network_mode::HOST,
});

/// Read-only or read-write access for a project mount.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub enum MountMode {
    Ro,
    #[default]
    Rw,
}

crate::wire_enum!(MountMode, {
    MountMode::Ro => crate::wire::enums::mount_mode::RO,
    MountMode::Rw => crate::wire::enums::mount_mode::RW,
});

/// An additional project directory mounted into the agent's sandbox at `/mnt/<project-name>`.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct Mount {
    pub project: String,
    #[serde(default)]
    pub mode: MountMode,
}

/// How private or host-mode sandboxes resolve names. The implicit answer follows the daemon's
/// current `/etc/resolv.conf`, including Docker's embedded resolver in slopcar. Explicit servers
/// are an opt-in for machines or projects that deliberately do not use the system resolver.
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
                out.serialize_field("mode", crate::wire::enums::dns_mode::RESOLVED)?;
                out.end()
            }
            Self::Servers { servers } => {
                let mut out = serializer.serialize_struct("DnsConfig", 2)?;
                out.serialize_field("mode", crate::wire::enums::dns_mode::SERVERS)?;
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
            crate::wire::enums::dns_mode::RESOLVED => Ok(Self::Resolved),
            crate::wire::enums::dns_mode::SERVERS => Ok(Self::Servers {
                servers: wire.servers,
            }),
            other => Err(D::Error::unknown_variant(
                other,
                &[
                    crate::wire::enums::dns_mode::RESOLVED,
                    crate::wire::enums::dns_mode::SERVERS,
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
            bail!("{owner} DNS server list must not be empty; use mode = \"resolved\"");
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

/// Per-agent resource caps, enforced by the systemd scope `build_argv` wraps the agent in.
/// Every field is optional: a session inherits any it leaves unset from its project, and one
/// unset in both means no cap at all. Reach is the sandbox's job; this is only how much.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize, Deserialize)]
pub struct Limits {
    /// Hard memory ceiling in MiB (systemd `MemoryMax`); the kernel OOM-kills the tree at it.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub memory_mb: Option<u32>,
    /// The most tasks - processes and threads together - the agent's tree may hold (`TasksMax`).
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

    /// This agent's own caps over its project's, field by field: a cap the session states wins,
    /// one it leaves unset falls through to the project, and unset in both stays no cap.
    pub(crate) fn inherit(self, project: Limits) -> Limits {
        Limits {
            memory_mb: self.memory_mb.or(project.memory_mb),
            pids: self.pids.or(project.pids),
            nofile: self.nofile.or(project.nofile),
            cpu_pct: self.cpu_pct.or(project.cpu_pct),
        }
    }

    /// A cap of zero is not a cap, it is a session that cannot start: refuse it where it is
    /// written rather than let the agent OOM or fail to fork on its first breath.
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

/// Three agents in the same repo want the same binds, and keeping that in three
/// session entries meant it was wrong in at least one of them.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct ProjectCfg {
    pub name: String,
    /// Defaulted rather than required, because a temporary project has none to give.
    /// `check_project` is what insists on one for every other kind.
    #[serde(default)]
    pub dir: String,
    /// The directory is `TEMP_ROOT/<name>`, coined when the entry is written and made when
    /// the first agent starts: what is temporary is the *ground*, not the entry. The other
    /// kind is never written here at all; see `LibraryItemLink::Temp`.
    #[serde(default)]
    pub temp: bool,
    /// Sandbox presets, by name. One this build has no file for is ignored with a warning
    /// rather than refused, because the files outlive the binary.
    #[serde(default)]
    pub sandbox: Vec<String>,
    /// Named breadcrumbs added to every agent in this project.
    #[serde(default)]
    pub breadcrumbs: Vec<String>,
    /// The default network mode for sandboxed agents in this project.
    #[serde(default)]
    pub network: NetworkMode,
    /// DNS for agents in this project. Missing means the configured system resolver.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub dns: Option<DnsConfig>,
    /// Resource caps applied to every agent here, each overridable per agent.
    #[serde(default, skip_serializing_if = "Limits::is_empty")]
    pub limits: Limits,
}

/// An agent is a command preset plus this file's answer to it - the command, sandbox presets
/// and breadcrumbs that entry adds.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct SessionCfg {
    pub name: String,
    /// A non-empty manual sidebar label disables automatic title summaries for this agent.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub label: Option<String>,
    /// Stable, daemon-owned UUID identity of this agent's private state. Names are UI and tmux
    /// handles and may change or be reused; this is deliberately neither.
    #[serde(default, skip_serializing_if = "String::is_empty")]
    pub state_id: String,
    /// Everything about where it runs and what it can reach comes from there.
    #[serde(default)]
    pub project: String,
    /// A command preset's name. Empty is `[defaults] agent`, or nothing at all when this
    /// entry states a `cmd` of its own.
    #[serde(default)]
    pub command: String,
    /// This agent's own answer to what that preset runs.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub cmd: Option<String>,
    /// Sandbox presets it adds to its command's and its project's.
    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    pub sandbox: Vec<String>,
    /// Named breadcrumbs added to this agent's first prompt.
    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    pub breadcrumbs: Vec<String>,
    /// Opt into the generated project-root runtime manifest; its mount path and discovery
    /// text are controlled by `daemon.instructions`.
    #[serde(default, skip_serializing_if = "is_false")]
    pub slopworld_md: bool,
    /// Add the configured discovery text when this agent also mounts the generated manifest.
    /// Defaults on so enabling the manifest keeps the original discovery behavior.
    #[serde(default = "yes", skip_serializing_if = "is_true")]
    pub instructions_breadcrumb: bool,
    /// Give this agent a durable, private `/tmp` instead of the sandbox's per-run tmpfs.
    #[serde(default, skip_serializing_if = "is_false")]
    pub persistent_tmp: bool,
    /// Paste all effective breadcrumbs in front of the first Enter after startup.
    #[serde(default = "yes", skip_serializing_if = "is_true")]
    pub breadcrumb_yolo: bool,
    /// An optional network mode for this agent. Missing means inherit the project default.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub network: Option<NetworkMode>,
    /// DNS override for this agent. Missing means inherit the project's DNS setting.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub dns: Option<DnsConfig>,
    /// This agent's own resource caps, each overriding the project's for the same field.
    #[serde(default, skip_serializing_if = "Limits::is_empty")]
    pub limits: Limits,
    /// Additional project directories mounted under `/mnt/<project-name>`.
    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    pub mounts: Vec<Mount>,
    #[serde(default)]
    pub autostart: bool,
    /// After a fresh process reaches its first settled prompt, select its latest conversation.
    #[serde(default)]
    pub auto_resume: bool,
    /// Daemon-owned metadata for a task-owned child. Ordinary session creation clears these
    /// fields; worker creation is the only route that sets them.
    #[serde(default, skip_serializing_if = "is_false")]
    pub worker: bool,
    #[serde(default, skip_serializing_if = "String::is_empty")]
    pub parent: String,
    #[serde(default, skip_serializing_if = "String::is_empty")]
    pub task_id: String,
    /// Runtime-only scoped credential passed to a task worker. It is never persisted or exposed
    /// in session views; a fresh grant is minted for every worker run.
    #[serde(skip)]
    pub(crate) worker_token: Option<String>,
}

/// A durable host terminal tab. The daemon owns this small record so a game or daemon restart
/// can put the shell back in the sidebar without making host execution a property of an agent.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct HostTerminalCfg {
    pub name: String,
    /// A fixed sidebar label. Empty means the terminal application's title is shown.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub label: Option<String>,
    #[serde(default)]
    pub project: String,
    /// The last directory observed from tmux. It is kept separately from the project's root so
    /// a shell that `cd`s somewhere remains there after the next daemon start.
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
            name: String::new(),
            label: None,
            state_id: uuid::Uuid::new_v4().to_string(),
            project: String::new(),
            command: String::new(),
            cmd: None,
            sandbox: Vec::new(),
            breadcrumbs: Vec::new(),
            slopworld_md: false,
            instructions_breadcrumb: true,
            persistent_tmp: false,
            breadcrumb_yolo: true,
            network: None,
            dns: None,
            limits: Limits::default(),
            mounts: Vec::new(),
            autostart: false,
            auto_resume: false,
            worker: false,
            parent: String::new(),
            task_id: String::new(),
            worker_token: None,
        }
    }
}

/// The only thing the two kinds disagree about at the far end: an agent's input
/// field, or a shell's prompt.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum LibraryItemKind {
    /// Claude Code unless the entry says otherwise, which is what makes it a prompt
    /// rather than a command.
    #[default]
    Prompt,
    /// Handed to an interactive shell inside the project's sandbox.
    Shell,
    /// A named piece of guidance pasted into an agent's first prompt.
    Breadcrumb,
    /// A command offered for the selected path in the Files sidebar.
    FileAction,
}

crate::wire_enum!(LibraryItemKind, {
    LibraryItemKind::Prompt => crate::wire::enums::library_kind::PROMPT,
    LibraryItemKind::Shell => crate::wire::enums::library_kind::SHELL,
    LibraryItemKind::Breadcrumb => crate::wire::enums::library_kind::BREADCRUMB,
    LibraryItemKind::FileAction => crate::wire::enums::library_kind::FA,
});

/// The one thing about a library item allowed not to be decided in advance.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum LibraryItemLink {
    /// The default, because it is what every library item written before this existed
    /// meant.
    #[default]
    Project,
    /// One per run, never written to this file. The entry's `project`, if it names one, is
    /// what the fresh one copies its sandbox from.
    Temp,
    /// Whoever runs it says where, per run.
    Ask,
}

crate::wire_enum!(LibraryItemLink, {
    LibraryItemLink::Project => crate::wire::enums::library_link::PROJECT,
    LibraryItemLink::Temp => crate::wire::enums::library_link::TEMP,
    LibraryItemLink::Ask => crate::wire::enums::library_link::ASK,
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
    FileActionMode::Ask => crate::wire::enums::file_action_mode::ASK,
    FileActionMode::ShowResult => crate::wire::enums::file_action_mode::SHOW_RESULT,
    FileActionMode::OpenTerminal => crate::wire::enums::file_action_mode::OPEN_TERMINAL,
    FileActionMode::Nothing => crate::wire::enums::file_action_mode::NOTHING,
});

/// A session template with a line of text attached. Spelled out rather than pointing at an
/// existing session, which would stop working the day that session was deleted.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct LibraryItemCfg {
    /// Labels a button and seeds a colonist's name; the session name derived from it
    /// is sanitised (see `slug`).
    pub name: String,
    #[serde(default)]
    pub kind: LibraryItemKind,
    #[serde(default)]
    pub link: LibraryItemLink,
    /// Read as the place to run when `link` is `project`, as the sandbox to copy when
    /// it is `temp`, and not at all when it is `ask`.
    #[serde(default)]
    pub project: String,
    /// A prompt for the agent, a command line for the shell.
    #[serde(default)]
    pub text: String,
    /// Empty means `[defaults] agent` or `[defaults] shell`.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub command: Option<String>,
    /// What a file action does after selection. `ask` keeps the old per-invocation menu.
    #[serde(default, skip_serializing_if = "is_file_action_mode_default")]
    pub mode: FileActionMode,
    /// Set on the entries the daemon ships. They are never in `config.toml` - the flag rides
    /// the wire so the GUI can keep them out of the library table and refuse to edit them,
    /// while the breadcrumb lists still offer them like any other. Skipped when false so an
    /// ordinary entry's TOML is unchanged.
    #[serde(default, skip_serializing_if = "not_set")]
    pub builtin: bool,
}

/// The builtin breadcrumbs, in the order the GUI lists them. `include_str!` is not worth a
/// file each: unlike a preset these are one string with no table around them.
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
