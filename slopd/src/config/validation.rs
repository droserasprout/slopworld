//! Configuration and project invariants. Live-session constraints belong to the runtime adapter.

use std::{
    collections::HashSet,
    path::{Component, Path},
};

use anyhow::{Context, Result, bail};

use super::{Config, ProjectCfg, mount_target};

/// Limit project names to one normal path component.
/// This prevents configuration edits from creating ambiguous project identities or paths that traverse directories.
pub(crate) fn project_name_component(name: &str) -> Result<&str> {
    if name.trim().is_empty() {
        bail!("Project name cannot be empty.");
    }
    if name.bytes().any(|b| matches!(b, b'/' | b'\\' | 0)) || name.chars().any(|c| c.is_control()) {
        bail!(
            "Project names must be one path component. Do not use slashes, control characters, '.' or '..'."
        );
    }

    let mut components = Path::new(name).components();
    match (components.next(), components.next()) {
        (Some(Component::Normal(_)), None) => Ok(name),
        _ => bail!(
            "Project names must be one path component. Do not use slashes, control characters, '.' or '..'."
        ),
    }
}

pub(crate) fn validate_project_names(projects: &[ProjectCfg]) -> Result<()> {
    let mut names = HashSet::new();
    for project in projects {
        if let Err(error) = project_name_component(&project.name) {
            bail!("Project name {:?} is invalid: {error}", project.name);
        }
        if !names.insert(project.name.as_str()) {
            bail!("A project with name {:?} already exists.", project.name);
        }
    }
    Ok(())
}

/// Check whether a session identity can be a path component.
/// Stored identities must also pass the opaque-ID check below.
/// This shared check also protects session records that exist only in daemon memory.
pub(crate) fn state_id_component(state_id: &str) -> Result<&str> {
    if state_id.is_empty()
        || state_id == ".trash"
        || state_id.chars().any(char::is_control)
        || state_id.bytes().any(|b| b == b'/' || b == b'\\')
    {
        bail!("The private-state ID must be one path component.");
    }

    let mut components = Path::new(state_id).components();
    match (components.next(), components.next()) {
        (Some(Component::Normal(_)), None) => Ok(state_id),
        _ => bail!("The private-state ID must be one path component."),
    }
}

/// Stored identities use the shared opaque format.
/// This check is stricter than the path component check.
/// It prevents manually edited names from becoming persistent state namespaces.
pub(crate) fn validate_state_id(state_id: &str) -> Result<()> {
    state_id_component(state_id)?;
    if !crate::storage_id::valid(state_id) {
        bail!("Use a 16-character lowercase hexadecimal private-state ID.");
    }
    Ok(())
}

pub(crate) fn validate_loaded(cfg: &Config) -> Result<()> {
    let mut host_ids = HashSet::new();
    for host in &cfg.host_terminals {
        if host.id.is_empty() {
            continue;
        }
        anyhow::ensure!(
            crate::storage_id::valid(&host.id),
            "Host shell {:?} has invalid storage ID {:?}.",
            host.name,
            host.id
        );
        anyhow::ensure!(
            host_ids.insert(&host.id),
            "Two host shells use the same storage ID {:?}.",
            host.id
        );
    }
    validate_project_names(&cfg.projects)?;
    for project in &cfg.projects {
        validate_mount_paths(project)?;
    }

    let mut state_ids = HashSet::new();
    for session in &cfg.sessions {
        if let Err(error) = validate_state_id(&session.state_id) {
            bail!(
                "Session {:?} has invalid private-state ID {:?}: {error}",
                session.name,
                session.state_id
            );
        }
        if !state_ids.insert(&session.state_id) {
            bail!(
                "Two sessions use the same private-state ID {:?}.",
                session.state_id
            );
        }
    }
    Ok(())
}

/// Validate host sources and sandbox destinations.
/// Resolve relative destinations from the project's expanded directory.
/// Check whether paths exist at launch so configuration can include disconnected disks.
pub(crate) fn validate_mount_paths(project: &ProjectCfg) -> Result<()> {
    let mut targets = HashSet::new();
    let mut relative_cache_targets: Vec<std::path::PathBuf> = Vec::new();
    for mount in &project.mounts {
        let cache = mount.mode == super::MountMode::Cache;
        let source = if cache && mount.from.trim().is_empty() {
            crate::sandbox::cache::source(project, mount)?
                .to_string_lossy()
                .into_owned()
        } else {
            super::expand(&mount.from)
        };
        let source_path = Path::new(&source);
        if (!cache && mount.from.trim().is_empty())
            || !source_path.is_absolute()
            || source.chars().any(|c| c.is_control())
            || source_path
                .components()
                .any(|c| matches!(c, Component::ParentDir))
        {
            bail!(
                "Set the mount source for project {:?} to an absolute path without control characters or '..' components.",
                project.name
            );
        }
        if let Some(what) = crate::sandbox::refused(&source) {
            bail!(
                "Mount source {source:?} for project {:?} overlaps a protected location: {what}.",
                project.name,
            );
        }

        let raw_target = super::expand(&mount.to);
        let target_path = Path::new(&raw_target);
        if mount.to.trim().is_empty()
            || raw_target.trim().is_empty()
            || raw_target.chars().any(|c| c.is_control())
            || target_path
                .components()
                .any(|c| matches!(c, Component::ParentDir))
        {
            bail!(
                "Set the mount destination for project {:?} to a path without control characters or '..' components.",
                project.name
            );
        }
        let target_string = mount_target(project, &mount.to);
        let target = Path::new(&target_string);
        if !target.is_absolute() {
            bail!(
                "The mount destination for project {:?} does not resolve to an absolute path.",
                project.name
            );
        }
        if let Some(what) = crate::sandbox::refused(target.to_string_lossy().as_ref()) {
            bail!(
                "Mount destination {:?} for project {:?} overlaps a protected location: {what}.",
                mount.to,
                project.name,
            );
        }
        if !targets.insert(target.to_path_buf()) {
            bail!(
                "Two mounts in project {:?} use the same destination {}.",
                project.name,
                target.display()
            );
        }
        if relative_cache_targets
            .iter()
            .any(|prior| target.starts_with(prior) || prior.starts_with(target))
        {
            bail!("Relative cache destinations cannot overlap another mount destination.");
        }
        if cache && crate::sandbox::cache::relative(mount) {
            if targets.iter().any(|prior| {
                prior.as_path() != target
                    && (target.starts_with(prior) || prior.starts_with(target))
            }) {
                bail!("Relative cache destinations cannot overlap another mount destination.");
            }
            relative_cache_targets.push(target.to_path_buf());
        }
        let primary = std::path::PathBuf::from(super::expand(&project.dir));
        if cache {
            crate::sandbox::cache::validate(project, mount)?;
            if target == primary || target.components().any(|c| c.as_os_str() == ".git") {
                bail!("A cache mount cannot replace the checkout or Git metadata.");
            }
        }
        if primary.starts_with(target) && (target != primary || source_path != primary) {
            bail!(
                "Mount destination {:?} cannot cover the primary directory of project {:?}.",
                mount.to,
                project.name
            );
        }
    }
    Ok(())
}

#[cfg(test)]
#[path = "validation_mount_tests.rs"]
mod mount_tests;

pub(crate) fn validate_config(cfg: &Config) -> Result<()> {
    anyhow::ensure!(
        !cfg.daemon.token.trim().is_empty(),
        "Set a non-empty [daemon] token. Daemon authentication cannot be disabled."
    );
    crate::config::validate_project_names(&cfg.projects)?;
    cfg.daemon
        .bind
        .parse::<std::net::SocketAddr>()
        .with_context(|| format!("The daemon bind address {:?} is invalid.", cfg.daemon.bind))?;
    let table = crate::presets::table();
    for (field, name) in [
        ("agent", &cfg.defaults.agent),
        ("shell", &cfg.defaults.shell),
    ] {
        let name = name.trim();
        if name.is_empty() {
            bail!("Set the default {field} to a command preset.");
        }
        if table.command(name).is_none() {
            bail!("Command preset {name:?} does not exist.");
        }
    }
    for s in &cfg.sessions {
        s.limits.validate()?;
        s.dns.validate(&format!("agent {}", s.name))?;
    }
    for p in &cfg.projects {
        validate_mount_paths(p)?;
    }
    crate::runtime::validate_config(cfg)?;
    Ok(())
}

pub(crate) fn check_project(p: &ProjectCfg) -> Result<()> {
    crate::config::project_name_component(&p.name)?;
    if p.dir.trim().is_empty() {
        bail!("Set a directory for project {:?}.", p.name);
    }
    let dir = super::expand(&p.dir);
    if let Some(what) = crate::sandbox::refused(&dir) {
        bail!(
            "Project {:?} cannot use directory {dir}. The path overlaps a protected location: {what}.",
            p.name
        );
    }
    Ok(())
}

fn check_presets(names: &[String]) -> Result<()> {
    let t = crate::presets::table();
    for name in names {
        if t.sandbox(name).is_none() {
            bail!("Sandbox preset {name:?} does not exist.");
        }
    }
    Ok(())
}

pub(crate) fn check_belongs(cfg: &Config, s: &super::SessionCfg) -> Result<()> {
    if s.project.trim().is_empty() {
        bail!("Choose a project for session {:?}.", s.name);
    }
    let Some(project) = cfg.project(&s.project) else {
        bail!("Project {:?} does not exist.", s.project);
    };
    s.dns.validate(&format!("agent {}", s.name))?;
    let live_presets: Vec<String> = s
        .sandbox
        .iter()
        .filter(|name| {
            !s.sandbox_snapshots
                .iter()
                .any(|snapshot| snapshot.name == **name)
        })
        .cloned()
        .collect();
    check_presets(&live_presets)?;
    s.limits.validate()?;
    crate::config::validate_mount_paths(project)?;
    Ok(())
}

#[cfg(test)]
#[path = "validation_tests.rs"]
mod tests;
