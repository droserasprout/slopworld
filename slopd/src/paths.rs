use anyhow::{Context, Result};
use std::path::{Path, PathBuf};
use std::time::SystemTime;

/// Last modification time, or none when metadata or its timestamp is unavailable.
pub(crate) async fn disk_mtime(path: &Path) -> Option<SystemTime> {
    tokio::fs::metadata(path).await.ok()?.modified().ok()
}

/// Store temporary work under `/tmp` so the host controls its removal.
pub const TEMP_ROOT: &str = "/tmp/slopworld";

/// Generate the temporary directory path from the project name.
pub fn temp_dir(name: &str) -> String {
    format!("{TEMP_ROOT}/{}", temp_slug(name))
}

/// Shared name normalization for temporary project previews and creation.
pub fn temp_slug(name: &str) -> String {
    let mut out = String::with_capacity(name.len());
    for ch in name.trim().chars() {
        if ch.is_whitespace() || matches!(ch, ':' | '.' | '/') {
            if !out.ends_with('-') {
                out.push('-');
            }
        } else {
            out.push(ch);
        }
    }
    let out = out.trim_matches('-');
    if out.is_empty() {
        "library".into()
    } else {
        out.to_string()
    }
}

fn temp_path(path: &Path) -> PathBuf {
    // Keep the established sidecar name. Tests use it to cause deterministic installation failures.
    // Each store already owns the lock that protects its writes.
    path.with_extension("toml.tmp")
}

fn cleanup_temp(path: &Path) {
    let _ = std::fs::remove_file(path);
}

#[cfg(unix)]
fn set_private_mode(path: &Path, mode: Option<u32>) -> Result<()> {
    if let Some(mode) = mode {
        use std::os::unix::fs::PermissionsExt;
        std::fs::set_permissions(path, std::fs::Permissions::from_mode(mode))?;
    }
    Ok(())
}

#[cfg(not(unix))]
fn set_private_mode(_path: &Path, _mode: Option<u32>) -> Result<()> {
    Ok(())
}

/// Replace a file through a temporary path in the same directory.
/// The caller chooses whether the destination has a private mode.
/// Ordinary catalogs pass `None` and retain their umask policy.
pub fn write_atomic(path: &Path, text: &str, mode: Option<u32>) -> Result<()> {
    if let Some(parent) = path.parent() {
        std::fs::create_dir_all(parent)?;
    }
    let tmp = temp_path(path);
    let result = (|| {
        std::fs::write(&tmp, text).with_context(|| format!("writing {}", tmp.display()))?;
        set_private_mode(&tmp, mode)?;
        std::fs::rename(&tmp, path).with_context(|| format!("installing {}", path.display()))
    })();
    if result.is_err() {
        cleanup_temp(&tmp);
    }
    result
}

/// Async counterpart of [`write_atomic`].
/// All filesystem operations use Tokio's fs API.
/// Callers do not need to move an async store to a blocking executor to replace a file.
pub async fn write_atomic_async(path: &Path, text: &str, mode: Option<u32>) -> Result<()> {
    if let Some(parent) = path.parent() {
        tokio::fs::create_dir_all(parent).await?;
    }
    let tmp = temp_path(path);
    let result = async {
        tokio::fs::write(&tmp, text)
            .await
            .with_context(|| format!("writing {}", tmp.display()))?;
        #[cfg(unix)]
        if let Some(mode) = mode {
            use std::os::unix::fs::PermissionsExt;
            tokio::fs::set_permissions(&tmp, std::fs::Permissions::from_mode(mode)).await?;
        }
        tokio::fs::rename(&tmp, path)
            .await
            .with_context(|| format!("installing {}", path.display()))
    }
    .await;
    if result.is_err() {
        let _ = tokio::fs::remove_file(&tmp).await;
    }
    result
}

/// Return the application directory below `base`, or `./slopworld` if `base` is missing.
pub fn root(base: Option<PathBuf>) -> PathBuf {
    base.unwrap_or_else(|| PathBuf::from(".")).join("slopworld")
}

/// Return the daemon's user configuration directory.
pub fn config_root() -> PathBuf {
    root(dirs::config_dir())
}

/// Return the daemon's cache directory. `SLOPD_CACHE` replaces the default path.
pub fn cache_root() -> PathBuf {
    if let Ok(dir) = std::env::var("SLOPD_CACHE") {
        return PathBuf::from(dir);
    }
    root(dirs::cache_dir())
}

/// Return an application subdirectory, or the path set by `variable`.
pub fn dir(variable: &str, base: Option<PathBuf>, child: &str) -> PathBuf {
    if let Ok(dir) = std::env::var(variable) {
        return PathBuf::from(dir);
    }
    root(base).join(child)
}

/// Return the latest readable modification time from the directory and its entries.
pub fn dir_stamp(dir: &Path) -> Option<SystemTime> {
    let entries = std::fs::read_dir(dir).ok()?;
    let newest = entries
        .filter_map(|e| e.ok())
        .filter_map(|e| e.metadata().ok())
        .filter_map(|m| m.modified().ok())
        .max();
    let own = std::fs::metadata(dir).ok().and_then(|m| m.modified().ok());
    match (newest, own) {
        (Some(a), Some(b)) => Some(a.max(b)),
        (a, b) => a.or(b),
    }
}

/// Write a TOML file atomically with mode `0600` on Unix.
pub fn write_private_toml(path: &Path, text: &str) -> Result<()> {
    write_atomic(path, text, Some(0o600))
}

#[cfg(test)]
#[path = "paths_tests.rs"]
mod tests;
