//! Cross-entry validation for a loaded configuration.

use std::{
    collections::HashSet,
    path::{Component, Path},
};

use anyhow::{bail, Result};
use uuid::Uuid;

use super::{mount_target, Config, ProjectCfg};

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
/// Stored identities must also pass the UUID check below.
/// This shared check also protects session records that exist only in daemon memory.
pub(crate) fn state_id_component(state_id: &str) -> Result<&str> {
    if state_id.is_empty()
        || state_id == ".trash"
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

/// The daemon creates canonical UUIDs for stored identities.
/// This check is stricter than the path component check.
/// It prevents manually edited names from becoming persistent state namespaces.
pub(crate) fn validate_state_id(state_id: &str) -> Result<()> {
    state_id_component(state_id)?;
    let uuid = Uuid::parse_str(state_id)
        .map_err(|_| anyhow::anyhow!("Use a canonical UUID for the private-state ID."))?;
    if uuid.to_string() != state_id {
        bail!("Use a canonical UUID for the private-state ID.");
    }
    Ok(())
}

pub(super) fn validate_loaded(cfg: &Config) -> Result<()> {
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
