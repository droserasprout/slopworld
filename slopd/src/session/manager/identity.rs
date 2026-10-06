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
        occupied.extend(self.tasks.participant_identities()?);
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
