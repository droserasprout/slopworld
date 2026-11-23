//! Execute explicit user mutations with literal repository-relative arguments.
use crate::shared::wire::{GitSelection, git_mutation_req::Operation};
use anyhow::{Context, Result, ensure};
use std::{path::Path, time::Duration};

async fn selected(
    root: &Path,
    selection: &GitSelection,
    rename_sources: bool,
) -> Result<Vec<String>> {
    let path = &selection.path;
    ensure!(
        !path.is_empty() && path.split('/').all(|p| !matches!(p, "" | "." | "..")),
        "invalid relative Git path"
    );
    let (rows, truncated) = super::status_rows(root).await?;
    let mut matched = false;
    let mut paths = vec![path.clone()];
    for row in rows {
        let relevant = match &selection.status {
            Some(status) => row.path == *path && row.status == *status,
            None => Path::new(&row.path).starts_with(path) && row.path != *path,
        };
        if relevant {
            matched = true;
            if rename_sources
                && row.status.starts_with('R')
                && let Some(source) = row.source
            {
                paths.push(source);
            }
        }
    }
    ensure!(matched, "Git status changed. Review the selection again.");
    ensure!(
        !(rename_sources && truncated && selection.status.is_none()),
        "Select individual files to unstage in a truncated tree."
    );
    Ok(paths)
}

pub(crate) async fn execute(dir: &Path, expected: &str, operation: Operation) -> Result<()> {
    let root = super::toplevel(dir)
        .await?
        .context("checkout is not a repository")?;
    ensure!(root == Path::new(expected), "repository root changed");
    let mut cmd = tokio::process::Command::new("git");
    cmd.arg("-C")
        .arg(&root)
        .arg("--literal-pathspecs")
        .env("GIT_TERMINAL_PROMPT", "0");
    match operation {
        Operation::StageAll(_) => {
            cmd.args(["add", "--all"]);
        }
        Operation::Stage(row) => {
            cmd.args(["add", "--"])
                .args(selected(&root, &row, false).await?);
        }
        Operation::Unstage(_) | Operation::UnstageAll(_) => {
            let paths = if let Operation::Unstage(row) = operation {
                selected(&root, &row, true).await?
            } else {
                vec![".".into()]
            };
            if super::head_exists(&root).await? {
                cmd.arg("reset");
            } else {
                cmd.args(["rm", "--cached", "-rf"]);
            }
            cmd.arg("--").args(paths);
        }
        Operation::Commit(message) => {
            ensure!(!message.trim().is_empty(), "empty commit message");
            cmd.args(["commit", "-m", &message, "--"]);
        }
        Operation::Discard(row) => {
            let status = row.status.as_deref().context("discard requires a file")?;
            ensure!(
                status == "??"
                    || matches!(
                        status.as_bytes(),
                        [
                            b' ' | b'M' | b'A' | b'R' | b'C' | b'T' | b'D',
                            b'M' | b'D' | b'T'
                        ]
                    ) && !matches!(status, "DD"),
                "cannot discard this status"
            );
            let paths = selected(&root, &row, false).await?;
            ensure!(
                !root
                    .join(&row.path)
                    .symlink_metadata()
                    .is_ok_and(|m| m.is_dir()),
                "cannot discard a directory"
            );
            if status == "??" {
                // Exclude directories at the selected depth even if a file becomes
                // a directory between inspection and Git walking the path.
                let directory_pattern = "/*".repeat(row.path.split('/').count()) + "/";
                cmd.args(["clean", "-f", "-e", &directory_pattern]);
            } else {
                cmd.args(["restore", "--worktree"]);
            }
            cmd.arg("--").args(paths);
        }
    }
    let out = crate::process::run_bounded(
        &mut cmd,
        Duration::from_secs(15),
        crate::process::CaptureLimits {
            stdout: 4096,
            stderr: 4096,
        },
    )
    .await?;
    ensure!(
        out.status.success(),
        "Git failed: {}",
        String::from_utf8_lossy(&out.stderr).trim()
    );
    Ok(())
}

#[cfg(test)]
#[path = "mutation_tests.rs"]
mod tests;
