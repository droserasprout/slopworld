//! Verify checkout identity and mount isolation; recognize completed interrupted allocations.
use super::{Store, Worktree, WorktreePhase, git};
use crate::config::{Config, ProjectCfg, expand};
use anyhow::{Result, anyhow, bail};
use std::path::Path;
pub(crate) async fn resolve(store: &Store, p: &ProjectCfg, id: &str) -> Result<ProjectCfg> {
    if id.is_empty() || id == "main" {
        super::validated_metadata_paths(Path::new(&expand(&p.dir)), None)?;
        let mut primary = p.clone();
        primary.worktree_repository = None;
        return Ok(primary);
    }
    let w = store
        .worktrees
        .iter()
        .find(|w| w.id == id && w.project_id == p.id && !p.id.is_empty())
        .ok_or_else(|| anyhow!("no worktree {id} in project {}", p.name))?;
    if w.phase != WorktreePhase::Ready {
        bail!("worktree {id} is not ready (phase {})", w.phase);
    }
    let checkout = Path::new(&w.path);
    if w.managed && checkout.file_name().and_then(|s| s.to_str()) != Some(&w.name) {
        bail!("managed worktree {id} path does not match its recorded name");
    }
    if checkout.is_symlink() || !checkout.is_dir() {
        bail!("worktree {id} checkout is missing or is not a directory");
    }
    let top = git(checkout, &["rev-parse", "--show-toplevel"]).await?;
    if Path::new(&top).canonicalize()? != checkout.canonicalize()? {
        bail!("worktree checkout no longer has its own Git metadata");
    }
    let repository = Path::new(&w.repository).canonicalize()?;
    let common = git(
        checkout,
        &["rev-parse", "--path-format=absolute", "--git-common-dir"],
    )
    .await?;
    if Path::new(&common).canonicalize()? != repository {
        bail!("worktree repository identity changed");
    }
    super::validated_metadata_paths(checkout, Some(Path::new(&w.repository)))?;
    let original = std::path::PathBuf::from(expand(&p.dir)).canonicalize()?;
    for mount in &p.mounts {
        if mount.mode == crate::config::MountMode::Cache {
            crate::sandbox::cache::validate(p, mount)?;
            let target = std::path::PathBuf::from(crate::config::mount_target(p, &mount.to));
            if std::path::Path::new(&expand(&mount.to)).is_absolute()
                && target.starts_with(&original)
            {
                bail!("cache destination must be relative to follow the selected worktree");
            }
            continue;
        }
        let source = std::path::PathBuf::from(expand(&mount.from)).canonicalize()?;
        let target = std::path::PathBuf::from(crate::config::mount_target(p, &mount.to));
        if source.starts_with(&original)
            || original.starts_with(&source)
            || (std::path::Path::new(&mount.to).is_absolute() && target.starts_with(&original))
        {
            bail!(
                "Project mount {} -> {} exposes the original checkout. Edit the mount before selecting a worktree.",
                mount.from,
                mount.to
            );
        }
    }
    let mut resolved = p.clone();
    resolved.dir = w.path.clone();
    resolved.worktree_repository = Some(w.repository.clone().into());
    resolved.temp = false;
    Ok(resolved)
}
/// Only verified daemon cache links may dirty an otherwise completed allocation.
pub(crate) async fn completed_allocation(cfg: &Config, w: &Worktree) -> Result<bool> {
    let checkout = Path::new(&w.path);
    let common = git(
        checkout,
        &["rev-parse", "--path-format=absolute", "--git-common-dir"],
    )
    .await?;
    if Path::new(&common).canonicalize()? != Path::new(&w.repository).canonicalize()? {
        return Ok(false);
    }
    if git(checkout, &["rev-parse", "--verify", "HEAD"]).await? != w.base {
        return Ok(false);
    }
    let project = cfg
        .projects
        .iter()
        .find(|project| project.id == w.project_id)
        .ok_or_else(|| anyhow!("worktree project is missing"))?;
    let status = git(
        checkout,
        &["status", "--porcelain=v1", "-z", "--untracked-files=all"],
    )
    .await?;
    let mut owned = std::collections::HashSet::new();
    for mount in crate::sandbox::cache::relative_mounts(project) {
        let target = expand(&mount.to);
        let expected = crate::sandbox::cache::validate(project, mount)?;
        if std::fs::read_link(checkout.join(&target)).is_ok_and(|actual| actual == expected) {
            owned.insert(std::path::PathBuf::from(target));
        }
    }
    Ok(status
        .split('\0')
        .filter(|entry| !entry.is_empty())
        .all(|entry| {
            entry
                .strip_prefix("?? ")
                .is_some_and(|path| owned.contains(Path::new(path)))
        }))
}

pub(crate) fn validate_config(store: &Store, old: &Config, new: &Config) -> Result<()> {
    use std::collections::HashMap;

    // Worker removal validates the whole candidate. Index cross-references
    // once so sessions and worker-owned worktrees do not multiply scans.
    let mut projects_by_name = HashMap::new();
    let mut projects_by_id = HashMap::new();
    for project in &new.projects {
        projects_by_name
            .entry(project.name.as_str())
            .or_insert(project);
        projects_by_id.entry(project.id.as_str()).or_insert(project);
    }
    let worktree_projects: std::collections::HashSet<_> = store
        .worktrees
        .iter()
        .map(|w| w.project_id.as_str())
        .collect();
    let worktrees: std::collections::HashSet<_> = store
        .worktrees
        .iter()
        .map(|w| (w.project_id.as_str(), w.id.as_str()))
        .collect();
    let mut ids = std::collections::HashSet::new();
    for p in &new.projects {
        if !p.id.is_empty() {
            anyhow::ensure!(crate::storage_id::valid(&p.id), "invalid project identity");
            if !ids.insert(&p.id) {
                bail!("duplicate project identity {}", p.id);
            }
        }
    }
    for p in &old.projects {
        if !p.id.is_empty() && worktree_projects.contains(p.id.as_str()) {
            let retained = projects_by_id.get(p.id.as_str()).ok_or_else(|| {
                anyhow!(
                    "remove project {} worktrees explicitly before removing its identity",
                    p.name
                )
            })?;
            if retained.dir != p.dir {
                bail!("remove project worktrees before changing the original checkout directory");
            }
        }
    }
    for session in &new.sessions {
        if !session.worktree.is_empty() && session.worktree != "main" {
            let p = projects_by_name
                .get(session.project.as_str())
                .ok_or_else(|| anyhow!("unknown worktree project"))?;
            if p.id.is_empty() || !worktrees.contains(&(p.id.as_str(), session.worktree.as_str())) {
                bail!("session {} selects an unknown worktree", session.name);
            }
        }
    }
    Ok(())
}
