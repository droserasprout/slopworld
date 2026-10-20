//! Project transaction adapter; application owns edits, relocation and publication.
use super::super::*;
use crate::config::transactions::ConfigMutation;

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
        replacement: crate::application::projects::ProjectReplacement,
    ) -> Result<Option<crate::config::transactions::PreparedConfigChange>> {
        self.prepare_cfg_change(ConfigMutation::ProjectReferences, |cfg| {
            replacement.apply(cfg)?;
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
            let pause = self
                .workspace
                .worktrees
                .relocation_pause
                .lock()
                .unwrap()
                .take();
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
