//! Cache-link changes across project configuration and its worktrees.

use crate::config::{Mount, MountMode};
use crate::sandbox::cache;
use crate::session::*;
use crate::worktrees::Store;

/// Filesystem changes owned by a config attempt until its persistence succeeds.
#[derive(Debug, Default)]
pub(super) struct LinkChanges {
    removed: Vec<(PathBuf, PathBuf)>,
    added: Vec<(PathBuf, PathBuf)>,
}
impl LinkChanges {
    pub(super) fn rollback(&self) -> Result<()> {
        let mut errors = Vec::new();
        for (target, source) in self.added.iter().rev() {
            let result = (|| -> Result<()> {
                if std::fs::read_link(target)? != *source {
                    bail!("cache link {} changed during rollback", target.display());
                }
                std::fs::remove_file(target)?;
                Ok(())
            })();
            if let Err(error) = result {
                errors.push(format!("{error:#}"));
            }
        }
        if let Err(error) = cache::restore_links(&self.removed) {
            errors.push(format!("{error:#}"));
        }
        if !errors.is_empty() {
            bail!("{}", errors.join("; "));
        }
        Ok(())
    }
    pub(super) fn rollback_error(&self, error: anyhow::Error) -> anyhow::Error {
        match self.rollback() {
            Ok(()) => error,
            Err(rollback) => {
                anyhow::anyhow!("{error:#}; restoring cache links also failed: {rollback:#}")
            }
        }
    }
}

pub(super) async fn reconcile_cache_links(
    config_path: &Path,
    old: &Config,
    cfg: &Config,
) -> Result<LinkChanges> {
    let store = Store::load(config_path).await?;
    let mut changes = LinkChanges::default();
    let result = remove_obsolete_links(old, cfg, &store, &mut changes.removed)
        .and_then(|()| reconcile_project_links(cfg, &store, &mut changes.added));
    if let Err(error) = result {
        return Err(changes.rollback_error(error));
    }
    Ok(changes)
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

fn reconcile_project_links(
    cfg: &Config,
    store: &Store,
    added: &mut Vec<(PathBuf, PathBuf)>,
) -> Result<()> {
    for project in &cfg.projects {
        if !project.mounts.iter().any(is_relative_cache) {
            continue;
        }
        let main = PathBuf::from(expand(&project.dir));
        reconcile_checkout_links(project, &main, added)?;
        for worktree in store
            .worktrees
            .iter()
            .filter(|w| w.project_id == project.id && Path::new(&w.path).is_dir())
        {
            reconcile_checkout_links(project, Path::new(&worktree.path), added)?;
        }
    }
    Ok(())
}

// Reconcile one mount at a time so every successful addition has a rollback owner
// before a later mount or checkout can fail. Cache contents themselves are retained.
fn reconcile_checkout_links(
    project: &ProjectCfg,
    checkout: &Path,
    added: &mut Vec<(PathBuf, PathBuf)>,
) -> Result<()> {
    for mount in project
        .mounts
        .iter()
        .filter(|mount| is_relative_cache(mount))
    {
        let target = checkout.join(expand(&mount.to));
        let absent = match std::fs::symlink_metadata(&target) {
            Ok(_) => false,
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => true,
            Err(error) => return Err(error.into()),
        };
        let mut selected = project.clone();
        selected.mounts = vec![mount.clone()];
        cache::reconcile(&selected, checkout)?;
        if absent {
            added.push((target, cache::source(project, mount)?));
        }
    }
    Ok(())
}

#[cfg(test)]
#[path = "cache_tests.rs"]
mod tests;
