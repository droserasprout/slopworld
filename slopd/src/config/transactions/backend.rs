//! Carry prepared record and library writes through one journaled commit.
//! Record stores own document plans and accepted indexes; the manager owns runtime
//! effects, publication ordering, and the persistence gate.

use super::*;
use crate::storage::{target::StorageBinding, transaction};
use tokio::sync::OwnedMutexGuard;

pub(super) struct PreparedDisk {
    binding: StorageBinding,
    records: Option<Box<records::PreparedRecords>>,
    library: Option<PreparedLibrary>,
}

struct PreparedLibrary {
    changes: Vec<transaction::Change>,
    revision: crate::config::catalog::Revision,
}

impl PreparedDisk {
    pub(super) fn writes_library(&self) -> bool {
        self.library.is_some()
    }

    pub(super) fn attach_worktrees(&mut self, values: &[crate::worktrees::Worktree]) -> Result<()> {
        let records = self
            .records
            .as_mut()
            .ok_or_else(|| anyhow::anyhow!("relocation requires workspace record owner"))?;
        records.attach_worktrees(values)
    }

    pub(super) async fn commit(&self, gate: &OwnedMutexGuard<()>) -> Result<()> {
        if let Some(library) = &self.library {
            anyhow::ensure!(
                crate::config::catalog::revision(&self.binding.config.join("config.toml"))?
                    == library.revision,
                "library changed while preparing the edit; retry after reload"
            );
        }
        let mut changes = self
            .records
            .as_ref()
            .map_or_else(Vec::new, |plan| plan.changes());
        if let Some(library) = &self.library {
            // Project references and library files share one recovery journal.
            changes.extend(library.changes.iter().cloned());
        }
        transaction::commit_in_operation(&self.binding, changes, gate).await
    }

    pub(super) fn publish(self) {
        if let Some(records) = self.records {
            records.publish();
        }
    }
}

impl Transactions {
    pub(super) async fn prepare_config_disk(
        &self,
        mutation: ConfigMutation,
        old: &Config,
        next: &Config,
    ) -> Result<PreparedDisk> {
        let records = &self.records;
        let library_changed =
            serde_json::to_value(&old.library)? != serde_json::to_value(&next.library)?;
        let library = if matches!(mutation, ConfigMutation::Library)
            || (mutation.writes_library() && library_changed)
        {
            let revision =
                crate::config::catalog::revision(&records.binding.config.join("config.toml"))?;
            anyhow::ensure!(
                self.library_revision
                    .lock()
                    .unwrap_or_else(|e| e.into_inner())
                    .as_ref()
                    == Some(&revision),
                "library changed since acceptance; retry after reload"
            );
            let changes =
                crate::config::catalog::replacement_changes(&records.binding, &next.library)
                    .await?;
            Some(PreparedLibrary { changes, revision })
        } else {
            None
        };
        let record_plans = if matches!(mutation, ConfigMutation::Library) {
            None
        } else {
            Some(Box::new(records.prepare(mutation, next)?))
        };
        Ok(PreparedDisk {
            binding: records.binding.clone(),
            records: record_plans,
            library,
        })
    }
}
