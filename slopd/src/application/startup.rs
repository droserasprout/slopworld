//! Recover daemon resources before session reconciliation and credential pruning.
use super::{ConfigAuthority, Services};
use crate::application::worktrees::Worktrees;
use crate::{session::Manager, storage::layout::LoadedStorage, tmux::Tmux};
use anyhow::Result;
use std::sync::Arc;

pub(crate) async fn start_sessions(
    storage: LoadedStorage,
    services: Arc<Services>,
    tmux: Tmux,
    authority: Arc<ConfigAuthority>,
) -> Result<Arc<Manager>> {
    services
        .library
        .seed(authority.read_config().await.library_items_all());
    let workspace = Arc::new(super::Workspace::new(storage, crate::endpoint::path()));
    let manager = Manager::new(workspace, services, tmux, authority).await?;
    // Filesystem recovery must finish before configured sessions are reconciled.
    match crate::sandbox::purge_trash() {
        Ok(n) if n > 0 => tracing::info!("purged {n} expired private-state trash entries"),
        Ok(_) => {}
        Err(error) => tracing::warn!("could not purge private-state trash: {error:#}"),
    }
    if let Err(error) = Worktrees::new(&manager).recover_worktrees().await {
        tracing::error!("Worktree recovery failed. Records remain: {error:#}");
    }
    if let Err(error) = manager.tmux.ensure_server().await {
        tracing::error!("could not prepare tmux server: {error:#}");
    }
    manager.sync_from_config().await;
    manager.prune_stale_grants().await;
    Ok(manager)
}
