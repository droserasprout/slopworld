//! Literal-path mutations with exclusive creation, no-replace rename, and symlink-safe removal.
use anyhow::{Context, bail};

pub(crate) fn file_path(path: &str) -> anyhow::Result<std::path::PathBuf> {
    // Browser paths identify literal entries, not configuration expressions.
    if path.trim().is_empty() {
        bail!("A path is required.");
    }

    let path = std::path::PathBuf::from(path);
    if !path.is_absolute() {
        bail!("Use an absolute path.");
    }
    use std::os::unix::ffi::OsStrExt;
    if path
        .as_os_str()
        .as_bytes()
        .split(|byte| *byte == b'/')
        .any(|part| part == b"." || part == b"..")
    {
        bail!("Use a path without . or .. components.");
    }
    // A trailing slash makes lstat follow a final directory symlink.
    Ok(path.components().collect())
}

pub(crate) fn entry_name(name: &str) -> anyhow::Result<&str> {
    let name = name.trim();
    if name.is_empty() || name == "." || name == ".." {
        bail!("Enter a name other than . or ..");
    }
    if name.contains('/') || name.contains('\\') || name.contains('\0') {
        bail!("Enter one file or folder name without path separators or null characters.");
    }
    Ok(name)
}

pub(crate) async fn create_file(path: &str, name: &str, kind: &str) -> anyhow::Result<()> {
    let parent = file_path(path)?;
    let name = entry_name(name)?;
    let meta = tokio::fs::metadata(&parent).await.with_context(|| {
        format!(
            "The daemon could not read the parent directory {}.",
            parent.display()
        )
    })?;
    if !meta.is_dir() {
        bail!("The parent path is not a directory: {}.", parent.display());
    }

    let target = parent.join(name);
    if tokio::fs::symlink_metadata(&target).await.is_ok() {
        bail!("A path already exists: {}.", target.display());
    }

    let result = if kind.trim() == "file" {
        tokio::fs::OpenOptions::new()
            .write(true)
            .create_new(true)
            .open(&target)
            .await
            .map(|_| ())
            .with_context(|| format!("The daemon could not create {}.", target.display()))
    } else if kind.trim() == "folder" {
        tokio::fs::create_dir(&target)
            .await
            .with_context(|| format!("The daemon could not create {}.", target.display()))
    } else {
        bail!("Set kind to file or folder.");
    };
    result?;

    Ok(())
}

pub(crate) async fn rename_file(path: &str, name: &str) -> anyhow::Result<()> {
    let source = file_path(path)?;
    let name = entry_name(name)?;
    let parent = source
        .parent()
        .filter(|p| !p.as_os_str().is_empty())
        .context("You cannot rename this path.")?;
    tokio::fs::symlink_metadata(&source)
        .await
        .with_context(|| format!("The daemon could not read {}.", source.display()))?;

    let target = parent.join(name);
    if tokio::fs::symlink_metadata(&target).await.is_ok() {
        bail!("A path already exists: {}.", target.display());
    }
    // The earlier existence check is only for a friendly message. The kernel
    // must enforce no replacement at commit time because another writer can race it.
    nix::fcntl::renameat2(
        None,
        &source,
        None,
        &target,
        nix::fcntl::RenameFlags::RENAME_NOREPLACE,
    )
    .with_context(|| format!("The daemon could not rename {}.", source.display()))?;
    Ok(())
}

pub(crate) async fn remove_file(path: &str) -> anyhow::Result<()> {
    let path = file_path(path)?;
    let meta = tokio::fs::symlink_metadata(&path)
        .await
        .with_context(|| format!("The daemon could not read {}.", path.display()))?;
    if path.parent().is_none() {
        bail!("You cannot remove this path.");
    }

    if meta.is_dir() {
        tokio::fs::remove_dir_all(&path)
            .await
            .with_context(|| format!("The daemon could not remove {}.", path.display()))?;
    } else {
        tokio::fs::remove_file(&path)
            .await
            .with_context(|| format!("The daemon could not remove {}.", path.display()))?;
    }
    Ok(())
}

#[cfg(test)]
#[path = "mutations_tests.rs"]
mod tests;
