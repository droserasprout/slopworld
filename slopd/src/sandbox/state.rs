//! Persistent per-session state and the operator-facing state inventory.

use std::path::{Component, Path, PathBuf};

use anyhow::{Context, Result};
use serde::Serialize;

use crate::config::SessionCfg;

#[cfg(test)]
#[path = "state_tests.rs"]
mod tests;

/// Where a session keeps what is its own. Under `~/.local/share` rather than `TEMP_ROOT`,
/// because what lives here is an agent's memory of itself and a reboot is not a reason to
/// forget it. `SLOPD_STATE` moves it, which is what the tests use.
pub(crate) fn state_root() -> PathBuf {
    crate::paths::dir("SLOPD_STATE", dirs::data_dir(), "sessions")
}

/// The copy of `host` this session gets. The original's shape is kept rather than its
/// basename, so `~/.config/opencode` and `~/.local/share/opencode` - one preset, two
/// directories, one name - never land on each other.
pub(crate) fn private_path(state_id: &str, host: &str) -> Result<PathBuf> {
    let host = Path::new(host);
    let root = state_root().join(crate::config::state_id_component(state_id)?);
    if let Some(home) = dirs::home_dir() {
        if let Ok(rel) = host.strip_prefix(&home) {
            return Ok(root.join("home").join(rel));
        }
    }
    Ok(root
        .join("root")
        .join(host.strip_prefix("/").unwrap_or(host)))
}

/// The durable `/tmp` for an opted-in agent. It lives beside private preset copies so the
/// whole tree follows the same identity through rename, reset, trash and restore.
pub(crate) fn persistent_tmp_path(s: &SessionCfg) -> Result<PathBuf> {
    Ok(state_dir(s)?.join("tmp"))
}

/// The durable, private directory for a configured agent. `state_id` is not supplied by
/// clients; the manager assigns it, so a name reused after deletion cannot inherit another
/// agent's transcripts or tool configuration.
pub(crate) fn state_dir(s: &SessionCfg) -> Result<PathBuf> {
    Ok(state_root().join(crate::config::state_id_component(&s.state_id)?))
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
    pub project: Option<String>,
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

pub(super) fn stored_entry(
    kind: &str,
    key: String,
    session: Option<String>,
    path: &Path,
) -> StoredState {
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
        project: None,
        path: path.to_string_lossy().into_owned(),
        bytes: tree_size(path),
        modified,
    }
}

/// Inventory for the settings UI. Anything not claimed by a configured state id is orphaned
/// state and requires an explicit delete.
pub(crate) fn stored_states(sessions: &[SessionCfg]) -> Vec<StoredState> {
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
                        .filter(|s| crate::config::state_id_component(&s.state_id).is_ok())
                        .find(|s| item_key.ends_with(&format!("-{}", s.state_id)))
                        .map(|s| s.name.clone())
                        .or_else(|| read_trashed_session(&item.path()).ok().map(|s| s.name));
                    out.push(stored_entry("trash", item_key, owner, &item.path()));
                }
            }
            continue;
        }
        let owner = sessions.iter().find_map(|s| {
            state_dir(s)
                .ok()
                .filter(|path| path.file_name().is_some_and(|n| n == entry.file_name()))
                .map(|_| s.name.clone())
        });
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

pub(crate) fn direct_child(root: &Path, key: &str) -> Result<PathBuf> {
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

pub(crate) fn delete_stored_state(kind: &str, key: &str, sessions: &[SessionCfg]) -> Result<()> {
    if kind != "orphan" && kind != "trash" {
        anyhow::bail!("only orphaned state or trash can be deleted here");
    }
    let root = if kind == "trash" {
        trash_root()
    } else {
        state_root()
    };
    let path = direct_child(&root, key)?;
    if kind == "orphan"
        && sessions
            .iter()
            .any(|s| state_dir(s).is_ok_and(|state| state == path))
    {
        anyhow::bail!("private state is still owned by a configured agent");
    }
    remove_stored_path(&path)
}

fn remove_stored_path(path: &Path) -> Result<()> {
    let meta =
        std::fs::symlink_metadata(path).with_context(|| format!("reading {}", path.display()))?;
    if meta.is_dir() {
        std::fs::remove_dir_all(path)
    } else {
        std::fs::remove_file(path)
    }
    .with_context(|| format!("removing {}", path.display()))
}

/// Permanently remove every entry in the daemon-owned trash. The trash root remains in place so
/// future resets can move state there without another special case.
pub(crate) fn empty_trash() -> Result<usize> {
    let root = trash_root();
    let entries = match std::fs::read_dir(&root) {
        Ok(entries) => entries,
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => return Ok(0),
        Err(e) => return Err(e).with_context(|| format!("reading {}", root.display())),
    };
    let mut removed = 0;
    for entry in entries {
        let entry = entry.with_context(|| format!("reading {}", root.display()))?;
        remove_stored_path(&entry.path())?;
        removed += 1;
    }
    Ok(removed)
}

pub(crate) fn restore_stored_state(key: &str, sessions: &[SessionCfg]) -> Result<String> {
    let source = direct_child(&trash_root(), key)?;
    if !source.exists() {
        anyhow::bail!("no such private-state trash entry");
    }
    let archived = read_trashed_session(&source)?;
    crate::config::validate_state_id(&archived.state_id)
        .context("invalid private-state identity in trash metadata")?;
    let session = sessions
        .iter()
        .filter(|s| crate::config::state_id_component(&s.state_id).is_ok())
        .find(|s| s.state_id == archived.state_id)
        .unwrap_or(&archived);
    let destination = state_dir(session)?;
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

pub(crate) fn trashed_session(key: &str) -> Result<SessionCfg> {
    let path = direct_child(&trash_root(), key)?;
    read_trashed_session(&path)
}

pub(crate) fn rollback_restored_state(key: &str, session: &SessionCfg) -> Result<()> {
    let source = state_dir(session)?;
    let destination = direct_child(&trash_root(), key)?;
    std::fs::rename(&source, &destination).with_context(|| {
        format!(
            "returning {} to {}",
            source.display(),
            destination.display()
        )
    })
}

pub(crate) fn finish_restored_state(session: &SessionCfg) -> Result<()> {
    let metadata = state_dir(session)?.join(TRASH_SESSION);
    if let Err(e) = std::fs::remove_file(&metadata) {
        if e.kind() != std::io::ErrorKind::NotFound {
            tracing::warn!(
                "removing restored-state metadata {}: {e:#}",
                metadata.display()
            );
        }
    }
    Ok(())
}

/// Move state out of the live namespace. The caller owns configuration consistency; the
/// returned path lets it restore the tree if saving the corresponding config change fails.
pub(crate) fn trash_state(s: &SessionCfg, label: &str) -> Result<Option<PathBuf>> {
    let state_id = crate::config::state_id_component(&s.state_id)?;
    let source = state_dir(s)?;
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
    let destination = root.join(format!("{stamp}-{safe}-{state_id}"));
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

pub(crate) fn restore_trashed_state(s: &SessionCfg, trash: &Path) -> Result<()> {
    let destination = state_dir(s)?;
    if !trash.exists() {
        return Ok(());
    }
    std::fs::rename(trash, &destination)
        .with_context(|| format!("restoring {} to {}", trash.display(), destination.display()))?;
    finish_restored_state(s)?;
    Ok(())
}

/// Temporary errands have no durable agent to restore this state to. Their ids are always
/// daemon-minted, so this can remove exactly one leaf without ever falling back to a name.
pub(crate) fn remove_ephemeral_state(s: &SessionCfg) -> Result<()> {
    let path = state_dir(s)?;
    if !path.exists() {
        return Ok(());
    }
    std::fs::remove_dir_all(&path).with_context(|| format!("removing {}", path.display()))
}

/// Only the daemon-owned trash is reclaimed automatically. Live and orphaned directories are
/// never age-pruned: a stopped agent can still be deliberately dormant.
pub(crate) fn purge_trash() -> Result<usize> {
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
        match remove_stored_path(&path) {
            Ok(()) => purged += 1,
            Err(e) => tracing::warn!("purging private-state trash {}: {e:#}", path.display()),
        }
    }
    Ok(purged)
}
