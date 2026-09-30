//! Filesystem paths, metadata and atomic publication; test_support owns test overrides.

#[cfg(test)]
use crate::test_support as env;
use anyhow::{Context, Result};
#[cfg(not(test))]
use std::env;
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
    // Exclusive creation owns this unique sibling; leftovers cannot block a retry.
    path.with_file_name(format!(".slopworld-{}.tmp", uuid::Uuid::new_v4()))
}

fn cleanup_temp(path: &Path) {
    drop(std::fs::remove_file(path));
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
    use std::io::Write;
    if let Some(parent) = path.parent() {
        std::fs::create_dir_all(parent)?;
    }
    #[cfg(test)]
    check_write_fault(path)?;
    let tmp = temp_path(path);
    let mut created = false;
    let result = (|| {
        let mut options = std::fs::OpenOptions::new();
        options.write(true).create_new(true);
        #[cfg(unix)]
        if let Some(mode) = mode {
            use std::os::unix::fs::OpenOptionsExt;
            options.mode(mode);
        }
        let mut file = options
            .open(&tmp)
            .with_context(|| format!("creating {}", tmp.display()))?;
        created = true;
        file.write_all(text.as_bytes())
            .with_context(|| format!("writing {}", tmp.display()))?;
        set_private_mode(&tmp, mode)?;
        drop(file);
        std::fs::rename(&tmp, path).with_context(|| format!("installing {}", path.display()))
    })();
    if result.is_err() && created {
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
    #[cfg(test)]
    check_write_fault(path)?;
    let tmp = temp_path(path);
    let mut created = false;
    let result = async {
        let mut options = tokio::fs::OpenOptions::new();
        options.write(true).create_new(true);
        #[cfg(unix)]
        if let Some(mode) = mode {
            options.mode(mode);
        }
        let mut file = options
            .open(&tmp)
            .await
            .with_context(|| format!("creating {}", tmp.display()))?;
        created = true;
        write_and_finish(&mut file, text.as_bytes())
            .await
            .with_context(|| format!("writing {}", tmp.display()))?;
        #[cfg(unix)]
        if let Some(mode) = mode {
            use std::os::unix::fs::PermissionsExt;
            tokio::fs::set_permissions(&tmp, std::fs::Permissions::from_mode(mode)).await?;
        }
        drop(file);
        tokio::fs::rename(&tmp, path)
            .await
            .with_context(|| format!("installing {}", path.display()))
    }
    .await;
    if result.is_err() && created {
        drop(tokio::fs::remove_file(&tmp).await);
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
    if let Ok(dir) = env::var(variable) {
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

#[cfg(test)]
static WRITE_FAULTS: std::sync::Mutex<Vec<PathBuf>> = std::sync::Mutex::new(Vec::new());

/// Scoped, path-specific failure at the persistence boundary, independent of temp naming.
#[cfg(test)]
pub(crate) struct WriteFault(PathBuf);

#[cfg(test)]
pub(crate) fn fail_writes(path: &Path) -> WriteFault {
    WRITE_FAULTS.lock().unwrap().push(path.to_owned());
    WriteFault(path.to_owned())
}

#[cfg(test)]
impl Drop for WriteFault {
    fn drop(&mut self) {
        WRITE_FAULTS.lock().unwrap().retain(|path| path != &self.0);
    }
}

#[cfg(test)]
fn check_write_fault(path: &Path) -> Result<()> {
    anyhow::ensure!(
        !WRITE_FAULTS
            .lock()
            .unwrap_or_else(|error| error.into_inner())
            .contains(&path.to_owned()),
        "injected persistence failure"
    );
    Ok(())
}

// Tokio writes can finish in the background. Observe completion and errors before
// publication; flushing here is not a power-loss durability barrier.
async fn write_and_finish(file: &mut tokio::fs::File, bytes: &[u8]) -> Result<()> {
    use tokio::io::AsyncWriteExt;
    file.write_all(bytes).await?;
    file.flush().await?;
    Ok(())
}
