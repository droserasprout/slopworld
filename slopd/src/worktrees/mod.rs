//! Durable checkout records. Task and session cleanup never mutate this store or its trees.
use anyhow::{Context, Result, bail};
use serde::{Deserialize, Serialize};
use std::path::{Path, PathBuf};
use std::time::Duration;
pub(crate) mod relocation;
mod write_guard;

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
    pub phase: String,
    pub error: String,
}

#[derive(Clone, Default, Serialize, Deserialize)]
#[serde(deny_unknown_fields)]
pub(crate) struct Store {
    #[serde(default)]
    pub worktrees: Vec<Worktree>,
}
impl Store {
    pub async fn load(config: &Path) -> Result<Self> {
        match tokio::fs::read_to_string(config.with_file_name("worktrees.toml")).await {
            Ok(text) => tokio::task::spawn_blocking(move || toml::from_str(&text))
                .await?
                .map_err(Into::into),
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => Ok(Self::default()),
            Err(e) => Err(e.into()),
        }
    }
    pub async fn save(&self, config: &Path) -> Result<()> {
        let store = self.clone();
        // Only CPU-heavy serialization leaves the async owner. A canceled serializer cannot
        // later overwrite a newer catalog after the owner's mutation lock has been released.
        let text = tokio::task::spawn_blocking(move || toml::to_string(&store)).await??;
        crate::paths::write_atomic_async(
            &config.with_file_name("worktrees.toml"),
            &text,
            Some(0o600),
        )
        .await
    }
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

async fn git_command(path: &Path, args: &[&str], writable: &[&Path]) -> Result<String> {
    let mut command = crate::git::inspection_std_command(path);
    if !writable.is_empty() {
        write_guard::restrict(&mut command, writable)?;
    }
    let mut command = tokio::process::Command::from(command);
    command
        .args(args)
        .env_remove("GIT_DIR")
        .env_remove("GIT_WORK_TREE")
        .env_remove("GIT_INDEX_FILE")
        .env("GIT_OPTIONAL_LOCKS", "0");
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
    if let Err(error) = repair {
        std::fs::rename(destination, source)
            .context("restoring checkout after failed Git repair")?;
        git_command(&repo, &["worktree", "repair", &w.path], &[&repo, source])
            .await
            .context("repairing original Git worktree after failed move")?;
        return Err(error);
    }
    Ok(())
}

/// Mount only Git metadata for linked worktrees. Mounting the main checkout would expose unrelated source files.
pub(crate) fn metadata_paths(path: &Path) -> Result<Vec<PathBuf>> {
    let dot = path.join(".git");
    if !dot.is_file() {
        return Ok(Vec::new());
    }
    let text = std::fs::read_to_string(&dot)?;
    let target = text
        .trim()
        .strip_prefix("gitdir: ")
        .context("invalid .git file")?;
    let gitdir = path.join(target).canonicalize()?;
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

/// Git starts a status helper when it removes a worktree. Run removal in a minimal Bubblewrap namespace.
/// Mount the named project's worktree directory and shared Git metadata.
/// Do not mount the original checkout, host home, or host configuration.
/// These restrictions prevent Git helpers from accessing host secrets.
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
    let repo = Path::new(&w.repository).canonicalize()?;
    if let Some(reason) = crate::sandbox::refused(&repo.to_string_lossy()) {
        bail!("Git metadata reaches {reason}");
    }
    let mut command = tokio::process::Command::new("bwrap");
    command.args([
        "--die-with-parent",
        "--unshare-all",
        "--new-session",
        "--clearenv",
        "--ro-bind",
        "/usr",
        "/usr",
        "--proc",
        "/proc",
        "--dev",
        "/dev",
        "--tmpfs",
        "/tmp",
    ]);
    for name in ["/lib", "/lib64", "/bin", "/sbin"] {
        if Path::new(name).exists() {
            command.args(["--ro-bind", name, name]);
        }
    }
    command
        .arg("--bind")
        .arg(&repo)
        .arg(&repo)
        .arg("--bind")
        .arg(parent)
        .arg(parent)
        .args([
            "--setenv",
            "PATH",
            "/usr/bin:/bin",
            "--setenv",
            "HOME",
            "/tmp",
            "--setenv",
            "GIT_CONFIG_NOSYSTEM",
            "1",
            "--setenv",
            "GIT_CONFIG_GLOBAL",
            "/dev/null",
            "--setenv",
            "GIT_TERMINAL_PROMPT",
            "0",
            "--setenv",
            "LC_ALL",
            "C",
            "--chdir",
        ])
        .arg(&repo)
        .args(["--", "/usr/bin/git"])
        .args(crate::git::inspection_args())
        .args(["worktree", "remove"])
        .arg(checkout);
    let output = crate::process::run_bounded(
        &mut command,
        Duration::from_secs(30),
        crate::process::CaptureLimits {
            stdout: 8192,
            stderr: 8192,
        },
    )
    .await?;
    if !output.status.success() {
        bail!(
            "Git worktree removal: {}",
            String::from_utf8_lossy(&output.stderr)
        );
    }
    if checkout.exists() {
        bail!("Git reported removal but checkout still exists");
    }
    Ok(())
}

/// Retry an authorized removal after the checkout no longer exists.
/// Restrict this command's writes to Git metadata.
/// If another process recreates the checkout, Git cannot delete its files.
pub(crate) async fn forget_missing(w: &Worktree) -> Result<()> {
    let repo = Path::new(&w.repository);
    git_command(repo, &["worktree", "remove", &w.path], &[repo]).await?;
    Ok(())
}
