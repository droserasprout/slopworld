//! Durable checkout records. Task and session cleanup never mutate this store or its trees.
use anyhow::{Context, Result, bail};
use serde::{Deserialize, Serialize};
use std::path::{Path, PathBuf};
use std::time::Duration;
pub(crate) mod checkout;
pub(crate) mod directories;
pub(crate) mod inspection;
pub(crate) mod relocation;
pub(crate) mod resolution;
pub(crate) mod write_guard;

/// Lifecycle values retain their existing spelling in storage and client views.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub(crate) enum WorktreePhase {
    #[default]
    Allocating,
    Ready,
    Removing,
    Relocating,
    Error,
}

impl std::fmt::Display for WorktreePhase {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str(match self {
            Self::Allocating => "allocating",
            Self::Ready => "ready",
            Self::Removing => "removing",
            Self::Relocating => "relocating",
            Self::Error => "error",
        })
    }
}

/// Both paths remain inspectable even when movement or reverse repair fails.
#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
pub(crate) struct RelocationIntent {
    pub source: String,
    pub destination: String,
}

#[derive(Clone, Debug, Default, Serialize, Deserialize)]
pub(crate) struct Worktree {
    pub id: String,
    pub project_id: String,
    pub name: String,
    pub path: String,
    pub repository: String,
    pub managed: bool,
    pub initial_branch: String,
    pub base: String,
    pub phase: WorktreePhase,
    pub error: String,
    // Present exactly while a checkout relocation is pending.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub relocation: Option<RelocationIntent>,
}

#[derive(Clone, Default, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub(crate) struct Store {
    #[serde(default)]
    pub worktrees: Vec<Worktree>,
}
/// Limit host Git output and execution time. Apply the repository inspection restrictions.
pub(crate) async fn git(path: &Path, args: &[&str]) -> Result<String> {
    git_command(path, args, &[]).await
}

/// Check a new managed worktree branch before registering or creating its checkout.
/// `allocate` still uses `update-ref` with an empty old value, so a branch created
/// concurrently cannot be reused or overwritten after this preflight check.
pub(crate) async fn validate_new_branch(root: &Path, branch: &str) -> Result<()> {
    let reference = format!("refs/heads/{branch}");
    if branch.starts_with('-')
        || branch == "HEAD"
        || git(root, &["check-ref-format", &reference]).await.is_err()
    {
        bail!("invalid Git branch name {branch:?}");
    }

    let refs = git(root, &["for-each-ref", "--format=%(refname)", &reference]).await?;
    if refs.lines().any(|existing| existing == reference) {
        bail!("local branch {branch:?} already exists");
    }
    Ok(())
}

/// Both captured and streamed Git reads use the selected checkout, never ambient
/// repository/index overrides. Callers add write authority only for explicit mutations.
fn repository_command(path: &Path) -> std::process::Command {
    let mut command = crate::git::inspection_std_command(path);
    command
        .env_remove("GIT_DIR")
        .env_remove("GIT_WORK_TREE")
        .env_remove("GIT_INDEX_FILE")
        .env("GIT_OPTIONAL_LOCKS", "0");
    command
}

async fn git_command(path: &Path, args: &[&str], writable: &[&Path]) -> Result<String> {
    let mut command = repository_command(path);
    if !writable.is_empty() {
        write_guard::restrict(&mut command, writable)?;
    }
    let mut command = tokio::process::Command::from(command);
    command.args(args);
    let output = crate::process::run_bounded(
        &mut command,
        Duration::from_secs(30),
        crate::process::CaptureLimits {
            stdout: 1024 * 1024,
            stderr: 8192,
        },
    )
    .await?;
    if !output.status.success() {
        bail!(
            "Git {}: {}",
            args.first().unwrap_or(&""),
            String::from_utf8_lossy(&output.stderr).trim()
        );
    }
    if output.stdout_truncated || output.stderr_truncated {
        bail!("Git output exceeded limit");
    }
    Ok(String::from_utf8(output.stdout)?
        .trim_end_matches('\n')
        .to_string())
}

/// Git's `worktree add -b` starts helper processes. Register the worktree without a checkout.
/// Then run Git's built-in commands directly under the repository inspection restrictions.
pub(crate) async fn allocate(root: &Path, path: &Path, branch: &str, base: &str) -> Result<()> {
    let target = path.to_str().context("worktree path is not UTF-8")?;
    let repository = PathBuf::from(
        git(
            root,
            &["rev-parse", "--path-format=absolute", "--git-common-dir"],
        )
        .await?,
    )
    .canonicalize()?;
    // The empty checkout directory already exists. Grant writes to this checkout only,
    // never to its parent (which may contain sibling worktrees).
    let writable = [repository.as_path(), path];
    git_command(
        root,
        &["worktree", "add", "--detach", "--no-checkout", target, base],
        &writable,
    )
    .await?;
    let reference = format!("refs/heads/{branch}");
    git_command(root, &["update-ref", &reference, base, ""], &writable).await?;
    git_command(path, &["symbolic-ref", "HEAD", &reference], &writable).await?;
    git_command(path, &["read-tree", "--reset", "-u", "HEAD"], &writable).await?;
    Ok(())
}

/// Default checkouts belong to the project directory, independent of its display name.
/// Explicit roots retain their project-name subdivision for shared storage.
pub(crate) fn project_root(project: &crate::config::ProjectCfg) -> PathBuf {
    if project.worktree_root.is_empty() {
        PathBuf::from(crate::config::expand(&project.dir)).join(".worktrees")
    } else {
        PathBuf::from(crate::config::expand(&project.worktree_root)).join(&project.name)
    }
}

/// Rename the checkout on disk, then repair Git's linked-worktree pointers.
/// A failed repair puts the directory back at its original path.
pub(crate) async fn relocate_tree(w: &Worktree, destination: &Path) -> Result<()> {
    relocate_tree_inner(
        w,
        destination,
        #[cfg(test)]
        None,
    )
    .await
}

#[cfg(test)]
#[derive(Clone, Copy)]
enum RepairFailure {
    AfterMove,
    RestoreDirectory,
    RestoreRegistration,
}

async fn relocate_tree_inner(
    w: &Worktree,
    destination: &Path,
    #[cfg(test)] failure: Option<RepairFailure>,
) -> Result<()> {
    let source = Path::new(&w.path);
    let destination_arg = destination.to_str().context("non-UTF-8 worktree path")?;
    if !std::fs::symlink_metadata(source)?.file_type().is_dir() || source.canonicalize()? != source
    {
        bail!("worktree source path changed");
    }
    if std::fs::symlink_metadata(destination).is_ok() {
        bail!("destination {} already exists", destination.display());
    }
    let repo = Path::new(&w.repository).canonicalize()?;
    let common = git(
        source,
        &["rev-parse", "--path-format=absolute", "--git-common-dir"],
    )
    .await?;
    if Path::new(&common).canonicalize()? != repo {
        bail!("worktree repository identity changed");
    }
    std::fs::rename(source, destination)?;
    let repair = git_command(
        &repo,
        &["worktree", "repair", destination_arg],
        &[&repo, destination],
    )
    .await;
    // Inject after real repair so rollback must undo partially committed Git metadata.
    #[cfg(test)]
    let repair = if let Some(failure) = failure {
        repair?;
        if matches!(failure, RepairFailure::RestoreDirectory) {
            std::fs::create_dir(source)?;
            std::fs::write(source.join("sentinel"), "do not overwrite")?;
        }
        Err(anyhow::anyhow!("injected repair failure after move"))
    } else {
        repair
    };
    if let Err(error) = repair {
        std::fs::rename(destination, source)
            .context("restoring checkout after failed Git repair")?;
        let restore = async {
            #[cfg(test)]
            if matches!(failure, Some(RepairFailure::RestoreRegistration)) {
                bail!("injected original registration repair failure");
            }
            git_command(&repo, &["worktree", "repair", &w.path], &[&repo, source]).await
        };
        restore
            .await
            .context("repairing original Git worktree after failed move")?;
        return Err(error);
    }
    Ok(())
}

/// Authorize metadata against the registered repository immediately before mounting.
/// Primary checkouts have no external metadata authority: their `.git` must be a
/// real directory (or absent for non-Git projects). Never learn authority from a gitfile.
pub(crate) fn validated_metadata_paths(
    path: &Path,
    repository: Option<&Path>,
) -> Result<Vec<PathBuf>> {
    let Some(repository) = repository else {
        match std::fs::symlink_metadata(path.join(".git")) {
            Ok(metadata) if metadata.file_type().is_dir() => {}
            Ok(_) => bail!(
                "primary checkout .git must be a directory; register linked checkouts as worktrees"
            ),
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => {}
            Err(error) => return Err(error.into()),
        }
        return Ok(Vec::new());
    };
    // Records contain canonical paths. Do not let a replacement symlink redirect
    // the authority itself to a different repository.
    if repository.canonicalize()? != repository {
        bail!("worktree repository identity changed");
    }
    let paths = metadata_paths(path)?;
    if paths.is_empty()
        || paths
            .iter()
            .any(|metadata| !metadata.starts_with(repository))
    {
        bail!("worktree Git metadata no longer belongs to its registered repository");
    }
    // Containment alone is insufficient: commondir must name the recorded root.
    if paths.last().map(PathBuf::as_path) != Some(repository) {
        bail!("worktree repository identity changed");
    }
    Ok(paths)
}

/// Parse checkout metadata without granting authority. Only validated_metadata_paths
/// may supply sandbox mounts.
fn metadata_paths(path: &Path) -> Result<Vec<PathBuf>> {
    let dot = path.join(".git");
    let gitdir = if dot.is_dir() {
        dot.canonicalize()?
    } else {
        if !dot.is_file() {
            return Ok(Vec::new());
        }
        let text = std::fs::read_to_string(&dot)?;
        let target = text
            .trim()
            .strip_prefix("gitdir: ")
            .context("invalid .git file")?;
        path.join(target).canonicalize()?
    };
    if !gitdir.is_dir() || !gitdir.join("HEAD").is_file() {
        bail!("linked Git directory has no HEAD");
    }
    let mut paths = vec![gitdir.clone()];
    match std::fs::read_to_string(gitdir.join("commondir")) {
        Ok(common) => {
            let common = gitdir.join(common.trim()).canonicalize()?;
            if !common.join("objects").is_dir() {
                bail!("linked Git common directory has no object store");
            }
            paths.push(common);
        }
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => {}
        Err(error) => return Err(error.into()),
    }
    for p in &paths {
        if let Some(why) = crate::sandbox::refused(&p.to_string_lossy()) {
            bail!("Git metadata path {} reaches {why}", p.display());
        }
    }
    Ok(paths)
}

#[cfg(test)]
mod tests;

/// Remove only the selected checkout in the daemon, then retire its Git registration.
/// Cleanliness and retained HEAD are checked by checkout before this filesystem stage.
/// Git never receives write access to the checkout parent, which contains other sessions.
pub(crate) async fn remove_tree(w: &Worktree) -> Result<()> {
    let checkout = Path::new(&w.path);
    let parent = checkout
        .parent()
        .context("missing worktree project directory")?;
    if checkout.file_name().and_then(|s| s.to_str()) != Some(&w.name) {
        bail!("worktree path does not match its recorded name");
    }
    if parent.canonicalize()? != parent || checkout.canonicalize()? != checkout {
        bail!("worktree path changed");
    }
    let repo = Path::new(&w.repository);
    let metadata = validated_metadata_paths(checkout, Some(repo))?;
    let gitdir = metadata.first().context("missing worktree Git directory")?;
    anyhow::ensure!(gitdir != repo, "cannot remove the primary checkout");
    // Preserve Git's refusal to delete locked worktrees or submodule storage. These
    // checks happen before deleting files, rather than while retiring registration.
    anyhow::ensure!(
        !gitdir.join("locked").try_exists()?,
        "cannot remove a locked worktree"
    );
    anyhow::ensure!(
        !gitdir.join("modules").try_exists()? && !inspection::contains_submodules(checkout).await?,
        "cannot remove a worktree containing submodules"
    );
    // Recursive removal does not follow directory symlinks, including cache links.
    // No repository-selected code runs during this narrowly owned deletion.
    // The application's owned operation awaits this worker while retaining both
    // lifecycle and worktree mutation guards, even after requester cancellation.
    let checkout = checkout.to_path_buf();
    let parent = parent.to_path_buf();
    tokio::task::spawn_blocking(move || -> Result<()> {
        if parent.canonicalize()? != parent || checkout.canonicalize()? != checkout {
            bail!("worktree path changed");
        }
        std::fs::remove_dir_all(&checkout).context("removing selected worktree files")
    })
    .await
    .context("joining worktree file deletion")??;
    // A failure leaves a missing checkout with its record intact; the normal retry
    // path retires only that registration. No unrelated worktree is pruned.
    forget_missing(w).await
}

/// Retry an authorized removal after the checkout no longer exists.
/// Restrict this command's writes to Git metadata.
/// If another process recreates the checkout, Git cannot delete its files.
pub(crate) async fn forget_missing(w: &Worktree) -> Result<()> {
    let repo = Path::new(&w.repository);
    git_command(repo, &["worktree", "remove", &w.path], &[repo]).await?;
    Ok(())
}
