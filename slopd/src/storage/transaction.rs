//! Explicit workspace commits and undo recovery across configuration/data filesystems.
//!
//! The manager acquires session boundary -> worktree mutation (when needed) ->
//! configuration persistence gate. It recovers before reading/preparing store state.
//! Commit takes that gate and an owned publication callback; caller cancellation
//! cannot release it between a disk commit and accepted-memory publication.
//! Additional lifecycle/checkout guards belong to the manager's owned operation;
//! this disk owner does not replace that outer cancellation boundary.
//! Tasks do not join this journal. Checkout owners commit relocation intent before
//! Git effects, then use separate finalization/rollback plans; an undo transaction
//! must never span a checkout move and restore a stale ready record.
//!
//! Durability covers process interruption, not power loss: atomic replacement and
//! flushed writes are used, without an fsync barrier. Journal removal is commit.

use std::collections::HashSet;
use std::future::Future;
use std::path::{Path, PathBuf};

use anyhow::{Context, Result, ensure};
use serde::{Deserialize, Serialize};
use tokio::sync::OwnedMutexGuard;

use super::target::{StorageBinding, Target};

pub(super) enum Mutation {
    Create(String),
    Replace(String),
    Retire,
}

pub(super) struct Change {
    pub(super) target: Target,
    pub(super) mutation: Mutation,
}

#[derive(Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
struct Undo {
    version: u32,
    binding: StorageBinding,
    files: Vec<Original>,
}

#[derive(Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
struct Original {
    target: Target,
    text: Option<String>,
}

pub(super) async fn recover(binding: &StorageBinding, _gate: &OwnedMutexGuard<()>) -> Result<()> {
    let journal = binding.journal()?;
    let Some(text) = read_optional(&journal).await? else {
        return Ok(());
    };
    let undo: Undo = serde_json::from_str(&text).context("parsing workspace recovery journal")?;
    undo.restore(binding).await
}

impl Undo {
    async fn restore(&self, current: &StorageBinding) -> Result<()> {
        ensure!(
            self.version == 1,
            "unsupported workspace journal version {}",
            self.version
        );
        ensure!(
            self.binding == *current,
            "workspace journal belongs to different storage roots or settings filename; restore the original SLOPD_CONFIG_ROOT, SLOPD_DATA, and SLOPD_CONFIG mapping before recovery"
        );
        // Validate every target before the first write. Journal metadata is never
        // passed to resolve; only the current, independently resolved mapping is.
        let paths = validate_targets(current, self.files.iter().map(|file| &file.target))?;
        for (file, path) in self.files.iter().zip(paths) {
            if read_optional(&path).await? != file.text {
                restore_file(&path, file.text.as_deref()).await?;
            }
        }
        tokio::fs::remove_file(current.journal()?)
            .await
            .context("retiring workspace journal")
    }
}

/// Transfer disk commit and publication to an owner that outlives its requester.
/// Publication must apply already prepared state without additional fallible I/O.
pub(super) async fn commit<T: Send + 'static, P: Future<Output = T> + Send + 'static>(
    binding: StorageBinding,
    changes: Vec<Change>,
    gate: OwnedMutexGuard<()>,
    publish: impl FnOnce() -> P + Send + 'static,
) -> Result<T> {
    tokio::spawn(async move {
        recover(&binding, &gate).await?;
        commit_files(&binding, changes, |_| Ok(())).await?;
        let accepted = publish().await;
        drop(gate);
        Ok(accepted)
    })
    .await
    .context("workspace commit owner failed")?
}

async fn commit_files(
    binding: &StorageBinding,
    changes: Vec<Change>,
    before_write: impl Fn(usize) -> Result<()>,
) -> Result<()> {
    let paths = validate_targets(binding, changes.iter().map(|change| &change.target))?;
    if changes.len() <= 1 {
        for (change, path) in changes.iter().zip(&paths) {
            before_write(0)?;
            apply(path, &change.mutation).await?;
        }
        return Ok(());
    }
    let mut originals = Vec::with_capacity(changes.len());
    for (change, path) in changes.iter().zip(&paths) {
        let text = read_optional(path).await?;
        ensure!(
            !matches!(change.mutation, Mutation::Create(_)) || text.is_none(),
            "storage identity already exists: {}",
            path.display()
        );
        originals.push(Original {
            target: change.target.clone(),
            text,
        });
    }
    let undo = Undo {
        version: 1,
        binding: binding.clone(),
        files: originals,
    };
    let journal = binding.journal()?;
    crate::paths::write_atomic_async(&journal, &serde_json::to_string(&undo)?, Some(0o600)).await?;
    let result = async {
        for (index, (change, path)) in changes.iter().zip(&paths).enumerate() {
            before_write(index)?;
            apply(path, &change.mutation).await?;
        }
        before_write(changes.len())?;
        tokio::fs::remove_file(&journal)
            .await
            .context("committing workspace transaction")
    }
    .await;
    if let Err(error) = result {
        undo.restore(binding).await.with_context(|| {
            format!(
                "workspace commit failed ({error:#}); rollback failed; recovery journal retained"
            )
        })?;
        return Err(error);
    }
    Ok(())
}

fn validate_targets<'a>(
    binding: &StorageBinding,
    targets: impl Iterator<Item = &'a Target>,
) -> Result<Vec<PathBuf>> {
    let journal = binding.journal()?;
    let mut seen = HashSet::new();
    targets
        .map(|target| {
            let path = target.resolve(binding)?;
            ensure!(path != journal, "transaction cannot target its own journal");
            ensure!(
                seen.insert(path.clone()),
                "duplicate transaction target {}",
                path.display()
            );
            Ok(path)
        })
        .collect()
}

async fn read_optional(path: &Path) -> Result<Option<String>> {
    match tokio::fs::read_to_string(path).await {
        Ok(text) => Ok(Some(text)),
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(None),
        Err(error) => Err(error).with_context(|| format!("reading {}", path.display())),
    }
}

async fn apply(path: &Path, mutation: &Mutation) -> Result<()> {
    match mutation {
        Mutation::Create(text) => crate::paths::create_atomic_async(path, text, Some(0o600)).await,
        Mutation::Replace(text) => crate::paths::write_atomic_async(path, text, Some(0o600)).await,
        Mutation::Retire => restore_file(path, None).await,
    }
}

async fn restore_file(path: &Path, text: Option<&str>) -> Result<()> {
    match text {
        Some(text) => crate::paths::write_atomic_async(path, text, Some(0o600)).await,
        None => match tokio::fs::remove_file(path).await {
            Ok(()) => Ok(()),
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(()),
            Err(error) => Err(error).with_context(|| format!("retiring {}", path.display())),
        },
    }
}

#[cfg(test)]
#[path = "transaction_tests.rs"]
mod tests;
