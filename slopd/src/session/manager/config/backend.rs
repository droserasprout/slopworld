//! Select the current persistence layout and carry prepared writes to commit.
//! The manager owns runtime effects and cancellation; record stores own documents.

use super::*;
use tokio::sync::OwnedMutexGuard;

pub(super) enum PreparedDisk {
    #[cfg(test)]
    Inline(crate::config::fixtures::PreparedWrite),
    Records(records::PreparedRecords),
    Library(
        crate::storage::target::StorageBinding,
        Vec<crate::storage::transaction::Change>,
        crate::config::catalog::Revision,
    ),
}

impl PreparedDisk {
    pub(super) async fn commit(&self, _path: &Path, _gate: &OwnedMutexGuard<()>) -> Result<()> {
        match self {
            #[cfg(test)]
            Self::Inline(write) => write.commit(_path).await,
            Self::Records(write) => write.commit(_gate).await,
            Self::Library(binding, changes, revision) => {
                anyhow::ensure!(
                    crate::config::catalog::revision(&binding.config.join("config.toml"))?
                        == *revision,
                    "library changed while preparing the edit; retry after reload"
                );
                crate::storage::transaction::commit_in_operation(binding, changes.clone(), _gate)
                    .await
            }
        }
    }

    pub(super) fn root_text(&self, _path: &Path) -> Option<&str> {
        match self {
            #[cfg(test)]
            Self::Inline(write) => write.root_text(_path),
            Self::Records(_) | Self::Library(..) => None,
        }
    }

    pub(super) fn publish(self) {
        match self {
            #[cfg(test)]
            Self::Inline(_) => {}
            Self::Records(write) => write.publish(),
            Self::Library(..) => {}
        }
    }
}

impl Manager {
    pub(super) async fn recover_config_backend(&self, _gate: &OwnedMutexGuard<()>) -> Result<()> {
        if let Some(records) = self.record_backend() {
            return records.recover(_gate).await;
        }
        #[cfg(test)]
        {
            crate::config::fixtures::recover(&self.cfg_path).await
        }
        #[cfg(not(test))]
        {
            bail!("record backend missing")
        }
    }

    pub(super) async fn prepare_config_disk(
        &self,
        mutation: ConfigMutation,
        next: &Config,
    ) -> Result<PreparedDisk> {
        if let Some(records) = self.record_backend() {
            if matches!(mutation, ConfigMutation::Library) {
                let revision =
                    crate::config::catalog::revision(&records.binding.config.join("config.toml"))?;
                anyhow::ensure!(
                    self.config_state
                        .library_revision
                        .lock()
                        .unwrap_or_else(|e| e.into_inner())
                        .as_ref()
                        == Some(&revision),
                    "library changed since acceptance; retry after reload"
                );
                let dirs = Config::library_dirs_for(&records.binding.config.join("config.toml"));
                let library = crate::config::catalog::prepare_library(&dirs, &next.library)?;
                let files = crate::config::catalog::replacement_changes(&dirs, library).await?;
                let changes = files
                    .into_iter()
                    .map(|(path, text)| {
                        Ok(crate::storage::transaction::Change {
                            target: crate::storage::target::Target::Config(
                                path.strip_prefix(&records.binding.config)?.to_owned(),
                            ),
                            mutation: match text {
                                Some(text) => crate::storage::transaction::Mutation::Replace(text),
                                None => crate::storage::transaction::Mutation::Retire,
                            },
                        })
                    })
                    .collect::<Result<Vec<_>>>()?;
                return Ok(PreparedDisk::Library(
                    records.binding.clone(),
                    changes,
                    revision,
                ));
            }
            return records.prepare(mutation, next).map(PreparedDisk::Records);
        }
        #[cfg(test)]
        {
            crate::config::fixtures::prepare(
                next,
                &self.cfg_path,
                mutation.writes_root(),
                mutation.writes_library(),
            )
            .await
            .map(PreparedDisk::Inline)
        }
        #[cfg(not(test))]
        {
            bail!("record backend missing")
        }
    }
}

// Production workspace persistence and accepted indexes.
pub(super) mod records;
