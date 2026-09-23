mod model;
mod persistence;
mod resolution;
mod validation;

pub use model::*;
pub(crate) use validation::{
    project_name_component, state_id_component, validate_mount_paths, validate_project_names,
    validate_state_id,
};

#[cfg(test)]
use model::resolvers_from;

impl Config {
    pub fn session(&self, name: &str) -> Option<&SessionCfg> {
        self.sessions.iter().find(|s| s.name == name)
    }

    pub fn project(&self, name: &str) -> Option<&ProjectCfg> {
        self.projects.iter().find(|p| p.name == name)
    }

    /// File entries take precedence over built-in entries with the same name.
    /// User presets take precedence over supplied presets in the same way.
    pub fn library_item(&self, name: &str) -> Option<LibraryItemCfg> {
        self.library
            .iter()
            .chain(builtin_library_items())
            .find(|s| s.name == name)
            .cloned()
    }

    /// Return file entries, then built-in entries whose names do not occur in the file.
    pub fn library_items_all(&self) -> Vec<LibraryItemCfg> {
        let mut all = self.library.clone();
        all.extend(
            builtin_library_items()
                .iter()
                .filter(|b| !self.library.iter().any(|s| s.name == b.name))
                .cloned(),
        );
        all
    }

    /// Check whether this name identifies a built-in entry without a file override.
    /// Clients cannot edit or delete these entries.
    pub fn is_builtin_library_item(&self, name: &str) -> bool {
        !self.library.iter().any(|s| s.name == name)
            && builtin_library_items().iter().any(|b| b.name == name)
    }

    /// A prompt without a command uses the `[defaults] agent` preset.
    /// Explicit presets and command lines keep their own command. The project may be empty.
    pub fn session_for(&self, sc: &LibraryItemCfg, name: String, project: String) -> SessionCfg {
        let t = crate::presets::table();
        let own = sc
            .command
            .as_deref()
            .map(str::trim)
            .filter(|c| !c.is_empty());
        let known = own.filter(|c| t.command(c).is_some());

        let (command, cmd) = match (sc.kind, known, own) {
            (_, Some(preset), _) => (preset.to_string(), None),
            (LibraryItemKind::Prompt, None, Some(line)) => (String::new(), Some(line.to_string())),
            (LibraryItemKind::Prompt, None, None) => {
                (self.command_name(&SessionCfg::default()), None)
            }
            // Use the shell preset to run the errand command in a shell.
            (LibraryItemKind::Shell, None, line) => (
                self.defaults.shell.trim().to_string(),
                line.map(str::to_string),
            ),
            (LibraryItemKind::Breadcrumb | LibraryItemKind::FileAction, _, _) => {
                (String::new(), None)
            }
        };
        SessionCfg {
            name,
            project,
            command,
            cmd,
            ..Default::default()
        }
    }

    /// Return the session's project, if it exists. The caller handles a missing project.
    /// Session startup reports an error. Session lists show an empty directory.
    pub fn project_of(&self, s: &SessionCfg) -> Option<&ProjectCfg> {
        self.project(&s.project)
    }

    /// The agent owns the network launch setting.
    /// Keep the project argument so launch and preview callers use the same resolver.
    pub fn network_of(&self, s: &SessionCfg, _p: &ProjectCfg) -> NetworkMode {
        s.network
    }

    pub fn dns_of(&self, s: &SessionCfg, _p: &ProjectCfg) -> DnsConfig {
        s.dns.clone()
    }

    /// Resource limits are final agent settings. They do not provide an isolation boundary.
    pub fn limits_of(&self, s: &SessionCfg, _p: &ProjectCfg) -> Limits {
        s.limits
    }

    /// Return the session's command preset name.
    /// An explicit command line without a preset gives an empty name.
    /// This prevents unrelated sessions from receiving preset mounts such as `~/.claude`.
    pub fn command_name(&self, s: &SessionCfg) -> String {
        if let Some(snapshot) = &s.command_snapshot {
            return snapshot.name.clone();
        }
        let own = s.command.trim();
        if !own.is_empty() {
            return own.to_string();
        }
        if s.cmd
            .as_deref()
            .map(str::trim)
            .is_some_and(|c| !c.is_empty())
        {
            return String::new();
        }
        match self.defaults.agent.trim() {
            "" => default_agent(),
            a => a.to_string(),
        }
    }

    /// Return the command to execute: the explicit command line, the snapshot, or the command preset.
    /// A missing preset gives an empty command, which `start` rejects.
    pub fn command_of(&self, s: &SessionCfg) -> String {
        if let Some(c) = s.cmd.as_deref().map(str::trim).filter(|c| !c.is_empty()) {
            return c.to_string();
        }
        if let Some(snapshot) = &s.command_snapshot {
            return snapshot.cmd.clone();
        }
        crate::presets::table()
            .command(&self.command_name(s))
            .map(|c| c.cmd.clone())
            .unwrap_or_default()
    }

    /// Resolve sandbox presets in this order: global, command preset, then agent additions.
    /// Each dependency precedes the preset that requires it.
    /// Use the first occurrence of each preset, as in `paths()`.
    pub fn sandbox_of(&self, s: &SessionCfg, _p: &ProjectCfg) -> Vec<String> {
        let t = crate::presets::table();
        let command_sandbox = s
            .command_snapshot
            .as_ref()
            .map(|c| c.sandbox.clone())
            .or_else(|| t.command(&self.command_name(s)).map(|c| c.sandbox.clone()))
            .unwrap_or_default();
        let asked: Vec<String> = std::iter::once("global".to_string())
            .chain(command_sandbox)
            .chain(s.sandbox.iter().cloned())
            .collect();

        fn add(
            name: &str,
            table: &crate::presets::Table,
            snapshots: &[crate::presets::SandboxPreset],
            out: &mut Vec<String>,
            visiting: &mut Vec<String>,
        ) {
            if out.iter().any(|seen| seen == name) {
                return;
            }
            if visiting.iter().any(|seen| seen == name) {
                tracing::warn!(
                    "sandbox preset dependency cycle at {name:?}, ignoring its back-edge"
                );
                return;
            }
            visiting.push(name.to_string());
            // Resolve the graph from the same definitions used to build the launch plan.
            if let Some(preset) = snapshots
                .iter()
                .find(|preset| preset.name == name)
                .or_else(|| table.sandbox(name))
            {
                for required in &preset.requires {
                    add(required, table, snapshots, out, visiting);
                }
            }
            visiting.pop();
            if !out.iter().any(|seen| seen == name) {
                out.push(name.to_string());
            }
        }

        let mut names = Vec::new();
        for name in asked {
            add(&name, &t, &s.sandbox_snapshots, &mut names, &mut Vec::new());
        }
        names
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
#[path = "config_tests.rs"]
mod tests;
