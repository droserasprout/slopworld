//! Configuration module, project/session lookups, and configured-path resolution.

mod daemon;
mod library;
mod model;
mod persistence;
mod resolution;
mod sandbox;
mod validation;

pub use daemon::*;
pub use library::*;
pub use model::*;
pub use persistence::{redact_token_text, TOKEN_REDACTED};
pub use sandbox::*;
pub(crate) use validation::{
    project_name_component, state_id_component, validate_mount_paths, validate_project_names,
    validate_state_id,
};

impl Config {
    pub fn session(&self, name: &str) -> Option<&SessionCfg> {
        self.sessions.iter().find(|s| s.name == name)
    }

    pub fn project(&self, name: &str) -> Option<&ProjectCfg> {
        self.projects.iter().find(|p| p.name == name)
    }

    /// Return the session's project, if it exists. The caller handles a missing project.
    /// Session startup reports an error. Session lists show an empty directory.
    pub fn project_of(&self, s: &SessionCfg) -> Option<&ProjectCfg> {
        self.project(&s.project)
    }
}

/// Expand `~` and environment variables for bwrap. Unset variables give an empty path.
/// This prevents `$XDG_RUNTIME_DIR/$WAYLAND_DISPLAY` from becoming `/` and mounting the entire filesystem.
pub fn expand(path: &str) -> String {
    let path = if let Some(rest) = path.strip_prefix("~/") {
        match dirs::home_dir() {
            Some(home) => home.join(rest).to_string_lossy().into_owned(),
            None => path.to_string(),
        }
    } else {
        path.to_string()
    };

    if !path.contains('$') {
        return path;
    }

    let mut out = String::with_capacity(path.len());
    let mut rest = path.as_str();
    while let Some(at) = rest.find('$') {
        out.push_str(&rest[..at]);
        let after = &rest[at + 1..];
        let (name, tail) = if let Some(braced) = after.strip_prefix('{') {
            match braced.find('}') {
                Some(end) => (&braced[..end], &braced[end + 1..]),
                None => {
                    out.push('$');
                    rest = after;
                    continue;
                }
            }
        } else {
            let end = after
                .find(|c: char| !c.is_ascii_alphanumeric() && c != '_')
                .unwrap_or(after.len());
            (&after[..end], &after[end..])
        };
        if name.is_empty() {
            out.push('$');
        } else {
            match std::env::var(name) {
                Ok(v) if !v.is_empty() => out.push_str(&v),
                _ => return String::new(),
            }
        }
        rest = tail;
    }
    out.push_str(rest);
    out
}

/// Resolve a sandbox destination. Absolute destinations keep their existing meaning.
/// Resolve relative destinations from the project's expanded directory.
pub(crate) fn mount_target(project: &ProjectCfg, raw: &str) -> String {
    let expanded = expand(raw);
    let path = if std::path::Path::new(&expanded).is_absolute() {
        std::path::PathBuf::from(expanded)
    } else {
        std::path::Path::new(&expand(&project.dir)).join(expanded)
    };
    normalize_path(path).to_string_lossy().into_owned()
}

fn normalize_path(path: std::path::PathBuf) -> std::path::PathBuf {
    let mut normalized = std::path::PathBuf::new();
    for component in path.components() {
        match component {
            std::path::Component::RootDir => normalized.push(std::path::Path::new("/")),
            std::path::Component::CurDir => {}
            std::path::Component::ParentDir => {
                normalized.pop();
            }
            std::path::Component::Normal(part) => normalized.push(part),
            std::path::Component::Prefix(prefix) => normalized.push(prefix.as_os_str()),
        }
    }
    normalized
}

#[cfg(test)]
mod tests;

// Serde defaults and omission predicates shared by configuration types.
pub(super) fn yes() -> bool {
    true
}
pub(super) fn is_true(b: &bool) -> bool {
    *b
}
pub(super) fn is_false(b: &bool) -> bool {
    !*b
}
