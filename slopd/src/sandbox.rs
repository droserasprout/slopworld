use std::ffi::OsString;
use std::path::{Component, Path, PathBuf};

use anyhow::{Context, Result};
use serde::Serialize;

use crate::config::{expand, Config, DnsConfig, Limits, NetworkMode, ProjectCfg, SessionCfg};
use crate::presets::{SandboxPreset, Table};

/// Forwarded whatever the config says, plus anything named `LC_*`. What belongs here says
/// something about *this machine* and nothing about what the sandbox can reach; anything
/// naming a socket, a token or a service is a preset.
const BASE_ENV: &[&str] = &["PATH", "LANG", "USER", "LOGNAME", "SHELL"];

const PANE_TERM: &str = "tmux-256color";

// Private networking gets an address space that cannot collide with the host's real LAN.
// pasta forwards DNS from this synthetic gateway to the host resolver, while the resolv.conf
// bind below keeps the guest from seeing a host-loopback stub address.
const PRIVATE_ADDRESS: &str = "192.0.2.2/24";
const PRIVATE_GATEWAY: &str = "192.0.2.1";
const PRIVATE_RESOLVER: &str = "192.0.2.1";

/// A name this build has no preset for is dropped with a warning rather than refused: the
/// files outlive the binary, and one bad name is not grounds for an agent that will not
/// start. A known preset with an unsafe definition is dropped by the same boundary after the
/// complete dependency closure is validated.
fn presets_for<'a>(
    cfg: &Config,
    s: &SessionCfg,
    p: &ProjectCfg,
    t: &'a Table,
) -> Vec<&'a SandboxPreset> {
    cfg.sandbox_of(s, p)
        .into_iter()
        .filter_map(|n| {
            let Some(hit) = t.sandbox(&n) else {
                tracing::warn!(
                    "session {:?} in project {:?} names unknown sandbox preset {n:?}, ignoring",
                    s.name,
                    p.name
                );
                return None;
            };
            if let Err(e) = validate_preset_name(&n, t) {
                tracing::warn!(
                    "session {:?} in project {:?} names invalid sandbox preset {n:?}: {e:#}, ignoring",
                    s.name,
                    p.name
                );
                return None;
            }
            Some(hit)
        })
        .collect()
}

/// Builds the unsandboxed "Terminal (host)" command. It inherits slopd's environment, keeps
/// the same tmux working directory as sandboxed sessions, and is intentionally not selectable
/// from `config.toml`.
pub fn host_argv(cfg: &Config, s: &SessionCfg, p: &ProjectCfg) -> Vec<String> {
    let mut a: Vec<String> = vec![
        "env".into(),
        format!("TERM={PANE_TERM}"),
        "COLORTERM=truecolor".into(),
        format!("SLOPWORLD_SESSION={}", s.name),
        format!("SLOPWORLD_PROJECT={}", p.name),
    ];
    a.extend(shell_split(&host_command(cfg, s, host_shell().as_deref())));
    a
}

/// `$SHELL`, the login shell of whoever slopd runs as, which is the shell a terminal on this
/// machine opens. `None` when the environment does not say - a daemon started without one -
/// and the preset answers instead.
pub fn host_shell() -> Option<String> {
    std::env::var("SHELL")
        .ok()
        .map(|s| s.trim().to_string())
        .filter(|s| !s.is_empty())
}

/// An unnamed host errand uses `$SHELL`; a preset or command line runs as configured.
fn host_command(cfg: &Config, s: &SessionCfg, shell: Option<&str>) -> String {
    let asked = s.cmd.is_some() || s.command.trim() != cfg.defaults.shell.trim();
    match shell {
        Some(sh) if !asked => sh.to_string(),
        _ => cfg.command_of(s),
    }
}

/// Names a host session `<project>-<shell>`, or just the shell when no project is set.
pub fn host_session_name(project: &str) -> String {
    session_name_for(project, host_shell().as_deref())
}

fn session_name_for(project: &str, shell: Option<&str>) -> String {
    let base = shell.unwrap_or("").rsplit('/').next().unwrap_or("").trim();
    let base = if base.is_empty() { "shell" } else { base };
    match project.trim() {
        "" => base.to_string(),
        p => format!("{p}-{base}"),
    }
}

/// Where a session keeps what is its own. Under `~/.local/share` rather than `TEMP_ROOT`,
/// because what lives here is an agent's memory of itself and a reboot is not a reason to
/// forget it. `SLOPD_STATE` moves it, which is what the tests use.
pub fn state_root() -> PathBuf {
    if let Ok(dir) = std::env::var("SLOPD_STATE") {
        return PathBuf::from(dir);
    }
    dirs::data_dir()
        .unwrap_or_else(|| PathBuf::from("."))
        .join("slopworld/sessions")
}

/// The copy of `host` this session gets. The original's shape is kept rather than its
/// basename, so `~/.config/opencode` and `~/.local/share/opencode` - one preset, two
/// directories, one name - never land on each other.
fn private_path(state_id: &str, host: &str) -> PathBuf {
    let host = Path::new(host);
    let root = state_root().join(state_id);
    if let Some(home) = dirs::home_dir() {
        if let Ok(rel) = host.strip_prefix(&home) {
            return root.join("home").join(rel);
        }
    }
    root.join("root")
        .join(host.strip_prefix("/").unwrap_or(host))
}

/// The durable, private directory for a configured agent.  `state_id` is not supplied by
/// clients; the manager assigns it, so a name reused after deletion cannot inherit another
/// agent's transcripts or tool configuration.
pub fn state_dir(s: &SessionCfg) -> PathBuf {
    state_root().join(&s.state_id)
}

fn trash_root() -> PathBuf {
    state_root().join(".trash")
}

const TRASH_SESSION: &str = ".slopworld-session.toml";

#[derive(Debug, Clone, Serialize)]
pub struct StoredState {
    pub kind: String,
    pub key: String,
    pub session: Option<String>,
    pub path: String,
    pub bytes: u64,
    pub modified: u64,
}

fn tree_size(path: &Path) -> u64 {
    let Ok(meta) = std::fs::symlink_metadata(path) else {
        return 0;
    };
    if !meta.is_dir() {
        return meta.len();
    }
    std::fs::read_dir(path)
        .ok()
        .into_iter()
        .flat_map(|entries| entries.flatten())
        .map(|entry| tree_size(&entry.path()))
        .sum()
}

fn stored_entry(kind: &str, key: String, session: Option<String>, path: &Path) -> StoredState {
    let modified = std::fs::symlink_metadata(path)
        .and_then(|m| m.modified())
        .ok()
        .and_then(|t| t.duration_since(std::time::UNIX_EPOCH).ok())
        .map(|d| d.as_secs())
        .unwrap_or(0);
    StoredState {
        kind: kind.into(),
        key,
        session,
        path: path.to_string_lossy().into_owned(),
        bytes: tree_size(path),
        modified,
    }
}

/// Inventory for the settings UI. Anything not claimed by a configured state id is orphaned
/// state and requires an explicit delete.
pub fn stored_states(sessions: &[SessionCfg]) -> Vec<StoredState> {
    if let Err(e) = purge_trash() {
        tracing::warn!("purging private-state trash before inventory: {e:#}");
    }
    let root = state_root();
    let mut out = Vec::new();
    let Ok(entries) = std::fs::read_dir(&root) else {
        return out;
    };
    for entry in entries.flatten() {
        let key = entry.file_name().to_string_lossy().into_owned();
        if key == ".trash" {
            if let Ok(trash) = std::fs::read_dir(entry.path()) {
                for item in trash.flatten() {
                    let item_key = item.file_name().to_string_lossy().into_owned();
                    let owner = sessions
                        .iter()
                        .filter(|s| !s.state_id.is_empty())
                        .find(|s| item_key.ends_with(&format!("-{}", s.state_id)))
                        .map(|s| s.name.clone())
                        .or_else(|| read_trashed_session(&item.path()).ok().map(|s| s.name));
                    out.push(stored_entry("trash", item_key, owner, &item.path()));
                }
            }
            continue;
        }
        let owner = sessions
            .iter()
            .find(|s| {
                state_dir(s)
                    .file_name()
                    .is_some_and(|n| n == entry.file_name())
            })
            .map(|s| s.name.clone());
        out.push(stored_entry(
            if owner.is_some() { "active" } else { "orphan" },
            key,
            owner,
            &entry.path(),
        ));
    }
    out.sort_by(|a, b| a.kind.cmp(&b.kind).then_with(|| a.key.cmp(&b.key)));
    out
}

fn direct_child(root: &Path, key: &str) -> Result<PathBuf> {
    if key == ".trash" {
        anyhow::bail!("private-state trash root is not an entry");
    }
    let mut parts = Path::new(key).components();
    let Some(Component::Normal(name)) = parts.next() else {
        anyhow::bail!("invalid private-state entry");
    };
    if parts.next().is_some() || name.is_empty() {
        anyhow::bail!("invalid private-state entry");
    }
    Ok(root.join(name))
}

pub fn delete_stored_state(kind: &str, key: &str, sessions: &[SessionCfg]) -> Result<()> {
    if kind != "orphan" && kind != "trash" {
        anyhow::bail!("only orphaned state or trash can be deleted here");
    }
    let root = if kind == "trash" {
        trash_root()
    } else {
        state_root()
    };
    let path = direct_child(&root, key)?;
    if kind == "orphan" && sessions.iter().any(|s| state_dir(s) == path) {
        anyhow::bail!("private state is still owned by a configured agent");
    }
    let meta =
        std::fs::symlink_metadata(&path).with_context(|| format!("reading {}", path.display()))?;
    if meta.is_dir() {
        std::fs::remove_dir_all(&path)
    } else {
        std::fs::remove_file(&path)
    }
    .with_context(|| format!("removing {}", path.display()))
}

pub fn restore_stored_state(key: &str, sessions: &[SessionCfg]) -> Result<String> {
    let source = direct_child(&trash_root(), key)?;
    if !source.exists() {
        anyhow::bail!("no such private-state trash entry");
    }
    let archived = read_trashed_session(&source)?;
    if archived.state_id.is_empty() {
        anyhow::bail!("trash entry has no private-state identity");
    }
    let session = sessions
        .iter()
        .filter(|s| !s.state_id.is_empty())
        .find(|s| s.state_id == archived.state_id)
        .unwrap_or(&archived);
    let destination = state_dir(session);
    if destination.exists() {
        anyhow::bail!(
            "agent {:?} already has fresh state; reset it before restoring this copy",
            session.name
        );
    }
    std::fs::rename(&source, &destination).with_context(|| {
        format!(
            "restoring {} to {}",
            source.display(),
            destination.display()
        )
    })?;
    Ok(session.name.clone())
}

fn read_trashed_session(path: &Path) -> Result<SessionCfg> {
    let metadata = path.join(TRASH_SESSION);
    let text = std::fs::read_to_string(&metadata)
        .with_context(|| format!("reading {}", metadata.display()))?;
    toml::from_str(&text).with_context(|| format!("parsing {}", metadata.display()))
}

pub fn trashed_session(key: &str) -> Result<SessionCfg> {
    let path = direct_child(&trash_root(), key)?;
    read_trashed_session(&path)
}

pub fn rollback_restored_state(key: &str, session: &SessionCfg) -> Result<()> {
    let source = state_dir(session);
    let destination = direct_child(&trash_root(), key)?;
    std::fs::rename(&source, &destination).with_context(|| {
        format!(
            "returning {} to {}",
            source.display(),
            destination.display()
        )
    })
}

pub fn finish_restored_state(session: &SessionCfg) {
    let metadata = state_dir(session).join(TRASH_SESSION);
    if let Err(e) = std::fs::remove_file(&metadata) {
        if e.kind() != std::io::ErrorKind::NotFound {
            tracing::warn!(
                "removing restored-state metadata {}: {e:#}",
                metadata.display()
            );
        }
    }
}

/// Move state out of the live namespace.  The caller owns configuration consistency; the
/// returned path lets it restore the tree if saving the corresponding config change fails.
pub fn trash_state(s: &SessionCfg, label: &str) -> Result<Option<PathBuf>> {
    if s.state_id.is_empty() {
        anyhow::bail!("session {:?} has no private-state identity", s.name);
    }
    let source = state_dir(s);
    if !source.exists() {
        return Ok(None);
    }
    let root = trash_root();
    std::fs::create_dir_all(&root).with_context(|| format!("making {}", root.display()))?;
    let stamp = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .unwrap_or_default()
        .as_millis();
    let safe = label
        .chars()
        .map(|c| {
            if c.is_ascii_alphanumeric() || c == '-' || c == '_' {
                c
            } else {
                '-'
            }
        })
        .collect::<String>();
    let destination = root.join(format!("{stamp}-{safe}-{}", s.state_id));
    std::fs::rename(&source, &destination)
        .with_context(|| format!("moving {} to {}", source.display(), destination.display()))?;
    let metadata = destination.join(TRASH_SESSION);
    let text = toml::to_string(s).context("serializing private-state trash metadata")?;
    if let Err(e) = std::fs::write(&metadata, text) {
        if let Err(restore) = std::fs::rename(&destination, &source) {
            tracing::error!(
                "writing trash metadata failed: {e:#}; state restore also failed: {restore:#}"
            );
        }
        return Err(e).with_context(|| format!("writing {}", metadata.display()));
    }
    Ok(Some(destination))
}

pub fn restore_trashed_state(s: &SessionCfg, trash: &Path) -> Result<()> {
    if s.state_id.is_empty() {
        anyhow::bail!("session {:?} has no private-state identity", s.name);
    }
    let destination = state_dir(s);
    if !trash.exists() {
        return Ok(());
    }
    std::fs::rename(trash, &destination)
        .with_context(|| format!("restoring {} to {}", trash.display(), destination.display()))?;
    finish_restored_state(s);
    Ok(())
}

/// Temporary errands have no durable agent to restore this state to. Their ids are always
/// daemon-minted, so this can remove exactly one leaf without ever falling back to a name.
pub fn remove_ephemeral_state(s: &SessionCfg) -> Result<()> {
    if s.state_id.is_empty() {
        anyhow::bail!(
            "temporary session {:?} has no private-state identity",
            s.name
        );
    }
    let path = state_dir(s);
    if !path.exists() {
        return Ok(());
    }
    std::fs::remove_dir_all(&path).with_context(|| format!("removing {}", path.display()))
}

/// Only the daemon-owned trash is reclaimed automatically. Live and orphaned directories are
/// never age-pruned: a stopped agent can still be deliberately dormant.
pub fn purge_trash() -> Result<usize> {
    const RETAIN: std::time::Duration = std::time::Duration::from_secs(14 * 24 * 60 * 60);
    let root = trash_root();
    let Ok(entries) = std::fs::read_dir(&root) else {
        return Ok(0);
    };
    let now = std::time::SystemTime::now();
    let mut purged = 0;
    for entry in entries.flatten() {
        // Not `entry.metadata()`, which follows the link: a symlink here would be read as the
        // directory it points at and then fail `remove_dir_all`, so it could never age out.
        // The rest of this module stats trash the same way.
        let Ok(meta) = std::fs::symlink_metadata(entry.path()) else {
            continue;
        };
        let Ok(modified) = meta.modified() else {
            continue;
        };
        if now.duration_since(modified).unwrap_or_default() < RETAIN {
            continue;
        }
        let path = entry.path();
        let removed = if meta.is_dir() {
            std::fs::remove_dir_all(&path)
        } else {
            std::fs::remove_file(&path)
        };
        match removed {
            Ok(()) => purged += 1,
            Err(e) => tracing::warn!("purging private-state trash {}: {e:#}", path.display()),
        }
    }
    Ok(purged)
}

/// Rejects paths reaching the daemon token/config, preset definitions, or another session's
/// private state, including paths above or below those protected roots.
pub fn refused(path: &str) -> Option<String> {
    let path = safety_path(Path::new(path));
    if path == Path::new("/") {
        return Some("the whole filesystem".into());
    }
    if dirs::home_dir().is_some_and(|home| safety_path(&home) == path) {
        return Some("the whole home directory".into());
    }

    let keep = [
        (
            "the daemon's config, and the token in it",
            Config::path_in_use(),
        ),
        (
            "the daemon's endpoint descriptor, and its token",
            crate::endpoint::path(),
        ),
        ("the preset files", Table::dir()),
        ("what the sessions keep to themselves", state_root()),
    ];
    for (what, kept) in keep {
        let kept = safety_path(&kept);
        if kept.starts_with(&path) || path.starts_with(&kept) {
            return Some(what.into());
        }
    }
    None
}

/// Resolve a path as far as the filesystem lets us, retaining any missing suffix. This keeps
/// safety checks aware of symlinks in existing parents while still allowing portable presets to
/// name software that is not installed on this machine yet.
fn safety_path(path: &Path) -> PathBuf {
    let mut missing: Vec<OsString> = Vec::new();
    let mut probe = path.to_path_buf();

    while !probe.exists() {
        let Some(name) = probe.file_name() else {
            return lexical_path(path);
        };
        missing.push(name.to_os_string());
        if !probe.pop() {
            return lexical_path(path);
        }
    }

    let mut out = std::fs::canonicalize(&probe).unwrap_or_else(|_| lexical_path(&probe));
    for name in missing.iter().rev() {
        out.push(name);
    }
    lexical_path(&out)
}

/// Normalize `.` and `..` without following symlinks. `safety_path` follows symlinks first where
/// possible, then uses this only for the missing suffix or paths whose parents do not exist.
fn lexical_path(path: &Path) -> PathBuf {
    let mut out = PathBuf::new();
    for component in path.components() {
        match component {
            Component::CurDir => {}
            Component::ParentDir => {
                out.pop();
            }
            other => out.push(other.as_os_str()),
        }
    }
    out
}

/// Validate one effective sandbox preset before either exposing it to bwrap or saving it from
/// the API. Keeping this beside argv construction prevents the API, hand-edited files and old
/// config entries from growing subtly different safety rules.
pub fn validate_preset(p: &SandboxPreset, table: &Table) -> Result<()> {
    validate_preset_fields(p, table)?;
    let mut visiting = vec![p.name.clone()];
    for required in &p.requires {
        validate_preset_name_inner(required, table, &mut visiting)?;
    }
    Ok(())
}

/// Validate a named preset and its complete dependency closure. A dependent preset is invalid
/// when any required preset is invalid, so runtime selection cannot silently apply only half of
/// a capability bundle.
pub fn validate_preset_name(name: &str, table: &Table) -> Result<()> {
    validate_preset_name_inner(name, table, &mut Vec::new())
}

fn validate_preset_name_inner(name: &str, table: &Table, visiting: &mut Vec<String>) -> Result<()> {
    if visiting.iter().any(|seen| seen == name) {
        anyhow::bail!("sandbox preset dependency cycle at {name:?}");
    }
    let p = table
        .sandbox(name)
        .ok_or_else(|| anyhow::anyhow!("unknown sandbox preset: {name}"))?;
    validate_preset_fields(p, table)?;
    visiting.push(name.to_string());
    for required in &p.requires {
        validate_preset_name_inner(required, table, visiting)?;
    }
    visiting.pop();
    Ok(())
}

fn validate_preset_fields(p: &SandboxPreset, table: &Table) -> Result<()> {
    validate_preset_paths(p)?;
    for required in &p.requires {
        if required == &p.name {
            anyhow::bail!("sandbox preset {:?} requires itself", p.name);
        }
        if table.sandbox(required).is_none() {
            anyhow::bail!(
                "sandbox preset {:?} requires unknown preset {:?}",
                p.name,
                required
            );
        }
    }
    Ok(())
}

fn validate_preset_paths(p: &SandboxPreset) -> Result<()> {
    if p.name.trim().is_empty() {
        anyhow::bail!("sandbox preset name is empty");
    }
    if p.requires.iter().any(|name| name.trim().is_empty()) {
        anyhow::bail!("sandbox preset {:?} has an empty dependency", p.name);
    }

    for (kind, paths) in [
        ("read-only", p.ro.as_slice()),
        ("read-write", p.rw.as_slice()),
        ("device", p.dev.as_slice()),
        ("private", p.private.as_slice()),
        ("seed", p.seed.as_slice()),
        ("skip", p.skip.as_slice()),
        ("shared", p.shared.as_slice()),
    ] {
        for raw in paths {
            let expanded = expand(raw);
            if expanded.is_empty() {
                continue;
            }
            if let Some(what) = refused(&expanded) {
                anyhow::bail!(
                    "sandbox preset {:?} {kind} path {raw:?} reaches {what}",
                    p.name
                );
            }
        }
    }

    let private: Vec<PathBuf> = p
        .private
        .iter()
        .filter_map(|raw| {
            let expanded = expand(raw);
            (!expanded.is_empty()).then(|| safety_path(Path::new(&expanded)))
        })
        .collect();

    for (kind, paths) in [("seed", p.seed.as_slice()), ("skip", p.skip.as_slice())] {
        for raw in paths {
            let expanded = expand(raw);
            if expanded.is_empty() {
                continue;
            }
            let path = safety_path(Path::new(&expanded));
            if !private.iter().any(|root| path.starts_with(root)) {
                anyhow::bail!(
                    "sandbox preset {:?} {kind} path {raw:?} is outside its private paths",
                    p.name
                );
            }
        }
    }

    for raw in &p.shared {
        let expanded = expand(raw);
        if expanded.is_empty() {
            anyhow::bail!(
                "sandbox preset {:?} shared path {raw:?} contains an unset variable",
                p.name
            );
        }
        if Path::new(&expanded).exists() && !Path::new(&expanded).is_file() {
            anyhow::bail!(
                "sandbox preset {:?} shared path {raw:?} is not a regular file",
                p.name
            );
        }
        let path = safety_path(Path::new(&expanded));
        if !private.iter().any(|root| path.starts_with(root)) {
            anyhow::bail!(
                "sandbox preset {:?} shared path {raw:?} must be inside a private path",
                p.name
            );
        }
    }
    Ok(())
}

/// Returns existing `(private_copy, host_path)` pairs used to shadow private state under
/// `$HOME`. Disk preparation happens in `prepare_network`.
fn private_binds(cfg: &Config, s: &SessionCfg, p: &ProjectCfg, t: &Table) -> Vec<(String, String)> {
    let mut out: Vec<(String, String)> = Vec::new();
    for pr in presets_for(cfg, s, p, t) {
        for path in &pr.private {
            let host = expand(path);
            if host.is_empty() || !Path::new(&host).exists() {
                continue;
            }
            if out.iter().any(|(_, seen)| seen == &host) {
                continue;
            }
            let copy = private_path(&s.state_id, &host);
            out.push((copy.to_string_lossy().into_owned(), host));
        }
    }
    out
}

/// Returns existing regular files that presets may share read-write into private state.
/// Refused paths and directories are excluded so shared state cannot grant execution or reach
/// another session's secrets.
fn shared_binds(cfg: &Config, s: &SessionCfg, p: &ProjectCfg, t: &Table) -> Vec<String> {
    let mut out: Vec<String> = Vec::new();
    for pr in presets_for(cfg, s, p, t) {
        for path in &pr.shared {
            let host = expand(path);
            if host.is_empty() || out.contains(&host) {
                continue;
            }
            if let Some(what) = refused(&host) {
                tracing::warn!("not sharing {host}: it reaches {what}");
                continue;
            }
            if !Path::new(&host).exists() {
                continue;
            }
            if !Path::new(&host).is_file() {
                tracing::warn!("not sharing {host}: shared paths are files, and this is not one");
                continue;
            }
            out.push(host);
        }
    }
    out
}

/// The tmux server is outside bwrap and uses the host uid in its socket directory. A bwrap
/// session is uid 0 in its user namespace, so a debug preset gets the host directory mounted at
/// the path tmux will calculate inside the session. The bind is read-only: clients talk through
/// the socket, while an absent or stale server costs the preset nothing.
fn tmux_socket_bind(socket: &str) -> Option<(String, String)> {
    if socket.trim().is_empty() {
        return None;
    }
    let root = std::env::var_os("TMUX_TMPDIR")
        .map(PathBuf::from)
        .unwrap_or_else(|| PathBuf::from("/tmp"));
    let host_dir = root.join(format!("tmux-{}", nix::unistd::getuid().as_raw()));
    if !host_dir.is_dir() || !host_dir.join(socket).exists() {
        return None;
    }
    let host_dir = host_dir.to_string_lossy().into_owned();
    if let Some(what) = refused(&host_dir) {
        tracing::warn!("not exposing tmux socket directory {host_dir}: it reaches {what}");
        return None;
    }
    Some((host_dir, "/tmp/tmux-0".into()))
}

/// Creates generated resolver files and seeds each private tree before argv construction;
/// existing copies are preserved. The private resolver is always synthetic, while an explicit
/// DNS list in host mode needs a generated `/etc/resolv.conf` source too.
pub fn prepare_network(cfg: &Config, s: &SessionCfg, p: &ProjectCfg) -> Result<()> {
    let network = cfg.network_of(s, p)?;
    let dns = cfg.dns_of(s, p);
    match network {
        NetworkMode::Private => prepare_resolver(&s.state_id, &[PRIVATE_RESOLVER.into()])?,
        NetworkMode::Host if matches!(dns, DnsConfig::Servers { .. }) => {
            prepare_resolver(&s.state_id, &dns.servers())?
        }
        NetworkMode::None | NetworkMode::Host => {}
    }

    let t = crate::presets::table();
    for pr in presets_for(cfg, s, p, &t) {
        for path in &pr.private {
            let host = expand(path);
            if host.is_empty() || !Path::new(&host).exists() {
                continue;
            }
            let copy = private_path(&s.state_id, &host);
            if copy.exists() {
                continue;
            }
            tracing::info!("session {:?} gets its own {host}", s.name);
            seed_into(pr, &host, &copy)?;
        }
    }
    Ok(())
}

fn private_resolver_path(session: &str) -> PathBuf {
    state_root().join(session).join("network/resolv.conf")
}

fn prepare_resolver(session: &str, servers: &[String]) -> Result<()> {
    let path = private_resolver_path(session);
    if let Some(parent) = path.parent() {
        std::fs::create_dir_all(parent).with_context(|| format!("making {}", parent.display()))?;
    }
    let text = servers
        .iter()
        .map(|server| format!("nameserver {server}"))
        .collect::<Vec<_>>()
        .join("\n");
    std::fs::write(&path, format!("{text}\n"))
        .with_context(|| format!("writing {}", path.display()))?;
    Ok(())
}

/// `/etc/resolv.conf` is commonly a symlink into `/run`, which is not mounted in the sandbox.
/// Bind the replacement onto the real target so the symlink still resolves inside bwrap.
fn resolver_target() -> Option<String> {
    std::fs::canonicalize("/etc/resolv.conf")
        .ok()
        .map(|p| p.to_string_lossy().into_owned())
        .filter(|target| target != "/etc/resolv.conf")
}

/// A transient systemd user scope carrying this session's resource caps, so the agent's whole
/// process tree lands in a cgroup the kernel enforces. An empty `Limits` never reaches here.
/// slopd already leans on `systemd-run` for the tmux server and the game launcher, so it is a
/// dependency in place rather than a new one - and unlike those two there is no silent inline
/// fallback: a cap the caller asked for is enforced or the session does not start.
fn scope_prefix(limits: &Limits) -> Vec<String> {
    let mut out = vec![
        "systemd-run".into(),
        "--user".into(),
        "--scope".into(),
        "--quiet".into(),
        "--collect".into(),
    ];
    let mut prop = |k: &str, v: String| {
        out.push("--property".into());
        out.push(format!("{k}={v}"));
    };
    if let Some(mb) = limits.memory_mb {
        prop("MemoryMax", format!("{mb}M"));
    }
    if let Some(pids) = limits.pids {
        prop("TasksMax", pids.to_string());
    }
    if let Some(nofile) = limits.nofile {
        prop("LimitNOFILE", nofile.to_string());
    }
    if let Some(cpu) = limits.cpu_pct {
        prop("CPUQuota", format!("{cpu}%"));
    }
    out.push("--".into());
    out
}

fn pasta_prefix(dns: &DnsConfig) -> Vec<String> {
    let mut out = vec![
        "pasta".into(),
        "--foreground".into(),
        "--quiet".into(),
        "--config-net".into(),
        "--no-map-gw".into(),
        "--ipv4-only".into(),
        "--tcp-ports".into(),
        "none".into(),
        "--udp-ports".into(),
        "none".into(),
        "--tcp-ns".into(),
        "none".into(),
        "--udp-ns".into(),
        "none".into(),
        "--address".into(),
        PRIVATE_ADDRESS.into(),
        "--gateway".into(),
        PRIVATE_GATEWAY.into(),
        "--dns-forward".into(),
        PRIVATE_RESOLVER.into(),
    ];
    for server in dns.servers() {
        out.push("--dns-host".into());
        out.push(server.to_string());
    }
    out.push("--".into());
    out
}

/// One private path, made and seeded. Returns having done nothing if the copy is already
/// there, which is what "once" means: what an agent has written is never trodden on by what
/// the host has changed since. The tree is an ordinary directory - deleting a session's is
/// how it is handed a fresh one.
fn seed_into(pr: &SandboxPreset, host: &str, copy: &Path) -> Result<()> {
    if copy.exists() {
        return Ok(());
    }
    if let Some(parent) = copy.parent() {
        std::fs::create_dir_all(parent).with_context(|| format!("making {}", parent.display()))?;
    }

    // A file is its own seed.
    if Path::new(host).is_file() {
        std::fs::copy(host, copy).with_context(|| format!("seeding {}", copy.display()))?;
        return Ok(());
    }
    std::fs::create_dir_all(copy).with_context(|| format!("making {}", copy.display()))?;

    // `skip` is expanded once here rather than per directory walked: it is a handful of paths
    // and the walk is not. A shared file is skipped too, and not because of its size: it is
    // bound over from the host anyway, and seeding it would leave a superseded credential
    // lying in the session directory for as long as that session exists.
    let skip: Vec<String> = pr
        .skip
        .iter()
        .chain(pr.shared.iter())
        .map(|s| expand(s))
        .collect();
    let skipped = |p: &Path| skip.iter().any(|s| Path::new(s) == p);

    // Copy top-level files generically for tool portability; `skip` also excludes sensitive
    // history and shared files.
    match std::fs::read_dir(host) {
        Ok(entries) => {
            for entry in entries.flatten() {
                if entry.path().is_file() && !skipped(&entry.path()) {
                    let to = copy.join(entry.file_name());
                    if let Err(e) = std::fs::copy(entry.path(), &to) {
                        tracing::warn!("seeding {}: {e:#}", to.display());
                    }
                }
            }
        }
        Err(e) => tracing::warn!("reading {host}: {e:#}"),
    }

    // And the subdirectories asked for by name, which is where what the *user* wrote lives -
    // agents, commands, plugins - as against what the tool wrote about them. The preset's list
    // is what every session of that software wants; a user preset attached to one project or
    // agent can carry any extra state that ground needs, without another override layer here.
    for from in &pr.seed {
        let from = expand(from);
        let Ok(rel) = Path::new(&from).strip_prefix(host) else {
            continue; // a seed for some other private path, or for another preset's
        };
        let to = copy.join(rel);
        // Said out loud, both ways. A seed path that is not there is *usually* honest - no two
        // machines keep all of what a preset names - but it is also exactly how a typo looks,
        // and `pi` shipped naming three directories it has never made without a word about it.
        // Twice the answer was "seeding worked, look elsewhere" when nothing had been copied.
        if !Path::new(&from).exists() {
            tracing::info!("seed {from} is not on this machine, nothing copied");
            continue;
        }
        match seed(Path::new(&from), &to, &skip) {
            Ok(()) => tracing::info!("seeded {from}"),
            Err(e) => tracing::warn!("seeding {} from {from}: {e:#}", to.display()),
        }
    }
    Ok(())
}

/// Recursively copies an existing seed entry, omitting paths in `skip`; missing sources are
/// allowed because presets describe optional software state.
fn seed(from: &Path, to: &Path, skip: &[String]) -> Result<()> {
    if !from.exists() || skip.iter().any(|s| Path::new(s) == from) {
        return Ok(());
    }
    if from.is_file() {
        if let Some(parent) = to.parent() {
            std::fs::create_dir_all(parent)?;
        }
        std::fs::copy(from, to)?;
        return Ok(());
    }
    std::fs::create_dir_all(to)?;
    for entry in std::fs::read_dir(from)? {
        let entry = entry?;
        seed(&entry.path(), &to.join(entry.file_name()), skip)?;
    }
    Ok(())
}

pub fn build_argv(cfg: &Config, s: &SessionCfg, p: &ProjectCfg) -> Result<Vec<String>> {
    let network = cfg.network_of(s, p)?;
    let dns = cfg.dns_of(s, p);
    let agent_argv: Vec<String> = shell_split(&cfg.command_of(s));
    let dir = expand(&p.dir);

    let table = crate::presets::table();
    let presets = presets_for(cfg, s, p, &table);

    let home = dirs::home_dir()
        .map(|p| p.to_string_lossy().into_owned())
        .unwrap_or_else(|| "/root".into());

    // Global first, then the resolved presets, so the most specific answer for a path is the
    // last one bwrap sees.
    let ro = paths(&presets, |pr| &pr.ro);
    let rw = paths(&presets, |pr| &pr.rw);
    let dev = paths(&presets, |pr| &pr.dev);
    let tmux = presets.iter().any(|pr| pr.tmux);

    let mut a: Vec<String> = vec!["bwrap".into()];
    let mut push = |args: &[&str]| a.extend(args.iter().map(|x| x.to_string()));

    // Built back up rather than inherited: a variable naming a socket that is not there is
    // worse than its absence, a program reading it as "this host has one" and failing at the
    // far end of a connect().
    push(&["--die-with-parent", "--unshare-all", "--clearenv"]);
    if network != NetworkMode::None {
        push(&["--share-net"]);
    }

    // /etc/resolv.conf often points into unmounted /run; use resolved's stub when available so
    // split-DNS routing is preserved.
    let resolv = if network == NetworkMode::Host {
        resolver_target().map(|target| match &dns {
            DnsConfig::Resolved => {
                let stub = "/run/systemd/resolve/stub-resolv.conf";
                let src = if std::path::Path::new(stub).exists() {
                    stub.to_string()
                } else {
                    target.clone()
                };
                (src, target)
            }
            DnsConfig::Servers { .. } => (
                private_resolver_path(&s.state_id)
                    .to_string_lossy()
                    .into_owned(),
                target,
            ),
        })
    } else if network == NetworkMode::Private {
        Some((
            private_resolver_path(&s.state_id)
                .to_string_lossy()
                .into_owned(),
            resolver_target().unwrap_or_else(|| "/etc/resolv.conf".into()),
        ))
    } else {
        None
    };
    // Usr-merge symlinks, otherwise nothing resolves inside the namespace.
    push(&["--symlink", "usr/lib", "/lib"]);
    push(&["--symlink", "usr/lib", "/lib64"]);
    push(&["--symlink", "usr/bin", "/bin"]);
    push(&["--symlink", "usr/bin", "/sbin"]);

    // The skeleton goes down before any bind: each of these covers what is under it and bwrap
    // mounts in the order given. `x11` binds /tmp/.X11-unix, this tmpfs buried it, and the
    // preset went on handing out a DISPLAY with no socket behind it.
    push(&["--proc", "/proc"]);
    push(&["--dev", "/dev"]);
    push(&["--tmpfs", "/tmp"]);

    for path in &ro {
        push(&["--ro-bind", path, path]);
    }
    // Host mode keeps the old resolver ordering: it sits above the ordinary ro binds, while
    // a preset that explicitly binds a larger tree still gets the final say.
    if network == NetworkMode::Host {
        if let Some((src, target)) = &resolv {
            push(&["--ro-bind", src.as_str(), target.as_str()]);
        }
    }

    for path in &rw {
        push(&["--bind", path, path]);
    }
    // After the /dev tmpfs, or it would be mounted over.
    for path in &dev {
        push(&["--dev-bind", path, path]);
    }

    // The debug preset asks for this after the ordinary binds so a broad `/tmp` bind from
    // another preset cannot bury the socket. Inside bwrap the guest uid is 0, hence the target.
    if tmux {
        if let Some((source, target)) = tmux_socket_bind(&cfg.daemon.tmux_socket) {
            push(&["--ro-bind", source.as_str(), target.as_str()]);
        }
    }

    // Last of the binds under $HOME, so the private copy wins over an ordinary preset bind:
    // the point of a private path is that there is no way to ask for the original, and an
    // earlier bind of the same target is one bwrap mounts over.
    for (copy, host) in private_binds(cfg, s, p, &table) {
        push(&["--bind", copy.as_str(), host.as_str()]);
    }

    // After the private binds, and only ever inside one: a shared file is a hole cut in a
    // copy, so it has to be mounted over the copy rather than under it. bwrap makes the
    // mount point, which is why nothing seeds one.
    for path in shared_binds(cfg, s, p, &table) {
        push(&["--bind", path.as_str(), path.as_str()]);
    }

    push(&["--bind", &dir, &dir]);
    // Private mode's resolver must be the last bind at this target. A user preset or project
    // directory may bind /etc or a file below it, but neither should restore a host-loopback
    // resolver.
    if network == NetworkMode::Private {
        if let Some((src, target)) = &resolv {
            push(&["--ro-bind", src.as_str(), target.as_str()]);
        }
    }

    push(&["--setenv", "HOME", &home]);
    push(&["--setenv", "SLOPWORLD_SESSION", &s.name]);
    push(&["--setenv", "SLOPWORLD_PROJECT", &p.name]);
    push(&["--chdir", &dir]);

    // Stated rather than forwarded: the terminal is one slopd built, and a daemon has
    // none of its own to inherit.
    push(&["--setenv", "TERM", PANE_TERM]);
    push(&["--setenv", "COLORTERM", "truecolor"]);

    // Compiled in: a config written before --clearenv lists none of them, and an agent with
    // no PATH is a session that starts and dies.
    let mut passed: Vec<String> = Vec::new();
    for (k, v) in std::env::vars() {
        if BASE_ENV.contains(&k.as_str()) || k.starts_with("LC_") {
            push(&["--setenv", k.as_str(), v.as_str()]);
            passed.push(k);
        }
    }

    let mut env: Vec<&str> = Vec::new();
    for pr in &presets {
        env.extend(pr.env.iter().map(String::as_str));
    }

    for k in env {
        if passed.iter().any(|seen| seen == k) {
            continue;
        }
        passed.push(k.to_string());
        if let Ok(v) = std::env::var(k) {
            push(&["--setenv", k, &v]);
        }
    }

    // Last, so a preset that knows what a value must be inside the sandbox beats
    // whatever slopd inherited for the same name.
    for pr in &presets {
        for (k, v) in &pr.setenv {
            push(&["--setenv", k.as_str(), v.as_str()]);
        }
    }

    // slopd captures Pi prompts before tmux, just as it does Codex prompts. Disable the
    // project-local extension in managed sessions so it cannot race the daemon or require
    // project trust and a sandbox-visible OpenRouter key.
    if agent_argv.first().is_some_and(|command| {
        Path::new(command)
            .file_name()
            .is_some_and(|name| name == "pi")
    }) {
        push(&["--setenv", "SLOPWORLD_PI_TITLES", "never"]);
    }

    a.push("--".into());
    a.extend(agent_argv);

    let a = if network == NetworkMode::Private {
        let mut pasta = pasta_prefix(&dns);
        pasta.extend(a);
        pasta
    } else {
        a
    };

    // The scope goes outermost, so pasta and bwrap and the agent all count against the caps.
    let limits = cfg.limits_of(s, p);
    if limits.is_empty() {
        Ok(a)
    } else {
        let mut scoped = scope_prefix(&limits);
        scoped.extend(a);
        Ok(scoped)
    }
}

/// Expanded, dropped if they are not on this host, and deduplicated.
fn paths(presets: &[&SandboxPreset], pick: fn(&SandboxPreset) -> &[String]) -> Vec<String> {
    let from_presets: Vec<String> = presets
        .iter()
        .flat_map(|pr| pick(pr).iter().cloned())
        .collect();

    let mut out: Vec<String> = Vec::new();
    for path in from_presets {
        let path = expand(&path);
        if path.is_empty() || out.contains(&path) {
            continue;
        }
        // Warned about and dropped rather than refused, the way an unknown preset name is:
        // the files outlive the binary, and a line somebody wrote a year ago is not grounds
        // for an agent that will not start. It is still never bound.
        if let Some(what) = refused(&path) {
            tracing::warn!("not binding {path}: it reaches {what}");
            continue;
        }
        if Path::new(&path).exists() {
            out.push(path);
        }
    }
    out
}

/// No expansion, no globbing - we are building an argv, not running a shell.
pub fn shell_split(s: &str) -> Vec<String> {
    let mut out = Vec::new();
    let mut cur = String::new();
    let mut quote: Option<char> = None;
    let mut any = false;
    let mut escaped = false;

    for c in s.chars() {
        if escaped {
            cur.push(c);
            any = true;
            escaped = false;
            continue;
        }

        match quote {
            Some('\'') if c == '\'' => quote = None,
            Some('\'') => cur.push(c),
            Some('"') if c == '"' => quote = None,
            Some('"') if c == '\\' => escaped = true,
            Some(_) => cur.push(c),
            None if c == '\\' => {
                escaped = true;
                any = true;
            }
            None if c == '\'' || c == '"' => {
                quote = Some(c);
                any = true;
            }
            None if c.is_whitespace() => {
                if !cur.is_empty() || any {
                    out.push(std::mem::take(&mut cur));
                    any = false;
                }
            }
            None => cur.push(c),
        }
    }
    if escaped {
        cur.push('\\');
    }
    if !cur.is_empty() || any {
        out.push(cur);
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    fn argv() -> Vec<String> {
        let cfg = Config::default();
        let s = SessionCfg {
            name: "a".into(),
            project: "p".into(),
            ..Default::default()
        };
        let p = ProjectCfg {
            name: "p".into(),
            dir: "/tmp".into(),
            sandbox: vec!["x11".into()],
            ..Default::default()
        };
        build_argv(&cfg, &s, &p).expect("sandbox argv")
    }

    fn at(a: &[String], needle: &str) -> usize {
        a.iter()
            .position(|x| x == needle)
            .unwrap_or_else(|| panic!("no {needle} in {a:?}"))
    }

    #[test]
    fn network_mode_selects_the_expected_namespace() {
        let cfg = Config::default();
        let s = SessionCfg {
            name: "a".into(),
            project: "p".into(),
            ..Default::default()
        };

        let mut p = ProjectCfg {
            name: "p".into(),
            dir: "/tmp".into(),
            ..Default::default()
        };
        p.network = NetworkMode::None;
        let none = build_argv(&cfg, &s, &p).expect("no-network argv");
        assert_eq!(none[0], "bwrap");
        assert!(!none.contains(&"--share-net".into()));

        p.network = NetworkMode::Host;
        let host = build_argv(&cfg, &s, &p).expect("host-network argv");
        assert_eq!(host[0], "bwrap");
        assert!(host.contains(&"--share-net".into()));

        p.network = NetworkMode::Private;
        let private = build_argv(&cfg, &s, &p).expect("private-network argv");
        assert_eq!(private[0], "pasta");
        assert!(private.contains(&"--ipv4-only".into()));
        assert!(private.contains(&"--tcp-ports".into()));
        assert!(private.contains(&"none".into()));
        assert!(private.contains(&"--share-net".into()));
        assert!(private.contains(&PRIVATE_ADDRESS.into()));
        let dns = private
            .windows(2)
            .find(|w| w[0] == "--dns-host")
            .expect("resolved DNS host");
        assert_eq!(dns[1], "127.0.0.53");

        p.dns = Some(DnsConfig::Servers {
            servers: vec!["10.0.0.53".parse().unwrap(), "10.0.0.54".parse().unwrap()],
        });
        let explicit = build_argv(&cfg, &s, &p).expect("explicit DNS argv");
        let hosts: Vec<_> = explicit
            .windows(2)
            .filter(|w| w[0] == "--dns-host")
            .map(|w| w[1].as_str())
            .collect();
        assert_eq!(hosts, vec!["10.0.0.53", "10.0.0.54"]);
    }

    #[test]
    fn resource_limits_wrap_the_agent_in_a_systemd_scope() {
        let cfg = Config::default();
        let s = SessionCfg {
            name: "a".into(),
            project: "p".into(),
            limits: Limits {
                memory_mb: Some(512),
                pids: Some(64),
                nofile: None,
                cpu_pct: Some(150),
            },
            ..Default::default()
        };
        let p = ProjectCfg {
            name: "p".into(),
            dir: "/tmp".into(),
            network: NetworkMode::None,
            ..Default::default()
        };
        let a = build_argv(&cfg, &s, &p).expect("scoped argv");

        // Outermost: the scope is the very first thing, ahead of bwrap.
        assert_eq!(a[0], "systemd-run");
        assert!(a.contains(&"--scope".into()));
        assert!(a
            .windows(2)
            .any(|w| w[0] == "--property" && w[1] == "MemoryMax=512M"));
        assert!(a
            .windows(2)
            .any(|w| w[0] == "--property" && w[1] == "TasksMax=64"));
        assert!(a
            .windows(2)
            .any(|w| w[0] == "--property" && w[1] == "CPUQuota=150%"));
        // An unset cap is absent, never zero.
        assert!(!a.iter().any(|x| x.starts_with("LimitNOFILE")));
        // bwrap follows the scope's own `--` separator.
        let sep = a.iter().position(|x| x == "--").expect("scope separator");
        assert_eq!(a[sep + 1], "bwrap");
    }

    #[test]
    fn no_limits_leaves_the_argv_unwrapped() {
        let cfg = Config::default();
        let s = SessionCfg {
            name: "a".into(),
            project: "p".into(),
            ..Default::default()
        };
        let p = ProjectCfg {
            name: "p".into(),
            dir: "/tmp".into(),
            ..Default::default()
        };
        let a = build_argv(&cfg, &s, &p).expect("argv");
        assert!(!a.contains(&"systemd-run".into()));
    }

    #[test]
    fn private_resolver_binds_to_the_canonical_target() {
        let Some(target) = resolver_target() else {
            return;
        };
        let cfg = Config::default();
        let s = SessionCfg {
            name: "resolver-test".into(),
            project: "p".into(),
            ..Default::default()
        };
        let p = ProjectCfg {
            name: "p".into(),
            dir: "/tmp".into(),
            ..Default::default()
        };
        let a = build_argv(&cfg, &s, &p).expect("private sandbox argv");
        let source = private_resolver_path(&s.state_id)
            .to_string_lossy()
            .into_owned();

        assert!(
            a.windows(3)
                .any(|w| w[0] == "--ro-bind" && w[1] == source && w[2] == target),
            "private resolver was not bound to {target}: {a:?}"
        );
        assert!(
            !a.windows(3)
                .any(|w| { w[0] == "--ro-bind" && w[1] == source && w[2] == "/etc/resolv.conf" }),
            "private resolver still targets the symlink: {a:?}"
        );
    }

    /// The environment is built, not inherited.
    #[test]
    fn the_environment_is_declared() {
        let a = argv();
        assert!(a.contains(&"--clearenv".to_string()));

        let term = at(&a, "TERM");
        assert_eq!(a[term - 1], "--setenv");
        assert_eq!(a[term + 1], PANE_TERM);

        assert!(a.contains(&"PATH".to_string()));
    }

    #[test]
    fn pi_extension_is_disabled_when_daemon_owns_titles() {
        let mut cfg = Config::default();
        cfg.daemon.pi_titles = crate::config::TitlePolicy::Once;
        cfg.daemon.pi_title_model = "test/title-model".into();
        let s = SessionCfg {
            name: "pi".into(),
            project: "p".into(),
            cmd: Some("pi --model test".into()),
            ..Default::default()
        };
        let p = ProjectCfg {
            name: "p".into(),
            dir: "/tmp".into(),
            ..Default::default()
        };
        let a = build_argv(&cfg, &s, &p).expect("pi sandbox argv");
        assert!(a
            .windows(3)
            .any(|w| { w[0] == "--setenv" && w[1] == "SLOPWORLD_PI_TITLES" && w[2] == "never" }));
    }

    /// The point of the host errand: no bwrap anywhere in it, and the shell at the end of it
    /// rather than behind a `--`.
    #[test]
    fn a_host_errand_is_not_sandboxed() {
        let cfg = Config::default();
        let s = SessionCfg {
            name: "a".into(),
            project: "p".into(),
            command: "shell".into(),
            sandbox: vec!["x11".into()],
            ..Default::default()
        };
        let p = ProjectCfg {
            name: "p".into(),
            dir: "/tmp".into(),
            ..Default::default()
        };
        let a = host_argv(&cfg, &s, &p);

        assert_eq!(a[0], "env");
        assert!(!a.iter().any(|x| x == "bwrap" || x == "--clearenv"));
        assert!(a.contains(&format!("TERM={PANE_TERM}")));
        assert!(a.contains(&"SLOPWORLD_PROJECT=p".to_string()));
        // Whichever shell this machine answers with, it is the tail and nothing follows it.
        let want = shell_split(&host_command(&cfg, &s, host_shell().as_deref()));
        assert_eq!(a[a.len() - want.len()..], want[..]);
    }

    /// `[defaults] shell` answers for a shell inside bwrap and `$SHELL` for one beside it -
    /// unless the errand named a shell itself, which is taken at its word either side.
    #[test]
    fn a_host_errand_opens_the_login_shell_unless_it_named_one() {
        let cfg = Config::default();
        let bare = SessionCfg {
            command: cfg.defaults.shell.clone(),
            ..Default::default()
        };
        assert_eq!(
            host_command(&cfg, &bare, Some("/usr/bin/zsh")),
            "/usr/bin/zsh"
        );
        // Nothing in the environment to read: the preset is still there to answer.
        assert_eq!(host_command(&cfg, &bare, None), cfg.command_of(&bare));

        // A preset by name, and a command line of its own: both are run as asked.
        let named = SessionCfg {
            command: "zsh".into(),
            ..Default::default()
        };
        assert_eq!(
            host_command(&cfg, &named, Some("/bin/bash")),
            cfg.command_of(&named)
        );
        let own = SessionCfg {
            command: cfg.defaults.shell.clone(),
            cmd: Some("htop".into()),
            ..Default::default()
        };
        assert_eq!(host_command(&cfg, &own, Some("/bin/bash")), "htop");
    }

    /// The name the sidebar shows, and the tmux target behind it: the project, then the
    /// shell. Not a constant either side - two projects open two differently named terminals.
    #[test]
    fn a_host_errand_is_named_for_its_project_and_its_shell() {
        assert_eq!(
            session_name_for("slopworld", Some("/usr/bin/zsh")),
            "slopworld-zsh"
        );
        assert_eq!(session_name_for("tmp", Some("/bin/bash")), "tmp-bash");
        assert_eq!(session_name_for("tmp", Some("fish")), "tmp-fish");
        // No shell to read, and no project to open on: both fall back on their own.
        assert_eq!(session_name_for("tmp", None), "tmp-shell");
        assert_eq!(session_name_for("", Some("/usr/bin/zsh")), "zsh");
    }

    /// Every bind lands after the tmpfs and devices that would otherwise be mounted
    /// over it - the bug that made the x11 preset a no-op.
    #[test]
    fn binds_come_after_the_skeleton() {
        let a = argv();
        let tmp = at(&a, "--tmpfs");
        let first_bind = a
            .iter()
            .position(|x| x == "--ro-bind" || x == "--bind" || x == "--dev-bind")
            .expect("some bind");
        assert!(
            tmp < first_bind,
            "tmpfs at {tmp} buries the bind at {first_bind}"
        );
        assert!(at(&a, "--dev") < first_bind);
        assert!(at(&a, "--proc") < first_bind);
    }

    /// The command preset's own binds, whether or not the project selected another preset:
    /// knowing a session is Claude Code is what lets the sandbox hand it ~/.claude.
    #[test]
    fn a_command_brings_its_own_presets() {
        let cfg = Config::default();
        let s = SessionCfg {
            name: "a".into(),
            project: "p".into(),
            command: "claude".into(),
            sandbox: vec!["docker".into()],
            ..Default::default()
        };
        let p = ProjectCfg {
            name: "p".into(),
            dir: "/tmp".into(),
            ..Default::default()
        };
        let t = crate::presets::table();
        let names: Vec<&str> = presets_for(&cfg, &s, &p, &t)
            .iter()
            .map(|pr| pr.name.as_str())
            .collect();
        assert!(names.contains(&"claude"), "no claude preset in {names:?}");
        assert!(names.contains(&"docker"), "no docker preset in {names:?}");
    }

    /// The guard, in both directions: a path *inside* what must stay out of reach, and a path
    /// *above* it. `~/.config` is as much a road to the token as the file itself.
    #[test]
    fn no_bind_list_reaches_the_token_the_presets_or_another_session() {
        assert!(refused("/").is_some());
        if let Some(home) = dirs::home_dir() {
            assert!(refused(&home.to_string_lossy()).is_some());
            // A directory *in* the home is ordinary; the home itself is not.
            assert!(refused(&home.join("src").to_string_lossy()).is_none());
        }

        for kept in [Config::path_in_use(), Table::dir(), state_root()] {
            assert!(
                refused(&kept.to_string_lossy()).is_some(),
                "{} is bindable",
                kept.display()
            );
            assert!(
                refused(&kept.join("within").to_string_lossy()).is_some(),
                "something under {} is bindable",
                kept.display()
            );
            let above = kept.parent().expect("a parent");
            assert!(
                refused(&above.to_string_lossy()).is_some(),
                "{} contains {} and is bindable",
                above.display(),
                kept.display()
            );
        }

        // The ordinary case, and the one every preset depends on.
        assert!(refused("/usr").is_none());
        assert!(refused("/etc").is_none());
    }

    /// The other side of the guard: nothing that ships is caught by it. A refusal is silent
    /// bar a log line, so a preset broken this way is a session that quietly cannot reach what
    /// it was ticked for - and `~/.local/share/pnpm` sits one directory from `state_root`.
    #[test]
    fn no_shipped_preset_asks_for_something_refused() {
        let t = Table::load();
        for pr in &t.sandbox {
            for path in pr
                .ro
                .iter()
                .chain(&pr.rw)
                .chain(&pr.dev)
                .chain(&pr.private)
                .chain(&pr.seed)
            {
                let full = expand(path);
                if full.is_empty() {
                    continue; // an unset $VAR, which `paths` drops anyway
                }
                assert!(
                    refused(&full).is_none(),
                    "preset {} asks for {path}, which the guard refuses: {}",
                    pr.name,
                    refused(&full).unwrap_or_default()
                );
            }
        }
    }

    /// The guard is reached from the effective preset list, not only from the validator.
    #[test]
    fn a_preset_asking_for_the_world_does_not_get_it() {
        let home = dirs::home_dir().map(|h| h.to_string_lossy().into_owned());
        let mut asked = vec!["/".to_string(), "/usr".to_string()];
        asked.extend(home.clone());
        asked.push(Config::path_in_use().to_string_lossy().into_owned());

        let preset = SandboxPreset {
            ro: asked,
            ..Default::default()
        };
        let out = paths(&[&preset], |pr| &pr.ro);
        assert_eq!(out, vec!["/usr".to_string()], "got {out:?}");
    }

    /// One copy per session, at the shape of the original, and never one directory for two
    /// paths that happen to share a basename.
    #[test]
    fn a_private_path_is_per_session_and_keeps_its_shape() {
        let a = private_path("one", "/home/u/.config/opencode");
        let b = private_path("one", "/home/u/.local/share/opencode");
        assert_ne!(a, b);

        assert_ne!(private_path("one", "/etc/x"), private_path("two", "/etc/x"));
        assert!(private_path("one", "/etc/x").starts_with(state_root().join("one")));

        // Under the home it keeps the relative path; outside it, the absolute one.
        if let Some(home) = dirs::home_dir() {
            let mine = private_path("one", &home.join(".claude").to_string_lossy());
            assert_eq!(mine, state_root().join("one/home/.claude"));
        }
        assert_eq!(
            private_path("one", "/etc/x"),
            state_root().join("one/root/etc/x")
        );
    }

    #[test]
    fn state_directory_follows_the_identity_not_the_agent_name() {
        let before = SessionCfg {
            name: "before".into(),
            state_id: "stable-agent-state".into(),
            ..Default::default()
        };
        let after = SessionCfg {
            name: "after".into(),
            ..before.clone()
        };

        assert_eq!(state_dir(&before), state_dir(&after));
        assert_eq!(state_dir(&before), state_root().join("stable-agent-state"));
    }

    #[test]
    fn stored_state_keys_cannot_escape_or_name_the_trash_root() {
        let root = Path::new("/tmp/state-root");
        assert_eq!(direct_child(root, "one").unwrap(), root.join("one"));
        for key in ["", ".", "..", "../one", "one/two", "/tmp/one", ".trash"] {
            assert!(direct_child(root, key).is_err(), "accepted {key:?}");
        }
    }

    /// The point of a private path: whatever else asked for that path, the copy is what the
    /// sandbox gets, because it is bound last and bwrap mounts in order.
    #[test]
    fn a_private_bind_is_last_and_beats_an_ordinary_preset_bind() {
        let Some(home) = dirs::home_dir() else { return };
        let claude = home.join(".claude").to_string_lossy().into_owned();
        if !std::path::Path::new(&claude).exists() {
            return; // nothing to keep private on a machine that has never run it
        }

        let cfg = Config::default();
        let s = SessionCfg {
            name: "a".into(),
            project: "p".into(),
            command: "claude".into(),
            ..Default::default()
        };
        let p = ProjectCfg {
            name: "p".into(),
            dir: "/tmp".into(),
            ..Default::default()
        };
        let a = build_argv(&cfg, &s, &p).expect("sandbox argv");

        let copy = private_path("a", &claude).to_string_lossy().into_owned();
        assert!(a.contains(&copy), "no private bind in {a:?}");
        // Every mention of the host path is before the private one that lands on top.
        let last = a.iter().rposition(|x| x == &claude).expect("the target");
        assert_eq!(a[last - 1], copy, "the copy is not what lands last");
    }

    /// A shared file is the hole in the copy, so it has to be mounted *after* the copy that
    /// would otherwise bury it. The credential is the case: bound over the private `~/.claude`
    /// rather than under it, or a refresh lands in the session directory and expires there.
    #[test]
    fn a_shared_file_lands_on_top_of_the_private_copy_it_sits_in() {
        let Some(home) = dirs::home_dir() else { return };
        let claude = home.join(".claude").to_string_lossy().into_owned();
        let creds = home.join(".claude/.credentials.json");
        if !creds.exists() {
            return; // nothing shared on a machine that has never logged in
        }
        let creds = creds.to_string_lossy().into_owned();

        let cfg = Config::default();
        let s = SessionCfg {
            name: "a".into(),
            project: "p".into(),
            command: "claude".into(),
            ..Default::default()
        };
        let p = ProjectCfg {
            name: "p".into(),
            dir: "/tmp".into(),
            ..Default::default()
        };
        let a = build_argv(&cfg, &s, &p).expect("sandbox argv");

        let copy = private_path("a", &claude).to_string_lossy().into_owned();
        let private_at = at(&a, &copy);
        let shared_at = a.iter().rposition(|x| x == &creds).expect("no shared bind");
        assert!(
            shared_at > private_at,
            "the copy is mounted over the credential it was supposed to expose"
        );
    }

    /// Files only, and the guard every other bind list passes through. A shared *directory* is
    /// a sandbox that can write `settings.json`, and hooks are command lines the host runs -
    /// the narrowness is the whole of what makes this safe, so it is enforced and not asked for.
    #[test]
    fn only_a_file_is_ever_shared() {
        let root = std::env::temp_dir().join(format!("slopd-shared-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&root);
        std::fs::create_dir_all(root.join("dir")).unwrap();
        std::fs::write(root.join("creds.json"), "token").unwrap();

        let file = root.join("creds.json").to_string_lossy().into_owned();
        let t = Table {
            sandbox: vec![SandboxPreset {
                name: "t".into(),
                private: vec![root.to_string_lossy().into_owned()],
                shared: vec![file.clone()],
                ..Default::default()
            }],
            commands: Vec::new(),
        };

        let p = ProjectCfg {
            name: "p".into(),
            dir: "/tmp".into(),
            sandbox: vec!["t".into()],
            ..Default::default()
        };
        let s = SessionCfg {
            name: "a".into(),
            project: "p".into(),
            ..Default::default()
        };
        assert_eq!(
            shared_binds(&Config::default(), &s, &p, &t),
            vec![file],
            "the valid shared file did not get through"
        );

        let invalid = SandboxPreset {
            name: "invalid".into(),
            private: vec![root.to_string_lossy().into_owned()],
            shared: vec![root.join("dir").to_string_lossy().into_owned()],
            ..Default::default()
        };
        let error = validate_preset(&invalid, &Table::default())
            .unwrap_err()
            .to_string();
        assert!(error.contains("not a regular file"), "{error}");

        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn preset_validation_covers_private_seed_skip_and_shared_paths() {
        let root = std::env::temp_dir().join(format!("slopd-validation-{}", std::process::id()));
        let private = root.join(".tool");
        let mut p = SandboxPreset {
            name: "tool".into(),
            private: vec![private.to_string_lossy().into_owned()],
            shared: vec![root
                .join(".toolbox/credentials")
                .to_string_lossy()
                .into_owned()],
            ..Default::default()
        };
        let table = Table {
            sandbox: vec![p.clone()],
            commands: Vec::new(),
        };

        let error = validate_preset(&p, &table).unwrap_err().to_string();
        assert!(error.contains("must be inside a private path"), "{error}");

        p.shared.clear();
        p.seed = vec![root.join("outside").to_string_lossy().into_owned()];
        let error = validate_preset(&p, &table).unwrap_err().to_string();
        assert!(error.contains("seed path"), "{error}");

        p.seed.clear();
        p.skip = vec![root.join("outside").to_string_lossy().into_owned()];
        let error = validate_preset(&p, &table).unwrap_err().to_string();
        assert!(error.contains("skip path"), "{error}");

        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn preset_validation_rejects_protected_private_paths_and_dependency_cycles() {
        let protected = Config::path_in_use().to_string_lossy().into_owned();
        let p = SandboxPreset {
            name: "tool".into(),
            private: vec![protected],
            ..Default::default()
        };
        let table = Table {
            sandbox: vec![p.clone()],
            commands: Vec::new(),
        };
        let error = validate_preset(&p, &table).unwrap_err().to_string();
        assert!(error.contains("reaches"), "{error}");

        let table = Table {
            sandbox: vec![
                SandboxPreset {
                    name: "a".into(),
                    requires: vec!["b".into()],
                    ..Default::default()
                },
                SandboxPreset {
                    name: "b".into(),
                    requires: vec!["a".into()],
                    ..Default::default()
                },
            ],
            commands: Vec::new(),
        };
        let error = validate_preset_name("a", &table).unwrap_err().to_string();
        assert!(error.contains("dependency cycle"), "{error}");
    }

    #[test]
    fn refused_normalizes_parent_components_before_checking_protected_paths() {
        let config = Config::path_in_use();
        let parent = config.parent().expect("config parent");
        let alias = parent
            .join("..")
            .join(parent.file_name().expect("config directory"))
            .join(config.file_name().expect("config file"));
        assert!(refused(&alias.to_string_lossy()).is_some());
    }

    #[cfg(unix)]
    #[test]
    fn refused_follows_existing_symlink_parents_before_checking_protected_paths() {
        use std::os::unix::fs::symlink;

        let root = std::env::temp_dir().join(format!("slopd-symlink-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&root);
        std::fs::create_dir_all(&root).unwrap();
        let home = dirs::home_dir().expect("home directory");
        let link = root.join("home");
        symlink(home, &link).unwrap();
        let alias = link;
        assert!(refused(&alias.to_string_lossy()).is_some());
        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn shipped_presets_pass_the_central_validator() {
        let table = Table::builtins();
        for preset in &table.sandbox {
            validate_preset_name(&preset.name, &table)
                .unwrap_or_else(|e| panic!("{} is invalid: {e:#}", preset.name));
        }
    }

    /// Seeding, which is what decides whether an agent can log in at all: the files at the
    /// top come across unasked, a named subdirectory comes across whole, an unnamed one does
    /// not, and a second start does not tread on what the agent has written since.
    #[test]
    fn a_private_tree_is_seeded_once_with_the_files_on_top_and_what_the_preset_names() {
        let root = std::env::temp_dir().join(format!("slopd-seed-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&root);
        let host = root.join("host");
        std::fs::create_dir_all(host.join("agents")).unwrap();
        std::fs::create_dir_all(host.join("projects")).unwrap();
        std::fs::write(host.join("auth.json"), "secret").unwrap();
        std::fs::write(host.join("agents/one.md"), "mine").unwrap();
        std::fs::write(host.join("projects/bulk"), "not this").unwrap();

        let pr = SandboxPreset {
            name: "t".into(),
            private: vec![host.to_string_lossy().into_owned()],
            seed: vec![host.join("agents").to_string_lossy().into_owned()],
            ..Default::default()
        };
        let copy = root.join("copy");
        seed_into(&pr, &host.to_string_lossy(), &copy).unwrap();

        // Credentials on top, without this file naming them.
        assert_eq!(
            std::fs::read_to_string(copy.join("auth.json")).unwrap(),
            "secret"
        );
        // What the preset named, and only that.
        assert_eq!(
            std::fs::read_to_string(copy.join("agents/one.md")).unwrap(),
            "mine"
        );
        assert!(!copy.join("projects").exists(), "unnamed bulk came across");

        // Seeded once: what the agent wrote survives, and the host's later edit stays out.
        std::fs::write(copy.join("auth.json"), "the agent's own").unwrap();
        std::fs::write(host.join("auth.json"), "changed since").unwrap();
        seed_into(&pr, &host.to_string_lossy(), &copy).unwrap();
        assert_eq!(
            std::fs::read_to_string(copy.join("auth.json")).unwrap(),
            "the agent's own"
        );

        let _ = std::fs::remove_dir_all(&root);
    }

    /// `skip` reaches the files on top, not only the directories `seed` names. Two of them,
    /// for different reasons: the user's prompt history is theirs and no agent's, and a shared
    /// credential is bound from the host anyway - seeding it would leave a superseded token
    /// lying in the session directory for as long as the session exists.
    #[test]
    fn the_files_on_top_are_cut_back_by_skip_and_by_what_is_shared() {
        let root = std::env::temp_dir().join(format!("slopd-top-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&root);
        let host = root.join("host");
        std::fs::create_dir_all(&host).unwrap();
        std::fs::write(host.join("settings.json"), "wanted").unwrap();
        std::fs::write(host.join("history.jsonl"), "every prompt ever typed").unwrap();
        std::fs::write(host.join(".credentials.json"), "rotates").unwrap();

        let pr = SandboxPreset {
            name: "t".into(),
            private: vec![host.to_string_lossy().into_owned()],
            skip: vec![host.join("history.jsonl").to_string_lossy().into_owned()],
            shared: vec![host
                .join(".credentials.json")
                .to_string_lossy()
                .into_owned()],
            ..Default::default()
        };
        let copy = root.join("copy");
        seed_into(&pr, &host.to_string_lossy(), &copy).unwrap();

        assert!(
            copy.join("settings.json").exists(),
            "the ordinary file went"
        );
        assert!(
            !copy.join("history.jsonl").exists(),
            "the user's prompt history came across"
        );
        assert!(
            !copy.join(".credentials.json").exists(),
            "a copy of the credential was left in the session directory"
        );

        let _ = std::fs::remove_dir_all(&root);
    }

    /// `skip` is what lets a preset name a whole directory. The shape is `~/.pi/agent`: the
    /// model selection and 21MB of transcripts in one place, and a seed list that named three
    /// subdirectories pi has never made brought across neither.
    #[test]
    fn a_seeded_directory_comes_across_whole_bar_what_skip_names() {
        let root = std::env::temp_dir().join(format!("slopd-skip-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&root);
        let host = root.join("host");
        std::fs::create_dir_all(host.join("agent/sessions")).unwrap();
        std::fs::create_dir_all(host.join("agent/nested/deep")).unwrap();
        std::fs::write(host.join("agent/models-store.json"), "opus").unwrap();
        std::fs::write(host.join("agent/nested/deep/kept.json"), "kept").unwrap();
        std::fs::write(host.join("agent/sessions/big.jsonl"), "21MB of talk").unwrap();

        let pr = SandboxPreset {
            name: "t".into(),
            private: vec![host.to_string_lossy().into_owned()],
            seed: vec![host.join("agent").to_string_lossy().into_owned()],
            skip: vec![host.join("agent/sessions").to_string_lossy().into_owned()],
            ..Default::default()
        };
        let copy = root.join("copy");
        seed_into(&pr, &host.to_string_lossy(), &copy).unwrap();

        // What the agent needs to be itself, however deep it sits.
        assert_eq!(
            std::fs::read_to_string(copy.join("agent/models-store.json")).unwrap(),
            "opus"
        );
        assert!(copy.join("agent/nested/deep/kept.json").exists());
        // And not the bulk, nor the directory that held it.
        assert!(
            !copy.join("agent/sessions").exists(),
            "the transcripts came across"
        );

        let _ = std::fs::remove_dir_all(&root);
    }

    #[test]
    fn splits_quoted_args() {
        assert_eq!(shell_split("claude"), vec!["claude"]);
        assert_eq!(
            shell_split(r#"claude --model opus "two words""#),
            vec!["claude", "--model", "opus", "two words"]
        );
        assert_eq!(shell_split("a  b"), vec!["a", "b"]);
        assert_eq!(shell_split(r#"x ''"#), vec!["x", ""]);
        assert_eq!(shell_split(r#"'a'\''b'"#), vec!["a'b"]);
        assert_eq!(shell_split(r#"x\ y"#), vec!["x y"]);
        assert_eq!(
            shell_split(r#"bash -lc 'cd -- '\''a b'\'' && exec "${SHELL:-bash}"'"#),
            vec!["bash", "-lc", "cd -- 'a b' && exec \"${SHELL:-bash}\""]
        );
    }
}
