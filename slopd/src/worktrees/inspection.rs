//! Checkout observations and revision verification; application owns listing assembly.
use super::{Worktree, WorktreePhase, git};
use anyhow::Result;
use std::path::Path;

pub(crate) async fn resolve_commit(path: &Path, revision: &str) -> Result<String> {
    git(
        path,
        &[
            "rev-parse",
            "--verify",
            "--end-of-options",
            &format!(
                "{}^{{commit}}",
                if revision.is_empty() {
                    "HEAD"
                } else {
                    revision
                }
            ),
        ],
    )
    .await
}

/// Project observed failures without changing the durable record.
pub(crate) async fn inspect(w: &mut Worktree) -> (String, String) {
    let path = Path::new(&w.path);
    let head = git(path, &["rev-parse", "--verify", "HEAD"])
        .await
        .unwrap_or_default();
    let branch = git(path, &["symbolic-ref", "--quiet", "--short", "HEAD"])
        .await
        .unwrap_or_default();
    // Keep interrupted requests visible for inspection during this daemon run.
    // Startup recovery identifies completed allocations but never restarts workers.
    if w.phase == WorktreePhase::Allocating {
        w.error = "Allocation was interrupted. Inspect the worktree before you remove or reuse it."
            .into();
    }
    if w.phase == WorktreePhase::Relocating {
        // The persisted error includes both source and destination; preserve it.
    } else if !path.is_dir() {
        w.phase = WorktreePhase::Error;
        w.error =
            "The checkout directory is missing. Retry manual removal to reconcile its record."
                .into();
    } else if w.managed && path.file_name().and_then(|s| s.to_str()) != Some(&w.name) {
        w.phase = WorktreePhase::Error;
        w.error = "The managed checkout path does not match its recorded name.".into();
    }
    (branch, head)
}

#[cfg(test)]
#[path = "inspection_tests.rs"]
mod tests;
