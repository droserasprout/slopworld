//! Project catalog mutations and rollback of managed checkout relocations.
//! Library execution and delayed input belong to library.rs.
use super::super::*;
use super::directories::CreatedDirectories;
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

    pub async fn add_project(self: &Arc<Self>, mut p: ProjectCfg) -> Result<()> {
        self.reload_if_changed().await;
        settle(&mut p);
        p.id = uuid::Uuid::new_v4().to_string();
        check_project(&p)?;
        self.update_cfg(|cfg| {
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
        tokio::spawn(async move {
            manager
                .session_operation(async {
                    manager.reload_if_changed().await;
                    manager.update_project_inner(&name, p).await?;
                    manager.announce_projects().await;
                    manager.announce_sessions().await;
                    Ok::<_, anyhow::Error>(())
                })
                .await
        })
        .await
        .map_err(|error| anyhow!("project update task failed: {error}"))?
    }

    async fn update_project_inner(&self, name: &str, mut p: ProjectCfg) -> Result<()> {
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
        relocations.execute(&self.cfg_path).await?;
        #[cfg(test)]
        {
            let pause = self.worktrees.relocation_pause.lock().unwrap().take();
            if let Some((reached, release)) = pause {
                reached.notify_one();
                release.notified().await;
            }
        }
        let result = self
            .update_cfg_if_changed_inner(|cfg| {
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
                if p.id.is_empty() {
                    p.id = uuid::Uuid::new_v4().to_string();
                }
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
                }
                Ok(((), true))
            })
            .await;
        if let Err(error) = result {
            return match relocations.rollback(&self.cfg_path).await {
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
        let store = crate::worktrees::Store::load(&self.cfg_path).await?;
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
                if std::fs::symlink_metadata(&project_dir)?
                    .file_type()
                    .is_symlink()
                {
                    bail!("worktree project directory cannot be a symlink");
                }
                let dest = project_dir.canonicalize()?.join(&w.name);
                if let Some(why) = crate::sandbox::refused(&dest.to_string_lossy()) {
                    bail!("worktree reaches {why}");
                }
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
        self.reload_if_changed().await;
        let cfg = self.config().await;
        if let Some(p) = cfg.project(name)
            && !p.id.is_empty()
            && crate::worktrees::Store::load(&self.cfg_path)
                .await?
                .worktrees
                .iter()
                .any(|w| w.project_id == p.id)
        {
            bail!("remove project worktrees explicitly first");
        }
        self.update_cfg(|cfg| {
            if cfg.project(name).is_none() {
                bail!("Project {name:?} does not exist.");
            }
            let users: Vec<&str> = cfg
                .sessions
                .iter()
                .filter(|s| s.project == name)
                .map(|s| s.name.as_str())
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
