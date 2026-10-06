//! Recoverable multi-file configuration saves. Catalog preparation belongs to catalog.rs.
//! Callers serialize writers; the private undo journal survives cancellation or interruption.
//! TODO(remove after user tests and approves workspace store migration): retain
//! legacy journal recovery until priv/notes/plan-storage-main.md permits cleanup.

use std::collections::BTreeMap;
use std::path::{Path, PathBuf};

use anyhow::{Context, Result, bail};
use serde::{Deserialize, Serialize};

use super::Config;

#[derive(Serialize, Deserialize)]
struct Undo {
    files: BTreeMap<PathBuf, Option<String>>,
}

pub(super) fn journal_path(path: &Path) -> PathBuf {
    let mut name = path.file_name().unwrap_or_default().to_os_string();
    name.push(".save-journal");
    path.with_file_name(name)
}

fn root(path: &Path) -> &Path {
    path.parent()
        .filter(|p| !p.as_os_str().is_empty())
        .unwrap_or_else(|| Path::new("."))
}

async fn read_optional(path: &Path) -> Result<Option<String>> {
    match tokio::fs::read_to_string(path).await {
        Ok(text) => Ok(Some(text)),
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(None),
        Err(error) => {
            Err(error).with_context(|| format!("reading {} before config save", path.display()))
        }
    }
}

async fn apply(path: &Path, text: Option<&str>) -> Result<()> {
    match text {
        Some(text) => crate::paths::write_atomic_async(path, text, Some(0o600)).await,
        None => match tokio::fs::remove_file(path).await {
            Ok(()) => Ok(()),
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(()),
            Err(error) => Err(error.into()),
        },
    }
}

impl Undo {
    fn validate_paths(&self, path: &Path) -> Result<()> {
        // Check every journal path before writing anything; recovery cannot escape this store.
        let dirs = Config::library_dirs_for(path);
        for relative in self.files.keys() {
            let target = root(path).join(relative);
            anyhow::ensure!(
                crate::paths::normalize(&target)? == target,
                "legacy recovery target aliases another path"
            );
            let is_main = relative.components().count() == 1
                && relative == Path::new(path.file_name().unwrap_or_default());
            let is_catalog = relative.components().count() == 2
                && dirs
                    .iter()
                    .any(|(_, dir)| target.parent() == Some(dir.as_path()))
                && target.extension().is_some_and(|ext| ext == "toml")
                && target
                    .file_stem()
                    .and_then(|s| s.to_str())
                    .is_some_and(|s| super::catalog::validate_library_name(s).is_ok());
            if !is_main && !is_catalog {
                bail!("invalid configuration recovery path {}", relative.display());
            }
        }
        Ok(())
    }

    async fn restore(&self, path: &Path) -> Result<()> {
        self.validate_paths(path)?;
        for (relative, text) in &self.files {
            let target = root(path).join(relative);
            // Unchanged entries need no write, including entries in an unavailable directory.
            if read_optional(&target).await? != *text {
                apply(&target, text.as_deref()).await?;
            }
        }
        tokio::fs::remove_file(journal_path(path)).await?;
        Ok(())
    }
}

pub(super) async fn recover(path: &Path) -> Result<()> {
    let Some(text) = read_optional(&journal_path(path)).await? else {
        return Ok(());
    };
    let undo: Undo =
        serde_json::from_str(&text).context("parsing configuration recovery journal")?;
    undo.restore(path)
        .await
        .context("restoring interrupted configuration save")
}

/// Commit only the files selected by their owners. Catalog discovery is deliberately
/// outside this boundary: a root-only change must not retire catalog siblings.
#[cfg(test)]
pub(super) async fn save(path: &Path, changes: BTreeMap<PathBuf, Option<String>>) -> Result<()> {
    save_with_hook(path, changes, |_| Ok(())).await
}

#[cfg(test)]
async fn save_with_hook(
    path: &Path,
    changes: BTreeMap<PathBuf, Option<String>>,
    before_write: impl Fn(usize) -> Result<()>,
) -> Result<()> {
    recover(path).await?;
    let mut undo = Undo {
        files: BTreeMap::new(),
    };
    for target in changes.keys() {
        let relative = if root(path) == Path::new(".") {
            target.strip_prefix(".").unwrap_or(target).to_owned()
        } else {
            target.strip_prefix(root(path))?.to_owned()
        };
        undo.files.insert(relative, read_optional(target).await?);
    }
    undo.validate_paths(path)?;
    crate::paths::write_atomic_async(
        &journal_path(path),
        &serde_json::to_string(&undo)?,
        Some(0o600),
    )
    .await?;
    // All writes and retirements belong to one recoverable revision. Removing the
    // journal commits it; cancellation before that point restores the old revision.
    let commit = async {
        for (index, (target, text)) in changes.iter().enumerate() {
            before_write(index)?;
            apply(target, text.as_deref()).await?;
        }
        tokio::fs::remove_file(journal_path(path)).await?;
        Ok::<_, anyhow::Error>(())
    }
    .await;
    if let Err(error) = commit {
        undo.restore(path).await.context(format!(
            "config save failed ({error:#}); rollback failed; recovery journal retained"
        ))?;
        return Err(error);
    }
    Ok(())
}

#[cfg(test)]
#[path = "transaction_tests.rs"]
mod tests;

pub(super) async fn recovery_settings(path: &Path) -> Result<Option<String>> {
    let Some(text) = read_optional(&journal_path(path)).await? else {
        return Ok(None);
    };
    let undo: Undo = serde_json::from_str(&text).context("parsing legacy recovery journal")?;
    undo.validate_paths(path)?;
    Ok(undo
        .files
        .get(Path::new(path.file_name().unwrap_or_default()))
        .map(|text| text.clone().unwrap_or_default()))
}
