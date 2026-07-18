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
    #[serde(default, rename = "session")]
    pub sessions: Vec<SessionCfg>,
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
    /// How often a subscribed pane is re-captured, in milliseconds.
    pub poll_ms: u64,
}

impl Default for Daemon {
    fn default() -> Self {
        Self {
            bind: "127.0.0.1:7717".into(),
            token: String::new(),
            tmux_socket: "slopworld".into(),
            poll_ms: 80,
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Defaults {
    /// Command run inside the sandbox. Split on whitespace, no shell involved.
    pub agent: String,
    pub cols: u16,
    pub rows: u16,
}

impl Default for Defaults {
    fn default() -> Self {
        Self {
            agent: "claude".into(),
            cols: 120,
            rows: 34,
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Sandbox {
    pub enabled: bool,
    /// Bound read-only into every sandbox.
    pub ro_paths: Vec<String>,
    /// Bound read-write into every sandbox, on top of the session's own dir.
    /// Agents need their own state dir here (~/.claude, ~/.claude.json).
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
            rw_paths: vec!["~/.claude".into(), "~/.claude.json".into()],
            pass_env: vec![
                "PATH".into(),
                "TERM".into(),
                "LANG".into(),
                "ANTHROPIC_API_KEY".into(),
            ],
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct SessionCfg {
    pub name: String,
    pub dir: String,
    #[serde(default)]
    pub agent: Option<String>,
    #[serde(default = "yes")]
    pub net: bool,
    #[serde(default = "yes")]
    pub sandbox: bool,
    #[serde(default)]
    pub autostart: bool,
    #[serde(default)]
    pub cols: Option<u16>,
    #[serde(default)]
    pub rows: Option<u16>,
    /// Extra rw binds for this session only.
    #[serde(default)]
    pub rw_paths: Vec<String>,
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
        let text = std::fs::read_to_string(path)
            .with_context(|| format!("reading {}", path.display()))?;
        Self::parse(&text)
    }

    pub fn parse(text: &str) -> Result<Self> {
        toml::from_str(text).context("parsing config.toml")
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
}

/// `~/foo` -> `/home/you/foo`. bwrap will not do this for us.
pub fn expand(path: &str) -> String {
    if let Some(rest) = path.strip_prefix("~/") {
        if let Some(home) = dirs::home_dir() {
            return home.join(rest).to_string_lossy().into_owned();
        }
    }
    path.to_string()
}
