//! Cache-link changes across project configuration and its worktrees.

use crate::config::{Mount, MountMode};
use crate::sandbox::cache;
use crate::session::*;
use crate::worktrees::Store;

/// Return removed links so the caller can restore them if configuration saving fails.
pub(super) async fn reconcile_cache_links(
    config_path: &Path,
    old: &Config,
    cfg: &Config,
) -> Result<Vec<(PathBuf, PathBuf)>> {
    let store = Store::load(config_path).await?;
    let mut removed = Vec::new();
    let result = remove_obsolete_links(old, cfg, &store, &mut removed)
        .and_then(|()| reconcile_project_links(cfg, &store));
    if let Err(error) = result {
        cache::restore_links(&removed)?;
        return Err(error);
    }
    Ok(removed)
}

fn is_relative_cache(mount: &Mount) -> bool {
    mount.mode == MountMode::Cache && cache::relative(mount)
}

/// A link survives only while its project directory and destination stay selected.
fn obsolete_cache_mounts(project: &ProjectCfg, next: Option<&ProjectCfg>) -> Vec<Mount> {
    project
        .mounts
        .iter()
        .filter(|mount| {
            is_relative_cache(mount)
                && !next.is_some_and(|p| {
                    p.dir == project.dir
                        && p.mounts
                            .iter()
                            .any(|m| is_relative_cache(m) && expand(&m.to) == expand(&mount.to))
                })
        })
        .cloned()
        .collect()
}

/// Record removals as they succeed so a later failure can restore earlier projects too.
fn remove_obsolete_links(
    old: &Config,
    cfg: &Config,
    store: &Store,
    removed: &mut Vec<(PathBuf, PathBuf)>,
) -> Result<()> {
    for project in &old.projects {
        let next = cfg
            .projects
            .iter()
            .find(|p| p.id == project.id && !p.id.is_empty());
        let obsolete = obsolete_cache_mounts(project, next);
        if obsolete.is_empty() {
            continue;
        }
        let mut retired = project.clone();
        retired.mounts = obsolete;
        let main = PathBuf::from(expand(&project.dir));
        if main.is_dir() {
            removed.extend(cache::remove_links(&retired, &main)?);
        }
        for worktree in store
            .worktrees
            .iter()
            .filter(|w| w.project_id == project.id && Path::new(&w.path).is_dir())
        {
            removed.extend(cache::remove_links(&retired, Path::new(&worktree.path))?);
        }
    }
    Ok(())
}

fn reconcile_project_links(cfg: &Config, store: &Store) -> Result<()> {
    for project in &cfg.projects {
        if !project.mounts.iter().any(is_relative_cache) {
            continue;
        }
        let main = PathBuf::from(expand(&project.dir));
        cache::reconcile(project, &main)?;
        for worktree in store
            .worktrees
            .iter()
            .filter(|w| w.project_id == project.id && Path::new(&w.path).is_dir())
        {
            cache::reconcile(project, Path::new(&worktree.path))?;
        }
    }
    Ok(())
}

#[cfg(test)]
#[path = "cache_tests.rs"]
mod tests;
