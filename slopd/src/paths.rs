use std::path::PathBuf;

/// The application directory below an XDG config or data root.
pub fn root(base: Option<PathBuf>) -> PathBuf {
    base.unwrap_or_else(|| PathBuf::from(".")).join("slopworld")
}
