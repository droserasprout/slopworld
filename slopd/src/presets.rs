use std::collections::{BTreeMap, HashSet};
use std::path::PathBuf;
use std::sync::{Arc, OnceLock, RwLock};

use serde::{Deserialize, Serialize};

/// What a sandbox is handed. Every path is bound only if it exists, so a preset for
/// something this host does not run costs nothing.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct SandboxPreset {
    pub name: String,
    #[serde(default)]
    pub description: String,
    /// Presets this one needs in order to function. Resolved before this preset so the
    /// supporting capability is present whenever the dependent one is chosen.
    #[serde(default)]
    pub requires: Vec<String>,
    #[serde(default)]
    pub ro: Vec<String>,
    /// Sockets go here: a bus you cannot write to is a bus you cannot talk on.
    #[serde(default)]
    pub rw: Vec<String>,
    /// Device nodes, which need `--dev-bind` to survive the `--dev` tmpfs.
    #[serde(default)]
    pub dev: Vec<String>,
    /// Per-session copies, so agent config writes do not become host state. See
    /// `sandbox::private_binds`; Claude settings/MCP files are typical examples.
    #[serde(default)]
    pub private: Vec<String>,
    /// Paths copied into a new `private` directory. Top-level files are copied automatically
    /// (credentials); this selects subdirectories, or a file directly.
    #[serde(default)]
    pub seed: Vec<String>,
    /// Excludes paths from `seed` and private top-level files; useful when a seeded directory
    /// contains bulky or disposable state such as `~/.pi/agent` transcripts.
    #[serde(default)]
    pub skip: Vec<String>,
    /// Shared entries are host-owned regular files bound read-write inside private state; shared
    /// directories could expose hooks or MCP configuration. This trades integrity, not execution.
    #[serde(default)]
    pub shared: Vec<String>,
    /// Non-empty marks a host-reachable capability such as a socket or display; the GUI shows
    /// the text as a warning. A secret-only bind is not an escape.
    #[serde(default)]
    pub escapes: String,
    /// Forwarded out of slopd's own environment.
    #[serde(default)]
    pub env: Vec<String>,
    /// Set to a literal value, for what is true only *inside* the sandbox. Applied after
    /// the forwarded ones, so the preset's answer beats how slopd was launched.
    #[serde(default)]
    pub setenv: BTreeMap<String, String>,
    /// Bind slopd's private tmux socket into the sandbox's uid-0 socket directory. This
    /// is deliberately opt-in: the socket is a live control channel to host terminals.
    #[serde(default)]
    pub tmux: bool,
    /// Bind only config.toml and endpoint.toml read-only for daemon diagnostics. Ordinary
    /// path lists still cannot reach either secret-bearing file.
    #[serde(default)]
    pub daemon_config: bool,
}

/// What an agent runs, and the sandbox presets that come with it: knowing a session is
/// Claude Code is what lets the sandbox hand it `~/.claude`.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct CommandPreset {
    pub name: String,
    #[serde(default)]
    pub description: String,
    pub cmd: String,
    #[serde(default)]
    pub sandbox: Vec<String>,
}

/// One file is one piece of software: its sandbox preset and its command preset
/// together, which is the pair anyone adding an agent writes.
#[derive(Debug, Default, Serialize, Deserialize)]
struct PresetFile {
    #[serde(default)]
    sandbox: Vec<SandboxPreset>,
    #[serde(default)]
    command: Vec<CommandPreset>,
}

/// Compiled in rather than installed: builtins that can be older than the binary reading
/// them are builtins a redeploy silently disagrees with.
const BUILTIN: &[(&str, &str)] = &[
    ("global", include_str!("../presets/global.toml")),
    ("claude", include_str!("../presets/claude.toml")),
    ("codex", include_str!("../presets/codex.toml")),
    ("opencode", include_str!("../presets/opencode.toml")),
    ("pi", include_str!("../presets/pi.toml")),
    ("psql", include_str!("../presets/psql.toml")),
    ("bash", include_str!("../presets/bash.toml")),
    ("zsh", include_str!("../presets/zsh.toml")),
    ("fish", include_str!("../presets/fish.toml")),
    ("nu", include_str!("../presets/nu.toml")),
    ("pwsh", include_str!("../presets/pwsh.toml")),
    (
        "bash-userdata",
        include_str!("../presets/bash-userdata.toml"),
    ),
    ("zsh-userdata", include_str!("../presets/zsh-userdata.toml")),
    (
        "fish-userdata",
        include_str!("../presets/fish-userdata.toml"),
    ),
    ("nu-userdata", include_str!("../presets/nu-userdata.toml")),
    (
        "pwsh-userdata",
        include_str!("../presets/pwsh-userdata.toml"),
    ),
    ("dbus", include_str!("../presets/dbus.toml")),
    ("systemd", include_str!("../presets/systemd.toml")),
    ("x11", include_str!("../presets/x11.toml")),
    ("wayland", include_str!("../presets/wayland.toml")),
    ("gpu", include_str!("../presets/gpu.toml")),
    ("audio", include_str!("../presets/audio.toml")),
    ("docker", include_str!("../presets/docker.toml")),
    ("podman", include_str!("../presets/podman.toml")),
    ("ssh", include_str!("../presets/ssh.toml")),
    ("ssh-agent", include_str!("../presets/ssh-agent.toml")),
    ("1password", include_str!("../presets/1password.toml")),
    ("git", include_str!("../presets/git.toml")),
    ("hg", include_str!("../presets/hg.toml")),
    ("rust", include_str!("../presets/rust.toml")),
    ("node", include_str!("../presets/node.toml")),
    ("python", include_str!("../presets/python.toml")),
    ("go", include_str!("../presets/go.toml")),
    ("dotnet", include_str!("../presets/dotnet.toml")),
    ("ruby", include_str!("../presets/ruby.toml")),
    ("gh", include_str!("../presets/gh.toml")),
    ("aws", include_str!("../presets/aws.toml")),
    ("kube", include_str!("../presets/kube.toml")),
    ("ollama", include_str!("../presets/ollama.toml")),
    ("rust-cache", include_str!("../presets/rust-cache.toml")),
    ("node-cache", include_str!("../presets/node-cache.toml")),
    ("python-cache", include_str!("../presets/python-cache.toml")),
    ("go-cache", include_str!("../presets/go-cache.toml")),
    ("nuget-cache", include_str!("../presets/nuget-cache.toml")),
    ("ruby-cache", include_str!("../presets/ruby-cache.toml")),
    ("ccache", include_str!("../presets/ccache.toml")),
    ("gpg", include_str!("../presets/gpg.toml")),
    ("gpg-agent", include_str!("../presets/gpg-agent.toml")),
    (
        "slopworld-debug",
        include_str!("../presets/slopworld-debug.toml"),
    ),
];

#[derive(Debug, Clone, Default)]
pub struct Table {
    pub sandbox: Vec<SandboxPreset>,
    pub commands: Vec<CommandPreset>,
}

impl Table {
    /// Alongside `config.toml`, because a preset is edited by hand the way the rest of
    /// that directory is; the shipped table is the binary's and lives in it.
    pub fn dir() -> PathBuf {
        crate::paths::dir("SLOPD_PRESETS", dirs::config_dir(), "presets")
    }

    pub fn load() -> Self {
        let mut t = Self::builtins();
        t.merge_dir(&Self::dir());
        t
    }

    /// The compiled table without user files. The settings page needs this distinction:
    /// an effective entry can be a system preset, a user-only preset, or a user override of
    /// one of these. Keeping the answer here avoids making the mod guess from filenames.
    pub fn builtins() -> Self {
        let mut t = Self::default();
        for (name, text) in BUILTIN {
            match toml::from_str::<PresetFile>(text) {
                Ok(f) => t.merge(f),
                Err(e) => tracing::error!("builtin preset {name} does not parse: {e}"),
            }
        }
        t
    }

    /// Only the files in the user directory, for the API's source labels and for editing.
    pub fn users() -> Self {
        let mut t = Self::default();
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

static USER_WRITE: OnceLock<std::sync::Mutex<()>> = OnceLock::new();

fn user_write_lock() -> &'static std::sync::Mutex<()> {
    USER_WRITE.get_or_init(|| std::sync::Mutex::new(()))
}

fn valid_name(name: &str) -> anyhow::Result<()> {
    let mut chars = name.chars();
    let valid = chars.next().is_some_and(|c| c.is_ascii_alphanumeric())
        && chars.all(|c| c.is_ascii_alphanumeric() || c == '_' || c == '-');
    if !valid {
        anyhow::bail!("invalid preset name {name:?}; use letters, numbers, '-' or '_'");
    }
    Ok(())
}

fn read_user_files(dir: &std::path::Path) -> anyhow::Result<Vec<(PathBuf, PresetFile)>> {
    let Ok(entries) = std::fs::read_dir(dir) else {
        return Ok(Vec::new());
    };
    let mut paths: Vec<PathBuf> = entries
        .filter_map(|e| e.ok().map(|e| e.path()))
        .filter(|p| p.extension().is_some_and(|x| x == "toml"))
        .collect();
    paths.sort();

    paths
        .into_iter()
        .map(|path| {
            let text = std::fs::read_to_string(&path)
                .map_err(|e| anyhow::anyhow!("reading {}: {e}", path.display()))?;
            let file = toml::from_str(&text)
                .map_err(|e| anyhow::anyhow!("{} does not parse: {e}", path.display()))?;
            Ok((path, file))
        })
        .collect()
}

fn write_file(path: &std::path::Path, file: &PresetFile) -> anyhow::Result<()> {
    let text = toml::to_string_pretty(file)?;
    let tmp = path.with_extension("toml.tmp");
    std::fs::write(&tmp, text)?;
    std::fs::rename(&tmp, path)?;
    Ok(())
}

fn write_user_file(
    kind: &str,
    name: &str,
    sandbox: Option<SandboxPreset>,
    command: Option<CommandPreset>,
) -> anyhow::Result<()> {
    valid_name(name)?;
    let _guard = user_write_lock()
        .lock()
        .map_err(|_| anyhow::anyhow!("preset write lock is poisoned"))?;
    write_user_file_in(&Table::dir(), kind, name, sandbox, command)
}

fn write_user_file_in(
    dir: &std::path::Path,
    kind: &str,
    name: &str,
    sandbox: Option<SandboxPreset>,
    command: Option<CommandPreset>,
) -> anyhow::Result<()> {
    std::fs::create_dir_all(dir)?;
    let target = dir.join(format!("{name}.toml"));
    let mut files = read_user_files(dir)?;
    if !files.iter().any(|(path, _)| *path == target) {
        files.push((target.clone(), PresetFile::default()));
    }

    // A name is a logical entry even when an older hand-written file put it elsewhere. Remove
    // just this kind from every file, preserving the other kind and every unrelated preset.
    let mut changed = HashSet::new();
    for (path, file) in &mut files {
        if kind == "sandbox" {
            let len = file.sandbox.len();
            file.sandbox.retain(|p| p.name != name);
            if file.sandbox.len() != len {
                changed.insert(path.clone());
            }
        } else {
            let len = file.command.len();
            file.command.retain(|p| p.name != name);
            if file.command.len() != len {
                changed.insert(path.clone());
            }
        }
    }
    let target_index = files.iter().position(|(path, _)| *path == target).unwrap();
    if kind == "sandbox" {
        if let Some(p) = sandbox {
            files[target_index].1.sandbox.push(p);
            changed.insert(target.clone());
        }
    } else if let Some(c) = command {
        files[target_index].1.command.push(c);
        changed.insert(target.clone());
    }

    // Rewrite the files whose entries changed too. Merely writing the target would leave an
    // old definition in another user file, and filename order would make the result depend on
    // an invisible stale copy. Empty files are removed, including the target on delete.
    for (path, file) in files {
        if !changed.contains(&path) {
            continue;
        }
        if file.sandbox.is_empty() && file.command.is_empty() {
            if path.exists() {
                std::fs::remove_file(path)?;
            }
        } else {
            write_file(&path, &file)?;
        }
    }
    Ok(())
}

pub fn save_sandbox(p: SandboxPreset) -> anyhow::Result<()> {
    let name = p.name.clone();
    write_user_file("sandbox", &name, Some(p), None)
}

pub fn save_command(c: CommandPreset) -> anyhow::Result<()> {
    let name = c.name.clone();
    write_user_file("command", &name, None, Some(c))
}

pub fn remove_sandbox(name: &str) -> anyhow::Result<()> {
    write_user_file("sandbox", name, None, None)
}

pub fn remove_command(name: &str) -> anyhow::Result<()> {
    write_user_file("command", name, None, None)
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
            }
            for c in &f.command {
                assert_eq!(&c.name, name, "{name}.toml names a command {:?}", c.name);
                assert!(!c.cmd.is_empty(), "{name} has no command");
            }
            t.merge(f);
        }

        // The command presets used by built-in shortcuts, including the shells.
        for name in [
            "claude", "opencode", "pi", "bash", "zsh", "fish", "nu", "pwsh",
        ] {
            assert!(t.command(name).is_some(), "no {name} command preset");
        }
        assert!(
            t.command("shell").is_none(),
            "old shell command preset remains"
        );
        assert_eq!(t.command("claude").unwrap().sandbox, vec!["claude"]);
        assert_eq!(t.command("codex").unwrap().sandbox, vec!["codex"]);
        assert_eq!(t.sandbox("codex").unwrap().requires, vec!["x11", "wayland"]);
        assert_eq!(
            t.sandbox("systemd").unwrap().setenv["SYSTEMCTL_FORCE_BUS"],
            "1"
        );
        assert_eq!(t.sandbox("gpu").unwrap().dev, vec!["/dev/dri", "/dev/kfd"]);

        // The new presets parse and name themselves correctly.
        for name in ["go", "gh", "aws", "kube", "slopworld-debug"] {
            let p = t
                .sandbox(name)
                .unwrap_or_else(|| panic!("no {name} sandbox preset"));
            assert!(!p.description.is_empty(), "{name} has no description");
        }
        assert!(t.sandbox("slopworld-debug").unwrap().tmux);
        assert!(t.sandbox("slopworld-debug").unwrap().daemon_config);
        assert_eq!(
            t.sandbox("slopworld-debug").unwrap().requires,
            vec![
                "systemd",
                "x11",
                "wayland",
                "gpu",
                "audio",
                "git",
                "rust-cache",
                "nuget-cache",
                "ccache",
                "python",
                "docker"
            ]
        );
        assert_eq!(
            t.sandbox("slopworld-debug").unwrap().setenv["RUST_BACKTRACE"],
            "1"
        );
        let debug = t.sandbox("slopworld-debug").unwrap();
        for path in [
            "$SLOPWORLD_GAME",
            "~/GOG Games/RimWorld/game",
            "~/.local/share/slopworld/profile",
            "~/.local/share/slopworld-car",
            "~/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios",
            "~/.local/bin",
            "~/.config/systemd/user",
        ] {
            assert!(
                debug.rw.iter().any(|seen| seen == path),
                "debug lacks {path}"
            );
        }
        assert!(
            debug.env.iter().any(|seen| seen == "SLOPCAR_PROFILE"),
            "debug does not forward SLOPCAR_PROFILE"
        );
        for path in ["/proc", "/sys", "/run/udev", "~/.local/share/applications"] {
            assert!(
                debug.ro.iter().any(|seen| seen == path),
                "debug lacks {path}"
            );
        }
        assert_eq!(
            t.sandbox("global").unwrap().ro,
            vec!["/usr", "/etc", "/opt", "~/.local/bin"]
        );
        assert!(t.sandbox("global").unwrap().rw.is_empty());
        assert_eq!(
            t.sandbox("go-cache").unwrap().rw,
            vec!["~/go/pkg/mod", "~/.cache/go-build"]
        );
        assert_eq!(
            t.sandbox("rust-cache").unwrap().rw,
            vec![
                "~/.cargo/registry",
                "~/.cargo/git",
                "~/.rustup/toolchains",
                "~/.rustup/update-hashes"
            ]
        );
        assert_eq!(t.sandbox("kube").unwrap().ro, vec!["~/.kube"]);
        for (cache, tool) in [
            ("rust-cache", "rust"),
            ("node-cache", "node"),
            ("python-cache", "python"),
            ("go-cache", "go"),
            ("nuget-cache", "dotnet"),
            ("ruby-cache", "ruby"),
        ] {
            assert_eq!(t.sandbox(cache).unwrap().requires, vec![tool]);
        }

        // Shell userdata is opt-in: the command only selects the executable, while the
        // matching sandbox is a separate checkbox that brings in host dotfiles and history.
        for name in ["bash", "zsh", "fish", "nu", "pwsh"] {
            assert!(
                t.command(name).unwrap().sandbox.is_empty(),
                "{name} command unexpectedly shares userdata by default"
            );
            let userdata = format!("{name}-userdata");
            let preset = t
                .sandbox(&userdata)
                .unwrap_or_else(|| panic!("no {userdata} sandbox preset"));
            assert!(!preset.ro.is_empty(), "{userdata} has no config paths");
            assert!(!preset.rw.is_empty(), "{userdata} has no userdata paths");
        }
    }

    #[test]
    fn legacy_preset_categories_are_dropped_when_rewritten() {
        let file: PresetFile = toml::from_str(
            r#"
            [[sandbox]]
            name = "old-sandbox"
            category = "dev"
            description = "still useful"

            [[command]]
            name = "old-command"
            category = "agent"
            cmd = "agent"
            "#,
        )
        .unwrap();

        let written = toml::to_string(&file).unwrap();
        assert!(!written.contains("category"));
        assert!(written.contains("description = \"still useful\""));
        assert!(written.contains("cmd = \"agent\""));
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
            // `shared` is the one exception, and it is an exception *within* a copy: a hole cut
            // in a private tree for a credential that rotates. One that fell outside every
            // `private` path would be an ordinary read-write bind on the host wearing the name
            // of a narrow one, which is exactly the thing the line above refuses.
            for s in &p.shared {
                assert!(
                    p.private.iter().any(|priv_| s.starts_with(priv_.as_str())),
                    "{name} shares {s}, which is under nothing it keeps private"
                );
            }
        }

        // A socket whose far end runs on the host, or a display every window shares.
        for name in [
            "docker",
            "podman",
            "dbus",
            "systemd",
            "x11",
            "ssh-agent",
            "1password",
            "gpg-agent",
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
        for name in [
            "rust",
            "rust-cache",
            "go",
            "go-cache",
            "python",
            "python-cache",
            "node",
            "node-cache",
            "dotnet",
            "nuget-cache",
            "ruby",
            "ruby-cache",
            "ccache",
            "git",
            "hg",
            "ollama",
            "gpg",
        ] {
            let p = t
                .sandbox(name)
                .unwrap_or_else(|| panic!("no {name} preset"));
            assert!(p.escapes.is_empty(), "{name} is marked as a way out");
        }

        // SSH configuration is safe to expose by itself; the agent socket is the explicit
        // capability that lets a sandbox ask the host to sign.
        assert!(t.sandbox("ssh").unwrap().rw.is_empty());
        assert_eq!(t.sandbox("ssh-agent").unwrap().rw, vec!["$SSH_AUTH_SOCK"]);
        assert_eq!(t.sandbox("systemd").unwrap().requires, vec!["dbus"]);

        // A config-root variable would bypass the private mount, so the shipped agent
        // presets rely on their default paths under HOME instead of forwarding one.
        for (name, forbidden) in [
            ("claude", "CLAUDE_CONFIG_DIR"),
            ("codex", "CODEX_HOME"),
            ("pi", "PI_CODING_AGENT_DIR"),
            ("opencode", "OPENCODE_CONFIG"),
            ("opencode", "OPENCODE_CONFIG_DIR"),
        ] {
            assert!(
                !t.sandbox(name)
                    .unwrap()
                    .env
                    .iter()
                    .any(|env| env == forbidden),
                "{name} forwards {forbidden}, bypassing its private state"
            );
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

    #[test]
    fn saving_one_preset_does_not_reserialize_an_unrelated_file() {
        let dir = std::env::temp_dir().join(format!("slopd-presets-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let untouched = dir.join("handwritten.toml");
        let original = "# keep this comment\n[[sandbox]]\nname = \"other\"\nfuture = true\n";
        std::fs::write(&untouched, original).unwrap();

        write_user_file_in(
            &dir,
            "sandbox",
            "changed",
            Some(SandboxPreset {
                name: "changed".into(),
                ..Default::default()
            }),
            None,
        )
        .unwrap();

        assert_eq!(std::fs::read_to_string(untouched).unwrap(), original);
        assert!(dir.join("changed.toml").exists());
        let _ = std::fs::remove_dir_all(dir);
    }
}
