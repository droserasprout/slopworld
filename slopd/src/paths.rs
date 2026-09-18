use anyhow::{Context, Result};
use std::path::{Path, PathBuf};
use std::time::SystemTime;

fn temp_path(path: &Path) -> PathBuf {
    // Keep the established sidecar name: callers use it to make an installation failure
    // deterministic in tests, and each store already owns the lock that protects its writes.
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

/// Replace a file through a same-directory temporary path. The caller chooses whether the
/// destination has a private mode; ordinary catalogs pass `None` and retain their umask policy.
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

/// Async counterpart of [`write_atomic`]. All filesystem operations remain on Tokio's fs API;
/// callers do not need to move an async store onto a blocking executor just to replace a file.
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

/// The application directory below an XDG config or data root.
pub fn root(base: Option<PathBuf>) -> PathBuf {
    base.unwrap_or_else(|| PathBuf::from(".")).join("slopworld")
}

/// The daemon's user configuration root.
pub fn config_root() -> PathBuf {
    root(dirs::config_dir())
}

/// The daemon's persistent cache root. `SLOPD_CACHE` is an alternate-instance/test override;
/// ordinary installs follow XDG and keep disposable runtime history out of configuration.
pub fn cache_root() -> PathBuf {
    if let Ok(dir) = std::env::var("SLOPD_CACHE") {
        return PathBuf::from(dir);
    }
    root(dirs::cache_dir())
}

/// A configurable application subdirectory below an XDG config or data root.
pub fn dir(variable: &str, base: Option<PathBuf>, child: &str) -> PathBuf {
    if let Ok(dir) = std::env::var(variable) {
        return PathBuf::from(dir);
    }
    root(base).join(child)
}

/// The newest mtime in a user-owned directory, including the directory itself. A missing
/// directory remains `None`, so callers do not reload a catalog that has nothing to watch.
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

/// Atomically install a private TOML file with owner-only permissions.
pub fn write_private_toml(path: &Path, text: &str) -> Result<()> {
    write_atomic(path, text, Some(0o600))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn root_uses_the_supplied_base_or_the_current_directory() {
        assert_eq!(
            root(Some(PathBuf::from("/tmp/config"))),
            PathBuf::from("/tmp/config/slopworld")
        );
        assert_eq!(root(None), PathBuf::from("./slopworld"));
    }

    #[test]
    fn dir_uses_its_environment_override() {
        let variable = format!("SLOPD_PATH_TEST_{}", std::process::id());
        let override_path = std::env::temp_dir().join("slopd-path-override");
        std::env::set_var(&variable, &override_path);

        assert_eq!(
            dir(&variable, Some(PathBuf::from("/ignored")), "sessions"),
            override_path
        );

        std::env::remove_var(variable);
    }

    #[test]
    fn dir_appends_the_child_below_the_application_root() {
        assert_eq!(
            dir(
                "SLOPD_PATH_TEST_UNSET",
                Some(PathBuf::from("/var/lib")),
                "sessions"
            ),
            PathBuf::from("/var/lib/slopworld/sessions")
        );
    }

    #[test]
    fn dir_stamp_returns_none_for_a_missing_directory() {
        let path =
            std::env::temp_dir().join(format!("slopworld-missing-dir-{}", std::process::id()));
        assert_eq!(dir_stamp(&path), None);
    }

    #[test]
    fn private_toml_is_written_with_owner_only_permissions() {
        let root = std::env::temp_dir().join(format!(
            "slopworld-private-toml-{}-{}",
            std::process::id(),
            std::time::SystemTime::now()
                .duration_since(std::time::UNIX_EPOCH)
                .unwrap()
                .as_nanos()
        ));
        let path = root.join("nested/cache.toml");

        write_private_toml(&path, "secret = true\n").unwrap();
        assert_eq!(std::fs::read_to_string(&path).unwrap(), "secret = true\n");
        #[cfg(unix)]
        {
            use std::os::unix::fs::PermissionsExt;
            assert_eq!(
                std::fs::metadata(&path).unwrap().permissions().mode() & 0o777,
                0o600
            );
        }
        std::fs::remove_dir_all(root).unwrap();
    }

    #[test]
    fn atomic_replacement_cleans_a_temporary_file_when_install_fails() {
        let root = std::env::temp_dir().join(format!(
            "slopworld-atomic-failure-{}-{}",
            std::process::id(),
            uuid::Uuid::new_v4()
        ));
        std::fs::create_dir_all(&root).unwrap();
        let target = root.join("target.toml");
        std::fs::create_dir(&target).unwrap();

        assert!(write_atomic(&target, "new", None).is_err());
        let leftovers: Vec<_> = std::fs::read_dir(&root)
            .unwrap()
            .filter_map(|entry| entry.ok())
            .map(|entry| entry.path())
            .collect();
        assert_eq!(leftovers, vec![target]);
        std::fs::remove_dir_all(root).unwrap();
    }

    #[tokio::test]
    async fn async_atomic_replacement_keeps_private_store_permissions() {
        let root = std::env::temp_dir().join(format!(
            "slopworld-atomic-async-{}-{}",
            std::process::id(),
            uuid::Uuid::new_v4()
        ));
        let path = root.join("nested/config.toml");
        write_atomic_async(&path, "secret = true\n", Some(0o600))
            .await
            .unwrap();
        assert_eq!(std::fs::read_to_string(&path).unwrap(), "secret = true\n");
        #[cfg(unix)]
        {
            use std::os::unix::fs::PermissionsExt;
            assert_eq!(
                std::fs::metadata(&path).unwrap().permissions().mode() & 0o777,
                0o600
            );
        }
        std::fs::remove_dir_all(root).unwrap();
    }
}
