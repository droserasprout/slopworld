//! Cross-entry validation for a loaded configuration.

use std::{
    collections::HashSet,
    path::{Component, Path},
};

use anyhow::{bail, Result};
use uuid::Uuid;

use super::{mount_target, Config, ProjectCfg};

/// Keep project names to one normal component so config edits cannot introduce ambiguous
/// project identities or traversal-like values.
pub(crate) fn project_name_component(name: &str) -> Result<&str> {
    if name.trim().is_empty() {
        bail!("project name must not be empty");
    }
    if name.bytes().any(|b| matches!(b, b'/' | b'\\' | 0)) || name.chars().any(|c| c.is_control()) {
        bail!("project name must be one safe path component");
    }

    let mut components = Path::new(name).components();
    match (components.next(), components.next()) {
        (Some(Component::Normal(_)), None) => Ok(name),
        _ => bail!("project name must be one safe path component"),
    }
}

pub(crate) fn validate_project_names(projects: &[ProjectCfg]) -> Result<()> {
    let mut names = HashSet::new();
    for project in projects {
        if let Err(error) = project_name_component(&project.name) {
            bail!("project {:?} has invalid name: {error}", project.name);
        }
        if !names.insert(project.name.as_str()) {
            bail!("projects contain duplicate name {:?}", project.name);
        }
    }
    Ok(())
}

/// Check the part of a session identity that is required before it can become a path
/// component. The persisted form adds the stronger UUID check below, while this shared
/// predicate also protects runtime-only session records constructed inside the daemon.
pub(crate) fn state_id_component(state_id: &str) -> Result<&str> {
    if state_id.is_empty()
        || state_id == ".trash"
        || state_id.bytes().any(|b| b == b'/' || b == b'\\')
    {
        bail!("private-state identity must be one path component");
    }

    let mut components = Path::new(state_id).components();
    match (components.next(), components.next()) {
        (Some(Component::Normal(_)), None) => Ok(state_id),
        _ => bail!("private-state identity must be one path component"),
    }
}

/// Persisted identities are daemon-minted canonical UUIDs. Keeping this stricter than the
/// path-component check prevents hand-edited names from becoming durable state namespaces.
pub(crate) fn validate_state_id(state_id: &str) -> Result<()> {
    state_id_component(state_id)?;
    let uuid = Uuid::parse_str(state_id)
        .map_err(|_| anyhow::anyhow!("private-state identity must be a canonical UUID"))?;
    if uuid.to_string() != state_id {
        bail!("private-state identity must be a canonical UUID");
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
                "session {:?} has invalid private-state identity {:?}: {error}",
                session.name,
                session.state_id
            );
        }
        if !state_ids.insert(&session.state_id) {
            bail!(
                "sessions contain duplicate private-state identity {:?}",
                session.state_id
            );
        }
    }
    Ok(())
}

/// Validate host sources and sandbox destinations. A relative destination is rooted at the
/// project's expanded directory. Existence is a launch-time check so disconnected disks can
/// remain configured.
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
                "project {} mount from must be an absolute path without parent traversal",
                project.name
            );
        }
        if let Some(what) = crate::sandbox::refused(&source) {
            bail!(
                "project {} mount from {:?} reaches {what}",
                project.name,
                mount.from
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
                "project {} mount to must be a path without parent traversal",
                project.name
            );
        }
        let target_string = mount_target(project, &mount.to);
        let target = Path::new(&target_string);
        if !target.is_absolute() {
            bail!(
                "project {} mount to must resolve to an absolute path",
                project.name
            );
        }
        if let Some(what) = crate::sandbox::refused(target.to_string_lossy().as_ref()) {
            bail!(
                "project {} mount to {:?} reaches {what}",
                project.name,
                mount.to
            );
        }
        if !targets.insert(target.to_path_buf()) {
            bail!(
                "project {} has duplicate mount destination {}",
                project.name,
                target.display()
            );
        }
        let primary = std::path::PathBuf::from(super::expand(&project.dir));
        if cache {
            crate::sandbox::cache::validate(project, mount)?;
            if target == primary || target.components().any(|c| c.as_os_str() == ".git") {
                bail!("cache cannot replace a checkout or Git metadata");
            }
        }
        if primary.starts_with(target) && (target != primary || source_path != primary) {
            bail!(
                "project {} mount cannot replace its primary directory",
                project.name
            );
        }
    }
    Ok(())
}

#[cfg(test)]
#[path = "validation_mount_tests.rs"]
mod mount_tests;
