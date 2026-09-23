//! All checkouts share project caches. Workers and worktrees do not own these caches.
use std::path::{Path, PathBuf};

use anyhow::{bail, Context, Result};

use super::state::{stored_entry, StoredState};
use crate::config::{expand, Mount, MountMode, ProjectCfg};

pub(crate) fn root() -> PathBuf {
    crate::paths::cache_root().join("mounts")
}

/// Managed storage uses the stable project ID.
/// Relative destinations use the selected checkout as their base. Absolute destinations specify the full mount path.
pub(crate) fn source(project: &ProjectCfg, mount: &Mount) -> Result<PathBuf> {
    if !mount.from.trim().is_empty() {
        return Ok(PathBuf::from(expand(&mount.from)));
    }
    let id = uuid::Uuid::parse_str(&project.id)
        .context("save the project before using automatically managed cache mounts")?;
    let target = expand(&mount.to);
    let target = Path::new(&target);
    if mount.to.trim().is_empty()
        || target
            .components()
            .any(|c| matches!(c, std::path::Component::ParentDir))
    {
        bail!("cache destination must be a path without parent traversal");
    }
    Ok(root()
        .join(id.to_string())
        .join(if target.is_absolute() {
            "absolute"
        } else {
            "relative"
        })
        .join(target.strip_prefix("/").unwrap_or(target)))
}

pub(crate) fn validate(project: &ProjectCfg, mount: &Mount) -> Result<PathBuf> {
    let path = source(project, mount)?;
    let value = path.to_string_lossy();
    if let Some(what) = super::refused(&value) {
        bail!("cache source reaches {what}");
    }
    if super::paths::overlaps(&value, &expand(&project.dir)) {
        bail!("cache source must be outside the project checkout");
    }
    let worktrees = if project.worktree_root.is_empty() {
        crate::worktrees::default_root()?
    } else {
        PathBuf::from(expand(&project.worktree_root))
    };
    if super::paths::overlaps(&value, &worktrees.to_string_lossy()) {
        bail!("cache source must be outside managed worktree storage");
    }
    Ok(path)
}

/// Keep managed caches in the inventory after removal of their mounts or projects.
/// Include explicit host directories only while the configuration lists them.
/// Private-state controls never delete these host directories.
pub(crate) fn inventory(projects: &[ProjectCfg]) -> Vec<StoredState> {
    let mut out = Vec::new();
    let mut seen = std::collections::BTreeSet::new();
    for project in projects {
        for mount in project.mounts.iter().filter(|m| m.mode == MountMode::Cache) {
            let Ok(source) = validate(project, mount) else {
                continue;
            };
            let managed = mount.from.trim().is_empty();
            let path = if managed {
                root().join(&project.id)
            } else {
                source
            };
            // One managed entry includes every destination in the project.
            // Each external host directory has one entry, even when multiple mounts or projects share it.
            let path = path.canonicalize().unwrap_or(path);
            if !seen.insert(path.clone()) {
                continue;
            }
            let mut entry = stored_entry(
                if managed {
                    "cache-managed"
                } else {
                    "cache-external"
                },
                if managed {
                    project.id.clone()
                } else {
                    path.to_string_lossy().into_owned()
                },
                None,
                &path,
            );
            entry.project = Some(project.name.clone());
            out.push(entry);
        }
    }
    if let Ok(entries) = std::fs::read_dir(root()) {
        for entry in entries.flatten() {
            let path = entry.path();
            let key = entry.file_name().to_string_lossy().into_owned();
            if uuid::Uuid::parse_str(&key).is_err()
                || !seen.insert(path.canonicalize().unwrap_or(path.clone()))
            {
                continue;
            }
            let mut row = stored_entry("cache-managed", key.clone(), None, &path);
            row.project = projects
                .iter()
                .find(|p| p.id == key)
                .map(|p| p.name.clone());
            out.push(row);
        }
    }
    out
}

#[cfg(test)]
#[path = "cache_tests.rs"]
mod tests;
