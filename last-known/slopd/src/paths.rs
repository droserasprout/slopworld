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
