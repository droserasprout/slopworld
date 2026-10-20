//! Live identity/reference capture for workspace reservation. The session boundary reserves the selected ID
//! through task/config publication; private-state discovery runs without state locks.

use super::super::*;
use std::collections::HashSet;

impl Manager {
    /// Called only while creating a session under the exclusive session boundary.
    /// Ordinary edits retain identity and never inventory private state or tasks.
    pub(super) async fn allocate_agent_identity(&self) -> Result<String> {
        assert!(
            self.session_write_operation_active(),
            "agent identity allocation requires session boundary"
        );
        let mut occupied: HashSet<String> = self
            .config_authority
            .read_config()
            .await
            .sessions
            .iter()
            .map(|session| session.state_id.clone())
            .collect();
        occupied.extend(
            self.live
                .read()
                .await
                .values()
                .map(|live| live.cfg.state_id.clone()),
        );
        occupied.extend(self.task_store().participant_identities_async().await?);
        occupied.extend(
            tokio::task::spawn_blocking(crate::sandbox::retained_state_identities)
                .await
                .context("private-state identity inventory failed")??,
        );
        self.workspace.allocate_agent_identity(occupied)
    }
}

#[cfg(test)]
#[path = "identity_tests.rs"]
mod tests;

impl Manager {
    /// Caller holds the worktree mutation guard inside a session boundary. Cache
    /// directories outlive projects and reserve their identities as well.
    pub(crate) async fn allocate_project_identity(&self) -> Result<String> {
        self.workspace
            .allocate_project_identity(&self.config().await, &self.worktree_records())
    }

    /// Worktree IDs remain reserved by configured/live references after a damaged
    /// catalog; a new checkout must never inherit those attachments.
    pub(super) async fn allocate_worktree_identity(&self) -> Result<String> {
        let mut occupied: HashSet<String> = self
            .worktree_records()
            .worktrees
            .into_iter()
            .map(|w| w.id)
            .collect();
        occupied.extend(self.config().await.sessions.into_iter().map(|s| s.worktree));
        occupied.extend(
            self.live
                .read()
                .await
                .values()
                .map(|live| live.cfg.worktree.clone()),
        );
        self.workspace.allocate_worktree_identity(occupied)
    }
}
