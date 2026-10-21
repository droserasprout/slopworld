//! Carry prepared record and library writes through commit and publication.
//! The manager owns runtime effects and cancellation; record stores own documents.

use super::*;
use tokio::sync::OwnedMutexGuard;

pub(super) enum PreparedDisk {
    Records(Box<records::PreparedRecords>),
    Library(
        crate::storage::target::StorageBinding,
        Vec<crate::storage::transaction::Change>,
        crate::config::catalog::Revision,
    ),
}

impl PreparedDisk {
    pub(super) fn writes_library(&self) -> bool {
        match self {
            Self::Records(write) => write.writes_library(),
            Self::Library(..) => true,
        }
    }

    pub(super) async fn commit(&self, gate: &OwnedMutexGuard<()>) -> Result<()> {
        match self {
            Self::Records(write) => write.commit(gate).await,
            Self::Library(binding, changes, revision) => {
                anyhow::ensure!(
                    crate::config::catalog::revision(&binding.config.join("config.toml"))?
                        == *revision,
                    "library changed while preparing the edit; retry after reload"
                );
                crate::storage::transaction::commit_in_operation(binding, changes.clone(), gate)
                    .await
            }
        }
    }

    pub(super) fn publish(self) {
        match self {
            Self::Records(write) => write.publish(),
            Self::Library(..) => {}
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
        if matches!(mutation, ConfigMutation::Library)
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
            if matches!(mutation, ConfigMutation::ProjectReferences) {
                let mut plan = records.prepare(mutation, next)?;
                plan.attach_library(changes, revision);
                return Ok(PreparedDisk::Records(Box::new(plan)));
            }
            return Ok(PreparedDisk::Library(
                records.binding.clone(),
                changes,
                revision,
            ));
        }
        records
            .prepare(mutation, next)
            .map(|plan| PreparedDisk::Records(Box::new(plan)))
    }
}
