//! Serialized configuration edits, disk reloads, and runtime publication.
//! crate::config owns data types and persistence; this module coordinates runtime changes.

mod backend;
mod cache;
mod mutation;
mod removal;
mod state;

pub(super) use mutation::ConfigMutation;

pub(crate) use state::ConfigState;

use super::super::*;
use crate::paths::disk_mtime;
#[cfg(test)]
use cache::reconcile_cache_links;

/// Changes whose callers need automatic reconciliation after publication.
enum ConfigRefresh {
    DocumentEdit,
    #[cfg(test)]
    DiskReload,
}

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

/// Validated configuration paired with the TOML document that preserves unknown fields.
#[cfg(test)]
struct DocumentChange {
    change: ConfigChange,
    document: toml::Value,
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

    pub(super) async fn update_cfg_if_changed_inner<T>(
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
        self.recover_config_backend(&persist).await?;
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
            #[cfg(test)]
            store.save(&self.cfg_path).await?;
            #[cfg(not(test))]
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
        self.owned_config_operation(
            matches!(prepared.mutation, ConfigMutation::Projects),
            Box::pin(async move { manager.commit_prepared_cfg_inner(prepared).await }),
        )
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
            None => self.load_worktrees().await?,
        };
        let links = cache::reconcile_store_links(&store, &old, &change.new)?;
        if let Err(error) = disk.commit(&self.cfg_path, &_persist).await {
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
        if let Some(text) = disk.root_text(&self.cfg_path) {
            self.mark_saved_document(text).await;
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

    /// Merge a JSON object into the current TOML document, then reconcile sessions.
    pub async fn patch_config(self: &Arc<Self>, patch: Value) -> Result<()> {
        self.session_operation(self.patch_config_inner(patch)).await
    }

    async fn patch_config_inner(self: &Arc<Self>, patch: Value) -> Result<()> {
        if self.record_backend().is_some() {
            return self.edit_record_settings(Some(patch), None).await;
        }
        #[cfg(not(test))]
        {
            bail!("record backend missing")
        }
        #[cfg(test)]
        {
            self.reload_if_changed().await;

            let persist = self.config_state.persist.lock().await;
            crate::config::fixtures::recover(&self.cfg_path).await?;
            let text = tokio::fs::read_to_string(&self.cfg_path)
                .await
                .with_context(|| format!("reading {}", self.cfg_path.display()))?;
            let mut document: toml::Value = toml::from_str(&text).context("parsing config.toml")?;
            let patch = json_to_toml(patch)?;
            if !patch.is_table() {
                bail!("config patch must be a JSON object");
            }
            merge_toml(&mut document, patch);

            let old = self.cfg.read().await.clone();
            let prepared = self
                .prepare_document_change(&old, &toml::to_string_pretty(&document)?)
                .await?;
            self.commit_document_change(&old, prepared).await?;
            drop(persist);
            self.finish_config_change(ConfigRefresh::DocumentEdit).await;
            Ok(())
        }
    }

    /// Validate and replace the TOML document, then reconcile sessions.
    pub async fn replace_config(self: &Arc<Self>, text: &str) -> Result<()> {
        self.session_operation(self.replace_config_inner(text))
            .await
    }

    async fn replace_config_inner(self: &Arc<Self>, text: &str) -> Result<()> {
        if self.record_backend().is_some() {
            return self.edit_record_settings(None, Some(text.to_owned())).await;
        }

        #[cfg(not(test))]
        {
            bail!("record backend missing")
        }
        #[cfg(test)]
        {
            let persist = self.config_state.persist.lock().await;
            crate::config::fixtures::recover(&self.cfg_path).await?;
            let old = self.cfg.read().await.clone();
            let prepared = self.prepare_document_change(&old, text).await?;
            self.commit_document_change(&old, prepared).await?;
            drop(persist);
            self.finish_config_change(ConfigRefresh::DocumentEdit).await;
            Ok(())
        }
    }

    async fn edit_record_settings(
        self: &Arc<Self>,
        patch: Option<Value>,
        text: Option<String>,
    ) -> Result<()> {
        let manager = self.clone();
        self.owned_session_operation(async move {
            let gate = manager.config_state.persist.clone().lock_owned().await;
            let records = manager.record_backend().context("record backend missing")?;
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
            manager.mark_saved_document(&prepared.text).await;
            let token = manager.publish_config(change).await;
            manager.update_endpoint(&token).await;
            drop(gate);
            manager
                .finish_config_change(ConfigRefresh::DocumentEdit)
                .await;
            Ok(())
        })
        .await
    }

    /// Prepare typed and TOML views together, preserving the library and redacted secret.
    /// The caller holds the persistence gate through preparation and commit.
    #[cfg(test)]
    async fn prepare_document_change(&self, old: &Config, text: &str) -> Result<DocumentChange> {
        let (mut parsed, mut document) = Config::parse_document(text)?;
        parsed.library = old.library.clone();
        if let Some(table) = document.as_table_mut() {
            table.remove("library");
        }
        if parsed.daemon.token == crate::config::TOKEN_REDACTED {
            restore_redacted_document_token(old, &mut document);
        }
        self.validate_worktree_config(old, &parsed).await?;
        let change = prepare_candidate(old, parsed)?;
        Ok(DocumentChange { change, document })
    }

    /// Save and publish under the caller's persistence gate; reconcile sessions after release.
    #[cfg(test)]
    async fn commit_document_change(&self, old: &Config, prepared: DocumentChange) -> Result<()> {
        let DocumentChange { change, document } = prepared;
        let links = reconcile_cache_links(&self.cfg_path, old, &change.new).await?;
        let text = toml::to_string_pretty(&document)?;
        if let Err(error) = self.persist_cfg_text(&text).await {
            return Err(links.rollback_error(error));
        }
        let endpoint_token = self.publish_config(change).await;
        self.update_endpoint(&endpoint_token).await;
        Ok(())
    }

    // Disk reloads.

    pub async fn reload_if_changed(self: &Arc<Self>) -> bool {
        if self.session_request_active() {
            return false;
        }
        self.session_operation(self.reload_if_changed_inner()).await
    }

    pub(super) async fn reload_if_stale(self: &Arc<Self>) {
        if self.record_backend().is_some() {
            self.reload_if_changed().await;
            #[cfg(test)]
            return;
        }

        #[cfg(test)]
        {
            let disk = disk_mtime(&self.cfg_path).await;
            let library = Config::library_stamp_for(&self.cfg_path);
            let stale = *self
                .config_state
                .cfg_mtime
                .lock()
                .unwrap_or_else(|error| error.into_inner())
                != disk
                || *self
                    .config_state
                    .library_mtime
                    .lock()
                    .unwrap_or_else(|error| error.into_inner())
                    != library;
            if stale {
                self.reload_if_changed().await;
            }
        }
    }

    async fn reload_if_changed_inner(self: &Arc<Self>) -> bool {
        if let Some(records) = self.record_backend() {
            return self.reload_record_libraries(&records).await;
        }
        #[cfg(not(test))]
        {
            false
        }
        #[cfg(test)]
        {
            // Serialize disk reads and publication with saves; release before reconciliation.
            let persist = self.config_state.persist.lock().await;
            let disk = disk_mtime(&self.cfg_path).await;
            let library_disk = Config::library_stamp_for(&self.cfg_path);
            {
                let cfg_seen = self
                    .config_state
                    .cfg_mtime
                    .lock()
                    .unwrap_or_else(|error| error.into_inner());
                let library_seen = self
                    .config_state
                    .library_mtime
                    .lock()
                    .unwrap_or_else(|error| error.into_inner());
                if *cfg_seen == disk && *library_seen == library_disk {
                    return false;
                }
            }

            let parsed = match Config::load(&self.cfg_path).await {
                Ok(cfg) => cfg,
                Err(e) => {
                    tracing::warn!(
                        "config changed on disk but is invalid, keeping the old one: {e:#}"
                    );
                    return false;
                }
            };
            let old = self.cfg.read().await.clone();
            if let Err(error) = self.validate_worktree_config(&old, &parsed).await {
                tracing::warn!("config worktree validation failed: {error:#}");
                return false;
            }
            let change = match prepare_candidate(&old, parsed) {
                Ok(change) => change,
                Err(e) => {
                    tracing::warn!(
                        "config changed on disk but is invalid, keeping the old one: {e:#}"
                    );
                    return false;
                }
            };
            if let Err(error) = reconcile_cache_links(&self.cfg_path, &old, &change.new).await {
                tracing::warn!("config cache links could not be reconciled: {error:#}");
                return false;
            }
            tracing::info!("config changed on disk, reloading");
            let endpoint_token = self.publish_config(change).await;
            // Record disk stamps only after accepted contents are visible in memory.
            *self
                .config_state
                .cfg_mtime
                .lock()
                .unwrap_or_else(|error| error.into_inner()) = disk;
            *self
                .config_state
                .library_mtime
                .lock()
                .unwrap_or_else(|error| error.into_inner()) = library_disk;
            self.update_endpoint(&endpoint_token).await;
            drop(persist);
            self.finish_config_change(ConfigRefresh::DiskReload).await;
            true
        }
    }

    // Shared commit steps: persist, publish, then reconcile and notify.

    #[cfg(test)]
    async fn persist_cfg_text(&self, text: &str) -> Result<()> {
        Config::save_text(&self.cfg_path, text).await?;
        self.mark_saved_document(text).await;
        Ok(())
    }

    // Sample revisions BEFORE verification. A write after verification must remain
    // unseen; a write before verification is accepted only when contents match.
    async fn mark_saved_document(&self, expected: &str) {
        let stamp = disk_mtime(&self.cfg_path).await;
        let matches = tokio::fs::read_to_string(&self.cfg_path)
            .await
            .is_ok_and(|actual| actual == expected);
        *self
            .config_state
            .cfg_mtime
            .lock()
            .unwrap_or_else(|error| error.into_inner()) = matches.then_some(stamp).flatten();
        // Document edits never accept a new library catalog revision.
    }

    async fn mark_saved_library(&self, expected: &Config) {
        let anchor = self.record_backend().map_or_else(
            || self.cfg_path.clone(),
            |records| records.binding.config.join("config.toml"),
        );
        let revision = crate::config::catalog::revision(&anchor).ok();
        let stamp = Config::library_stamp_for(&anchor);
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
        *self
            .config_state
            .library_mtime
            .lock()
            .unwrap_or_else(|error| error.into_inner()) = matches.then_some(stamp).flatten();
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
    async fn finish_config_change(self: &Arc<Self>, refresh: ConfigRefresh) {
        match refresh {
            ConfigRefresh::DocumentEdit => {
                self.sync_from_config().await;
                self.announce_projects().await;
                self.announce_library().await;
            }
            #[cfg(test)]
            ConfigRefresh::DiskReload => {
                self.sync_from_config().await;
                self.announce_sessions().await;
                self.announce_projects().await;
                self.announce_library().await;
            }
        }
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
fn restore_redacted_document_token(old: &Config, document: &mut toml::Value) {
    if let Some(daemon) = document
        .get_mut("daemon")
        .and_then(toml::Value::as_table_mut)
    {
        daemon.insert(
            "token".into(),
            toml::Value::String(old.daemon.token.clone()),
        );
    }
}

#[cfg(test)]
mod tests;

#[cfg(test)]
pub(super) mod record_tests;

#[cfg(test)]
mod workspace_tests;
