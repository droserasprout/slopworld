//! Project catalog policy and checkout relocation coordination.
//! Session adapters retain operation guards, identity allocation, and attachment checks.
use crate::config::transactions::PreparedConfigChange;
use crate::config::{Config, ProjectCfg, check_project, expand, validate_mount_paths};
use crate::worktrees::{Store, Worktree, WorktreePhase, directories::CreatedDirectories};
use anyhow::{Context, Result, anyhow, bail};
use std::path::{Path, PathBuf};

pub(crate) trait ProjectRuntime: crate::worktrees::relocation::Persistence + Sync {
    fn config(&self) -> impl std::future::Future<Output = Config> + Send;
    fn worktree_records(&self) -> Store;
    fn worktree_attachments(
        &self,
        worktree: &Worktree,
    ) -> impl std::future::Future<Output = Vec<String>> + Send;
    fn prepare(
        &self,
        name: &str,
        project: ProjectCfg,
    ) -> impl std::future::Future<Output = Result<Option<PreparedConfigChange>>> + Send;
    fn commit(
        &self,
        prepared: PreparedConfigChange,
        worktrees: Option<&Store>,
    ) -> impl std::future::Future<Output = Result<()>> + Send;
    fn after_relocations(&self) -> impl std::future::Future<Output = ()> + Send;
}

pub(crate) fn add(cfg: &mut Config, p: ProjectCfg) -> Result<()> {
    validate_mount_paths(&p)?;
    if cfg.project(&p.name).is_some() {
        bail!("project {} already exists", p.name);
    }
    cfg.projects.push(p);
    Ok(())
}
pub(crate) fn replace(cfg: &mut Config, name: &str, mut p: ProjectCfg) -> Result<()> {
    let idx = cfg
        .projects
        .iter()
        .position(|x| x.name == name)
        .ok_or_else(|| anyhow!("Project {name:?} does not exist."))?;
    let old_project = cfg
        .projects
        .get(idx)
        .ok_or_else(|| anyhow!("Project {name:?} does not exist."))?;
    if old_project.temp != p.temp {
        bail!("The daemon cannot change project temporary mode after creation.");
    }
    p.id = old_project.id.clone();
    check_project(&p)?;
    validate_mount_paths(&p)?;
    if p.name != name && cfg.project(&p.name).is_some() {
        bail!("project {} already exists", p.name);
    }
    let renamed = p.name.clone();
    let project = cfg
        .projects
        .get_mut(idx)
        .ok_or_else(|| anyhow!("Project {name:?} does not exist."))?;
    *project = p;
    if renamed != name {
        for s in cfg.sessions.iter_mut().filter(|s| s.project == name) {
            s.project = renamed.clone();
        }
        for shell in cfg.host_terminals.iter_mut().filter(|s| s.project == name) {
            shell.project = renamed.clone();
        }
    }
    Ok(())
}
pub(crate) fn remove(cfg: &mut Config, name: &str) -> Result<()> {
    if cfg.project(name).is_none() {
        bail!("Project {name:?} does not exist.");
    }
    let users: Vec<&str> = cfg
        .sessions
        .iter()
        .filter(|s| s.project == name)
        .map(|s| s.name.as_str())
        .chain(
            cfg.host_terminals
                .iter()
                .filter(|s| s.project == name)
                .map(|s| s.name.as_str()),
        )
        .collect();
    if !users.is_empty() {
        bail!(
            "project {name} still has agents in it: {}. Remove or move them first.",
            users.join(", ")
        );
    }
    cfg.projects.retain(|p| p.name != name);
    Ok(())
}
pub(crate) async fn update(
    runtime: &impl ProjectRuntime,
    name: &str,
    mut p: ProjectCfg,
) -> Result<()> {
    let old_project = runtime
        .config()
        .await
        .project(name)
        .cloned()
        .ok_or_else(|| anyhow!("Project {name:?} does not exist."))?;
    if old_project.temp != p.temp {
        bail!("The daemon cannot change project temporary mode after creation.");
    }
    p.id = old_project.id.clone();
    check_project(&p)?;
    validate_mount_paths(&p)?;
    let RelocationPlan {
        store,
        moves,
        mut directories,
    } = plan_relocations(runtime, name, &old_project, &p).await?;
    let mut relocations = crate::worktrees::relocation::Relocations::new(store, moves)?;
    relocations.execute_pending(runtime).await?;
    runtime.after_relocations().await;
    let result = runtime.prepare(name, p).await;
    let result = match result {
        Ok(Some(prepared)) => {
            if relocations.has_moves() {
                runtime
                    .commit(prepared, Some(relocations.candidate()))
                    .await
            } else {
                runtime.commit(prepared, None).await
            }
        }
        Ok(None) => Ok(()),
        Err(error) => Err(error),
    };
    if let Err(error) = result {
        return match relocations.rollback(runtime).await {
            Ok(()) => Err(error),
            Err(rollback) => Err(anyhow!(
                "{error:#}; restoring project worktrees also failed: {rollback:#}"
            )),
        };
    }
    directories.commit();
    Ok(())
}

struct RelocationPlan {
    store: Store,
    moves: Vec<(usize, Worktree)>,
    directories: CreatedDirectories,
}

async fn plan_relocations(
    runtime: &impl ProjectRuntime,
    name: &str,
    old_project: &ProjectCfg,
    p: &ProjectCfg,
) -> Result<RelocationPlan> {
    let store = runtime.worktree_records();
    let mut planned = Vec::new();
    let mut directories = CreatedDirectories::default();
    if p.name != name {
        crate::config::project_name_component(&p.name)?;
        if runtime.config().await.project(&p.name).is_some() {
            bail!("project {} already exists", p.name);
        }
        let local_root = PathBuf::from(expand(&old_project.dir)).join(".worktrees");
        let local_root = local_root.canonicalize().unwrap_or(local_root);
        for (i, w) in store
            .worktrees
            .iter()
            .enumerate()
            .filter(|(_, w)| w.managed && w.project_id == old_project.id)
        {
            // Project-local worktrees do not depend on the project's display name.
            if Path::new(&w.path).parent() == Some(local_root.as_path()) {
                continue;
            }
            if w.phase != WorktreePhase::Ready {
                bail!(
                    "finish worktree {} recovery before renaming the project",
                    w.id
                );
            }
            let users = runtime.worktree_attachments(w).await;
            if !users.is_empty() {
                bail!(
                    "Worktree {} remains attached to {}. Remove or move these sessions first.",
                    w.name,
                    users.join(", ")
                );
            }
            let old_path = Path::new(&w.path);
            if old_path.file_name().and_then(|s| s.to_str()) != Some(&w.name) {
                bail!("worktree path does not match its recorded name");
            }
            let root = old_path
                .parent()
                .and_then(Path::parent)
                .context("worktree root")?;
            let project_dir = root.join(&p.name);
            directories.create_all(&project_dir)?;
            let dest =
                crate::worktrees::checkout::checked_worktree_destination(&project_dir, &w.name)
                    .await?;
            if std::fs::symlink_metadata(&dest).is_ok() {
                bail!("destination {} already exists", dest.display());
            }
            let mut destination = w.clone();
            destination.path = dest.to_string_lossy().into_owned();
            planned.push((i, destination));
        }
    }
    Ok(RelocationPlan {
        store,
        moves: planned,
        directories,
    })
}
