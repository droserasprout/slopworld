//! Session-operation guards and runtime publication for configuration transactions.
//! config::transactions owns preparation, persistence, rollback, and catalog revisions.

mod removal;
use super::super::*;
pub(super) use crate::config::transactions::ConfigMutation;
use crate::config::transactions::{ConfigChange, PreparedConfigChange, SettingsEdit};

impl crate::config::transactions::Runtime for Manager {
    async fn snapshot(&self) -> Config {
        self.config().await
    }
    async fn validate(&self, old: &Config, new: &Config) -> Result<()> {
        self.validate_worktree_config(old, new).await
    }
    fn worktrees(&self) -> crate::worktrees::Store {
        self.worktree_records()
    }
    async fn publish(&self, change: ConfigChange) -> String {
        self.publish_config(change).await
    }
    async fn update_endpoint(&self, token: &str) {
        self.workspace.update_endpoint(token).await;
    }
}

impl Manager {
    pub async fn config(&self) -> Config {
        self.config_authority.read_config().await.clone()
    }
    pub(super) async fn update_cfg<T>(
        self: &Arc<Self>,
        mutation: ConfigMutation,
        update: impl FnOnce(&mut Config) -> Result<T>,
    ) -> Result<T> {
        self.update_cfg_if_changed(mutation, |cfg| update(cfg).map(|result| (result, true)))
            .await
    }
    pub(super) async fn update_cfg_if_changed<T>(
        self: &Arc<Self>,
        mutation: ConfigMutation,
        update: impl FnOnce(&mut Config) -> Result<(T, bool)>,
    ) -> Result<T> {
        self.session_operation(async {
            let (result, prepared) = self.prepare_cfg_change(mutation, update).await?;
            if let Some(prepared) = prepared {
                self.commit_prepared_cfg(prepared).await?;
            }
            Ok(result)
        })
        .await
    }
    pub(super) async fn prepare_cfg_change<T>(
        &self,
        mutation: ConfigMutation,
        update: impl FnOnce(&mut Config) -> Result<(T, bool)>,
    ) -> Result<(T, Option<PreparedConfigChange>)> {
        self.workspace
            .configuration
            .prepare(self, mutation, update)
            .await
    }
    pub(super) async fn commit_prepared_cfg(
        self: &Arc<Self>,
        prepared: PreparedConfigChange,
    ) -> Result<()> {
        let manager = self.clone();
        self.owned_session_operation(Box::pin(async move {
            manager
                .workspace
                .configuration
                .commit(manager.as_ref(), prepared)
                .await
        }))
        .await
    }
    pub(super) async fn commit_project_relocation(
        self: &Arc<Self>,
        prepared: PreparedConfigChange,
        store: &crate::worktrees::Store,
    ) -> Result<()> {
        let manager = self.clone();
        let store = store.clone();
        self.owned_session_operation(Box::pin(async move {
            manager
                .workspace
                .configuration
                .commit_project_relocation(manager.as_ref(), prepared, &store)
                .await
        }))
        .await
    }
    pub async fn patch_config(self: &Arc<Self>, patch: Value) -> Result<()> {
        self.edit_record_settings(SettingsEdit::Patch(json_to_toml(patch)?))
            .await
    }
    pub async fn replace_config(self: &Arc<Self>, text: &str) -> Result<()> {
        self.edit_record_settings(SettingsEdit::Replace(text.to_owned()))
            .await
    }
    async fn edit_record_settings(self: &Arc<Self>, edit: SettingsEdit) -> Result<()> {
        let manager = self.clone();
        self.owned_session_operation(async move {
            manager
                .workspace
                .configuration
                .edit_settings(manager.as_ref(), edit)
                .await?;
            manager.finish_config_change().await;
            Ok(())
        })
        .await
    }
    pub async fn reload_if_changed(self: &Arc<Self>) -> bool {
        if self.session_request_active() {
            return false;
        }
        let manager = self.clone();
        self.owned_session_operation(async move {
            if !manager
                .workspace
                .configuration
                .reload(manager.as_ref())
                .await
            {
                return false;
            }
            manager.announce_library().await;
            true
        })
        .await
    }
    pub(super) async fn reload_if_stale(self: &Arc<Self>) {
        if !self.workspace.configuration.library_is_current() {
            self.reload_if_changed().await;
        }
    }
    async fn publish_config(&self, change: ConfigChange) -> String {
        let old = self.config().await;
        let next_sessions = change.new.session_index();
        for session in &old.sessions {
            if !next_sessions
                .get(session.name.as_str())
                .is_some_and(|next| next.state_id == session.state_id)
            {
                self.invalidate_session(&session.name).await;
            }
        }

        // Keep the owned candidate out of every enclosing transaction future.
        let endpoint_token = Box::pin(self.config_authority.publish_config(change)).await;
        self.signals.maintenance_wake.notify_waiters();

        endpoint_token
    }

    /// Called after releasing the persistence gate. Structured edits let their caller reconcile.
    async fn finish_config_change(self: &Arc<Self>) {
        self.sync_from_config().await;
        self.announce_projects().await;
        self.announce_library().await;
    }
}

#[cfg(test)]
pub(super) mod record_tests;
#[cfg(test)]
mod tests;
#[cfg(test)]
mod workspace_tests;
