use std::collections::BTreeMap;
use std::path::PathBuf;
use std::sync::{Arc, OnceLock, RwLock};

use serde::{Deserialize, Serialize};

/// What a sandbox is handed. Every path is bound only if it exists, so a preset for
/// something this host does not run costs nothing.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct SandboxPreset {
    pub name: String,
    /// How the GUI groups the checkboxes. Free text: a category this build has never
    /// heard of is a heading, not an error.
    #[serde(default)]
    pub category: String,
    #[serde(default)]
    pub description: String,
    #[serde(default)]
    pub ro: Vec<String>,
    /// Sockets go here: a bus you cannot write to is a bus you cannot talk on.
    #[serde(default)]
    pub rw: Vec<String>,
    /// Device nodes, which need `--dev-bind` to survive the `--dev` tmpfs.
    #[serde(default)]
    pub dev: Vec<String>,
    /// Bound to a copy of its own, one per session, rather than to the host's. What an agent
    /// writes to its own config directory is what the *host* runs the next time it starts
    /// one: `~/.claude/settings.json` names hooks, `~/.claude.json` names MCP servers, and
    /// both are ordinary files to whatever is inside the sandbox. Shared state is deferred
    /// execution; a copy is not. See `sandbox::private_binds`.
    #[serde(default)]
    pub private: Vec<String>,
    /// Subdirectories copied into a `private` directory when it is first made, and never
    /// again. The *files* at the top of that directory come across unasked - that is where a
    /// tool keeps its credentials - so this names only what the user wrote a directory of:
    /// plugins, agents, commands. A `private` entry that is itself a file is its own seed.
    #[serde(default)]
    pub seed: Vec<String>,
    /// Cut out of whatever `seed` names, which is what makes naming a whole directory safe. A
    /// tool scatters its config and concentrates its bulk in one place - `~/.pi/agent` holds
    /// the model selection *and* 21MB of transcripts - so seeding the directory and skipping
    /// the one subdirectory beats listing by hand every file that turns out to matter, and
    /// gets the list wrong towards an agent that works rather than one that does not.
    #[serde(default)]
    pub skip: Vec<String>,
    /// Non-empty when ticking this hands the sandbox a way back out: a socket whose far end
    /// runs on the host, a display every other window shares. Free text, because what it
    /// costs is the part worth reading, and the GUI draws it beside the checkbox rather than
    /// burying it in a tooltip. A preset that merely carries a secret is not this - that is a
    /// trade about *reach*, and this one is about the wall itself.
    #[serde(default)]
    pub escapes: String,
    /// Forwarded out of slopd's own environment.
    #[serde(default)]
    pub env: Vec<String>,
    /// Set to a literal value, for what is true only *inside* the sandbox. Applied after
    /// the forwarded ones, so the preset's answer beats how slopd was launched.
    #[serde(default)]
    pub setenv: BTreeMap<String, String>,
}

/// What an agent runs, and the sandbox presets that come with it: knowing a session is
/// Claude Code is what lets the sandbox hand it `~/.claude`.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct CommandPreset {
    pub name: String,
    #[serde(default)]
    pub category: String,
    #[serde(default)]
    pub description: String,
    pub cmd: String,
    #[serde(default)]
    pub sandbox: Vec<String>,
}

/// One file is one piece of software: its sandbox preset and its command preset
/// together, which is the pair anyone adding an agent writes.
#[derive(Debug, Default, Deserialize)]
struct PresetFile {
    #[serde(default)]
    sandbox: Vec<SandboxPreset>,
    #[serde(default)]
    command: Vec<CommandPreset>,
}

/// Compiled in rather than installed: builtins that can be older than the binary reading
/// them are builtins a redeploy silently disagrees with.
const BUILTIN: &[(&str, &str)] = &[
    ("claude", include_str!("../presets/claude.toml")),
    ("codex", include_str!("../presets/codex.toml")),
    ("opencode", include_str!("../presets/opencode.toml")),
    ("pi", include_str!("../presets/pi.toml")),
    ("psql", include_str!("../presets/psql.toml")),
    ("shell", include_str!("../presets/shell.toml")),
    ("zsh", include_str!("../presets/zsh.toml")),
    ("dbus", include_str!("../presets/dbus.toml")),
    ("systemd", include_str!("../presets/systemd.toml")),
    ("x11", include_str!("../presets/x11.toml")),
    ("wayland", include_str!("../presets/wayland.toml")),
    ("gpu", include_str!("../presets/gpu.toml")),
    ("audio", include_str!("../presets/audio.toml")),
    ("docker", include_str!("../presets/docker.toml")),
    ("podman", include_str!("../presets/podman.toml")),
    ("ssh", include_str!("../presets/ssh.toml")),
    ("1password", include_str!("../presets/1password.toml")),
    ("git", include_str!("../presets/git.toml")),
    ("rust", include_str!("../presets/rust.toml")),
    ("node", include_str!("../presets/node.toml")),
    ("python", include_str!("../presets/python.toml")),
    ("go", include_str!("../presets/go.toml")),
    ("gh", include_str!("../presets/gh.toml")),
    ("aws", include_str!("../presets/aws.toml")),
    ("kube", include_str!("../presets/kube.toml")),
    ("ollama", include_str!("../presets/ollama.toml")),
];

#[derive(Debug, Default)]
pub struct Table {
    pub sandbox: Vec<SandboxPreset>,
    pub commands: Vec<CommandPreset>,
}

impl Table {
    /// Alongside `config.toml`, because a preset is edited by hand the way the rest of
    /// that directory is; the shipped table is the binary's and lives in it.
    pub fn dir() -> PathBuf {
        if let Ok(dir) = std::env::var("SLOPD_PRESETS") {
            return PathBuf::from(dir);
        }
        dirs::config_dir()
            .unwrap_or_else(|| PathBuf::from("."))
            .join("slopworld/presets")
    }

    pub fn load() -> Self {
        let mut t = Self::default();
        for (name, text) in BUILTIN {
            match toml::from_str::<PresetFile>(text) {
                Ok(f) => t.merge(f),
                Err(e) => tracing::error!("builtin preset {name} does not parse: {e}"),
            }
        }
        t.merge_dir(&Self::dir());
        t
    }

    /// Read in filename order so two files naming the same preset settle the same way on
    /// every load. A file that does not parse is complained about and skipped: the rest
    /// of the table is still worth having.
    fn merge_dir(&mut self, dir: &std::path::Path) {
        let Ok(entries) = std::fs::read_dir(dir) else {
            return;
        };
        let mut files: Vec<PathBuf> = entries
            .filter_map(|e| e.ok().map(|e| e.path()))
            .filter(|p| p.extension().is_some_and(|x| x == "toml"))
            .collect();
        files.sort();

        for path in files {
            let text = match std::fs::read_to_string(&path) {
                Ok(t) => t,
                Err(e) => {
                    tracing::warn!("reading {}: {e}", path.display());
                    continue;
                }
            };
            match toml::from_str::<PresetFile>(&text) {
                Ok(f) => self.merge(f),
                Err(e) => tracing::warn!("{} does not parse: {e}", path.display()),
            }
        }
    }

    /// By name, in place: a user file naming `claude` replaces the builtin rather than
    /// shadowing it from the end of a list, so the GUI never draws two of it.
    fn merge(&mut self, f: PresetFile) {
        for p in f.sandbox {
            match self.sandbox.iter_mut().find(|x| x.name == p.name) {
                Some(slot) => *slot = p,
                None => self.sandbox.push(p),
            }
        }
        for c in f.command {
            match self.commands.iter_mut().find(|x| x.name == c.name) {
                Some(slot) => *slot = c,
                None => self.commands.push(c),
            }
        }
    }

    pub fn sandbox(&self, name: &str) -> Option<&SandboxPreset> {
        self.sandbox.iter().find(|p| p.name == name)
    }

    pub fn command(&self, name: &str) -> Option<&CommandPreset> {
        self.commands.iter().find(|c| c.name == name)
    }
}

static TABLE: OnceLock<RwLock<Arc<Table>>> = OnceLock::new();

fn cell() -> &'static RwLock<Arc<Table>> {
    TABLE.get_or_init(|| RwLock::new(Arc::new(Table::load())))
}

/// Held for the length of a call rather than borrowed: a reload behind a caller building
/// an argv would otherwise pull the table out from under it.
pub fn table() -> Arc<Table> {
    cell().read().unwrap().clone()
}

pub fn reload() {
    let fresh = Arc::new(Table::load());
    *cell().write().unwrap() = fresh;
}

/// The newest mtime in the user directory, so the caller can reload on the same terms
/// `config.toml` is re-read on. `None` when there is no directory to watch.
pub fn dir_stamp() -> Option<std::time::SystemTime> {
    let dir = Table::dir();
    let entries = std::fs::read_dir(&dir).ok()?;
    let newest = entries
        .filter_map(|e| e.ok())
        .filter_map(|e| e.metadata().ok())
        .filter_map(|m| m.modified().ok())
        .max();
    // The directory's own mtime as well, or a deleted file reads as no change at all.
    let own = std::fs::metadata(&dir).ok().and_then(|m| m.modified().ok());
    match (newest, own) {
        (Some(a), Some(b)) => Some(a.max(b)),
        (a, b) => a.or(b),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn builtins_parse_and_name_themselves() {
        let mut t = Table::default();
        for (name, text) in BUILTIN {
            let f: PresetFile = toml::from_str(text)
                .unwrap_or_else(|e| panic!("builtin preset {name} does not parse: {e}"));
            for p in &f.sandbox {
                assert_eq!(
                    &p.name, name,
                    "{name}.toml names a sandbox preset {:?}",
                    p.name
                );
                assert!(!p.category.is_empty(), "{name} has no category");
            }
            for c in &f.command {
                assert_eq!(&c.name, name, "{name}.toml names a command {:?}", c.name);
                assert!(!c.cmd.is_empty(), "{name} has no command");
            }
            t.merge(f);
        }

        // The three kinds that used to be an enum, and the shell shortcuts run.
        for name in ["claude", "opencode", "pi", "shell"] {
            assert!(t.command(name).is_some(), "no {name} command preset");
        }
        assert_eq!(t.command("claude").unwrap().sandbox, vec!["claude"]);
        assert_eq!(
            t.sandbox("systemd").unwrap().setenv["SYSTEMCTL_FORCE_BUS"],
            "1"
        );
        assert_eq!(t.sandbox("gpu").unwrap().dev, vec!["/dev/dri"]);

        // The new presets parse and name themselves correctly.
        for name in ["go", "gh", "aws", "kube"] {
            let p = t
                .sandbox(name)
                .unwrap_or_else(|| panic!("no {name} sandbox preset"));
            assert!(!p.category.is_empty(), "{name} has no category");
            assert!(!p.description.is_empty(), "{name} has no description");
        }
        assert_eq!(
            t.sandbox("go").unwrap().rw,
            vec!["~/.go", "~/.cache/go-build"]
        );
        assert_eq!(t.sandbox("kube").unwrap().ro, vec!["~/.kube"]);
    }

    /// Every agent keeps its own state, and every way back out of the sandbox says so. Both
    /// are properties of the shipped files rather than of any code, so this is where they are
    /// held: a preset added without either is the mistake worth catching, since the whole
    /// point of both fields is that nobody has to remember them at the checkbox.
    #[test]
    fn the_agents_keep_their_state_and_the_ways_out_are_marked() {
        let t = Table::load();

        // An agent's config directory is a command line the host runs later. Bound to a copy
        // or not bound at all - never the user's own.
        for name in ["claude", "codex", "pi", "opencode"] {
            let p = t
                .sandbox(name)
                .unwrap_or_else(|| panic!("no {name} preset"));
            assert!(
                !p.private.is_empty(),
                "{name} shares its state with the host"
            );
            assert!(
                p.rw.is_empty(),
                "{name} still binds {:?} read-write on the host's own copy",
                p.rw
            );
        }

        // A socket whose far end runs on the host, or a display every window shares.
        for name in [
            "docker",
            "podman",
            "dbus",
            "systemd",
            "x11",
            "ssh",
            "1password",
        ] {
            let p = t
                .sandbox(name)
                .unwrap_or_else(|| panic!("no {name} preset"));
            assert!(
                !p.escapes.is_empty(),
                "{name} is a way out and does not say so"
            );
        }

        // And the ordinary ones are not crying wolf.
        for name in ["rust", "go", "python", "node", "git"] {
            let p = t
                .sandbox(name)
                .unwrap_or_else(|| panic!("no {name} preset"));
            assert!(p.escapes.is_empty(), "{name} is marked as a way out");
        }
    }

    /// A user file replaces the builtin of the same name in place, and adds what it names
    /// that nothing shipped.
    #[test]
    fn user_files_override_by_name() {
        let mut t = Table::default();
        t.merge(
            toml::from_str(
                r#"
                [[sandbox]]
                name = "claude"
                rw = ["~/.claude"]
                [[command]]
                name = "claude"
                cmd = "claude"
                "#,
            )
            .unwrap(),
        );
        t.merge(
            toml::from_str(
                r#"
                [[command]]
                name = "claude"
                cmd = "claude --model opus"
                sandbox = ["claude", "docker"]
                [[command]]
                name = "codex"
                cmd = "codex --yolo"
                "#,
            )
            .unwrap(),
        );

        assert_eq!(t.commands.len(), 2);
        assert_eq!(t.command("claude").unwrap().cmd, "claude --model opus");
        assert_eq!(t.command("codex").unwrap().cmd, "codex --yolo");
        assert_eq!(t.sandbox.len(), 1);
    }
}
