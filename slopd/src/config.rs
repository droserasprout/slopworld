use std::path::{Path, PathBuf};
use std::sync::OnceLock;

use anyhow::{bail, Context, Result};
use serde::{Deserialize, Serialize};

/// One TOML file, which the mod reads and writes back verbatim, so hand-edits and
/// in-game edits use the same format.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct Config {
    #[serde(default)]
    pub daemon: Daemon,
    #[serde(default)]
    pub defaults: Defaults,
    /// A session is an agent *in* one of these, and takes its directory and sandbox
    /// from it rather than carrying either.
    #[serde(default, rename = "project")]
    pub projects: Vec<ProjectCfg>,
    #[serde(default, rename = "session")]
    pub sessions: Vec<SessionCfg>,
    /// One-shot errands, run by a temporary agent that exists only as long as its
    /// process does.
    #[serde(default, rename = "shortcut")]
    pub shortcuts: Vec<ShortcutCfg>,
    #[serde(default, rename = "state_rule")]
    pub state_rules: Vec<StateRule>,
}

/// What a set token reads as over the wire, and what a write may send back to mean "leave it
/// as it was". A client that only ever saw this cannot echo the real token, so it cannot blank
/// auth it never held; a deliberate change - a new value, or an empty string to turn auth off -
/// is anything that is not this. A token literally set to the sentinel is harmless: the write
/// restores it to the same value it already is.
pub const TOKEN_REDACTED: &str = "<redacted>";

/// The same substitution on the raw file text `GET /api/config` also hands back, so the raw
/// editor shows the sentinel rather than the secret while keeping every comment and blank line
/// the file has. Only a non-empty `token` inside the `[daemon]` table is touched. The write
/// path reserializes from the struct, so this is display only; the restore lives in
/// `Manager::replace_config`.
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
    /// Dedicated, so we never collide with the user's own tmux and `tmux -L slopworld
    /// attach` still works from a real terminal.
    pub tmux_socket: String,
    /// How often the state tick decays working -> idle. Screen content itself arrives
    /// event-driven from the control readers, not by polling.
    pub poll_ms: u64,
    /// Also all we have to reseed an emulator from when slopd restarts under a session
    /// that kept running, so it is the ceiling on history surviving a redeploy.
    #[serde(default = "default_history_limit")]
    pub history_limit: u32,
    /// Empty disables the endpoint. Split like an agent command: no shell. Names the
    /// *launcher*, which makes the profile the mod insists on and waits on the game, so the
    /// process this path matches is the one that goes away when the colony is saved.
    #[serde(default = "default_game_cmd")]
    pub game_cmd: String,
    /// Off means slopd never reads the credentials file and never leaves the machine.
    #[serde(default = "yes")]
    pub usage: bool,
    /// The windows it reports move in minutes; floored at 10 in the poller.
    #[serde(default = "default_usage_poll")]
    pub usage_poll_secs: u64,
    /// Read fresh each time and never copied, so a refresh behind us is picked up.
    #[serde(default = "default_credentials")]
    pub claude_credentials: String,
    /// The other subscription this machine spends, and off by default: unlike Claude's,
    /// there is no login on the host to infer one from - a key is either given to slopd
    /// or it is not. Shares `usage_poll_secs`; a balance moves slower than a rate limit,
    /// never faster.
    #[serde(default)]
    pub openrouter: bool,
    /// Blank reads `OPENROUTER_API_KEY` out of slopd's own environment, which is where the
    /// `pi` preset forwards it from, so a machine that can already run that agent needs no
    /// second copy of the key. A path here is read fresh per poll and trimmed, the way the
    /// credentials file is, and neither is ever logged or written back.
    #[serde(default)]
    pub openrouter_key_file: String,
    /// Codex signs in with ChatGPT and keeps the short-lived access token here. Like the
    /// Claude credentials this is read fresh, never copied or sent over the wire.
    #[serde(default = "default_openai_credentials")]
    pub openai_credentials: String,
    /// Off means slopd never reads Codex's auth file or asks ChatGPT for its limits.
    #[serde(default = "yes")]
    pub openai: bool,
    /// Automatic task titles are opt-in because a title request sends part of a prompt to
    /// OpenRouter. `once` names the first real prompt in each Codex conversation.
    #[serde(default)]
    pub agent_titles: TitlePolicy,
    /// Kept configurable because OpenRouter model names have a shorter lifetime than this
    /// config schema.
    #[serde(default = "default_title_model")]
    pub title_model: String,
}

#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum TitlePolicy {
    #[default]
    Never,
    Once,
    Always,
}

fn default_history_limit() -> u32 {
    5000
}

fn default_usage_poll() -> u64 {
    60
}

fn default_credentials() -> String {
    "~/.claude/.credentials.json".into()
}

fn default_openai_credentials() -> String {
    "~/.codex/auth.json".into()
}

fn default_title_model() -> String {
    "google/gemini-3.1-flash-lite".into()
}

/// Where `make install-runner` puts it, in full rather than left to PATH: a user unit's PATH
/// is the manager's, and `~/.local/bin` is not reliably on it.
fn default_game_cmd() -> String {
    "~/.local/bin/slopworld".into()
}

impl Default for Daemon {
    fn default() -> Self {
        Self {
            bind: "127.0.0.1:7717".into(),
            token: String::new(),
            tmux_socket: "slopworld".into(),
            poll_ms: 80,
            history_limit: default_history_limit(),
            game_cmd: default_game_cmd(),
            usage: true,
            usage_poll_secs: default_usage_poll(),
            claude_credentials: default_credentials(),
            openrouter: false,
            openrouter_key_file: String::new(),
            openai_credentials: default_openai_credentials(),
            openai: true,
            agent_titles: TitlePolicy::Never,
            title_model: default_title_model(),
        }
    }
}

/// Both fields name a *command preset*. What one runs is that preset's file, so this
/// section says which agent is meant rather than what it is.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Defaults {
    /// What an agent that names no command of its own runs.
    pub agent: String,
    /// What a shell errand runs. Here rather than in every shortcut: which shell this
    /// machine has is the machine's answer.
    #[serde(default = "default_shell")]
    pub shell: String,
}

fn default_agent() -> String {
    "claude".into()
}

fn default_shell() -> String {
    "shell".into()
}

impl Default for Defaults {
    fn default() -> Self {
        Self {
            agent: default_agent(),
            shell: default_shell(),
        }
    }
}

/// Under `/tmp` deliberately: the machine clears it, so nothing here has to decide
/// when scratch work has outlived its use.
pub const TEMP_ROOT: &str = "/tmp/slopworld";

/// Coined rather than typed, which is the whole point of the flag.
pub fn temp_dir(name: &str) -> String {
    format!("{TEMP_ROOT}/{name}")
}

/// The network a sandbox may use. `host` is deliberately the widest mode: a project
/// chooses the ceiling, and an agent can only lower it with its optional override.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum NetworkMode {
    None,
    #[default]
    Private,
    Host,
}

impl NetworkMode {
    pub fn no_wider_than(self, ceiling: Self) -> bool {
        self.rank() <= ceiling.rank()
    }

    fn rank(self) -> u8 {
        match self {
            Self::None => 0,
            Self::Private => 1,
            Self::Host => 2,
        }
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
    /// kind is never written here at all; see `ShortcutLink::Temp`.
    #[serde(default)]
    pub temp: bool,
    /// Sandbox presets, by name. One this build has no file for is ignored with a warning
    /// rather than refused, because the files outlive the binary.
    #[serde(default)]
    pub sandbox: Vec<String>,
    /// Named breadcrumbs added to every agent in this project.
    #[serde(default)]
    pub breadcrumbs: Vec<String>,
    /// The maximum network reach of every sandboxed agent in this project.
    #[serde(default)]
    pub network: NetworkMode,
}

/// An agent is a command preset plus this file's answer to it - the command, sandbox presets
/// and breadcrumbs that entry adds.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct SessionCfg {
    pub name: String,
    /// Stable, daemon-owned identity of this agent's private state.  Names are UI and tmux
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
    /// Paste all effective breadcrumbs in front of the first Enter after startup.
    #[serde(default = "yes", skip_serializing_if = "is_true")]
    pub breadcrumb_yolo: bool,
    /// An optional reduction from the project's network ceiling. Missing means inherit.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub network: Option<NetworkMode>,
    #[serde(default)]
    pub autostart: bool,
}

impl Default for SessionCfg {
    fn default() -> Self {
        Self {
            name: String::new(),
            state_id: uuid::Uuid::new_v4().to_string(),
            project: String::new(),
            command: String::new(),
            cmd: None,
            sandbox: Vec::new(),
            breadcrumbs: Vec::new(),
            breadcrumb_yolo: true,
            network: None,
            autostart: false,
        }
    }
}

/// The only thing the two kinds disagree about at the far end: an agent's input
/// field, or a shell's prompt.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum ShortcutKind {
    /// Claude Code unless the entry says otherwise, which is what makes it a prompt
    /// rather than a command.
    #[default]
    Prompt,
    /// Handed to an interactive shell inside the project's sandbox.
    Shell,
    /// A named piece of guidance pasted into an agent's first prompt.
    Breadcrumb,
}

/// The one thing about a shortcut allowed not to be decided in advance.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum ShortcutLink {
    /// The default, because it is what every shortcut written before this existed
    /// meant.
    #[default]
    Project,
    /// One per run, never written to this file. The entry's `project`, if it names one, is
    /// what the fresh one copies its sandbox from.
    Temp,
    /// Whoever runs it says where, per run.
    Ask,
}

/// A session template with a line of text attached. Spelled out rather than pointing at an
/// existing session, which would stop working the day that session was deleted.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct ShortcutCfg {
    /// Labels a button and seeds a colonist's name; the session name derived from it
    /// is sanitised (see `slug`).
    pub name: String,
    #[serde(default)]
    pub kind: ShortcutKind,
    #[serde(default)]
    pub link: ShortcutLink,
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
    /// Set on the entries the daemon ships. They are never in `config.toml` - the flag rides
    /// the wire so the GUI can keep them out of the shortcuts table and refuse to edit them,
    /// while the breadcrumb lists still offer them like any other. Skipped when false so an
    /// ordinary entry's TOML is unchanged.
    #[serde(default, skip_serializing_if = "not_set")]
    pub builtin: bool,
}

/// The builtin breadcrumbs, in the order the GUI lists them. `include_str!` is not worth a
/// file each: unlike a preset these are one string with no table around them.
pub fn builtin_shortcuts() -> &'static [ShortcutCfg] {
    static BUILTIN: OnceLock<Vec<ShortcutCfg>> = OnceLock::new();
    BUILTIN.get_or_init(|| {
        vec![ShortcutCfg {
            name: "Useful tips".into(),
            kind: ShortcutKind::Breadcrumb,
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

fn yes() -> bool {
    true
}

fn is_true(b: &bool) -> bool {
    *b
}

/// Ordered: first match wins. Shipped defaults target Claude Code's TUI.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct StateRule {
    pub state: String,
    pub pattern: String,
}

impl Config {
    pub fn path() -> PathBuf {
        dirs::config_dir()
            .unwrap_or_else(|| PathBuf::from("."))
            .join("slopworld/config.toml")
    }

    /// The file this daemon actually read, `SLOPD_CONFIG` included. `main` loads from it, and
    /// `sandbox::refused` keeps every bind list away from it: the token is in there, and an
    /// agent that can read it is an agent that can ask for a host terminal.
    pub fn path_in_use() -> PathBuf {
        std::env::var("SLOPD_CONFIG")
            .map(PathBuf::from)
            .unwrap_or_else(|_| Self::path())
    }

    pub fn load(path: &Path) -> Result<Self> {
        if !path.exists() {
            let cfg = Config::seed();
            cfg.save(path)?;
            return Ok(cfg);
        }
        let text =
            std::fs::read_to_string(path).with_context(|| format!("reading {}", path.display()))?;
        Self::parse(&text)
    }

    pub fn parse(text: &str) -> Result<Self> {
        toml::from_str(text).context("parsing config.toml")
    }

    pub fn save(&self, path: &Path) -> Result<()> {
        Self::save_text(path, &toml::to_string_pretty(self)?)
    }

    pub fn save_text(path: &Path, text: &str) -> Result<()> {
        if let Some(parent) = path.parent() {
            std::fs::create_dir_all(parent)?;
        }

        let tmp = path.with_extension("toml.tmp");
        std::fs::write(&tmp, text)?;
        #[cfg(unix)]
        {
            use std::os::unix::fs::PermissionsExt;
            std::fs::set_permissions(&tmp, std::fs::Permissions::from_mode(0o600))?;
        }
        std::fs::rename(tmp, path)?;
        Ok(())
    }

    /// A clone safe to hand a client: a set token becomes the sentinel, so `GET /api/config`
    /// never carries the secret to anything that reaches the endpoint. An empty token stays
    /// empty - "no auth" is a fact worth telling honestly, and there is nothing to leak.
    pub fn redacted(&self) -> Self {
        let mut c = self.clone();
        if !c.daemon.token.is_empty() {
            c.daemon.token = TOKEN_REDACTED.to_string();
        }
        c
    }

    fn seed() -> Self {
        Self {
            state_rules: vec![
                StateRule {
                    state: "waiting".into(),
                    pattern: r"(?i)(do you want|❯\s*1\.|yes, and don't ask again|press enter to continue)".into(),
                },
                StateRule {
                    state: "working".into(),
                    pattern: r"(?i)(esc to interrupt|to interrupt\))".into(),
                },
            ],
            ..Default::default()
        }
    }

    pub fn session(&self, name: &str) -> Option<&SessionCfg> {
        self.sessions.iter().find(|s| s.name == name)
    }

    pub fn project(&self, name: &str) -> Option<&ProjectCfg> {
        self.projects.iter().find(|p| p.name == name)
    }

    /// The file first, so an entry a person wrote shadows a builtin of the same name the way
    /// a user preset replaces a shipped one.
    pub fn shortcut(&self, name: &str) -> Option<&ShortcutCfg> {
        self.shortcuts
            .iter()
            .chain(builtin_shortcuts())
            .find(|s| s.name == name)
    }

    /// What a client is shown: the file's entries, then the builtins nothing has shadowed.
    pub fn shortcuts_all(&self) -> Vec<ShortcutCfg> {
        let mut all = self.shortcuts.clone();
        all.extend(
            builtin_shortcuts()
                .iter()
                .filter(|b| !self.shortcuts.iter().any(|s| s.name == b.name))
                .cloned(),
        );
        all
    }

    /// A name only the daemon owns: not editable, not deletable, and not in the file.
    pub fn is_builtin_shortcut(&self, name: &str) -> bool {
        !self.shortcuts.iter().any(|s| s.name == name)
            && builtin_shortcuts().iter().any(|b| b.name == name)
    }

    /// A prompt shortcut with no command comes out an agent running the *preset*
    /// `[defaults] agent` names, rather than that preset's command line: the preset is what
    /// hands the sandbox `~/.claude`. An errand naming a preset gets it; one naming a
    /// command line runs that. The project is handed in, the entry being allowed not to
    /// name one.
    pub fn session_for(&self, sc: &ShortcutCfg, name: String, project: String) -> SessionCfg {
        let t = crate::presets::table();
        let own = sc
            .command
            .as_deref()
            .map(str::trim)
            .filter(|c| !c.is_empty());
        let known = own.filter(|c| t.command(c).is_some());

        let (command, cmd) = match (sc.kind, known, own) {
            (_, Some(preset), _) => (preset.to_string(), None),
            (ShortcutKind::Prompt, None, Some(line)) => (String::new(), Some(line.to_string())),
            (ShortcutKind::Prompt, None, None) => (self.command_name(&SessionCfg::default()), None),
            // The shell preset, so a line typed on the errand still runs in one.
            (ShortcutKind::Shell, None, line) => (
                self.defaults.shell.trim().to_string(),
                line.map(str::to_string),
            ),
            (ShortcutKind::Breadcrumb, _, _) => (String::new(), None),
        };
        SessionCfg {
            name,
            project,
            command,
            cmd,
            ..Default::default()
        }
    }

    /// A session naming one that has gone is an error where it matters (starting it)
    /// and a blank directory where it does not (listing it), so the caller decides.
    pub fn project_of(&self, s: &SessionCfg) -> Option<&ProjectCfg> {
        self.project(&s.project)
    }

    pub fn network_of(&self, s: &SessionCfg, p: &ProjectCfg) -> Result<NetworkMode> {
        let mode = s.network.unwrap_or(p.network);
        if !mode.no_wider_than(p.network) {
            bail!(
                "agent {} network mode {:?} exceeds project {} ceiling {:?}",
                s.name,
                mode,
                p.name,
                p.network
            );
        }
        Ok(mode)
    }

    /// The command preset a session runs under, by name. Empty when it states a command
    /// line of its own: an agent that named no preset is not handed one, which is what
    /// keeps `~/.claude` off a session running something else.
    pub fn command_name(&self, s: &SessionCfg) -> String {
        let own = s.command.trim();
        if !own.is_empty() {
            return own.to_string();
        }
        if s.cmd
            .as_deref()
            .map(str::trim)
            .is_some_and(|c| !c.is_empty())
        {
            return String::new();
        }
        match self.defaults.agent.trim() {
            "" => default_agent(),
            a => a.to_string(),
        }
    }

    /// As it will be exec'd: the entry's own answer, else its command preset's. Empty when
    /// it names a preset there is no file for, which `start` refuses rather than guesses at.
    pub fn command_of(&self, s: &SessionCfg) -> String {
        if let Some(c) = s.cmd.as_deref().map(str::trim).filter(|c| !c.is_empty()) {
            return c.to_string();
        }
        crate::presets::table()
            .command(&self.command_name(s))
            .map(|c| c.cmd.clone())
            .unwrap_or_default()
    }

    /// Breadcrumb text in project-then-agent order, with duplicate names removed.
    pub fn breadcrumbs_of(&self, s: &SessionCfg, p: &ProjectCfg) -> Vec<String> {
        let mut names = Vec::new();
        for name in p.breadcrumbs.iter().chain(s.breadcrumbs.iter()) {
            if !names.contains(name) {
                names.push(name.clone());
            }
        }
        names
            .into_iter()
            .filter_map(|name| {
                let b = self.shortcut(&name)?;
                (b.kind == ShortcutKind::Breadcrumb && !b.text.trim().is_empty())
                    .then(|| b.text.clone())
            })
            .collect()
    }

    /// The implicit global base, then its command preset's sandbox presets, the project's,
    /// then its own, plus every preset dependency before the thing that needs it. First mention
    /// wins, as in `paths()`.
    pub fn sandbox_of(&self, s: &SessionCfg, p: &ProjectCfg) -> Vec<String> {
        let t = crate::presets::table();
        let asked: Vec<String> = std::iter::once("global".to_string())
            .chain(
                t.command(&self.command_name(s))
                    .map(|c| c.sandbox.clone())
                    .unwrap_or_default(),
            )
            .chain(p.sandbox.iter().cloned())
            .chain(s.sandbox.iter().cloned())
            .collect();

        fn add(
            name: &str,
            table: &crate::presets::Table,
            out: &mut Vec<String>,
            visiting: &mut Vec<String>,
        ) {
            if out.iter().any(|seen| seen == name) {
                return;
            }
            if visiting.iter().any(|seen| seen == name) {
                tracing::warn!(
                    "sandbox preset dependency cycle at {name:?}, ignoring its back-edge"
                );
                return;
            }
            visiting.push(name.to_string());
            if let Some(preset) = table.sandbox(name) {
                for required in &preset.requires {
                    add(required, table, out, visiting);
                }
            }
            visiting.pop();
            if !out.iter().any(|seen| seen == name) {
                out.push(name.to_string());
            }
        }

        let mut names = Vec::new();
        for name in asked {
            add(&name, &t, &mut names, &mut Vec::new());
        }
        names
    }
}

/// bwrap expands neither `~` nor `$VAR`, and the preset table is written in both. A path
/// naming a variable this machine has not set expands to **nothing at all**, and every bind is
/// skipped unless the path is there, so an unset `WAYLAND_DISPLAY` drops that bind rather than
/// mounting `/run/user/1000/` whole.
///
/// The whole path rather than the one variable, which is not a nicety: `wayland` asks for
/// `$XDG_RUNTIME_DIR/$WAYLAND_DISPLAY`, and with neither set, dropping each in turn leaves the
/// separator behind - `"/"`, which exists, and which was therefore bound read-write into the
/// sandbox. A path about a thing this machine does not have is not a path.
pub fn expand(path: &str) -> String {
    let path = if let Some(rest) = path.strip_prefix("~/") {
        match dirs::home_dir() {
            Some(home) => home.join(rest).to_string_lossy().into_owned(),
            None => path.to_string(),
        }
    } else {
        path.to_string()
    };

    if !path.contains('$') {
        return path;
    }

    let mut out = String::with_capacity(path.len());
    let mut rest = path.as_str();
    while let Some(at) = rest.find('$') {
        out.push_str(&rest[..at]);
        let after = &rest[at + 1..];
        let (name, tail) = if let Some(braced) = after.strip_prefix('{') {
            match braced.find('}') {
                Some(end) => (&braced[..end], &braced[end + 1..]),
                None => {
                    out.push('$');
                    rest = after;
                    continue;
                }
            }
        } else {
            let end = after
                .find(|c: char| !c.is_ascii_alphanumeric() && c != '_')
                .unwrap_or(after.len());
            (&after[..end], &after[end..])
        };
        if name.is_empty() {
            out.push('$');
        } else {
            match std::env::var(name) {
                Ok(v) if !v.is_empty() => out.push_str(&v),
                _ => return String::new(),
            }
        }
        rest = tail;
    }
    out.push_str(rest);
    out
}

#[cfg(test)]
mod tests {
    use super::{
        expand, redact_token_text, temp_dir, Config, NetworkMode, ProjectCfg, SessionCfg,
        ShortcutCfg, ShortcutKind, ShortcutLink, TitlePolicy, TOKEN_REDACTED,
    };

    #[test]
    fn automatic_titles_are_opt_in_and_once_parses() {
        assert_eq!(Config::default().daemon.agent_titles, TitlePolicy::Never);
        let mut cfg = Config::default();
        cfg.daemon.agent_titles = TitlePolicy::Once;
        let back = Config::parse(&toml::to_string_pretty(&cfg).unwrap()).unwrap();
        assert_eq!(back.daemon.agent_titles, TitlePolicy::Once);
    }

    /// What a client sees never carries the secret, and a token that is not set still reads as
    /// not set - "no auth" being a fact worth telling straight.
    #[test]
    fn a_set_token_is_redacted_and_an_empty_one_is_left() {
        let mut cfg = Config::default();
        cfg.daemon.token = "s3cr3t".into();
        assert_eq!(cfg.redacted().daemon.token, TOKEN_REDACTED);

        cfg.daemon.token = String::new();
        assert_eq!(cfg.redacted().daemon.token, "");
    }

    /// The raw editor's copy is redacted in place: the token line, and nothing else - not a
    /// `token` under another table, not an empty one, not the comments around it.
    #[test]
    fn redacting_the_text_touches_only_the_daemon_token() {
        let text = "\
# keep me
[daemon]
bind = \"127.0.0.1:7717\"
token = \"s3cr3t\"
tmux_socket = \"slopworld\"

[[shortcuts]]
name = \"x\"
token = \"not-a-daemon-token\"
";
        let out = redact_token_text(text);
        assert!(out.contains(&format!("token = \"{TOKEN_REDACTED}\"")));
        assert!(!out.contains("s3cr3t"));
        assert!(out.contains("# keep me"));
        // A `token` key in another table is a different thing and is left exactly.
        assert!(out.contains("token = \"not-a-daemon-token\""));

        // Nothing to hide: an unset token is not rewritten to the sentinel.
        let empty = "[daemon]\ntoken = \"\"\n";
        assert_eq!(redact_token_text(empty), empty);
    }

    /// The round trip a save has to survive: the sentinel a client hands back parses as the
    /// sentinel, which the manager restores. Here we prove the shape a write receives.
    #[test]
    fn the_sentinel_round_trips_as_itself() {
        let cfg = Config::parse(&format!(
            "[daemon]\nbind = \"127.0.0.1:7717\"\ntoken = \"{TOKEN_REDACTED}\"\ntmux_socket = \"slopworld\"\npoll_ms = 80\n"
        ))
        .expect("config with the sentinel should parse");
        assert_eq!(cfg.daemon.token, TOKEN_REDACTED);
    }

    /// A prompt shortcut that names no command comes out running the preset
    /// `[defaults] agent` names: a preset is what hands the sandbox ~/.claude.
    /// A path is about a thing this machine has, or it is not a path. The two-variable case is
    /// the one that mattered: dropping each in turn used to leave the separator behind, and
    /// `"/"` exists, so `wayland` on a machine with no Wayland bound the whole filesystem
    /// read-write. `sandbox::refused` would stop that now; nothing should get that far.
    #[test]
    fn a_path_naming_a_variable_this_machine_lacks_is_nothing() {
        assert_eq!(expand("$SLOPD_NO_SUCH_VAR_A/thing"), "");
        assert_eq!(expand("$SLOPD_NO_SUCH_VAR_A/$SLOPD_NO_SUCH_VAR_B"), "");
        assert_eq!(expand("${SLOPD_NO_SUCH_VAR_A}/thing"), "");

        // What has no variable in it is left exactly, and one that is set is filled in.
        assert_eq!(expand("/usr/lib"), "/usr/lib");
        assert!(expand("$PATH/bin").ends_with("/bin"));
        assert_ne!(expand("$PATH/bin"), "/bin");
        if let Some(home) = dirs::home_dir() {
            assert_eq!(expand("~/x"), home.join("x").to_string_lossy());
        }
    }

    #[test]
    fn shortcuts_become_sessions() {
        let cfg = Config::parse(
            r#"
            [defaults]
            agent = "pi"
            shell = "shell"

            [[shortcut]]
            name = "review diff"
            project = "slopworld"
            text = "review the working diff"

            [[shortcut]]
            name = "tests"
            kind = "shell"
            project = "slopworld"
            text = "make test"

            [[shortcut]]
            name = "codex"
            project = "slopworld"
            text = "have a look"
            command = "codex --yolo"
            "#,
        )
        .expect("shortcuts should parse");

        let sc = cfg.shortcut("review diff").unwrap();
        let prompt = cfg.session_for(sc, "review-diff".into(), sc.project.clone());
        // The preset this machine calls its default, rather than that preset's command.
        assert_eq!(prompt.command, "pi");
        assert_eq!(prompt.cmd, None);
        assert_eq!(cfg.command_of(&prompt), "pi");
        assert_eq!(
            cfg.sandbox_of(&prompt, &Default::default()),
            vec!["global", "pi"]
        );
        assert_eq!(prompt.project, "slopworld");

        let shell = cfg.session_for(cfg.shortcut("tests").unwrap(), "tests".into(), "x".into());
        assert_eq!(shell.command, "shell");
        assert_eq!(cfg.command_of(&shell), "bash");

        // A command line rather than a preset name: run as it stands, with only the implicit
        // global base and no agent's state directory.
        let custom = cfg.session_for(cfg.shortcut("codex").unwrap(), "codex".into(), "x".into());
        assert_eq!(custom.command, "");
        assert_eq!(cfg.command_of(&custom), "codex --yolo");
        assert_eq!(cfg.sandbox_of(&custom, &Default::default()), vec!["global"]);

        // The place is the caller's answer and not the entry's, which is what lets one
        // errand be run somewhere it never named.
        let anywhere = cfg.session_for(sc, "review-diff-2".into(), "elsewhere".into());
        assert_eq!(anywhere.project, "elsewhere");
    }

    #[test]
    fn preset_dependencies_arrive_before_the_preset_that_needs_them() {
        let cfg = Config::default();
        let session = SessionCfg {
            command: "shell".into(),
            sandbox: vec!["systemd".into()],
            ..Default::default()
        };
        assert_eq!(
            cfg.sandbox_of(&session, &Default::default()),
            vec!["global", "dbus", "systemd"]
        );
    }

    /// An entry written before links existed has to keep meaning what it did: a
    /// shortcut that names a project runs there.
    #[test]
    fn shortcut_links_round_trip_and_default_to_the_project() {
        let cfg = Config::parse(
            r#"
            [[shortcut]]
            name = "old"
            project = "slopworld"
            text = "carry on"

            [[shortcut]]
            name = "scratch"
            link = "temp"
            text = "have a go"

            [[shortcut]]
            name = "wherever"
            link = "ask"
            text = "you decide"
            "#,
        )
        .expect("links should parse");

        assert_eq!(cfg.shortcut("old").unwrap().link, ShortcutLink::Project);
        assert_eq!(cfg.shortcut("scratch").unwrap().link, ShortcutLink::Temp);
        assert_eq!(cfg.shortcut("wherever").unwrap().link, ShortcutLink::Ask);

        let back = Config::parse(&toml::to_string_pretty(&cfg).unwrap()).unwrap();
        assert_eq!(back.shortcut("scratch").unwrap().link, ShortcutLink::Temp);
        assert_eq!(back.shortcut("wherever").unwrap().link, ShortcutLink::Ask);
    }

    /// The directory under it is coined; the point is that nobody typed it.
    #[test]
    fn temp_projects_name_their_own_directory() {
        assert_eq!(temp_dir("scratch"), "/tmp/slopworld/scratch");

        let cfg = Config::parse(
            r#"
            [[project]]
            name = "scratch"
            dir = "/tmp/slopworld/scratch"
            temp = true
            "#,
        )
        .expect("a temp project should parse");

        assert!(cfg.project("scratch").unwrap().temp);
        // And an ordinary one is not one by accident.
        let plain = Config::parse(
            r#"
            [[project]]
            name = "repo"
            dir = "/home/you/git/repo"
            "#,
        )
        .unwrap();
        assert!(!plain.project("repo").unwrap().temp);
    }

    #[test]
    fn agent_network_can_only_reduce_the_project_ceiling() {
        let cfg = Config::parse(
            r#"
            [[project]]
            name = "repo"
            dir = "/home/you/git/repo"
            network = "private"

            [[session]]
            name = "safe"
            project = "repo"
            network = "none"

            [[session]]
            name = "too-wide"
            project = "repo"
            network = "host"
            "#,
        )
        .expect("network modes should parse");

        let project = cfg.project("repo").unwrap();
        assert_eq!(
            cfg.network_of(cfg.session("safe").unwrap(), project)
                .unwrap(),
            NetworkMode::None
        );
        assert!(cfg
            .network_of(cfg.session("too-wide").unwrap(), project)
            .is_err());
        assert_eq!(
            cfg.network_of(
                &SessionCfg {
                    name: "inherited".into(),
                    project: "repo".into(),
                    ..Default::default()
                },
                project,
            )
            .unwrap(),
            NetworkMode::Private
        );
    }

    /// A shortcut that came back as a prompt would run the wrong thing in the right
    /// place.
    #[test]
    fn shortcuts_round_trip_through_toml() {
        let mut cfg = Config::default();
        cfg.shortcuts.push(ShortcutCfg {
            name: "tests".into(),
            kind: ShortcutKind::Shell,
            link: ShortcutLink::Project,
            project: "slopworld".into(),
            text: "make test".into(),
            command: None,
            builtin: false,
        });

        let back = Config::parse(&toml::to_string_pretty(&cfg).unwrap()).unwrap();
        let sc = back.shortcut("tests").expect("shortcut should survive");
        assert_eq!(sc.kind, ShortcutKind::Shell);
        assert_eq!(sc.text, "make test");
        assert!(sc.command.is_none());
    }

    #[test]
    fn breadcrumb_yolo_defaults_on_and_only_writes_the_opt_out() {
        let old: SessionCfg = toml::from_str("name = 'Ada'").unwrap();
        assert!(old.breadcrumb_yolo);
        assert!(!toml::to_string(&old).unwrap().contains("breadcrumb_yolo"));

        let opted_out = SessionCfg {
            breadcrumb_yolo: false,
            ..Default::default()
        };
        assert!(toml::to_string(&opted_out)
            .unwrap()
            .contains("breadcrumb_yolo = false"));
    }

    /// A shipped breadcrumb is offered like any other and written down like none of them:
    /// the file is what a person owns, and a builtin that leaked into it would come back as
    /// an ordinary entry the next binary could not correct.
    #[test]
    fn the_shipped_breadcrumb_is_offered_but_never_written_down() {
        let cfg = Config::default();
        assert!(cfg.shortcuts.is_empty());

        let sc = cfg
            .shortcut("Useful tips")
            .expect("shipped with the daemon");
        assert_eq!(sc.kind, ShortcutKind::Breadcrumb);
        assert!(sc.builtin);
        assert_eq!(sc.text.matches("{{ random_tip }}").count(), 5);
        assert!(cfg.is_builtin_shortcut("Useful tips"));
        assert!(cfg
            .shortcuts_all()
            .iter()
            .any(|s| s.name == "Useful tips" && s.builtin));

        // Saving the config states nothing about it, and reading it back does not double it.
        let text = toml::to_string_pretty(&cfg).unwrap();
        assert!(!text.contains("Useful tips"));
        let back = Config::parse(&text).unwrap();
        assert_eq!(
            back.shortcuts_all()
                .iter()
                .filter(|s| s.name == "Useful tips")
                .count(),
            1
        );
    }

    /// The same rule a user preset gets: a written entry of that name is the one that is
    /// read, and the builtin stops being one - so it can be edited and deleted again.
    #[test]
    fn a_written_entry_shadows_the_builtin_it_is_named_after() {
        let mut cfg = Config::default();
        cfg.shortcuts.push(ShortcutCfg {
            name: "Useful tips".into(),
            kind: ShortcutKind::Breadcrumb,
            text: "mine".into(),
            ..Default::default()
        });

        assert_eq!(cfg.shortcut("Useful tips").unwrap().text, "mine");
        assert!(!cfg.is_builtin_shortcut("Useful tips"));
        assert_eq!(cfg.shortcuts_all().len(), 1);
    }

    /// Project first, then the agent's own, each name once however many times it is asked
    /// for - and the shipped one resolves like anything else.
    #[test]
    fn breadcrumbs_resolve_in_order_and_only_once() {
        let mut cfg = Config::default();
        cfg.shortcuts.push(ShortcutCfg {
            name: "house rules".into(),
            kind: ShortcutKind::Breadcrumb,
            text: "never commit".into(),
            ..Default::default()
        });
        // Not a breadcrumb, so an attachment naming it resolves to nothing.
        cfg.shortcuts.push(ShortcutCfg {
            name: "tests".into(),
            kind: ShortcutKind::Shell,
            text: "make test".into(),
            ..Default::default()
        });

        let p = ProjectCfg {
            name: "repo".into(),
            breadcrumbs: vec!["Useful tips".into(), "house rules".into()],
            ..Default::default()
        };
        let s = SessionCfg {
            name: "claude".into(),
            project: "repo".into(),
            breadcrumbs: vec!["house rules".into(), "tests".into(), "gone".into()],
            ..Default::default()
        };

        let out = cfg.breadcrumbs_of(&s, &p);
        assert_eq!(out.len(), 2);
        assert!(out[0].starts_with("___"));
        assert_eq!(out[1], "never commit");
    }

    #[test]
    fn config_round_trips_through_toml() {
        let mut cfg = Config::default();
        cfg.daemon.history_limit = 200;
        cfg.daemon.game_cmd = "/home/you/RimWorld/game/RimWorldLinux -popupwindow".into();
        cfg.projects.push(ProjectCfg {
            name: "repo".into(),
            dir: "/home/you/repo".into(),
            network: NetworkMode::Host,
            ..Default::default()
        });
        cfg.sessions.push(SessionCfg {
            name: "quiet".into(),
            project: "repo".into(),
            network: Some(NetworkMode::None),
            ..Default::default()
        });

        let text = toml::to_string_pretty(&cfg).unwrap();
        let back = Config::parse(&text).unwrap();

        assert_eq!(back.daemon.history_limit, 200);
        assert_eq!(back.daemon.game_cmd, cfg.daemon.game_cmd);
        assert_eq!(back.project("repo").unwrap().network, NetworkMode::Host);
        assert_eq!(
            back.session("quiet").unwrap().network,
            Some(NetworkMode::None)
        );
    }

    #[test]
    fn old_session_entries_can_lack_a_state_identity() {
        let old = Config::parse(
            r#"
                [[project]]
                name = "repo"
                dir = "/tmp"

                [[session]]
                name = "agent"
                project = "repo"
            "#,
        )
        .unwrap();
        assert!(old.session("agent").unwrap().state_id.is_empty());

        // New in-memory sessions, including short-lived errands, always have an identity.
        assert!(!SessionCfg::default().state_id.is_empty());
    }
}
