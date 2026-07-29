use std::path::{Path, PathBuf};

use anyhow::{Context, Result};
use serde::{Deserialize, Serialize};

/// One TOML file, which the mod reads and writes back verbatim, so hand-edits and
/// in-game edits use the same format.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct Config {
    #[serde(default)]
    pub daemon: Daemon,
    #[serde(default)]
    pub defaults: Defaults,
    #[serde(default)]
    pub sandbox: Sandbox,
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

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Daemon {
    pub bind: String,
    /// Empty means no auth, which is fine on a loopback bind.
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
    /// Empty disables the endpoint. Split like an agent command: no shell.
    #[serde(default)]
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
    /// Split on whitespace, no shell involved.
    pub agent: String,
    /// Here rather than in every shortcut for the same reason `agent` is: which shell
    /// this machine has is an answer about the machine. Bare, because tmux hands it a
    /// pty and it is interactive already.
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
/// The base every sandbox is built on. There is no master switch here: whether an
/// agent is sandboxed is the project's answer (`ProjectCfg::sandbox`) and only the
/// project's, because a daemon-wide override that silently beat every one of those
/// checkboxes read as a checkbox that did nothing.
pub struct Sandbox {
    /// Bound into every sandbox, whatever the project.
    pub ro_paths: Vec<String>,
    /// An agent's own state dir is not here: `~/.claude` rides on the `claude` preset,
    /// which a Claude session gets whether or not its project asked.
    pub rw_paths: Vec<String>,
    pub pass_env: Vec<String>,
}

impl Default for Sandbox {
    fn default() -> Self {
        Self {
            // ~/.local/bin so agent-run tools on PATH resolve inside the sandbox; without it
            // their spawn fails with ENOENT.
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

/// Under `/tmp` deliberately: the machine clears it, so nothing here has to decide
/// when scratch work has outlived its use.
pub const TEMP_ROOT: &str = "/tmp/slopworld";

/// Coined rather than typed, which is the whole point of the flag.
pub fn temp_dir(name: &str) -> String {
    format!("{TEMP_ROOT}/{name}")
}

/// Three agents in the same repo want the same binds, and keeping that in three
/// session entries meant it was wrong in at least one of them.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ProjectCfg {
    pub name: String,
    /// Defaulted rather than required, because a temporary project has none to give.
    /// `check_project` is what insists on one for every other kind.
    #[serde(default)]
    pub dir: String,
    /// The directory is `TEMP_ROOT/<name>`, coined when the entry is written and made
    /// when the first agent starts. What is temporary is the *ground*, not the entry.
    /// The other kind of temporary project is never written here at all; see
    /// `ShortcutLink::Temp`.
    #[serde(default)]
    pub temp: bool,
    /// A name this build has never heard of is ignored with a warning rather than
    /// refused, because the file outlives the binary.
    #[serde(default)]
    pub presets: Vec<String>,
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
            temp: false,
            presets: Vec::new(),
            ro_paths: Vec::new(),
            rw_paths: Vec::new(),
            pass_env: Vec::new(),
            net: true,
            sandbox: true,
        }
    }
}

/// Claude is a kind rather than a command string because knowing it is Claude is
/// what lets the sandbox hand it `~/.claude`.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum SessionKind {
    #[default]
    Claude,
    Custom,
}

impl SessionKind {
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
    /// Everything about where it runs and what it can reach comes from there.
    #[serde(default)]
    pub project: String,
    #[serde(default)]
    pub kind: SessionKind,
    /// Read only when `kind` is custom; the Claude kind takes `[defaults] agent`.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub command: Option<String>,
    #[serde(default)]
    pub autostart: bool,

    // Legacy. `Config::migrate` turns each old session into a project on load and
    // clears these. Skipped when empty, so they leave the file for good rather than
    // lingering as nulls.
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
}

/// The one thing about a shortcut allowed not to be decided in advance.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum ShortcutLink {
    /// The default, because it is what every shortcut written before this existed
    /// meant.
    #[default]
    Project,
    /// One per run, never written to this file: coined when the errand starts and
    /// dropped when the agent goes. The entry's `project`, if it names one, is what
    /// the fresh one copies its sandbox from.
    Temp,
    /// Whoever runs it says where, per run.
    Ask,
}

/// A session template with a line of text attached. The agent it lands is
/// temporary, so what is saved here is the errand and not the agent. The template
/// is spelled out rather than pointing at an existing session: a shortcut that
/// names an agent stops working the day that agent is deleted.
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
}

fn yes() -> bool {
    true
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

    /// Every session still shaped the old way - a directory and a sandbox with a name
    /// on it - gets a project of its own, and two sessions pointing at the same
    /// directory get the *same* project. Runs on every load, including of a file we
    /// just wrote, so it has to be idempotent.
    fn migrate(&mut self) {
        for i in 0..self.sessions.len() {
            let dir = self.sessions[i].dir.take().unwrap_or_default();
            let agent = self.sessions[i].agent.take();
            let net = self.sessions[i].net.take();
            let sandbox = self.sessions[i].sandbox.take();
            let rw = std::mem::take(&mut self.sessions[i].rw_paths);

            if let Some(a) = agent {
                // A command that was written out is one somebody chose, so it survives as a
                // custom session.
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

    /// The directory's own last component first, then the session's name, then a
    /// number.
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

    /// A prompt shortcut with nothing to say about the command is a *Claude* session
    /// rather than a custom one running `[defaults] agent`: the kind is what hands the
    /// sandbox `~/.claude`. The project is handed in rather than read off the entry,
    /// because the entry is allowed not to name one.
    pub fn session_for(&self, sc: &ShortcutCfg, name: String, project: String) -> SessionCfg {
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
            project,
            kind,
            command,
            ..Default::default()
        }
    }

    /// A session naming one that has gone is an error where it matters (starting it)
    /// and a blank directory where it does not (listing it), so the caller decides.
    pub fn project_of(&self, s: &SessionCfg) -> Option<&ProjectCfg> {
        self.project(&s.project)
    }

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

/// bwrap expands neither, and the preset table is written in both. A variable with
/// nothing behind it expands to nothing, leaving a path that cannot exist - and
/// every bind is skipped unless the path is there, so an unset `WAYLAND_DISPLAY`
/// drops that bind instead of mounting `/run/user/1000/` whole.
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
    use super::{temp_dir, Config, SessionKind, ShortcutCfg, ShortcutKind, ShortcutLink};

    /// The file on disk outlives any one build, and a redeploy that refused to start
    /// would take the agents' only supervisor with it.
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
        // Usage polling defaults on, so an existing install gets the readout without
        // anyone editing a file.
        assert!(cfg.daemon.usage);
        assert_eq!(cfg.daemon.usage_poll_secs, 60);
        assert_eq!(cfg.daemon.claude_credentials, "~/.claude/.credentials.json");
        assert!(cfg.shortcuts.is_empty());
        assert_eq!(cfg.defaults.shell, "bash");
    }

    /// `[sandbox] enabled` was a daemon-wide override that silently beat every
    /// project's own checkbox, so it went. A config written before that still has the
    /// key: it has to be ignored on the way in rather than refused, and gone on the way
    /// back out, or an install that never opens the config window keeps a dead line
    /// forever.
    #[test]
    fn stale_sandbox_switch_is_ignored() {
        let cfg = Config::parse(
            r#"
            [daemon]
            bind = "127.0.0.1:7717"
            tmux_socket = "slopworld"
            poll_ms = 80

            [sandbox]
            enabled = false
            ro_paths = ["/usr"]
            rw_paths = []
            pass_env = ["PATH"]

            [[project]]
            name = "p"
            dir = "/tmp/p"
            "#,
        )
        .expect("a config with the old switch should still parse");

        assert_eq!(cfg.sandbox.ro_paths, vec!["/usr".to_string()]);

        let out = toml::to_string_pretty(&cfg).expect("should serialise");
        assert!(
            !out.contains("enabled"),
            "the dead switch should not be written back:\n{out}"
        );

        // And the project's own answer is the only one left: an unsandboxed project is
        // unsandboxed, a sandboxed one is sandboxed, whatever that key said.
        assert!(cfg.projects[0].sandbox);
    }

    /// A prompt shortcut that names no command has to come out a *Claude* session: the
    /// kind is what hands the sandbox ~/.claude.
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

        let sc = cfg.shortcut("review diff").unwrap();
        let prompt = cfg.session_for(sc, "review-diff".into(), sc.project.clone());
        assert_eq!(prompt.kind, SessionKind::Claude);
        assert_eq!(prompt.command, None);
        assert_eq!(cfg.command_of(&prompt), "claude --model opus");
        assert_eq!(prompt.project, "slopworld");

        let shell = cfg.session_for(cfg.shortcut("tests").unwrap(), "tests".into(), "x".into());
        assert_eq!(shell.kind, SessionKind::Custom);
        assert_eq!(cfg.command_of(&shell), "fish");

        let custom = cfg.session_for(cfg.shortcut("codex").unwrap(), "codex".into(), "x".into());
        assert_eq!(custom.kind, SessionKind::Custom);
        assert_eq!(cfg.command_of(&custom), "codex --yolo");

        // The place is the caller's answer and not the entry's, which is what lets one
        // errand be run somewhere it never named.
        let anywhere = cfg.session_for(sc, "review-diff-2".into(), "elsewhere".into());
        assert_eq!(anywhere.project, "elsewhere");
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
        });

        let back = Config::parse(&toml::to_string_pretty(&cfg).unwrap()).unwrap();
        let sc = back.shortcut("tests").expect("shortcut should survive");
        assert_eq!(sc.kind, ShortcutKind::Shell);
        assert_eq!(sc.text, "make test");
        assert!(sc.command.is_none());
    }

    /// A file written when a session *was* a directory keeps its agents. Two sessions
    /// in the same directory share a project, which is the arrangement whoever wrote
    /// the file meant.
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

        // A command somebody wrote out is a custom session; one that never set one is
        // what "Claude Code" means.
        assert_eq!(cfg.session("alpha").unwrap().kind, SessionKind::Claude);
        assert_eq!(cfg.session("gamma").unwrap().kind, SessionKind::Custom);
        assert_eq!(
            cfg.command_of(cfg.session("gamma").unwrap()),
            "codex --yolo"
        );
        assert_eq!(cfg.command_of(cfg.session("alpha").unwrap()), "claude");
    }

    /// Migration runs on every load, including of a file it wrote itself.
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
