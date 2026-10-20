//! Checkout validation and filesystem effects; session adapters own attachment guards and publication.
use super::directories::CreatedDirectories;
use super::{Worktree, git};
use crate::config::ProjectCfg;
use anyhow::{Context, Result, bail};
use std::path::{Path, PathBuf};

pub(crate) async fn remove_worktree_checkout(
    project: &ProjectCfg,
    worktree: &Worktree,
) -> Result<()> {
    // Unregister external checkouts without touching their files or Git registration.
    if !worktree.managed {
        return Ok(());
    }
    let path = Path::new(&worktree.path);
    if !path.exists() {
        return forget_missing_worktree(worktree).await;
    }
    validate_removal_repository(worktree).await?;

    // Cache contents outlive checkouts. Remove only their unchanged links before
    // cleanliness inspection, and restore those links if validation or deletion fails.
    let links = crate::sandbox::cache::remove_links(project, path)?;
    let result = remove_clean_worktree(worktree).await;
    if result.is_err() && path.is_dir() {
        crate::sandbox::cache::restore_links(&links)?;
    }
    result
}

async fn validate_removal_repository(worktree: &Worktree) -> Result<()> {
    let path = Path::new(&worktree.path);
    if path.is_symlink() {
        bail!("A symlink replaced the worktree path.");
    }
    let common = git(
        path,
        &["rev-parse", "--path-format=absolute", "--git-common-dir"],
    )
    .await?;
    if Path::new(&common).canonicalize()? != Path::new(&worktree.repository).canonicalize()? {
        bail!("worktree repository identity changed");
    }
    Ok(())
}

async fn remove_clean_worktree(worktree: &Worktree) -> Result<()> {
    let path = Path::new(&worktree.path);
    let status = git(
        path,
        &[
            "status",
            "--porcelain=v1",
            "--untracked-files=all",
            "--ignored",
            "--ignore-submodules=none",
        ],
    )
    .await?;
    anyhow::ensure!(
        status.is_empty(),
        "The worktree has tracked changes, untracked files, or ignored files. Resolve them before removal."
    );
    let head = git(path, &["rev-parse", "--verify", "HEAD"]).await?;
    let retained_branches = git(
        path,
        &[
            "for-each-ref",
            "--format=%(refname)",
            &format!("--contains={head}"),
            "refs/heads/",
        ],
    )
    .await?;
    anyhow::ensure!(
        !retained_branches.is_empty(),
        "HEAD has no retained local branch. Create one before you remove the worktree."
    );
    crate::worktrees::remove_tree(worktree).await
}

async fn forget_missing_worktree(worktree: &Worktree) -> Result<()> {
    // Git can delete the checkout before its record is saved. Retire only this
    // registration; never prune unrelated worktrees or delete a recreated checkout.
    let listing = git(
        Path::new(&worktree.repository),
        &["worktree", "list", "--porcelain", "-z"],
    )
    .await?;
    let registration = format!("worktree {}", worktree.path);
    if listing.split('\0').any(|line| line == registration) {
        crate::worktrees::forget_missing(worktree).await?;
    }
    Ok(())
}

pub(crate) async fn validate_external_worktree(
    root: &Path,
    repository: &str,
    path: &Path,
) -> Result<()> {
    let common = git(
        path,
        &["rev-parse", "--path-format=absolute", "--git-common-dir"],
    )
    .await?;
    if Path::new(&common).canonicalize()? != Path::new(repository).canonicalize()? {
        bail!("external worktree belongs to another repository");
    }
    let top = git(path, &["rev-parse", "--show-toplevel"]).await?;
    if Path::new(&top).canonicalize()? != path {
        bail!("worktree path must be the checkout root");
    }
    if path == root {
        bail!("original checkout is already registered as main");
    }
    Ok(())
}

/// Callers own directory creation and collision policy; all managed destinations share these guards.
pub(crate) async fn checked_worktree_destination(
    project_dir: &Path,
    name: &str,
) -> Result<PathBuf> {
    if tokio::fs::symlink_metadata(project_dir)
        .await?
        .file_type()
        .is_symlink()
    {
        bail!("worktree project directory cannot be a symlink");
    }
    let path = project_dir.canonicalize()?.join(name);
    if let Some(why) = crate::sandbox::refused(&path.to_string_lossy()) {
        bail!("worktree reaches {why}");
    }
    Ok(path)
}

pub(crate) async fn create_managed_worktree_path(
    requested_path: PathBuf,
    name: &str,
    directories: &mut CreatedDirectories,
) -> Result<PathBuf> {
    let project_dir = requested_path
        .parent()
        .context("worktree project directory")?;
    directories.create_all(project_dir)?;
    let path = checked_worktree_destination(project_dir, name).await?;
    match std::fs::symlink_metadata(&path) {
        Ok(_) => bail!("worktree path {} already exists", path.display()),
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => {}
        Err(error) => return Err(error.into()),
    }
    directories.create_all(&path)?;
    Ok(path)
}

#[cfg(test)]
#[path = "checkout_tests.rs"]
mod tests;
