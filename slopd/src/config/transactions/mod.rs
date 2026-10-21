//! Configuration transaction coordination, independent of live sessions and transport.
//! Storage owns documents and undo recovery. The runtime adapter owns session validation,
//! atomic authentication publication, and endpoint updates. Callers must retain their
//! operation guards in an owned task during recovery, commit, rollback, and publication.

mod backend;
mod cache;
mod mutation;
pub(crate) mod records;

use crate::config::Config;
use crate::config::validate_config;
use anyhow::{Result, bail};
pub(crate) use mutation::ConfigMutation;
use std::sync::{Arc, Mutex};

/// Runtime effects execute while the transaction's persistence gate remains held.
/// Implementations must publish credentials and their revocation epoch atomically.
/// Endpoint updates remain ordered with persisted commits. Each owner must always use
/// the runtime associated with its accepted records.
pub(crate) trait Runtime: Sync {
    fn snapshot(&self) -> impl std::future::Future<Output = Config> + Send;
    fn validate(
        &self,
        old: &Config,
        new: &Config,
    ) -> impl std::future::Future<Output = Result<()>> + Send;
    fn worktrees(&self) -> crate::worktrees::Store;
    fn publish(&self, change: ConfigChange) -> impl std::future::Future<Output = String> + Send;
    fn update_endpoint(&self, token: &str) -> impl std::future::Future<Output = ()> + Send;
}

pub(crate) struct Transactions {
    pub(crate) records: Arc<records::Records>,
    pub(crate) persist: Arc<tokio::sync::Mutex<()>>,
    pub(crate) library_revision: Mutex<Option<crate::config::catalog::Revision>>,
    #[cfg(test)]
    pub(crate) commit_pause: Mutex<Option<(Arc<tokio::sync::Barrier>, Arc<tokio::sync::Notify>)>>,
}

/// Validated candidate and authentication changes.
pub(crate) struct ConfigChange {
    pub(crate) new: Config,
    pub(crate) root_token_changed: bool,
}

pub(crate) struct PreparedConfigChange {
    change: ConfigChange,
    disk: backend::PreparedDisk,
    worktrees: Option<crate::worktrees::Store>,
    // Prevent stale candidates, including across a tmux rename before commit.
    _persist: tokio::sync::OwnedMutexGuard<()>,
}

pub(crate) enum SettingsEdit {
    Patch(toml::Value),
    Replace(String),
}

impl Transactions {
    pub(crate) fn new(
        records: Arc<records::Records>,
        library_revision: Option<crate::config::catalog::Revision>,
    ) -> Self {
        Self {
            records,
            library_revision: Mutex::new(library_revision),
            persist: Arc::new(tokio::sync::Mutex::new(())),
            #[cfg(test)]
            commit_pause: Mutex::new(None),
        }
    }

    /// Worktree lifecycle callers retain their own session/worktree guards.
    pub(crate) async fn save_worktrees(&self, store: &crate::worktrees::Store) -> Result<()> {
        let gate = self.persist.clone().lock_owned().await;
        self.records
            .save_worktrees(store.worktrees.clone(), &gate)
            .await
    }

    /// Validate a private snapshot and retain the persistence lock until commit or drop.
    pub(crate) async fn prepare<T>(
        &self,
        runtime: &impl Runtime,
        mutation: ConfigMutation,
        update: impl FnOnce(&mut Config) -> Result<(T, bool)>,
    ) -> Result<(T, Option<PreparedConfigChange>)> {
        let persist = self.persist.clone().lock_owned().await;
        self.records.recover(&persist).await?;
        let old = runtime.snapshot().await;
        let mut candidate = old.clone();
        let (result, changed) = update(&mut candidate)?;
        if !changed {
            return Ok((result, None));
        }

        mutation.validate(&old, &candidate)?;
        runtime.validate(&old, &candidate).await?;
        let change = prepare_candidate(&old, candidate)?;
        let disk = self
            .prepare_config_disk(mutation, &old, &change.new)
            .await?;
        Ok((
            result,
            Some(PreparedConfigChange {
                change,
                disk,
                worktrees: None,
                _persist: persist,
            }),
        ))
    }

    /// Intent is already durable and Git has moved. The record undo baseline is
    /// the relocating state, never a ready record pointing at the old checkout.
    pub(crate) async fn commit_project_relocation(
        &self,
        runtime: &impl Runtime,
        mut prepared: PreparedConfigChange,
        store: &crate::worktrees::Store,
    ) -> Result<()> {
        if let backend::PreparedDisk::Records(plan) = &mut prepared.disk {
            plan.attach_worktrees(&store.worktrees)?;
        } else {
            bail!("relocation requires workspace record owner");
        }
        prepared.worktrees = Some(store.clone());
        self.commit(runtime, prepared).await
    }

    /// Run inside the caller's owned operation, retaining all runtime guards until return.
    pub(crate) async fn commit(
        &self,
        runtime: &impl Runtime,
        prepared: PreparedConfigChange,
    ) -> Result<()> {
        anyhow::ensure!(
            Arc::ptr_eq(
                tokio::sync::OwnedMutexGuard::mutex(&prepared._persist),
                &self.persist
            ),
            "prepared configuration belongs to another transaction owner"
        );
        let PreparedConfigChange {
            change,
            disk,
            worktrees,
            _persist,
        } = prepared;
        let old = runtime.snapshot().await;
        let store = match worktrees {
            Some(store) => store,
            None => runtime.worktrees(),
        };
        let links = cache::reconcile_store_links(&store, &old, &change.new)?;
        if let Err(error) = disk.commit(&_persist).await {
            return Err(links.rollback_error(error));
        }
        #[cfg(test)]
        {
            let pause = self
                .commit_pause
                .lock()
                .unwrap_or_else(|error| error.into_inner())
                .take();
            if let Some((committed, release)) = pause {
                committed.wait().await;
                release.notified().await;
            }
        }
        if disk.writes_library() {
            self.mark_saved_library(&change.new).await;
        }
        disk.publish();
        let token = runtime.publish(change).await;
        runtime.update_endpoint(&token).await;
        drop(_persist);
        // The mutation caller owns session reconciliation and announcements.
        Ok(())
    }

    /// Read-request shortcut only; pending recovery always requires the owned reload path.
    pub(crate) fn library_is_current(&self) -> bool {
        let records = &self.records;
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
                .library_revision
                .lock()
                .unwrap_or_else(|e| e.into_inner())
                == revision
        {
            return true;
        }
        false
    }

    async fn mark_saved_library(&self, expected: &Config) {
        let anchor = self.records.binding.config.join("config.toml");
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
                .library_revision
                .lock()
                .unwrap_or_else(|e| e.into_inner()) = revision;
        }
        // Library edits cannot accept an unseen root document revision.
    }

    pub(crate) async fn reload(&self, runtime: &impl Runtime) -> bool {
        let records = &self.records;
        let gate = self.persist.clone().lock_owned().await;
        // A failed rollback can leave valid-looking but uncommitted catalog files.
        // Recover before sampling revisions, including the unchanged fast path.
        if let Err(error) = records.recover(&gate).await {
            tracing::warn!(
                "workspace recovery failed; keeping accepted library catalog: {error:#}"
            );
            return false;
        }
        let anchor = records.binding.config.join("config.toml");
        let Ok(stamp) = crate::config::catalog::revision(&anchor) else {
            return false;
        };
        if self
            .library_revision
            .lock()
            .unwrap_or_else(|e| e.into_inner())
            .as_ref()
            == Some(&stamp)
        {
            return false;
        }
        let old = runtime.snapshot().await;
        let result = async {
            let mut next = old.clone();
            next.library = Config::load_library_for(&anchor).await?;
            prepare_candidate(&old, next)
        }
        .await;
        let change = match result {
            Ok(change) => change,
            Err(error) => {
                tracing::warn!(
                    "library changed on disk but is invalid, keeping accepted catalog: {error:#}"
                );
                return false;
            }
        };
        if crate::config::catalog::revision(&anchor).ok().as_ref() != Some(&stamp) {
            return false;
        }
        runtime.publish(change).await;
        *self
            .library_revision
            .lock()
            .unwrap_or_else(|e| e.into_inner()) = Some(stamp);
        drop(gate);
        true
    }
    /// Settings never accept workspace membership or an unrelated library revision.
    pub(crate) async fn edit_settings(
        &self,
        runtime: &impl Runtime,
        edit: SettingsEdit,
    ) -> Result<()> {
        let gate = self.persist.clone().lock_owned().await;
        let records = &self.records;
        records.recover(&gate).await?;
        let old = runtime.snapshot().await;
        let prepared = match edit {
            SettingsEdit::Patch(patch) => {
                let current = tokio::fs::read_to_string(&records.binding.settings).await?;
                crate::config::settings::document::patch(&old, &current, patch)?
            }
            SettingsEdit::Replace(text) => crate::config::settings::document::replace(&old, &text)?,
        };
        runtime.validate(&old, &prepared.candidate).await?;
        let change = prepare_candidate(&old, prepared.candidate)?;
        crate::storage::transaction::commit_in_operation(
            &records.binding,
            vec![crate::storage::transaction::Change {
                target: crate::storage::target::Target::Settings,
                mutation: crate::storage::transaction::Mutation::Replace(prepared.text),
            }],
            &gate,
        )
        .await?;
        let token = runtime.publish(change).await;
        runtime.update_endpoint(&token).await;
        Ok(())
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
