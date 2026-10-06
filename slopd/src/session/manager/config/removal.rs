//! Commit one configured session deletion without rewriting unrelated catalogs.
//! TODO(storage cutover): retire this inline-document shortcut with the legacy adapter.
//! Persistence owns document editing; the session caller owns stop, trash, and publication.

use super::*;

impl Manager {
    pub(in crate::session::manager) async fn remove_configured_session(
        self: &Arc<Self>,
        name: &str,
    ) -> Result<()> {
        let manager = self.clone();
        let name = name.to_owned();
        self.owned_session_operation(
            async move { manager.remove_configured_session_inner(&name).await },
        )
        .await
    }

    async fn remove_configured_session_inner(self: &Arc<Self>, name: &str) -> Result<()> {
        self.session_operation(async {
            #[cfg(test)]
            if self.record_backend().is_some() {
                return self
                    .update_cfg(ConfigMutation::Agents, |cfg| {
                        cfg.sessions.retain(|session| session.name != name);
                        Ok(())
                    })
                    .await;
            }
            let persist = self.config_state.persist.lock().await;
            let accepted = *self
                .config_state
                .cfg_mtime
                .lock()
                .unwrap_or_else(|error| error.into_inner());
            if let Some(text) = Config::session_removal_text(&self.cfg_path, name, accepted).await?
            {
                // Deletion cannot introduce worktree/mount references or change
                // root credentials. Commit before publishing; leave library stamps alone.
                self.persist_cfg_text(&text).await?;
                self.invalidate_session(name).await;
                self.cfg
                    .write()
                    .await
                    .sessions
                    .retain(|session| session.name != name);
                self.signals.maintenance_wake.notify_waiters();
                return Ok(());
            }
            // The fallback acquires its own persistence guard. Keep the session
            // boundary across the handoff so another mutation cannot interleave.
            drop(persist);
            self.update_cfg(ConfigMutation::Agents, |cfg| {
                cfg.sessions.retain(|session| session.name != name);
                Ok(())
            })
            .await
        })
        .await
    }
}

#[cfg(test)]
#[path = "removal_tests.rs"]
mod tests;
