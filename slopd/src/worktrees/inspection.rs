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

/// Stream fixed-width index modes rather than retaining paths from a potentially huge index.
/// Keep the restricted Git command, deadline, and child cleanup through pipe failures.
pub(super) async fn contains_submodules(path: &Path) -> Result<bool> {
    use anyhow::{Context, bail};
    use std::process::Stdio;
    use std::time::Duration;
    use tokio::io::{AsyncReadExt, BufReader};

    let mut command = tokio::process::Command::from(super::repository_command(path));
    let mut child = command
        .args(["ls-files", "--format=%(objectmode)"])
        .kill_on_drop(true)
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()?;
    let stdout = child.stdout.take().context("capturing Git index modes")?;
    let stderr = child.stderr.take().context("capturing Git index errors")?;
    let collected = tokio::time::timeout(Duration::from_secs(30), async {
        let scan = async {
            let mut reader = BufReader::new(stdout);
            let mut mode = [0; 7];
            let mut found = false;
            while reader.read(&mut mode[..1]).await? != 0 {
                reader.read_exact(&mut mode[1..]).await?;
                anyhow::ensure!(mode[6] == b'\n', "invalid Git index mode");
                found |= &mode == b"160000\n";
            }
            Ok::<_, anyhow::Error>(found)
        };
        let (found, (stderr, truncated), status) = tokio::try_join!(
            scan,
            async { Ok(crate::process::read_bounded(stderr, 8192).await?) },
            async { Ok(child.wait().await?) },
        )?;
        if !status.success() {
            bail!("Git ls-files: {}", String::from_utf8_lossy(&stderr).trim());
        }
        anyhow::ensure!(!truncated, "Git output exceeded limit");
        Ok::<_, anyhow::Error>(found)
    })
    .await
    .unwrap_or_else(|_| Err(crate::process::TimedOut.into()));
    if collected.is_err() {
        drop(child.kill().await);
        drop(child.wait().await);
    }
    collected
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
