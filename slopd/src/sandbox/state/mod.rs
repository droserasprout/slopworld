//! Persistent per-session state and the operator-facing state inventory.

use std::path::{Component, Path, PathBuf};

use anyhow::{Context, Result};

use crate::config::SessionCfg;

#[cfg(test)]
mod tests;

/// Return the root directory for persistent session state.
/// The default location is under the user data directory, outside `TEMP_ROOT`, so state remains after a restart.
/// `SLOPD_STATE` overrides the location for tests.
pub(crate) fn state_root() -> PathBuf {
    crate::paths::override_path("SLOPD_STATE", crate::paths::data_root().join("sessions"))
}

/// Return the path for this session's private copy of `host`.
/// Preserve the source directory structure to keep paths with the same basename separate.
/// For example, `~/.config/opencode` and `~/.local/share/opencode` use different destinations.
pub(crate) fn private_path(state_id: &str, host: &str) -> Result<PathBuf> {
    let host = Path::new(host);
    let root = state_root().join(crate::config::state_id_component(state_id)?);
    if let Some(home) = dirs::home_dir()
        && let Ok(rel) = host.strip_prefix(&home)
    {
        return Ok(root.join("home").join(rel));
    }
    Ok(root
        .join("root")
        .join(host.strip_prefix("/").unwrap_or(host)))
}

/// Return the persistent `/tmp` path for an agent that enables this option.
/// This directory and private preset copies share one state identity during rename, reset, trash, and restore operations.
pub(crate) fn persistent_tmp_path(s: &SessionCfg) -> Result<PathBuf> {
    Ok(state_dir(s)?.join("tmp"))
}

/// Return the persistent private directory for a configured agent.
/// The manager assigns `state_id`. Clients do not supply it.
/// An agent that reuses a deleted agent's name cannot inherit its transcripts or tool configuration.
pub(crate) fn state_dir(s: &SessionCfg) -> Result<PathBuf> {
    Ok(state_root().join(crate::config::state_id_component(&s.state_id)?))
}

fn trash_root() -> PathBuf {
    state_root().join(".trash")
}

const TRASH_SESSION: &str = ".slopworld-session.toml";

mod inventory;
pub use inventory::StoredState;
#[cfg(test)]
use inventory::tree_size;
pub(crate) use inventory::{stored_entry, stored_states};

/// The trash root is daemon-owned storage, never a link to external data.
/// Entries may be symlinks: inventory reports the link itself and deletion unlinks it.
fn trash_entries() -> Result<Option<std::fs::ReadDir>> {
    let root = trash_root();
    match std::fs::symlink_metadata(&root) {
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => return Ok(None),
        Err(error) => return Err(error).with_context(|| format!("reading {}", root.display())),
        Ok(meta) => anyhow::ensure!(
            meta.is_dir(),
            "trash root must be a directory without symlinks"
        ),
    }
    Ok(Some(
        std::fs::read_dir(&root).with_context(|| format!("reading {}", root.display()))?,
    ))
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
        anyhow::bail!("This operation deletes only orphaned state or trash.");
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

/// Permanently remove every entry in the daemon's trash directory.
/// Keep the trash directory for subsequent resets.
pub(crate) fn empty_trash() -> Result<usize> {
    let root = trash_root();
    let Some(entries) = trash_entries()? else {
        return Ok(0);
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
            "Agent {:?} already has fresh state. Reset it before you restore this copy.",
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
    anyhow::ensure!(
        std::fs::symlink_metadata(path)?.is_dir(),
        "trash entry must be a directory without symlinks"
    );
    let metadata = path.join(TRASH_SESSION);
    anyhow::ensure!(
        std::fs::symlink_metadata(&metadata)?.is_file(),
        "trash metadata must be a regular file without symlinks"
    );
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
    if let Err(e) = std::fs::remove_file(&metadata)
        && e.kind() != std::io::ErrorKind::NotFound
    {
        tracing::warn!(
            "removing restored-state metadata {}: {e:#}",
            metadata.display()
        );
    }
    Ok(())
}

/// Move state to the trash directory. The caller must keep the configuration consistent.
/// The returned path lets the caller restore the state if it cannot save the configuration change.
pub(crate) fn trash_state(s: &SessionCfg, label: &str) -> Result<Option<PathBuf>> {
    let state_id = crate::config::state_id_component(&s.state_id)?;
    let source = state_dir(s)?;
    if !source.exists() {
        return Ok(None);
    }
    let text = toml::to_string(s).context("serializing private-state trash metadata")?;
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
    if let Err(e) = std::fs::write(&metadata, text) {
        if let Err(restore) = std::fs::rename(&destination, &source) {
            tracing::error!(
                "Writing trash metadata failed: {e:#}. Restoring the state also failed: {restore:#}."
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

/// Temporary errands have no persistent agent that can use restored state.
/// The daemon assigns their IDs. Use the ID to remove one state directory without a name-based fallback.
pub(crate) fn remove_ephemeral_state(s: &SessionCfg) -> Result<()> {
    let path = state_dir(s)?;
    if !path.exists() {
        return Ok(());
    }
    std::fs::remove_dir_all(&path).with_context(|| format!("removing {}", path.display()))
}

/// Automatically remove expired entries only from the daemon's trash directory.
/// Keep live and orphaned directories regardless of age. A stopped agent can need its state later.
pub(crate) fn purge_trash() -> Result<usize> {
    const RETAIN: std::time::Duration = std::time::Duration::from_secs(14 * 24 * 60 * 60);
    let root = trash_root();
    let Some(entries) = trash_entries()? else {
        return Ok(0);
    };
    let now = std::time::SystemTime::now();
    let mut purged = 0;
    for entry in entries {
        let entry = entry.with_context(|| format!("reading {}", root.display()))?;
        // Read symlink metadata without following the link, as elsewhere in this module.
        // Treating a symlink as its target directory causes `remove_dir_all` to fail and prevents deletion.
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
