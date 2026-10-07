//! Serialized configuration edits, disk reloads, and runtime publication.
//! crate::config owns data types and persistence; this module coordinates runtime changes.

pub(super) mod backend;
mod cache;
mod mutation;
mod removal;
mod state;

pub(super) use mutation::ConfigMutation;

pub(crate) use state::ConfigState;

use super::super::*;
/// Validated candidate and authentication changes.
struct ConfigChange {
    new: Config,
    root_token_changed: bool,
}

pub(super) struct PreparedConfigChange {
    change: ConfigChange,
    disk: backend::PreparedDisk,
    mutation: ConfigMutation,
    worktrees: Option<crate::worktrees::Store>,
    // Prevent stale candidates, including across a tmux rename before commit.
    _persist: tokio::sync::OwnedMutexGuard<()>,
}

impl Manager {
    pub async fn config(&self) -> Config {
        self.cfg.read().await.clone()
    }

    // Structured mutations. Callers own session reconciliation and announcements.

    /// Serialize snapshot edits, saving before publication without holding the cfg lock.
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
        self.session_operation(self.update_cfg_if_changed_inner(mutation, update))
            .await
    }

    async fn update_cfg_if_changed_inner<T>(
        self: &Arc<Self>,
        mutation: ConfigMutation,
        update: impl FnOnce(&mut Config) -> Result<(T, bool)>,
    ) -> Result<T> {
        let (result, change) = self.prepare_cfg_change(mutation, update).await?;
        let Some(change) = change else {
            return Ok(result);
        };
        self.commit_prepared_cfg(change).await?;
        Ok(result)
    }

    /// Validate a private snapshot and retain the persistence lock until commit or drop.
    pub(super) async fn prepare_cfg_change<T>(
        &self,
        mutation: ConfigMutation,
        update: impl FnOnce(&mut Config) -> Result<(T, bool)>,
    ) -> Result<(T, Option<PreparedConfigChange>)> {
        let persist = self.config_state.persist.clone().lock_owned().await;
        self.config_state.records.recover(&persist).await?;
        let old = self.cfg.read().await.clone();
        let mut candidate = old.clone();
        let (result, changed) = update(&mut candidate)?;
        if !changed {
            return Ok((result, None));
        }

        mutation.validate(&old, &candidate)?;
        self.validate_worktree_config(&old, &candidate).await?;
        let change = prepare_candidate(&old, candidate)?;
        let disk = self.prepare_config_disk(mutation, &change.new).await?;
        Ok((
            result,
            Some(PreparedConfigChange {
                change,
                disk,
                mutation,
                worktrees: None,
                _persist: persist,
            }),
        ))
    }

    /// Intent is already durable and Git has moved. The record undo baseline is
    /// the relocating state, never a ready record pointing at the old checkout.
    pub(super) async fn commit_project_relocation(
        self: &Arc<Self>,
        mut prepared: PreparedConfigChange,
        store: &crate::worktrees::Store,
    ) -> Result<()> {
        if let backend::PreparedDisk::Records(plan) = &mut prepared.disk {
            plan.attach_worktrees(store.worktrees.clone())?;
        } else {
            bail!("relocation requires workspace record owner");
        }
        prepared.worktrees = Some(store.clone());
        self.commit_prepared_cfg(prepared).await
    }

    pub(super) async fn commit_prepared_cfg(
        self: &Arc<Self>,
        prepared: PreparedConfigChange,
    ) -> Result<()> {
        let manager = self.clone();
        self.owned_session_operation(Box::pin(async move {
            manager.commit_prepared_cfg_inner(prepared).await
        }))
        .await
    }

    async fn commit_prepared_cfg_inner(&self, prepared: PreparedConfigChange) -> Result<()> {
        let PreparedConfigChange {
            change,
            disk,
            mutation,
            worktrees,
            _persist,
        } = prepared;
        let old = self.cfg.read().await.clone();
        let store = match worktrees {
            Some(store) => store,
            None => self.worktree_records(),
        };
        let links = cache::reconcile_store_links(&store, &old, &change.new)?;
        if let Err(error) = disk.commit(&_persist).await {
            return Err(links.rollback_error(error));
        }
        #[cfg(test)]
        {
            let pause = self
                .config_state
                .commit_pause
                .lock()
                .unwrap_or_else(|error| error.into_inner())
                .take();
            if let Some((committed, release)) = pause {
                committed.wait().await;
                release.notified().await;
            }
        }
        if mutation.writes_library() {
            self.mark_saved_library(&change.new).await;
        }
        disk.publish();
        let endpoint_token = self.publish_config(change).await;
        // Keep endpoint token writes ordered with configuration commits.
        self.update_endpoint(&endpoint_token).await;
        drop(_persist);
        // The mutation caller owns session reconciliation and announcements.
        Ok(())
    }

    // Document edits preserve unmodeled fields and keep the library in its own store.

    /// Merge settings while preserving accepted workspace records.
    pub async fn patch_config(self: &Arc<Self>, patch: Value) -> Result<()> {
        self.edit_record_settings(Some(patch), None).await
    }

    pub async fn replace_config(self: &Arc<Self>, text: &str) -> Result<()> {
        self.edit_record_settings(None, Some(text.to_owned())).await
    }

    async fn edit_record_settings(
        self: &Arc<Self>,
        patch: Option<Value>,
        text: Option<String>,
    ) -> Result<()> {
        let manager = self.clone();
        self.owned_session_operation(async move {
            let gate = manager.config_state.persist.clone().lock_owned().await;
            let records = &manager.config_state.records;
            records.recover(&gate).await?;
            let old = manager.config().await;
            let prepared = if let Some(patch) = patch {
                let current = tokio::fs::read_to_string(&manager.cfg_path).await?;
                crate::config::settings::document::patch(&old, &current, json_to_toml(patch)?)?
            } else {
                crate::config::settings::document::replace(
                    &old,
                    text.as_deref().context("missing replacement")?,
                )?
            };
            manager
                .validate_worktree_config(&old, &prepared.candidate)
                .await?;
            let change = prepare_candidate(&old, prepared.candidate)?;
            crate::storage::transaction::commit_in_operation(
                &records.binding,
                vec![crate::storage::transaction::Change {
                    target: crate::storage::target::Target::Settings,
                    mutation: crate::storage::transaction::Mutation::Replace(prepared.text.clone()),
                }],
                &gate,
            )
            .await?;
            let token = manager.publish_config(change).await;
            manager.update_endpoint(&token).await;
            drop(gate);
            manager.finish_config_change().await;
            Ok(())
        })
        .await
    }

    pub async fn reload_if_changed(self: &Arc<Self>) -> bool {
        if self.session_request_active() {
            return false;
        }
        self.reload_record_libraries().await
    }

    pub(super) async fn reload_if_stale(self: &Arc<Self>) {
        let records = &self.config_state.records;
        let revision =
            crate::config::catalog::revision(&records.binding.config.join("config.toml")).ok();
        // This is only a read-request shortcut, never publication. A pending
        // undo journal always goes through recovery before revision acceptance.
        if records
            .binding
            .journal()
            .is_ok_and(|path| matches!(path.try_exists(), Ok(false)))
            && revision.is_some()
            && *self
                .config_state
                .library_revision
                .lock()
                .unwrap_or_else(|e| e.into_inner())
                == revision
        {
            return;
        }
        self.reload_if_changed().await;
    }

    async fn mark_saved_library(&self, expected: &Config) {
        let anchor = self.config_state.records.binding.config.join("config.toml");
        let revision = crate::config::catalog::revision(&anchor).ok();
        let matches = match Config::load_library_for(&anchor).await {
            Ok(mut actual) => {
                let mut expected = expected.library.clone();
                // File discovery orders by path; accepted API insertion order
                // is not an external edit. Names are unique across the catalog.
                actual.sort_by(|a, b| a.name.cmp(&b.name));
                expected.sort_by(|a, b| a.name.cmp(&b.name));
                match (serde_json::to_value(actual), serde_json::to_value(expected)) {
                    (Ok(actual), Ok(expected)) => actual == expected,
                    _ => false,
                }
            }
            Err(_) => false,
        };
        if matches && crate::config::catalog::revision(&anchor).ok() == revision {
            *self
                .config_state
                .library_revision
                .lock()
                .unwrap_or_else(|e| e.into_inner()) = revision;
        }
        // Library edits cannot accept an unseen root document revision.
    }

    async fn publish_config(&self, change: ConfigChange) -> String {
        let endpoint_token = change.new.daemon.token.clone();

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

        *self.cfg.write().await = change.new;
        if change.root_token_changed {
            self.invalidate_auth(crate::session::AuthChange::RootTokenChanged);
        }
        self.signals.maintenance_wake.notify_waiters();

        endpoint_token
    }

    async fn update_endpoint(&self, token: &str) {
        if let Err(e) = crate::endpoint::update_token(&self.endpoint_path, token).await {
            tracing::warn!(
                "The daemon accepted the config but could not update the endpoint descriptor: {e:#}"
            );
        }
    }

    /// Called after releasing the persistence gate. Structured edits let their caller reconcile.
    async fn finish_config_change(self: &Arc<Self>) {
        self.sync_from_config().await;
        self.announce_projects().await;
        self.announce_library().await;
    }
}

fn prepare_candidate(old: &Config, mut new: Config) -> Result<ConfigChange> {
    if new.daemon.token == crate::config::TOKEN_REDACTED {
        new.daemon.token = old.daemon.token.clone();
    }
    let root_token_changed = old.daemon.token != new.daemon.token;
    validate_config(&new)?;
    Ok(ConfigChange {
        new,
        root_token_changed,
    })
}

#[cfg(test)]
mod tests;

#[cfg(test)]
pub(super) mod record_tests;

#[cfg(test)]
mod workspace_tests;
