//! Cross-entry validation for a loaded configuration.

use std::{
    collections::HashSet,
    path::{Component, Path},
};

use anyhow::{bail, Result};
use uuid::Uuid;

use super::{Config, ProjectCfg};

/// A project name is also the guest-side alias under `/mnt`. Keep it to one normal path
/// component so a config edit cannot change the mount target through separators or traversal.
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
