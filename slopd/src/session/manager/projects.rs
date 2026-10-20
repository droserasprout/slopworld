//! Session guards, identity allocation, and publication for project operations.
//! Library execution and delayed input belong to library.rs.
use super::super::*;
use crate::application::projects::ProjectView;
use crate::session::manager::config::ConfigMutation;

impl Manager {
    pub async fn projects(&self) -> Vec<ProjectView> {
        self.config_authority
            .read_config()
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
            crate::application::projects::add(cfg, p)
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

    async fn update_project_inner(self: &Arc<Self>, name: &str, p: ProjectCfg) -> Result<()> {
        let _lock = self.worktrees.mutation.lock().await;
        crate::application::projects::update(self, name, p).await
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
            crate::application::projects::remove(cfg, name)
        })
        .await?;
        self.announce_projects().await;
        Ok(())
    }
}

impl crate::application::projects::ProjectRuntime for Arc<Manager> {
    async fn config(&self) -> Config {
        Manager::config(self).await
    }
    fn worktree_records(&self) -> crate::worktrees::Store {
        Manager::worktree_records(self)
    }
    async fn worktree_attachments(&self, worktree: &crate::worktrees::Worktree) -> Vec<String> {
        Manager::worktree_attachments(self, worktree).await
    }
    async fn prepare(
        &self,
        name: &str,
        project: ProjectCfg,
    ) -> Result<Option<crate::config::transactions::PreparedConfigChange>> {
        self.prepare_cfg_change(ConfigMutation::ProjectReferences, |cfg| {
            crate::application::projects::replace(cfg, name, project)?;
            Ok(((), true))
        })
        .await
        .map(|((), prepared)| prepared)
    }
    async fn commit(
        &self,
        prepared: crate::config::transactions::PreparedConfigChange,
        worktrees: Option<&crate::worktrees::Store>,
    ) -> Result<()> {
        if let Some(store) = worktrees {
            self.commit_project_relocation(prepared, store).await
        } else {
            self.commit_prepared_cfg(prepared).await
        }
    }
    async fn after_relocations(&self) {
        #[cfg(test)]
        {
            let pause = self.worktrees.relocation_pause.lock().unwrap().take();
            if let Some((reached, release)) = pause {
                reached.notify_one();
                release.notified().await;
            }
        }
    }
}
impl crate::worktrees::relocation::Persistence for Arc<Manager> {
    async fn save_worktree_records(&self, store: &crate::worktrees::Store) -> Result<()> {
        self.save_worktrees(store).await
    }
}

#[cfg(test)]
#[path = "projects_tests.rs"]
mod tests;
