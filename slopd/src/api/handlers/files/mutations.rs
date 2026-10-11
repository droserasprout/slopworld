//! Files UI mutations and path validation; previews and browsing have separate owners.
use super::super::{ApiResult, err};
use crate::api::protobuf::{Proto, domain};
use crate::api::types::FileReq;
use crate::shared::wire;
use anyhow::{Context, bail};
use axum::http::StatusCode;

pub(crate) fn file_path(path: &str) -> anyhow::Result<std::path::PathBuf> {
    let path = crate::config::expand(path);
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
    Ok(path)
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

pub(crate) async fn create_file(Proto(q): Proto<wire::FileReq>) -> ApiResult<wire::Ack> {
    let q: FileReq = domain(q)?;
    let parent = file_path(&q.path).map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    let name = entry_name(&q.name).map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    let meta = tokio::fs::metadata(&parent)
        .await
        .with_context(|| {
            format!(
                "The daemon could not read the parent directory {}.",
                parent.display()
            )
        })
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    if !meta.is_dir() {
        return Err(err(
            StatusCode::BAD_REQUEST,
            format!("The parent path is not a directory: {}.", parent.display()),
        ));
    }

    let target = parent.join(name);
    if tokio::fs::symlink_metadata(&target).await.is_ok() {
        return Err(err(
            StatusCode::BAD_REQUEST,
            format!("A path already exists: {}.", target.display()),
        ));
    }

    let result = if q.kind.trim() == "file" {
        tokio::fs::OpenOptions::new()
            .write(true)
            .create_new(true)
            .open(&target)
            .await
            .map(|_| ())
            .with_context(|| format!("The daemon could not create {}.", target.display()))
    } else if q.kind.trim() == "folder" {
        tokio::fs::create_dir(&target)
            .await
            .with_context(|| format!("The daemon could not create {}.", target.display()))
    } else {
        return Err(err(StatusCode::BAD_REQUEST, "Set kind to file or folder."));
    };
    result.map_err(|e| err(StatusCode::BAD_REQUEST, e))?;

    Ok(Proto(wire::Ack { ok: true }))
}

pub(crate) async fn rename_file(Proto(q): Proto<wire::FileReq>) -> ApiResult<wire::Ack> {
    let q: FileReq = domain(q)?;
    let source = file_path(&q.path).map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    let name = entry_name(&q.name).map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    let parent = source
        .parent()
        .filter(|p| !p.as_os_str().is_empty())
        .ok_or_else(|| err(StatusCode::BAD_REQUEST, "You cannot rename this path."))?;
    tokio::fs::symlink_metadata(&source)
        .await
        .with_context(|| format!("The daemon could not read {}.", source.display()))
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;

    let target = parent.join(name);
    if tokio::fs::symlink_metadata(&target).await.is_ok() {
        return Err(err(
            StatusCode::BAD_REQUEST,
            format!("A path already exists: {}.", target.display()),
        ));
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
    .with_context(|| format!("The daemon could not rename {}.", source.display()))
    .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Proto(wire::Ack { ok: true }))
}

pub(crate) async fn remove_file(Proto(q): Proto<wire::FileReq>) -> ApiResult<wire::Ack> {
    let q: FileReq = domain(q)?;
    let path = file_path(&q.path).map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    let meta = tokio::fs::symlink_metadata(&path)
        .await
        .with_context(|| format!("The daemon could not read {}.", path.display()))
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    if path.parent().is_none() {
        return Err(err(StatusCode::BAD_REQUEST, "You cannot remove this path."));
    }

    if meta.is_dir() {
        tokio::fs::remove_dir_all(&path)
            .await
            .with_context(|| format!("The daemon could not remove {}.", path.display()))
            .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    } else {
        tokio::fs::remove_file(&path)
            .await
            .with_context(|| format!("The daemon could not remove {}.", path.display()))
            .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    }
    Ok(Proto(wire::Ack { ok: true }))
}

#[cfg(test)]
#[path = "mutations_tests.rs"]
mod tests;
