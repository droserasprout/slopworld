//! Live validation and accepted-state publication shared by application record writers.
//! Constructed for an operation only; persistent record owners never retain the runtime.
use super::{ConfigAuthority, Workspace};
use crate::config::{Config, transactions::ConfigChange};
use crate::session::SessionRuntime;
use anyhow::Result;
use std::sync::Arc;

pub(super) struct ConfigRuntime {
    sessions: Arc<SessionRuntime>,
    workspace: Arc<Workspace>,
    config_authority: Arc<ConfigAuthority>,
}
impl ConfigRuntime {
    pub(super) fn new(
        sessions: Arc<SessionRuntime>,
        workspace: Arc<Workspace>,
        config_authority: Arc<ConfigAuthority>,
    ) -> Self {
        Self {
            sessions,
            workspace,
            config_authority,
        }
    }
}
impl crate::config::transactions::Runtime for ConfigRuntime {
    async fn snapshot(&self) -> Config {
        self.config_authority.snapshot().await
    }
    async fn validate(&self, old: &Config, new: &Config) -> Result<()> {
        self.sessions.validate_worktree_config(old, new).await
    }
    fn worktrees(&self) -> crate::worktrees::Store {
        self.workspace.configuration.records.worktrees()
    }
    async fn publish(&self, change: ConfigChange) -> String {
        self.sessions
            .invalidate_replaced_sessions(&change.new)
            .await;
        let token = Box::pin(self.config_authority.publish_config(change)).await;
        self.sessions.maintenance_wake().notify_waiters();
        token
    }
    async fn update_endpoint(&self, token: &str) {
        self.workspace.update_endpoint(token).await;
    }
}
