use std::collections::{BTreeMap, HashSet};
use std::fmt;
use std::path::{Path, PathBuf};
use std::sync::{Arc, OnceLock, RwLock};

use serde::{Deserialize, Serialize};

/// The two top-level preset collections exposed by the HTTP API.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum PresetKind {
    Sandbox,
    Command,
}

impl PresetKind {
    pub fn as_str(self) -> &'static str {
        match self {
            Self::Sandbox => "sandbox",
            Self::Command => "command",
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
            "sandbox" => Ok(Self::Sandbox),
            "command" => Ok(Self::Command),
            _ => Err(format!("unknown preset kind: {kind}")),
        }
    }
}

/// What a sandbox is handed. Every path is bound only if it exists, so a preset for
/// something this host does not run costs nothing.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
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

/// Whether a command is an agent CLI or an interactive shell.
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum CommandKind {
    #[default]
    Agent,
    Shell,
}

/// What a session runs, and the sandbox presets that come with it: knowing a session is
/// Claude Code is what lets the sandbox hand it `~/.claude`.
#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub struct CommandPreset {
    pub name: String,
    #[serde(default)]
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

/// One file is one piece of software: its sandbox preset and its command preset
/// together, which is the pair anyone adding an agent writes.
#[derive(Debug, Default, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
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

    fn try_load_from(dir: &Path) -> anyhow::Result<Self> {
        let mut t = Self::builtins();
        for (_, file) in read_user_files(dir)? {
            t.merge(file);
        }
        Ok(t)
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

    pub fn definition(&self, kind: PresetKind, name: &str) -> Option<PresetDefinitionRef<'_>> {
        match kind {
            PresetKind::Sandbox => self.sandbox(name).map(PresetDefinitionRef::Sandbox),
            PresetKind::Command => self.command(name).map(PresetDefinitionRef::Command),
        }
    }

    pub fn contains(&self, kind: PresetKind, name: &str) -> bool {
        self.definition(kind, name).is_some()
    }

    pub fn source(&self, kind: PresetKind, name: &str, users: &Self) -> PresetSource {
        PresetSource::from_presence(self.contains(kind, name), users.contains(kind, name))
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
    let entries = match std::fs::read_dir(dir) {
        Ok(entries) => entries,
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => return Ok(Vec::new()),
        Err(e) => return Err(anyhow::anyhow!("reading {}: {e}", dir.display())),
    };
    let mut paths: Vec<PathBuf> = entries
        .map(|e| e.map(|e| e.path()))
        .collect::<std::io::Result<Vec<_>>>()?
        .into_iter()
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
    // Presets intentionally retain the existing umask-controlled permission policy.
    crate::paths::write_atomic(path, &text, None)
}

fn write_user_file(
    kind: PresetKind,
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
    kind: PresetKind,
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
        match kind {
            PresetKind::Sandbox => {
                let len = file.sandbox.len();
                file.sandbox.retain(|p| p.name != name);
                if file.sandbox.len() != len {
                    changed.insert(path.clone());
                }
            }
            PresetKind::Command => {
                let len = file.command.len();
                file.command.retain(|p| p.name != name);
                if file.command.len() != len {
                    changed.insert(path.clone());
                }
            }
        }
    }
    let target_index = files.iter().position(|(path, _)| *path == target).unwrap();
    match kind {
        PresetKind::Sandbox => {
            if let Some(p) = sandbox {
                files[target_index].1.sandbox.push(p);
                changed.insert(target.clone());
            }
        }
        PresetKind::Command => {
            if let Some(c) = command {
                files[target_index].1.command.push(c);
                changed.insert(target.clone());
            }
        }
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

fn save_definition(definition: PresetDefinition) -> anyhow::Result<()> {
    match definition {
        PresetDefinition::Sandbox(preset) => {
            let name = preset.name.clone();
            write_user_file(PresetKind::Sandbox, &name, Some(*preset), None)
        }
        PresetDefinition::Command(preset) => {
            let name = preset.name.clone();
            write_user_file(PresetKind::Command, &name, None, Some(*preset))
        }
    }
}

fn remove_user(kind: PresetKind, name: &str) -> anyhow::Result<()> {
    write_user_file(kind, name, None, None)
}

#[derive(Debug)]
pub enum PresetError {
    Missing(String),
    Invalid(String),
}

impl fmt::Display for PresetError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::Missing(message) | Self::Invalid(message) => f.write_str(message),
        }
    }
}

fn copy_definition(
    kind: PresetKind,
    old_name: &str,
    target: &str,
    builtins: &Table,
    users: &Table,
) -> Result<PresetDefinition, PresetError> {
    if users.contains(kind, old_name) {
        return Err(PresetError::Invalid(
            "that preset already has a user definition".into(),
        ));
    }
    if users.contains(kind, target) || (target != old_name && builtins.contains(kind, target)) {
        return Err(PresetError::Invalid(
            "the target name already exists".into(),
        ));
    }

    let mut definition = match builtins.definition(kind, old_name) {
        Some(PresetDefinitionRef::Sandbox(preset)) => {
            PresetDefinition::Sandbox(Box::new(preset.clone()))
        }
        Some(PresetDefinitionRef::Command(preset)) => {
            PresetDefinition::Command(Box::new(preset.clone()))
        }
        None => {
            return Err(PresetError::Missing(format!(
                "unknown {kind} preset: {old_name}"
            )))
        }
    };
    definition.rename(target.to_string());
    Ok(definition)
}

/// Copy a built-in definition into the user catalog, optionally under a new name.
pub fn copy_builtin(kind: PresetKind, old_name: &str, target: &str) -> Result<(), PresetError> {
    let builtins = Table::builtins();
    let users = Table::users();
    let definition = copy_definition(kind, old_name, target, &builtins, &users)?;
    save_definition(definition).map_err(|error| PresetError::Invalid(error.to_string()))
}

/// Validate an API replacement against the table containing that replacement, then persist it.
pub fn validate_and_save(definition: PresetDefinition) -> anyhow::Result<()> {
    match &definition {
        PresetDefinition::Sandbox(preset) => {
            let current = table();
            let mut candidate = (*current).clone();
            candidate
                .sandbox
                .retain(|existing| existing.name != preset.name);
            candidate.sandbox.push((**preset).clone());
            crate::sandbox::validate_preset(preset, &candidate)?;
        }
        PresetDefinition::Command(preset) => {
            if preset.cmd.trim().is_empty() {
                anyhow::bail!("command line is empty");
            }
            let table = table();
            for dependency in &preset.sandbox {
                crate::sandbox::validate_preset_name(dependency, &table).map_err(|error| {
                    anyhow::anyhow!("invalid sandbox dependency {dependency:?}: {error}")
                })?;
            }
        }
    }
    save_definition(definition)
}

fn check_delete(
    kind: PresetKind,
    name: &str,
    builtins: &Table,
    users: &Table,
) -> Result<(), PresetError> {
    if !users.contains(kind, name) {
        return Err(PresetError::Missing(
            "no user definition exists for that preset".into(),
        ));
    }

    if kind == PresetKind::Sandbox && !builtins.contains(PresetKind::Sandbox, name) {
        for command in users.commands.iter().chain(builtins.commands.iter()) {
            if command.sandbox.iter().any(|dependency| dependency == name) {
                return Err(PresetError::Invalid(format!(
                    "sandbox {name:?} is required by command {:?}",
                    command.name
                )));
            }
        }
        for preset in users.sandbox.iter().chain(builtins.sandbox.iter()) {
            if preset.requires.iter().any(|dependency| dependency == name) {
                return Err(PresetError::Invalid(format!(
                    "sandbox {name:?} is required by sandbox {:?}",
                    preset.name
                )));
            }
        }
    }

    Ok(())
}

/// Delete a user definition only when removing it cannot leave a dependency unresolved.
pub fn delete_user(kind: PresetKind, name: &str) -> Result<(), PresetError> {
    let builtins = Table::builtins();
    let users = Table::users();
    check_delete(kind, name, &builtins, &users)?;
    remove_user(kind, name).map_err(|error| PresetError::Invalid(error.to_string()))
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

pub fn reload() -> bool {
    let fresh = match Table::try_load_from(&Table::dir()) {
        Ok(table) => Arc::new(table),
        Err(e) => {
            tracing::warn!("presets changed on disk but are not reloadable: {e:#}");
            return false;
        }
    };
    *cell().write().unwrap() = fresh;
    true
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

        // The command presets used by built-in library, including the shells.
        for name in [
            "claude", "opencode", "pi", "bash", "zsh", "fish", "nu", "pwsh", "sh",
        ] {
            assert!(t.command(name).is_some(), "no {name} command preset");
        }
        assert!(
            t.command("shell").is_none(),
            "old shell command preset remains"
        );
        assert_eq!(t.command("claude").unwrap().sandbox, vec!["claude"]);
        assert_eq!(t.command("codex").unwrap().sandbox, vec!["codex"]);
        for name in ["claude", "codex", "opencode", "pi"] {
            assert_eq!(t.command(name).unwrap().kind, CommandKind::Agent);
        }
        for name in ["bash", "zsh", "fish", "nu", "pwsh", "sh"] {
            assert_eq!(t.command(name).unwrap().kind, CommandKind::Shell);
        }
        assert_eq!(t.sandbox("codex").unwrap().requires, vec!["x11", "wayland"]);
        assert_eq!(
            t.sandbox("systemd").unwrap().setenv["SYSTEMCTL_FORCE_BUS"],
            "1"
        );
        assert_eq!(t.sandbox("gpu").unwrap().dev, vec!["/dev/dri", "/dev/kfd"]);

        // The new presets parse and name themselves correctly.
        for name in [
            "go",
            "gh",
            "aws",
            "kube",
            "android-dev",
            "android-debug",
            "ios-dev",
            "ios-debug",
            "slopworld-debug",
        ] {
            let p = t
                .sandbox(name)
                .unwrap_or_else(|| panic!("no {name} sandbox preset"));
            assert!(!p.description.is_empty(), "{name} has no description");
        }
        assert_eq!(
            t.sandbox("android-debug").unwrap().requires,
            vec!["android-dev", "gpu", "x11", "wayland"]
        );
        assert_eq!(t.sandbox("ios-debug").unwrap().requires, vec!["ios-dev"]);
        assert!(t
            .sandbox("android-debug")
            .unwrap()
            .dev
            .iter()
            .any(|path| path == "/dev/bus/usb"));
        assert!(t
            .sandbox("ios-debug")
            .unwrap()
            .dev
            .iter()
            .any(|path| path == "/dev/bus/usb"));
        assert!(!t.sandbox("android-debug").unwrap().escapes.is_empty());
        assert!(!t.sandbox("ios-debug").unwrap().escapes.is_empty());
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
            "$XDG_DATA_HOME/slopworld/jukebox",
            "~/.local/share/slopworld/jukebox",
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
    fn unknown_preset_fields_are_rejected() {
        assert!(toml::from_str::<PresetFile>(
            r#"
            [[sandbox]]
            name = "sandbox"
            unexpected = "value"
            "#,
        )
        .is_err());
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
            "android-dev",
            "ios-dev",
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
        assert_eq!(
            t.sandbox("ssh").unwrap().private,
            vec!["/etc/ssh/ssh_config.d"]
        );
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

    #[test]
    fn command_kind_defaults_to_agent_for_legacy_files() {
        let file: PresetFile = toml::from_str(
            r#"
            [[command]]
            name = "legacy"
            cmd = "legacy"
            "#,
        )
        .unwrap();

        assert_eq!(file.command[0].kind, CommandKind::Agent);
    }

    #[test]
    fn kind_parsing_and_typed_table_dispatch_cover_all_sources() {
        assert_eq!(
            "sandbox".parse::<PresetKind>().unwrap(),
            PresetKind::Sandbox
        );
        assert_eq!(
            "command".parse::<PresetKind>().unwrap(),
            PresetKind::Command
        );
        assert_eq!(
            "other".parse::<PresetKind>().unwrap_err(),
            "unknown preset kind: other"
        );

        let builtins = Table {
            sandbox: vec![SandboxPreset {
                name: "system".into(),
                ..Default::default()
            }],
            commands: vec![CommandPreset {
                name: "command".into(),
                cmd: "tool".into(),
                ..Default::default()
            }],
        };
        let users = Table {
            sandbox: vec![SandboxPreset {
                name: "user".into(),
                ..Default::default()
            }],
            commands: vec![CommandPreset {
                name: "command".into(),
                cmd: "replacement".into(),
                ..Default::default()
            }],
        };

        assert!(builtins.contains(PresetKind::Sandbox, "system"));
        assert!(!builtins.contains(PresetKind::Command, "system"));
        assert!(matches!(
            builtins.definition(PresetKind::Sandbox, "system"),
            Some(PresetDefinitionRef::Sandbox(_))
        ));
        assert_eq!(
            builtins
                .source(PresetKind::Sandbox, "system", &users)
                .as_str(),
            "system"
        );
        assert_eq!(
            builtins
                .source(PresetKind::Sandbox, "user", &users)
                .as_str(),
            "user"
        );
        assert_eq!(
            builtins
                .source(PresetKind::Command, "command", &users)
                .as_str(),
            "override"
        );
        assert_eq!(
            builtins
                .source(PresetKind::Sandbox, "missing", &users)
                .as_str(),
            "unknown"
        );
    }

    #[test]
    fn copying_checks_user_and_target_conflicts_before_lookup() {
        let builtins = Table {
            sandbox: vec![
                SandboxPreset {
                    name: "builtin".into(),
                    ..Default::default()
                },
                SandboxPreset {
                    name: "occupied".into(),
                    ..Default::default()
                },
            ],
            commands: Vec::new(),
        };
        let users = Table {
            sandbox: vec![SandboxPreset {
                name: "builtin".into(),
                ..Default::default()
            }],
            commands: Vec::new(),
        };

        let error =
            copy_definition(PresetKind::Sandbox, "builtin", "copy", &builtins, &users).unwrap_err();
        assert_eq!(
            error.to_string(),
            "that preset already has a user definition"
        );

        let error = copy_definition(
            PresetKind::Sandbox,
            "builtin",
            "occupied",
            &builtins,
            &Table::default(),
        )
        .unwrap_err();
        assert_eq!(error.to_string(), "the target name already exists");
    }

    #[test]
    fn user_only_sandbox_delete_checks_command_and_sandbox_dependencies() {
        let builtins = Table::default();
        let command_users = Table {
            sandbox: vec![SandboxPreset {
                name: "user-only".into(),
                ..Default::default()
            }],
            commands: vec![CommandPreset {
                name: "dependent-command".into(),
                cmd: "tool".into(),
                sandbox: vec!["user-only".into()],
                ..Default::default()
            }],
        };
        let error =
            check_delete(PresetKind::Sandbox, "user-only", &builtins, &command_users).unwrap_err();
        assert!(error.to_string().contains("dependent-command"));

        let sandbox_users = Table {
            sandbox: vec![
                SandboxPreset {
                    name: "user-only".into(),
                    ..Default::default()
                },
                SandboxPreset {
                    name: "dependent-sandbox".into(),
                    requires: vec!["user-only".into()],
                    ..Default::default()
                },
            ],
            commands: Vec::new(),
        };
        let error =
            check_delete(PresetKind::Sandbox, "user-only", &builtins, &sandbox_users).unwrap_err();
        assert!(error.to_string().contains("dependent-sandbox"));

        let override_users = Table {
            sandbox: vec![SandboxPreset {
                name: "builtin".into(),
                ..Default::default()
            }],
            commands: Vec::new(),
        };
        let builtin = Table {
            sandbox: vec![SandboxPreset {
                name: "builtin".into(),
                ..Default::default()
            }],
            commands: Vec::new(),
        };
        assert!(check_delete(PresetKind::Sandbox, "builtin", &builtin, &override_users).is_ok());
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
        let original =
            "# keep this comment\n[[sandbox]]\nname = \"other\"\ndescription = \"handwritten\"\n";
        std::fs::write(&untouched, original).unwrap();

        write_user_file_in(
            &dir,
            PresetKind::Sandbox,
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
