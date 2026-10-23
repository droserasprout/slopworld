//! Stored private-state and cache administration. Sandbox owns filesystem policy;
//! Cleanup retains the same session lifecycle boundary as reset/restore, including
//! blocking filesystem work after requester cancellation. Inventory is read-only.
use super::{Application, Configuration};
use crate::config::Config;
use anyhow::{Result, anyhow};

pub(crate) async fn stored_states(
    cfg: Config,
    measure_sizes: bool,
) -> Result<Vec<crate::sandbox::StoredState>> {
    tokio::task::spawn_blocking(move || {
        let mut entries = crate::sandbox::stored_states(&cfg.sessions, measure_sizes);
        entries.extend(crate::sandbox::cache::inventory(
            &cfg.projects,
            measure_sizes,
        ));
        entries
    })
    .await
    .map_err(|e| anyhow!("scanning private state: {e}"))
}

/// Run blocking destruction only from an owned lifecycle operation. The outer owned
/// task awaits the worker to completion, so cancelling the requester cannot unlock
/// reset/restore while the filesystem worker is still removing directories.
async fn cleanup<T: Send + 'static>(
    action: impl FnOnce() -> Result<T> + Send + 'static,
) -> Result<T> {
    tokio::task::spawn_blocking(move || {
        #[cfg(test)]
        tests::pause_cleanup();
        action()
    })
    .await
    .map_err(|e| anyhow!("cleaning up private state: {e}"))?
}

pub(crate) async fn empty_trash(application: &Application) -> Result<()> {
    application
        .sessions
        .owned_session_operation(async {
            cleanup(crate::sandbox::empty_trash).await?;
            Ok(())
        })
        .await
}

pub(crate) async fn delete_stored_state(
    application: &Application,
    kind: &str,
    key: &str,
) -> Result<()> {
    let configuration = Configuration::new(application);
    let kind = kind.to_string();
    let key = key.to_string();
    application
        .sessions
        .owned_session_operation(async move {
            configuration.reload_if_changed().await;
            // Snapshot ownership after acquiring the lifecycle boundary: a queued
            // deletion must observe any agent creation that preceded its admission.
            let sessions = configuration.config().await.sessions;
            cleanup(move || crate::sandbox::delete_stored_state(&kind, &key, &sessions)).await
        })
        .await
}

#[cfg(test)]
#[path = "stored_state_tests.rs"]
mod tests;
