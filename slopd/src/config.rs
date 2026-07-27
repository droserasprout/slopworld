use std::path::{Path, PathBuf};

use anyhow::{Context, Result};
use serde::{Deserialize, Serialize};

/// Everything slopd knows lives in one TOML file. The mod can read it and write
/// it back verbatim, so hand-edits and in-game edits use the same format.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct Config {
    #[serde(default)]
    pub daemon: Daemon,
    #[serde(default)]
    pub defaults: Defaults,
    #[serde(default)]
    pub sandbox: Sandbox,
    /// Where the work is. A session is an agent *in* one of these, and takes its
    /// directory and its sandbox from it rather than carrying either.
    #[serde(default, rename = "project")]
    pub projects: Vec<ProjectCfg>,
    #[serde(default, rename = "session")]
    pub sessions: Vec<SessionCfg>,
    /// One-shot errands: a stock prompt, or a shell command, run by a temporary
    /// agent that exists only as long as its process does.
    #[serde(default, rename = "shortcut")]
    pub shortcuts: Vec<ShortcutCfg>,
    #[serde(default, rename = "state_rule")]
    pub state_rules: Vec<StateRule>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Daemon {
    pub bind: String,
    /// Shared secret. Empty means no auth, which is fine on a loopback bind.
    #[serde(default)]
    pub token: String,
    /// tmux server socket name. Dedicated so we never collide with the user's
    /// own tmux, and so `tmux -L slopworld attach` still works from a real term.
    pub tmux_socket: String,
    /// How often, in milliseconds, the in-memory state tick re-runs the rules so
    /// a session that fell quiet decays working -> idle. Screen content itself
    /// arrives event-driven from the control readers, not by polling.
    pub poll_ms: u64,
    /// Lines of scrollback tmux keeps per pane. It is also all we have to reseed
    /// an emulator from when slopd restarts under a session that kept running,
    /// so it is the ceiling on how much history survives a daemon redeploy.
    #[serde(default = "default_history_limit")]
    pub history_limit: u32,
    /// How the game is launched, for the in-game "save and restart". Empty
    /// disables the endpoint. Split like an agent command: no shell, no globbing.
    #[serde(default)]
    pub game_cmd: String,
    /// Whether to poll Anthropic for what is left of the subscription, which is
    /// what the mod draws as its resource readout. Off means slopd never reads
    /// the credentials file and never leaves the machine.
    #[serde(default = "yes")]
    pub usage: bool,
    /// Seconds between usage polls. The windows it reports move in minutes, so
    /// there is nothing to gain by asking often; floored at 10 in the poller.
    #[serde(default = "default_usage_poll")]
    pub usage_poll_secs: u64,
    /// Where Claude Code keeps the OAuth token those polls are made with. Read
    /// fresh each time and never copied, so a refresh behind us is picked up.
    #[serde(default = "default_credentials")]
    pub claude_credentials: String,
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

impl Default for Daemon {
    fn default() -> Self {
        Self {
            bind: "127.0.0.1:7717".into(),
            token: String::new(),
            tmux_socket: "slopworld".into(),
            poll_ms: 80,
            history_limit: default_history_limit(),
            game_cmd: String::new(),
            usage: true,
            usage_poll_secs: default_usage_poll(),
            claude_credentials: default_credentials(),
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Defaults {
    /// Command run inside the sandbox. Split on whitespace, no shell involved.
    pub agent: String,
    /// What a shell shortcut runs. Here rather than in every shortcut for the
    /// same reason `agent` is: which shell this machine has is an answer about
    /// the machine, and repeating it in five entries means getting it wrong in
    /// one. Bare because tmux hands it a pty, so it is interactive already.
    #[serde(default = "default_shell")]
    pub shell: String,
}

fn default_shell() -> String {
    "bash".into()
}

impl Default for Defaults {
    fn default() -> Self {
        Self {
            agent: "claude".into(),
            shell: default_shell(),
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Sandbox {
    pub enabled: bool,
    /// Bound read-only into every sandbox, whatever the project.
    pub ro_paths: Vec<String>,
    /// Bound read-write into every sandbox, on top of the project's own dir and
    /// whatever its presets ask for. An agent's own state dir is not here any
    /// more: `~/.claude` rides on the `claude` preset, which a Claude session
    /// gets whether or not its project asked, so the one bind every agent of
    /// that kind needs is not a line somebody has to remember to type.
    pub rw_paths: Vec<String>,
    /// Env vars passed through from slopd's environment.
    pub pass_env: Vec<String>,
}

impl Default for Sandbox {
    fn default() -> Self {
        Self {
            enabled: true,
            // ~/.local/bin so agent-run tools (MCP servers, wrappers) on PATH
            // resolve inside the sandbox; without it their spawn fails with ENOENT.
            ro_paths: vec![
                "/usr".into(),
                "/etc".into(),
                "/opt".into(),
                "~/.local/bin".into(),
            ],
            rw_paths: Vec::new(),
            pass_env: vec!["PATH".into(), "TERM".into(), "LANG".into()],
        }
    }
}

/// One place work happens: a directory plus the sandbox every agent in it gets.
///
/// It exists because the two things a session used to carry - where it runs and
/// what it can reach - are properties of the *work*, not of the agent doing it.
/// Three agents in the same repo want the same binds, and keeping that in three
/// session entries meant it was wrong in at least one of them.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ProjectCfg {
    pub name: String,
    pub dir: String,
    /// Named bundles of binds and env from `sandbox::PRESETS` - "dbus",
    /// "systemd", "x11" and so on. A name this build has never heard of is
    /// ignored with a warning rather than refused, because the file outlives the
    /// binary and a preset removed upstream must not stop a project starting.
    #[serde(default)]
    pub presets: Vec<String>,
    /// Anything the presets do not cover, added on top of the global lists.
    #[serde(default)]
    pub ro_paths: Vec<String>,
    #[serde(default)]
    pub rw_paths: Vec<String>,
    #[serde(default)]
    pub pass_env: Vec<String>,
    #[serde(default = "yes")]
    pub net: bool,
    #[serde(default = "yes")]
    pub sandbox: bool,
}

impl Default for ProjectCfg {
    fn default() -> Self {
        Self {
            name: String::new(),
            dir: String::new(),
            presets: Vec::new(),
            ro_paths: Vec::new(),
            rw_paths: Vec::new(),
            pass_env: Vec::new(),
            net: true,
            sandbox: true,
        }
    }
}

/// What a session runs. Claude Code is a first-class answer rather than a
/// command string, because it is the one agent this whole thing is shaped
/// around: knowing it is Claude is what lets the sandbox hand it its own state
/// dir without anyone listing `~/.claude` in a project by hand.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum SessionKind {
    #[default]
    Claude,
    Custom,
}

impl SessionKind {
    /// The same spelling serde uses, for the wire view.
    pub fn as_str(self) -> &'static str {
        match self {
            Self::Claude => "claude",
            Self::Custom => "custom",
        }
    }
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct SessionCfg {
    pub name: String,
    /// The project this agent works in. Everything about where it runs and what
    /// it can reach comes from there.
    #[serde(default)]
    pub project: String,
    #[serde(default)]
    pub kind: SessionKind,
    /// Read only when `kind` is custom; the Claude kind takes `[defaults] agent`.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub command: Option<String>,
    #[serde(default)]
    pub autostart: bool,

    // ---------------------------------------------------------------- legacy
    // Sessions used to carry all of this themselves. `Config::migrate` turns
    // each one into a project on load and clears these, so an old file keeps
    // working and the next write is in the new shape. Skipped when empty so
    // they leave the file for good rather than lingering as nulls.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub dir: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub agent: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub net: Option<bool>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub sandbox: Option<bool>,
    #[serde(default, skip_serializing_if = "Vec::is_empty")]
    pub rw_paths: Vec<String>,
}

/// What a shortcut sends its text *to*, which is the only thing the two kinds
/// disagree about at the far end: an agent's input field, or a shell's prompt.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum ShortcutKind {
    /// A stock prompt handed to an agent - Claude Code unless the entry says
    /// otherwise, which is what makes it a prompt rather than a command.
    #[default]
    Prompt,
    /// A command handed to an interactive shell inside the project's sandbox.
    Shell,
}

/// One errand, kept because it is worth repeating.
///
/// A shortcut is a session template with a line of text attached: where to run
/// (a project), what to run there, and what to type into it once it is up. The
/// agent it lands is temporary - it is never written to this file and it goes
/// when its process does - so what is saved here is the errand, not the agent.
///
/// The template is spelled out rather than pointing at an existing session,
/// because a shortcut that names an agent stops working the day that agent is
/// deleted, and the thing it actually needs from it - the project - is the one
/// field that would have been copied anyway.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct ShortcutCfg {
    /// Free-form: it labels a button and seeds a colonist's name, and the
    /// session name derived from it is sanitised (see `slug`).
    pub name: String,
    #[serde(default)]
    pub kind: ShortcutKind,
    /// The project the temporary agent works in - its directory and its whole
    /// sandbox, the same as for any other agent.
    #[serde(default)]
    pub project: String,
    /// What gets typed in. A prompt for the agent, a command line for the shell.
    #[serde(default)]
    pub text: String,
    /// Overrides what runs: another agent for a prompt shortcut, another shell
    /// for a shell one. Empty means `[defaults] agent` or `[defaults] shell`.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub command: Option<String>,
}

fn yes() -> bool {
    true
}

/// Screen-scraping heuristics that turn a pane's text into an agent state.
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
        let mut cfg: Self = toml::from_str(text).context("parsing config.toml")?;
        cfg.migrate();
        Ok(cfg)
    }

    /// Brings a config written before projects existed up to date.
    ///
    /// A session used to be a directory and a sandbox with a name on it; now it
    /// is an agent inside a project. Every entry still shaped the old way gets a
    /// project of its own, and two sessions pointing at the same directory get
    /// the *same* project - which is the arrangement whoever wrote that file
    /// meant, and the one they would have made by hand.
    ///
    /// Runs on every load, including of a file we just wrote, so it has to be
    /// idempotent: an entry that already names a project is left alone.
    fn migrate(&mut self) {
        for i in 0..self.sessions.len() {
            let dir = self.sessions[i].dir.take().unwrap_or_default();
            let agent = self.sessions[i].agent.take();
            let net = self.sessions[i].net.take();
            let sandbox = self.sessions[i].sandbox.take();
            let rw = std::mem::take(&mut self.sessions[i].rw_paths);

            if let Some(a) = agent {
                // A command that was written out is one somebody chose, so it
                // survives as a custom session. The default is what "claude"
                // means, and an entry that never set one is exactly that.
                self.sessions[i].kind = SessionKind::Custom;
                self.sessions[i].command = Some(a);
            }

            if !self.sessions[i].project.is_empty() || dir.is_empty() {
                continue;
            }

            let net = net.unwrap_or(true);
            let sandbox = sandbox.unwrap_or(true);
            let existing = self
                .projects
                .iter()
                .find(|p| p.dir == dir && p.net == net && p.sandbox == sandbox && p.rw_paths == rw)
                .map(|p| p.name.clone());

            let name = match existing {
                Some(n) => n,
                None => {
                    let name = self.free_project_name(&dir, &self.sessions[i].name);
                    self.projects.push(ProjectCfg {
                        name: name.clone(),
                        dir: dir.clone(),
                        rw_paths: rw,
                        net,
                        sandbox,
                        ..Default::default()
                    });
                    name
                }
            };
            self.sessions[i].project = name;
        }
    }

    /// A project name nothing else has yet: the directory's own last component
    /// first, because that is what a person would have called it, then the
    /// session's name, then a number.
    fn free_project_name(&self, dir: &str, session: &str) -> String {
        let base = dir
            .trim_end_matches('/')
            .rsplit('/')
            .next()
            .filter(|s| !s.is_empty())
            .unwrap_or(session)
            .to_string();

        let taken = |n: &str| self.projects.iter().any(|p| p.name == n);
        if !taken(&base) {
            return base;
        }
        if !taken(session) {
            return session.to_string();
        }
        (2..)
            .map(|i| format!("{base}-{i}"))
            .find(|n| !taken(n))
            .unwrap()
    }

    pub fn save(&self, path: &Path) -> Result<()> {
        if let Some(parent) = path.parent() {
            std::fs::create_dir_all(parent)?;
        }
        std::fs::write(path, toml::to_string_pretty(self)?)?;
        Ok(())
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

    pub fn shortcut(&self, name: &str) -> Option<&ShortcutCfg> {
        self.shortcuts.iter().find(|s| s.name == name)
    }

    /// The agent a shortcut lands, under the name it is to be called by.
    ///
    /// A prompt shortcut with nothing to say about the command is a *Claude*
    /// session rather than a custom one running `[defaults] agent`, and the
    /// difference is not cosmetic: the kind is what hands the sandbox
    /// `~/.claude`, so spelling the command out here would land an agent
    /// without its own state dir.
    pub fn session_for(&self, sc: &ShortcutCfg, name: String) -> SessionCfg {
        let (kind, command) = match sc.kind {
            ShortcutKind::Prompt => match sc.command.as_deref().map(str::trim) {
                Some(c) if !c.is_empty() => (SessionKind::Custom, Some(c.to_string())),
                _ => (SessionKind::Claude, None),
            },
            ShortcutKind::Shell => {
                let c = sc
                    .command
                    .as_deref()
                    .map(str::trim)
                    .filter(|c| !c.is_empty())
                    .unwrap_or(&self.defaults.shell);
                (SessionKind::Custom, Some(c.to_string()))
            }
        };
        SessionCfg {
            name,
            project: sc.project.clone(),
            kind,
            command,
            ..Default::default()
        }
    }

    /// The project a session belongs to. A session naming one that has gone is
    /// an error everywhere it matters (starting it), and merely a blank
    /// directory everywhere it does not (listing it), so the caller decides.
    pub fn project_of(&self, s: &SessionCfg) -> Option<&ProjectCfg> {
        self.project(&s.project)
    }

    /// What a session actually runs: its own command when it is a custom one,
    /// and the daemon's default - which is what "Claude Code" means here - when
    /// it is not.
    pub fn command_of(&self, s: &SessionCfg) -> String {
        match s.kind {
            SessionKind::Custom => s
                .command
                .clone()
                .filter(|c| !c.trim().is_empty())
                .unwrap_or_else(|| self.defaults.agent.clone()),
            SessionKind::Claude => self.defaults.agent.clone(),
        }
    }
}

/// `~/foo` -> `/home/you/foo`, `$XDG_RUNTIME_DIR/bus` -> `/run/user/1000/bus`.
/// bwrap will not do either for us, and the preset table is written in both.
///
/// A variable with nothing behind it expands to nothing, which leaves a path
/// that cannot exist - and every bind is skipped unless the path is there, so an
/// unset `WAYLAND_DISPLAY` drops that bind instead of mounting `/run/user/1000/`
/// whole.
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
            out.push_str(&std::env::var(name).unwrap_or_default());
        }
        rest = tail;
    }
    out.push_str(rest);
    out
}

#[cfg(test)]
mod tests {
    use super::{Config, SessionKind, ShortcutCfg, ShortcutKind};

    /// A config written before these fields existed has to keep loading: the file
    /// on disk outlives any one build of the daemon, and a redeploy that refused
    /// to start would take the agents' only supervisor with it.
    #[test]
    fn older_config_keeps_loading() {
        let cfg = Config::parse(
            r#"
            [daemon]
            bind = "127.0.0.1:7717"
            tmux_socket = "slopworld"
            poll_ms = 80
            "#,
        )
        .expect("old config should parse");

        assert_eq!(cfg.daemon.history_limit, 5000);
        assert_eq!(cfg.daemon.game_cmd, "");
        // Usage polling defaults on, so an existing install gets the readout
        // without anyone editing a file - and off is one line when it is not
        // wanted.
        assert!(cfg.daemon.usage);
        assert_eq!(cfg.daemon.usage_poll_secs, 60);
        assert_eq!(cfg.daemon.claude_credentials, "~/.claude/.credentials.json");
        // A file written before shortcuts existed has none, and a shell to run
        // them with regardless.
        assert!(cfg.shortcuts.is_empty());
        assert_eq!(cfg.defaults.shell, "bash");
    }

    /// The two kinds differ in what they land, and a prompt shortcut that names
    /// no command has to come out a *Claude* session: the kind is what hands the
    /// sandbox ~/.claude, so a custom session running the same string would be
    /// an agent without its own state dir.
    #[test]
    fn shortcuts_become_sessions() {
        let cfg = Config::parse(
            r#"
            [defaults]
            agent = "claude --model opus"
            shell = "fish"

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

        let prompt = cfg.session_for(cfg.shortcut("review diff").unwrap(), "review-diff".into());
        assert_eq!(prompt.kind, SessionKind::Claude);
        assert_eq!(prompt.command, None);
        assert_eq!(cfg.command_of(&prompt), "claude --model opus");
        assert_eq!(prompt.project, "slopworld");

        let shell = cfg.session_for(cfg.shortcut("tests").unwrap(), "tests".into());
        assert_eq!(shell.kind, SessionKind::Custom);
        assert_eq!(cfg.command_of(&shell), "fish");

        let custom = cfg.session_for(cfg.shortcut("codex").unwrap(), "codex".into());
        assert_eq!(custom.kind, SessionKind::Custom);
        assert_eq!(cfg.command_of(&custom), "codex --yolo");
    }

    /// Shortcuts survive the round trip the GUI puts every write through, kind
    /// and all - a shortcut that came back as a prompt would run the wrong
    /// thing in the right place.
    #[test]
    fn shortcuts_round_trip_through_toml() {
        let mut cfg = Config::default();
        cfg.shortcuts.push(ShortcutCfg {
            name: "tests".into(),
            kind: ShortcutKind::Shell,
            project: "slopworld".into(),
            text: "make test".into(),
            command: None,
        });

        let back = Config::parse(&toml::to_string_pretty(&cfg).unwrap()).unwrap();
        let sc = back.shortcut("tests").expect("shortcut should survive");
        assert_eq!(sc.kind, ShortcutKind::Shell);
        assert_eq!(sc.text, "make test");
        assert!(sc.command.is_none());
    }

    /// The one that matters most: a file written when a session *was* a
    /// directory keeps its agents, and comes out the other side with a project
    /// under each of them. Two sessions in the same directory share it, because
    /// that is the arrangement whoever wrote the file meant.
    #[test]
    fn sessions_with_dirs_become_projects() {
        let cfg = Config::parse(
            r#"
            [[session]]
            name = "alpha"
            dir = "/home/you/git/slopworld"

            [[session]]
            name = "beta"
            dir = "/home/you/git/slopworld"

            [[session]]
            name = "gamma"
            dir = "/home/you/git/other"
            agent = "codex --yolo"
            net = false
            "#,
        )
        .expect("an old config should parse");

        assert_eq!(cfg.projects.len(), 2);
        assert_eq!(cfg.session("alpha").unwrap().project, "slopworld");
        assert_eq!(cfg.session("beta").unwrap().project, "slopworld");
        assert_eq!(cfg.session("gamma").unwrap().project, "other");

        // The flags followed the directory into the project.
        assert!(cfg.project("slopworld").unwrap().net);
        assert!(!cfg.project("other").unwrap().net);

        // A command somebody wrote out is a custom session; one that never set
        // one is what "Claude Code" means.
        assert_eq!(cfg.session("alpha").unwrap().kind, SessionKind::Claude);
        assert_eq!(cfg.session("gamma").unwrap().kind, SessionKind::Custom);
        assert_eq!(
            cfg.command_of(cfg.session("gamma").unwrap()),
            "codex --yolo"
        );
        assert_eq!(cfg.command_of(cfg.session("alpha").unwrap()), "claude");
    }

    /// Migration runs on every load, including of a file it wrote itself, so a
    /// second pass has to be a no-op rather than a second project.
    #[test]
    fn migration_is_idempotent() {
        let once = Config::parse(
            r#"
            [[session]]
            name = "alpha"
            dir = "/home/you/git/slopworld"
            "#,
        )
        .unwrap();
        let twice = Config::parse(&toml::to_string_pretty(&once).unwrap()).unwrap();

        assert_eq!(twice.projects.len(), 1);
        assert_eq!(twice.session("alpha").unwrap().project, "slopworld");
        // The legacy fields left the file rather than lingering as empties.
        assert!(twice.session("alpha").unwrap().dir.is_none());
    }

    #[test]
    fn config_round_trips_through_toml() {
        let mut cfg = Config::default();
        cfg.daemon.history_limit = 200;
        cfg.daemon.game_cmd = "/home/you/RimWorld/game/RimWorldLinux -popupwindow".into();

        let text = toml::to_string_pretty(&cfg).unwrap();
        let back = Config::parse(&text).unwrap();

        assert_eq!(back.daemon.history_limit, 200);
        assert_eq!(back.daemon.game_cmd, cfg.daemon.game_cmd);
    }
}
