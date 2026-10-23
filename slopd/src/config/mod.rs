//! Configuration module, project/session lookups, and configured-path resolution.

mod accepted;
pub(crate) use accepted::DaemonSettings;
pub(crate) mod catalog;
mod commands;
mod daemon;
#[cfg(test)]
pub(crate) mod fixtures;
mod library;
mod model;
mod persistence;
mod resolution;
mod sandbox;
pub(crate) mod settings;
pub(crate) mod transactions;
mod validation;
pub(crate) use validation::check_belongs;

pub(crate) use commands::resolve_highlighter;
pub use daemon::*;
pub use library::*;
pub use model::*;
pub use persistence::{TOKEN_REDACTED, redact_token_text};
pub use sandbox::*;
pub use settings::Settings;
pub(crate) use validation::{
    check_project, project_name_component, state_id_component, validate_config, validate_loaded,
    validate_mount_paths, validate_project_names, validate_state_id,
};

// Keep bounded short scans allocation-free; larger snapshots amortize an index.
const MAX_LINEAR_PROJECTS: usize = 16;

/// Name resolution borrows one configuration snapshot, so its index cannot outlive the records.
pub(crate) struct ProjectLookup<'a> {
    config: &'a Config,
    index: Option<std::collections::HashMap<&'a str, &'a ProjectCfg>>,
}

impl<'a> ProjectLookup<'a> {
    pub(crate) fn get(&self, name: &str) -> Option<&'a ProjectCfg> {
        match &self.index {
            Some(index) => index.get(name).copied(),
            None => self.config.project(name),
        }
    }
}

impl Config {
    /// Build once for whole-config comparisons instead of scanning per session.
    /// Preserve `session`'s first-match behavior for duplicate names.
    pub(crate) fn session_index(&self) -> std::collections::HashMap<&str, &SessionCfg> {
        let mut index = std::collections::HashMap::with_capacity(self.sessions.len());
        for session in &self.sessions {
            index.entry(session.name.as_str()).or_insert(session);
        }
        index
    }

    pub fn session(&self, name: &str) -> Option<&SessionCfg> {
        self.sessions.iter().find(|s| s.name == name)
    }

    pub fn project(&self, name: &str) -> Option<&ProjectCfg> {
        self.projects.iter().find(|p| p.name == name)
    }

    /// Resolve many sessions against one snapshot without scanning projects per session.
    /// Preserve `project`'s first-match behavior for duplicate names.
    pub(crate) fn project_index(&self) -> std::collections::HashMap<&str, &ProjectCfg> {
        let mut index = std::collections::HashMap::with_capacity(self.projects.len());
        for project in &self.projects {
            index.entry(project.name.as_str()).or_insert(project);
        }
        index
    }

    pub(crate) fn project_lookup(&self) -> ProjectLookup<'_> {
        ProjectLookup {
            config: self,
            index: (self.projects.len() > MAX_LINEAR_PROJECTS).then(|| self.project_index()),
        }
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
    while let Some((before, after)) = rest.split_once('$') {
        out.push_str(before);
        let (name, tail) = if let Some(braced) = after.strip_prefix('{') {
            match braced.split_once('}') {
                Some((name, tail)) => (name, tail),
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
            after.split_at_checked(end).unwrap_or((after, ""))
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
    normalize_path(&path).to_string_lossy().into_owned()
}

fn normalize_path(path: &std::path::Path) -> std::path::PathBuf {
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

pub(crate) mod patch;
