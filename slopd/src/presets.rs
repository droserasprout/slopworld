//! Preset model and catalog snapshots. `presets/edit.rs` owns mutation transactions.

use std::collections::BTreeMap;
use std::fmt;
use std::path::{Path, PathBuf};
use std::sync::{Arc, OnceLock, RwLock};

use serde::{Deserialize, Serialize};

/// The two top-level preset collections exposed by the HTTP API and stored in config.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum PresetKind {
    SandboxPresets,
    AppPresets,
}

impl PresetKind {
    pub fn as_str(self) -> &'static str {
        match self {
            Self::SandboxPresets => "sandbox_presets",
            Self::AppPresets => "app_presets",
        }
    }
}

impl fmt::Display for PresetKind {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(self.as_str())
    }
}

impl std::str::FromStr for PresetKind {
    type Err = String;

    fn from_str(kind: &str) -> Result<Self, Self::Err> {
        match kind {
            "sandbox_presets" => Ok(Self::SandboxPresets),
            "app_presets" => Ok(Self::AppPresets),
            _ => Err(format!("unknown preset kind: {kind}")),
        }
    }
}

/// Resources to supply to a sandbox. Bind each path only if it exists.
/// Missing paths do not create mounts.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct SandboxPreset {
    pub name: String,
    #[serde(default)]
    pub description: String,
    /// Presets that this preset requires. Resolve them before this preset to supply its dependencies.
    #[serde(default)]
    pub requires: Vec<String>,
    #[serde(default)]
    pub ro: Vec<String>,
    /// Read-write paths. Include sockets here because bus communication requires write access.
    #[serde(default)]
    pub rw: Vec<String>,
    /// Shared host cache directories, created at launch before read-write binding.
    #[serde(default)]
    pub cache: Vec<String>,
    /// Device nodes that need `--dev-bind` to remain accessible with the `--dev` tmpfs.
    #[serde(default)]
    pub dev: Vec<String>,
    /// Private copies for each session, such as Claude settings and MCP files.
    /// Agent writes to these copies do not change host state. See `sandbox::private_binds`.
    #[serde(default)]
    pub private: Vec<String>,
    /// Paths to copy into a new `private` directory.
    /// The daemon copies top-level files, such as credentials, automatically.
    /// This list selects subdirectories or individual files.
    #[serde(default)]
    pub seed: Vec<String>,
    /// Paths to exclude from `seed` and private top-level files.
    /// Use this list to exclude large or temporary data, such as `~/.pi/agent` transcripts.
    #[serde(default)]
    pub skip: Vec<String>,
    /// Host-owned regular files to bind read-write inside private state.
    /// Shared directories could expose hooks or MCP configuration.
    /// Sharing these files permits changes to host data, but does not permit host execution.
    #[serde(default)]
    pub shared: Vec<String>,
    /// A nonempty value identifies host access through a capability such as a socket or display.
    /// The GUI shows this text as a warning. A bind that exposes only a secret is not an escape.
    #[serde(default)]
    pub escapes: String,
    /// Environment variables to forward from slopd.
    #[serde(default)]
    pub env: Vec<String>,
    /// Literal environment values for the sandbox.
    /// Apply these after forwarded variables so preset values take precedence over the slopd environment.
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

/// Whether a command is an agent CLI or an interactive shell.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum CommandKind {
    #[default]
    Agent,
    Shell,
}

/// The session command and its sandbox presets.
/// For example, the Claude Code preset supplies `~/.claude` to its sandbox.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct CommandPreset {
    pub name: String,
    pub kind: CommandKind,
    #[serde(default)]
    pub description: String,
    pub cmd: String,
    #[serde(default)]
    pub sandbox: Vec<String>,
}

/// A concrete preset selected for a domain operation. Keeping the variants typed means the
/// sandbox validator never has to recover a schema from JSON.
#[derive(Debug, Clone)]
pub enum PresetDefinition {
    Sandbox(Box<SandboxPreset>),
    Command(Box<CommandPreset>),
}

#[derive(Debug, Clone, Copy)]
pub enum PresetDefinitionRef<'a> {
    Sandbox(&'a SandboxPreset),
    Command(&'a CommandPreset),
}

impl PresetDefinition {
    fn rename(&mut self, name: String) {
        match self {
            Self::Sandbox(preset) => preset.name = name,
            Self::Command(preset) => preset.name = name,
        }
    }
}

/// Each file defines the sandbox and command presets for one application.
#[derive(Debug, Default, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
struct PresetFile {
    #[serde(default)]
    sandbox: Vec<SandboxPreset>,
    #[serde(default)]
    command: Vec<CommandPreset>,
}

/// Compile built-in presets into the binary to keep their versions consistent after deployment.
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
    ("sh", include_str!("../presets/sh.toml")),
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
    ("rust-sccache", include_str!("../presets/rust-sccache.toml")),
    ("node-cache", include_str!("../presets/node-cache.toml")),
    ("python-cache", include_str!("../presets/python-cache.toml")),
    ("go-cache", include_str!("../presets/go-cache.toml")),
    ("nuget-cache", include_str!("../presets/nuget-cache.toml")),
    ("ruby-cache", include_str!("../presets/ruby-cache.toml")),
    ("ccache", include_str!("../presets/ccache.toml")),
    ("android-dev", include_str!("../presets/android-dev.toml")),
    (
        "android-debug",
        include_str!("../presets/android-debug.toml"),
    ),
    ("ios-dev", include_str!("../presets/ios-dev.toml")),
    ("ios-debug", include_str!("../presets/ios-debug.toml")),
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

/// Where an effective definition came from.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum PresetSource {
    System,
    User,
    Override,
    Unknown,
}

impl PresetSource {
    pub fn as_str(self) -> &'static str {
        match self {
            Self::System => "system",
            Self::User => "user",
            Self::Override => "override",
            Self::Unknown => "unknown",
        }
    }

    pub fn from_presence(has_builtin: bool, has_user: bool) -> Self {
        match (has_builtin, has_user) {
            (true, true) => Self::Override,
            (true, false) => Self::System,
            (false, true) => Self::User,
            (false, false) => Self::Unknown,
        }
    }
}

impl Table {
    /// The root directory for user presets. Built-in presets remain compiled into the daemon.
    /// Each file under `sandbox_presets/` or `app_presets/` contains one user definition.
    pub fn dir() -> PathBuf {
        crate::paths::config_root()
    }

    pub fn dir_for(kind: PresetKind) -> PathBuf {
        Self::dir().join(kind.as_str())
    }

    pub fn stamp() -> Option<std::time::SystemTime> {
        [PresetKind::SandboxPresets, PresetKind::AppPresets]
            .into_iter()
            .filter_map(|kind| crate::paths::dir_stamp(&Self::dir_for(kind)))
            .max()
    }

    pub fn load() -> Self {
        let mut t = Self::builtins();
        t.merge_user_dirs(&Self::dir());
        t
    }

    fn try_load_from(dir: &Path) -> anyhow::Result<Self> {
        let mut t = Self::builtins();
        t.read_user_dir(
            PresetKind::SandboxPresets,
            &dir.join("sandbox_presets"),
            true,
        )?;
        t.read_user_dir(PresetKind::AppPresets, &dir.join("app_presets"), true)?;
        Ok(t)
    }

    /// The compiled table without user files.
    /// The settings page distinguishes system presets, user presets, and user overrides.
    /// This table lets the mod identify each source without examining filenames.
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
        t.merge_user_dirs(&Self::dir());
        t
    }

    fn merge_user_dirs(&mut self, root: &Path) {
        for kind in [PresetKind::SandboxPresets, PresetKind::AppPresets] {
            drop(self.read_user_dir(kind, &root.join(kind.as_str()), false));
        }
    }

    /// Startup keeps valid definitions; reload and mutations reject partial catalogs.
    fn read_user_dir(&mut self, kind: PresetKind, dir: &Path, strict: bool) -> anyhow::Result<()> {
        let paths = crate::paths::read_sorted_dir(dir, |error| {
            if strict {
                return Err(error);
            }
            tracing::warn!("reading {}: {error}", dir.display());
            Ok(())
        })?;
        for path in paths
            .into_iter()
            .filter(|p| p.extension().is_some_and(|x| x == "toml"))
        {
            let result = std::fs::read_to_string(&path)
                .map_err(anyhow::Error::from)
                .and_then(|text| self.parse_user_definition(kind, &path, &text));
            if let Err(error) = result {
                if strict {
                    return Err(error);
                }
                tracing::warn!("reading definition {}: {error}", path.display());
            }
        }
        Ok(())
    }

    fn parse_user_definition(
        &mut self,
        kind: PresetKind,
        path: &Path,
        text: &str,
    ) -> anyhow::Result<()> {
        let file_name = path
            .file_stem()
            .and_then(|name| name.to_str())
            .unwrap_or_default();
        match kind {
            PresetKind::SandboxPresets => {
                let preset: SandboxPreset = toml::from_str(text)?;
                valid_name(&preset.name)?;
                if file_name != preset.name {
                    anyhow::bail!("file name does not match sandbox name {:?}", preset.name);
                }
                self.merge(PresetFile {
                    sandbox: vec![preset],
                    command: Vec::new(),
                });
            }
            PresetKind::AppPresets => {
                let preset: CommandPreset = toml::from_str(text)?;
                valid_name(&preset.name)?;
                if file_name != preset.name {
                    anyhow::bail!("file name does not match app name {:?}", preset.name);
                }
                self.merge(PresetFile {
                    sandbox: Vec::new(),
                    command: vec![preset],
                });
            }
        }
        if file_name.is_empty() {
            anyhow::bail!("definition file has no name");
        }
        Ok(())
    }

    /// Replace entries with matching names in place.
    /// For example, a user file named `claude` replaces the built-in entry so the GUI shows only one entry.
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

    pub fn definition(&self, kind: PresetKind, name: &str) -> Option<PresetDefinitionRef<'_>> {
        match kind {
            PresetKind::SandboxPresets => self.sandbox(name).map(PresetDefinitionRef::Sandbox),
            PresetKind::AppPresets => self.command(name).map(PresetDefinitionRef::Command),
        }
    }

    pub fn contains(&self, kind: PresetKind, name: &str) -> bool {
        self.definition(kind, name).is_some()
    }

    pub fn source(&self, kind: PresetKind, name: &str, users: &Self) -> PresetSource {
        PresetSource::from_presence(self.contains(kind, name), users.contains(kind, name))
    }
}

mod edit;
use edit::valid_name;
pub use edit::{PresetError, copy_builtin, delete_user, validate_and_save};
#[cfg(test)]
use edit::{check_delete, copy_definition, save_definition_in};

static TABLE: OnceLock<RwLock<Arc<Table>>> = OnceLock::new();

fn cell() -> &'static RwLock<Arc<Table>> {
    TABLE.get_or_init(|| RwLock::new(Arc::new(Table::load())))
}

/// Keep a shared reference to this table during the call.
/// This preserves the table if presets reload while a caller builds command arguments.
pub fn table() -> Arc<Table> {
    cell()
        .read()
        .unwrap_or_else(|error| error.into_inner())
        .clone()
}

pub fn reload() -> bool {
    let fresh = match Table::try_load_from(&Table::dir()) {
        Ok(table) => Arc::new(table),
        Err(e) => {
            tracing::warn!("presets changed on disk but are not reloadable: {e:#}");
            return false;
        }
    };
    *cell().write().unwrap_or_else(|error| error.into_inner()) = fresh;
    true
}

#[cfg(test)]
#[path = "presets_tests.rs"]
mod tests;
