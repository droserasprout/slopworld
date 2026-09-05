use anyhow::{Context, Result};
use std::path::{Path, PathBuf};
use std::time::SystemTime;

/// The application directory below an XDG config or data root.
pub fn root(base: Option<PathBuf>) -> PathBuf {
    base.unwrap_or_else(|| PathBuf::from(".")).join("slopworld")
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
    if let Some(parent) = path.parent() {
        std::fs::create_dir_all(parent)?;
    }
    let tmp = path.with_extension("toml.tmp");
    std::fs::write(&tmp, text)?;
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        std::fs::set_permissions(&tmp, std::fs::Permissions::from_mode(0o600))?;
    }
    std::fs::rename(&tmp, path).with_context(|| format!("installing {}", path.display()))
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
}
