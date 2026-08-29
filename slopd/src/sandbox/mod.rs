mod bind;
mod paths;

use std::path::{Component, Path, PathBuf};

use anyhow::{Context, Result};
use serde::Serialize;

use crate::config::{expand, Config, DnsConfig, MountMode, NetworkMode, ProjectCfg, SessionCfg};
use crate::presets::{SandboxPreset, Table};

pub use paths::{refused, validate_preset, validate_preset_name};

const PANE_TERM: &str = "tmux-256color";
const PRIVATE_RESOLVER: &str = "192.0.2.1";

pub(crate) struct ResolvedMount {
    pub host_dir: String,
    pub guest_dir: String,
    pub mode: MountMode,
}

/// Resolve configuration and build the complete sandbox command through the bind layer.
pub fn build_argv(cfg: &Config, s: &SessionCfg, p: &ProjectCfg) -> Result<Vec<String>> {
    let network = cfg.network_of(s, p);
    let dns = cfg.dns_of(s, p);
    let agent_argv = shell_split(&cfg.command_of(s));
    let dir = expand(&p.dir);
    let table = crate::presets::table();
    let presets = presets_for(cfg, s, p, &table);
    let home = dirs::home_dir()
        .map(|path| path.to_string_lossy().into_owned())
        .unwrap_or_else(|| "/root".into());
    let manifest = if s.slopworld_md {
        let path = Path::new(&dir).join(crate::manifest::FILE_NAME);
        if !crate::manifest::is_generated(&path) {
            anyhow::bail!(
                "session {} requested {} but {} is missing or not SlopWorld-generated",
                s.name,
                crate::manifest::FILE_NAME,
                path.display()
            );
        }
        Some(path)
    } else {
        None
    };

    let mut mounts = vec![ResolvedMount {
        host_dir: dir.clone(),
        // The primary project keeps the exact configured path inside the sandbox. This is
        // important for tools whose trust/cache keys and diagnostics are path-sensitive.
        guest_dir: dir.clone(),
        mode: MountMode::Rw,
    }];
    for m in &s.mounts {
        if m.project == p.name {
            mounts[0].mode = m.mode;
            continue;
        }
        if let Some(mp) = cfg.project(&m.project) {
            let mdir = expand(&mp.dir);
            if mdir.is_empty() || !Path::new(&mdir).is_dir() {
                continue;
            }
            if refused(&mdir).is_some() {
                continue;
            }
            mounts.push(ResolvedMount {
                host_dir: mdir,
                guest_dir: format!("/mnt/{}", mp.name),
                mode: m.mode,
            });
        }
    }

    bind::assemble_argv(bind::BuildArgs {
        cfg,
        s,
        p,
        network,
        dns: &dns,
        agent_argv,
        dir: &dir,
        table: &table,
        presets: &presets,
        home: &home,
        mounts: &mounts,
        manifest: manifest.as_deref(),
        manifest_mount_path: &cfg.daemon.instructions.mount_path,
    })
}

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
    let raw = session_name_for(project, host_shell().as_deref());
    let mut out = String::with_capacity(raw.len());
    for ch in raw.chars() {
        if ch.is_whitespace() || ch == ':' || ch == '.' || ch == '/' {
            if !out.ends_with('-') {
                out.push('-');
            }
        } else {
            out.push(ch);
        }
    }
    let out = out.trim_matches('-').to_string();
    if out.is_empty() {
        "shell".into()
    } else {
        out
    }
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
    crate::paths::dir("SLOPD_STATE", dirs::data_dir(), "sessions")
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

/// The durable `/tmp` for an opted-in agent. It lives beside private preset copies so the
/// whole tree follows the same identity through rename, reset, trash and restore.
fn persistent_tmp_path(s: &SessionCfg) -> PathBuf {
    state_dir(s).join("tmp")
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

/// Creates generated resolver files and seeds each private tree before argv construction;
/// existing copies are preserved. The private resolver is always synthetic, while an explicit
/// DNS list in host mode needs a generated `/etc/resolv.conf` source too.
pub fn prepare_network(cfg: &Config, s: &SessionCfg, p: &ProjectCfg) -> Result<()> {
    if s.persistent_tmp {
        let path = persistent_tmp_path(s);
        if path.exists() && !path.is_dir() {
            anyhow::bail!("persistent /tmp path {} is not a directory", path.display());
        }
        std::fs::create_dir_all(&path)
            .with_context(|| format!("making persistent /tmp {}", path.display()))?;
    }

    let network = cfg.network_of(s, p);
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
            if host.is_empty() {
                continue;
            }
            let copy = private_path(&s.state_id, &host);
            if copy.exists() {
                continue;
            }
            let host_exists = Path::new(&host).exists();
            if host_exists {
                tracing::info!("session {:?} gets its own {host}", s.name);
                seed_into(pr, &host, &copy)?;
            } else if host.starts_with("/tmp/") {
                // /tmp is a tmpfs in the skeleton, so the host path never exists. Create an
                // empty session-state directory and let the bind land on the tmpfs mount point.
                tracing::info!("session {:?} gets a fresh {host}", s.name);
                std::fs::create_dir_all(&copy)
                    .with_context(|| format!("making {}", copy.display()))?;
            }
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

/// `shell_split` preserves argv boundaries without expanding shell syntax.
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

    /// The point of the host errand: no bwrap anywhere in it, and the shell at the end of it
    /// rather than behind a `--`.
    #[test]
    fn a_host_errand_is_not_sandboxed() {
        let cfg = Config::default();
        let s = SessionCfg {
            name: "a".into(),
            project: "p".into(),
            command: "bash".into(),
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
        assert!(!host_session_name("my.project").contains('.'));
        // Missing shell and project values use their respective fallback names.
        assert_eq!(session_name_for("tmp", None), "tmp-shell");
        assert_eq!(session_name_for("", Some("/usr/bin/zsh")), "zsh");
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
    fn persistent_tmp_is_created_under_the_agent_state() {
        let state_id = format!("persistent-tmp-{}", uuid::Uuid::new_v4());
        let s = SessionCfg {
            name: "tmp-agent".into(),
            state_id,
            persistent_tmp: true,
            ..Default::default()
        };
        let p = ProjectCfg {
            name: "p".into(),
            dir: "/tmp".into(),
            network: NetworkMode::None,
            ..Default::default()
        };

        prepare_network(&Config::default(), &s, &p).expect("persistent tmp preparation");
        assert!(persistent_tmp_path(&s).is_dir());
        std::fs::remove_dir_all(state_dir(&s)).unwrap();
    }

    #[test]
    fn stored_state_keys_cannot_escape_or_name_the_trash_root() {
        let root = Path::new("/tmp/state-root");
        assert_eq!(direct_child(root, "one").unwrap(), root.join("one"));
        for key in ["", ".", "..", "../one", "one/two", "/tmp/one", ".trash"] {
            assert!(direct_child(root, key).is_err(), "accepted {key:?}");
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
