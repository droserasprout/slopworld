//! Stored private-state and cache administration. Sandbox owns filesystem policy;
//! application session_edits coordinates reset/restore under session lifecycle guards.
use crate::config::Config;
use anyhow::{Result, anyhow};

pub(crate) async fn stored_states(
    cfg: Config,
    measure_sizes: bool,
) -> Result<Vec<crate::sandbox::StoredState>> {
    tokio::task::spawn_blocking(move || {
        // Quick inventory skips expired-tree deletion as well as recursive accounting.
        if measure_sizes && let Err(e) = crate::sandbox::purge_trash() {
            tracing::warn!("purging private-state trash before inventory: {e:#}");
        }
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

pub(crate) async fn empty_trash() -> Result<()> {
    tokio::task::spawn_blocking(crate::sandbox::empty_trash)
        .await
        .map_err(|e| anyhow!("emptying private-state trash: {e}"))??;
    Ok(())
}

pub(crate) async fn delete_stored_state(cfg: Config, kind: &str, key: &str) -> Result<()> {
    let sessions = cfg.sessions;
    let kind = kind.to_string();
    let key = key.to_string();
    tokio::task::spawn_blocking(move || crate::sandbox::delete_stored_state(&kind, &key, &sessions))
        .await
        .map_err(|e| anyhow!("deleting private state: {e}"))?
}

#[cfg(test)]
#[path = "stored_state_tests.rs"]
mod tests;
