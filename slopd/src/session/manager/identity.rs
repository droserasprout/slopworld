//! New agent identity allocation. The session boundary reserves the selected ID
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
            .cfg
            .read()
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
        occupied.extend(self.tasks.participant_identities_async().await?);
        occupied.extend(
            tokio::task::spawn_blocking(crate::sandbox::retained_state_identities)
                .await
                .context("private-state identity inventory failed")??,
        );
        let records = crate::paths::data_root().join("agents");
        crate::storage_id::allocate(|id| {
            if occupied.contains(id) {
                return Ok(true);
            }
            match std::fs::symlink_metadata(records.join(format!("{id}.toml"))) {
                Ok(_) => Ok(true),
                Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(false),
                Err(error) => Err(error).context("checking agent record identity"),
            }
        })
    }
}

#[cfg(test)]
#[path = "identity_tests.rs"]
mod tests;

impl Manager {
    /// Caller holds the worktree mutation guard inside a session boundary. Cache
    /// directories outlive projects and reserve their identities as well.
    pub(super) async fn allocate_project_identity(&self) -> Result<String> {
        let mut occupied: HashSet<String> = self
            .config()
            .await
            .projects
            .into_iter()
            .map(|p| p.id)
            .collect();
        occupied.extend(
            self.load_worktrees()
                .await?
                .worktrees
                .into_iter()
                .map(|w| w.project_id),
        );
        let root = crate::paths::config_root();
        #[cfg(test)]
        let root = self
            .record_backend()
            .map_or(root, |records| records.binding.config.clone());
        let records = root.join("projects");
        let caches = crate::sandbox::cache::root();
        crate::storage_id::allocate(|id| {
            Ok(occupied.contains(id)
                || occupied_path(&records.join(format!("{id}.toml")))?
                || occupied_path(&caches.join(id))?)
        })
    }

    /// Worktree IDs remain reserved by configured/live references after a damaged
    /// catalog; a new checkout must never inherit those attachments.
    pub(super) async fn allocate_worktree_identity(&self) -> Result<String> {
        let mut occupied: HashSet<String> = self
            .load_worktrees()
            .await?
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
        let root = crate::paths::data_root();
        #[cfg(test)]
        let root = self
            .record_backend()
            .map_or(root, |records| records.binding.data.clone());
        let records = root.join("worktrees");
        crate::storage_id::allocate(|id| {
            Ok(occupied.contains(id) || occupied_path(&records.join(format!("{id}.toml")))?)
        })
    }
}

fn occupied_path(path: &Path) -> Result<bool> {
    match std::fs::symlink_metadata(path) {
        Ok(_) => Ok(true),
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(false),
        Err(error) => Err(error).context("checking retained workspace identity"),
    }
}
