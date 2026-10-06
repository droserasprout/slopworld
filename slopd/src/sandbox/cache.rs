//! All checkouts share project caches. Workers and worktrees do not own these caches.
use std::os::unix::fs::symlink;
use std::path::{Path, PathBuf};

use anyhow::{Context, Result, bail};

use super::state::{StoredState, stored_entry};
use crate::config::{Mount, MountMode, ProjectCfg, expand};

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
    if target.is_absolute() {
        return Ok(root()
            .join(id.to_string())
            .join("absolute")
            .join(target.strip_prefix("/")?));
    }
    // Escape separators as well as '%' so every relative path has a distinct, readable key.
    let key = target
        .to_string_lossy()
        .bytes()
        .map(|byte| match byte {
            b'/' | b'%' | b'\\' => format!("%{byte:02X}"),
            _ => (byte as char).to_string(),
        })
        .collect::<String>();
    Ok(root().join(id.to_string()).join(format!("link-{key}")))
}

pub(crate) fn relative(mount: &Mount) -> bool {
    !Path::new(&expand(&mount.to)).is_absolute()
}

fn check_parent(checkout: &Path, target: &Path) -> Result<()> {
    let parent = target.parent().context("cache destination parent")?;
    let ancestor = parent
        .ancestors()
        .find(|path| path.exists())
        .context("cache destination ancestor")?;
    let root = checkout.canonicalize()?;
    if !ancestor.canonicalize()?.starts_with(&root) {
        bail!(
            "cache destination {} escapes the checkout",
            target.display()
        );
    }
    Ok(())
}

pub(crate) fn is_relative_cache(mount: &Mount) -> bool {
    mount.mode == MountMode::Cache && relative(mount)
}

pub(crate) fn relative_mounts(project: &ProjectCfg) -> impl Iterator<Item = &Mount> {
    project
        .mounts
        .iter()
        .filter(|mount| is_relative_cache(mount))
}

pub(crate) fn reconcile(project: &ProjectCfg, checkout: &Path) -> Result<()> {
    for mount in relative_mounts(project) {
        reconcile_mount(project, checkout, mount)?;
    }
    Ok(())
}

/// Install one relative cache link and return it only when this call creates it.
/// Config transactions record each returned link before attempting the next mount.
pub(crate) fn reconcile_mount(
    project: &ProjectCfg,
    checkout: &Path,
    mount: &Mount,
) -> Result<Option<(PathBuf, PathBuf)>> {
    if crate::runtime::is_slopcar() {
        bail!(
            "relative cache links are unavailable in sidecar mode until host and container cache paths are mapped identically"
        );
    }
    let source = validate(project, mount)?;
    if super::paths::overlaps(&source.to_string_lossy(), &checkout.to_string_lossy()) {
        bail!("cache source must be outside the selected worktree");
    }
    let target = checkout.join(expand(&mount.to));
    check_parent(checkout, &target)?;
    if target.components().any(|c| c.as_os_str() == ".git") {
        bail!("cache link cannot replace Git metadata");
    }
    // Refuse occupied checkout targets before migration or source creation.
    // An expected existing link still needs its source prepared below.
    link_present(&target, &source)?;
    // Move the old managed layout only when the new location is still absent.
    if mount.from.trim().is_empty() {
        let old = root()
            .join(&project.id)
            .join("relative")
            .join(expand(&mount.to));
        if old.exists() {
            if old.is_symlink() || source.is_symlink() || !old.is_dir() {
                bail!(
                    "legacy cache {} must be a directory without symlinks",
                    old.display()
                );
            }
            if source.is_dir() {
                if std::fs::read_dir(&source)?.next().is_some() {
                    bail!(
                        "legacy cache {} and new cache {} both contain data; merge them manually",
                        old.display(),
                        source.display()
                    );
                }
                std::fs::remove_dir(&source)?;
            }
            std::fs::create_dir_all(source.parent().context("cache parent")?)?;
            std::fs::rename(&old, &source)
                .with_context(|| format!("migrating cache {}", old.display()))?;
        }
    }
    std::fs::create_dir_all(&source)?;
    validate(project, mount)?;
    if !source.is_dir() {
        bail!("cache source {} is not a directory", source.display());
    }
    if link_present(&target, &source)? {
        return Ok(None);
    }
    let parent = target.parent().context("cache destination parent")?;
    std::fs::create_dir_all(parent)?;
    check_parent(checkout, &target)?;
    if link_present(&target, &source)? {
        return Ok(None);
    }
    symlink(&source, &target).with_context(|| format!("linking cache {}", target.display()))?;
    Ok(Some((target, source)))
}

pub(crate) fn require_links(project: &ProjectCfg, checkout: &Path) -> Result<()> {
    if crate::runtime::is_slopcar() && relative_mounts(project).next().is_some() {
        bail!(
            "relative cache links are unavailable in sidecar mode until host and container cache paths are mapped identically"
        );
    }
    for mount in relative_mounts(project) {
        let target = checkout.join(expand(&mount.to));
        check_parent(checkout, &target)?;
        let expected = validate(project, mount)?;
        let actual = std::fs::read_link(&target).with_context(|| {
            format!(
                "cache link {} is missing or changed; save project mounts to reconcile it",
                target.display()
            )
        })?;
        if actual != expected {
            bail!(
                "cache link {} points to {} instead of {}",
                target.display(),
                actual.display(),
                expected.display()
            );
        }
    }
    Ok(())
}

pub(crate) fn remove_links(
    project: &ProjectCfg,
    checkout: &Path,
) -> Result<Vec<(PathBuf, PathBuf)>> {
    let mut links = Vec::new();
    let result = (|| -> Result<()> {
        for mount in relative_mounts(project) {
            let target = checkout.join(expand(&mount.to));
            check_parent(checkout, &target)?;
            let source = validate(project, mount)?;
            match std::fs::read_link(&target) {
                Ok(actual) if actual == source => {
                    std::fs::remove_file(&target)?;
                    links.push((target, source));
                }
                Ok(_) => bail!(
                    "cache link {} changed; resolve it before removal",
                    target.display()
                ),
                Err(error) if error.kind() == std::io::ErrorKind::NotFound => {}
                Err(error) => return Err(error.into()),
            }
        }
        Ok(())
    })();
    if let Err(error) = result {
        return match restore_links(&links) {
            Ok(()) => Err(error),
            Err(rollback) => Err(anyhow::anyhow!(
                "{error:#}; restoring cache links also failed: {rollback:#}"
            )),
        };
    }
    Ok(links)
}

pub(crate) fn restore_links(links: &[(PathBuf, PathBuf)]) -> Result<()> {
    for (target, source) in links {
        if !link_present(target, source)? {
            symlink(source, target)
                .with_context(|| format!("restoring cache link {}", target.display()))?;
        }
    }
    Ok(())
}

/// True only for the expected link. A dangling or foreign occupant is a conflict.
fn link_present(target: &Path, source: &Path) -> Result<bool> {
    match std::fs::symlink_metadata(target) {
        Ok(meta) if meta.file_type().is_symlink() && std::fs::read_link(target)? == source => {
            Ok(true)
        }
        Ok(_) => bail!(
            "cache destination {} already exists; move its contents into {} and remove it before retrying",
            target.display(),
            source.display()
        ),
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(false),
        Err(error) => {
            Err(error).with_context(|| format!("checking cache link {}", target.display()))
        }
    }
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
        crate::worktrees::project_root(project)
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
pub(crate) fn inventory(projects: &[ProjectCfg], measure_sizes: bool) -> Vec<StoredState> {
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
                measure_sizes,
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
            let mut row = stored_entry("cache-managed", key.clone(), None, &path, measure_sizes);
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
