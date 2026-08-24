use std::path::PathBuf;

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
}
