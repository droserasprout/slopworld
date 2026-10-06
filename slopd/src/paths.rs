//! Filesystem paths, metadata and atomic publication; test_support owns test overrides.

#[cfg(test)]
use crate::test_support as env;
use anyhow::{Context, Result};
#[cfg(not(test))]
use std::env;
use std::ffi::OsString;
use std::path::{Component, Path, PathBuf};
use std::time::SystemTime;

/// Last modification time, or none when metadata or its timestamp is unavailable.
pub(crate) async fn disk_mtime(path: &Path) -> Option<SystemTime> {
    tokio::fs::metadata(path).await.ok()?.modified().ok()
}

/// Discover entries in filename order. Missing directories are empty; callers
/// decide whether other read failures abort a reload or are skipped at startup.
/// Catalog owners retain extension, parsing, and duplicate-resolution policy.
pub(crate) fn read_sorted_dir(
    dir: &Path,
    mut on_error: impl FnMut(std::io::Error) -> std::io::Result<()>,
) -> std::io::Result<Vec<PathBuf>> {
    let entries = match std::fs::read_dir(dir) {
        Ok(entries) => entries,
        Err(error) => {
            if error.kind() != std::io::ErrorKind::NotFound {
                on_error(error)?;
            }
            return Ok(Vec::new());
        }
    };
    let mut paths = Vec::new();
    for entry in entries {
        match entry {
            Ok(entry) => paths.push(entry.path()),
            Err(error) => on_error(error)?,
        }
    }
    paths.sort();
    Ok(paths)
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
    publish_atomic_async(path, text, mode, false).await
}

/// Publish a complete file only if its identity is still unoccupied. Hard-linking
/// a fully written sibling reserves atomically without exposing an empty record.
/// Selected by the new record owners at storage cutover.
#[cfg(test)]
pub(crate) async fn create_atomic_async(path: &Path, text: &str, mode: Option<u32>) -> Result<()> {
    publish_atomic_async(path, text, mode, true).await
}

async fn publish_atomic_async(
    path: &Path,
    text: &str,
    mode: Option<u32>,
    exclusive: bool,
) -> Result<()> {
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
        if exclusive {
            tokio::fs::hard_link(&tmp, path)
                .await
                .with_context(|| format!("reserving {}", path.display()))?;
            // Publication has committed. A leftover temporary sibling must not
            // turn a successful creation into a reported failure.
            drop(tokio::fs::remove_file(&tmp).await);
            Ok(())
        } else {
            tokio::fs::rename(&tmp, path)
                .await
                .with_context(|| format!("installing {}", path.display()))
        }
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
    override_path("SLOPD_CONFIG_ROOT", root(dirs::config_dir()))
}

/// Select the root settings document without relocating the other roots.
pub fn config_file() -> PathBuf {
    override_path("SLOPD_CONFIG", config_root().join("config.toml"))
}

/// Return the daemon's cache directory. `SLOPD_CACHE` replaces the default path.
pub fn cache_root() -> PathBuf {
    override_path("SLOPD_CACHE", root(dirs::cache_dir()))
}

/// Return the daemon's local data directory, independently of configuration.
pub fn data_root() -> PathBuf {
    override_path("SLOPD_DATA", root(dirs::data_local_dir()))
}

/// Resolve a per-store override before its root-derived default.
pub fn override_path(variable: &str, default: PathBuf) -> PathBuf {
    env::var_os(variable).map(PathBuf::from).unwrap_or(default)
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

/// Resolve existing path components and keep the missing suffix.
/// Safety checks then account for symlinks in existing parents.
/// Presets can still specify paths for software that this machine does not have.
pub(crate) fn normalize(path: &Path) -> Result<PathBuf> {
    let mut missing: Vec<OsString> = Vec::new();
    let mut probe = std::path::absolute(path)?;
    loop {
        match std::fs::canonicalize(&probe) {
            Ok(mut resolved) => {
                for name in missing.iter().rev() {
                    resolved.push(name);
                }
                let normalized = lexical_path(&resolved);
                // A missing `child/..` can reveal an existing alias after
                // normalization. Resolve that spelling again instead of
                // assuming the entire suffix is still missing.
                if missing.iter().any(|name| name == "..") {
                    return normalize(&normalized);
                }
                return Ok(normalized);
            }
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => {
                // An existing dangling symlink is an unresolved alias, not an
                // optional missing suffix. Permission and other lookup failures
                // must likewise never fall back to a lexical allow decision.
                match std::fs::symlink_metadata(&probe) {
                    Err(error) if error.kind() == std::io::ErrorKind::NotFound => {}
                    Ok(_) => return Err(error).context("resolving existing safety path"),
                    Err(error) => return Err(error).context("looking up safety path"),
                }
                let name = probe
                    .components()
                    .next_back()
                    .context("safety path has no existing ancestor")?;
                missing.push(name.as_os_str().to_owned());
                anyhow::ensure!(probe.pop(), "safety path has no existing ancestor");
            }
            Err(error) => {
                return Err(error).with_context(|| format!("resolving {}", probe.display()));
            }
        }
    }
}

/// Normalize `.` and `..` without following symlinks.
/// `normalize` first follows symlinks where possible.
/// Use this only for suffixes below successfully resolved existing parents.
fn lexical_path(path: &Path) -> PathBuf {
    let mut out = PathBuf::new();
    for component in path.components() {
        match component {
            Component::CurDir => {}
            Component::ParentDir => {
                out.pop();
            }
            other => out.push(other.as_os_str()),
        }
    }
    out
}
