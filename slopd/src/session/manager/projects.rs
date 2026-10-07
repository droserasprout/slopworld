//! Project catalog mutations and rollback of managed checkout relocations.
//! Library execution and delayed input belong to library.rs.
use super::super::*;
use super::directories::CreatedDirectories;
use crate::session::manager::config::ConfigMutation;
use crate::session::validation::check_project_mounts;
use anyhow::anyhow;

impl Manager {
    pub async fn projects(&self) -> Vec<ProjectView> {
        self.cfg
            .read()
            .await
            .projects
            .iter()
            .cloned()
            .map(ProjectView::from)
            .collect()
    }

    pub(super) async fn announce_projects(&self) {
        self.emit(Event::Projects {
            projects: self.projects().await,
        });
    }

    pub async fn add_project(self: &Arc<Self>, p: ProjectCfg) -> Result<()> {
        let manager = self.clone();
        self.owned_session_operation(async move { manager.add_project_inner(p).await })
            .await
    }

    async fn add_project_inner(self: &Arc<Self>, mut p: ProjectCfg) -> Result<()> {
        let _worktrees = self.worktrees.mutation.lock().await;
        self.reload_if_changed().await;
        settle(&mut p);
        p.id = self.allocate_project_identity().await?;
        check_project(&p)?;
        self.update_cfg(ConfigMutation::Projects, |cfg| {
            check_project_mounts(cfg, &p)?;
            if cfg.project(&p.name).is_some() {
                bail!("project {} already exists", p.name);
            }
            cfg.projects.push(p);
            Ok(())
        })
        .await?;
        self.announce_projects().await;
        Ok(())
    }

    pub async fn update_project(self: &Arc<Self>, name: &str, mut p: ProjectCfg) -> Result<()> {
        settle(&mut p);
        let manager = self.clone();
        let name = name.to_string();
        // The detached owner retains the boundary and rollback plan after caller cancellation.
        self.owned_session_operation(Box::pin(async move {
            manager.reload_if_changed().await;
            manager.update_project_inner(&name, p).await?;
            manager.announce_projects().await;
            manager.announce_sessions().await;
            Ok(())
        }))
        .await
    }

    async fn update_project_inner(self: &Arc<Self>, name: &str, mut p: ProjectCfg) -> Result<()> {
        let _lock = self.worktrees.mutation.lock().await;
        let old_project = self
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
        check_project_mounts(&self.config().await, &p)?;
        let (store, planned, mut directories) = self
            .plan_project_worktree_relocations(name, &old_project, &p)
            .await?;
        let mut relocations = crate::worktrees::relocation::Relocations::new(store, planned)?;
        relocations.execute_pending(self.as_ref()).await?;
        #[cfg(test)]
        {
            let pause = self.worktrees.relocation_pause.lock().unwrap().take();
            if let Some((reached, release)) = pause {
                reached.notify_one();
                release.notified().await;
            }
        }
        let result = self
            .prepare_cfg_change(ConfigMutation::ProjectReferences, |cfg| {
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
                check_project_mounts(cfg, &p)?;
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
                Ok(((), true))
            })
            .await;
        let result = match result {
            Ok(((), Some(prepared))) => {
                if relocations.has_moves() {
                    self.commit_project_relocation(prepared, relocations.candidate())
                        .await
                } else {
                    self.commit_prepared_cfg(prepared).await
                }
            }
            Ok(((), None)) => Ok(()),
            Err(error) => Err(error),
        };
        if let Err(error) = result {
            return match relocations.rollback(self.as_ref()).await {
                Ok(()) => Err(error),
                Err(rollback) => Err(anyhow!(
                    "{error:#}; restoring project worktrees also failed: {rollback:#}"
                )),
            };
        }
        directories.commit();
        Ok(())
    }

    async fn plan_project_worktree_relocations(
        &self,
        name: &str,
        old_project: &ProjectCfg,
        p: &ProjectCfg,
    ) -> Result<(
        crate::worktrees::Store,
        Vec<(usize, crate::worktrees::Worktree)>,
        CreatedDirectories,
    )> {
        let store = self.worktree_records();
        let mut planned = Vec::new();
        let mut directories = CreatedDirectories::default();
        if p.name != name {
            crate::config::project_name_component(&p.name)?;
            if self.config().await.project(&p.name).is_some() {
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
                if w.phase != "ready" {
                    bail!(
                        "finish worktree {} recovery before renaming the project",
                        w.id
                    );
                }
                let users = self.worktree_attachments(w).await;
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
                    super::worktrees::checked_worktree_destination(&project_dir, &w.name).await?;
                if std::fs::symlink_metadata(&dest).is_ok() {
                    bail!("destination {} already exists", dest.display());
                }
                let mut destination = w.clone();
                destination.path = dest.to_string_lossy().into_owned();
                planned.push((i, destination));
            }
        }
        Ok((store, planned, directories))
    }

    pub async fn remove_project(self: &Arc<Self>, name: &str) -> Result<()> {
        let manager = self.clone();
        let name = name.to_owned();
        self.owned_session_operation(async move { manager.remove_project_inner(&name).await })
            .await
    }

    async fn remove_project_inner(self: &Arc<Self>, name: &str) -> Result<()> {
        let _worktrees = self.worktrees.mutation.lock().await;
        self.reload_if_changed().await;
        let cfg = self.config().await;
        if let Some(p) = cfg.project(name)
            && !p.id.is_empty()
            && self
                .worktree_records()
                .worktrees
                .iter()
                .any(|w| w.project_id == p.id)
        {
            bail!("remove project worktrees explicitly first");
        }
        self.update_cfg(ConfigMutation::ProjectReferences, |cfg| {
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
        })
        .await?;
        self.announce_projects().await;
        Ok(())
    }
}
