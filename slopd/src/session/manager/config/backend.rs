//! Select the current persistence layout and carry prepared writes to commit.
//! The manager owns runtime effects and cancellation; record stores own documents.

use super::*;
use tokio::sync::OwnedMutexGuard;

pub(super) enum PreparedDisk {
    Inline(crate::config::legacy::PreparedWrite),
    #[cfg(test)]
    Records(records::PreparedRecords),
}

impl PreparedDisk {
    pub(super) async fn commit(&self, path: &Path, _gate: &OwnedMutexGuard<()>) -> Result<()> {
        match self {
            Self::Inline(write) => write.commit(path).await,
            #[cfg(test)]
            Self::Records(write) => write.commit(_gate).await,
        }
    }

    pub(super) fn root_text(&self, path: &Path) -> Option<&str> {
        match self {
            Self::Inline(write) => write.root_text(path),
            #[cfg(test)]
            Self::Records(_) => None,
        }
    }

    pub(super) fn publish(self) {
        match self {
            Self::Inline(_) => {}
            #[cfg(test)]
            Self::Records(write) => write.publish(),
        }
    }
}

impl Manager {
    pub(super) async fn recover_config_backend(&self, _gate: &OwnedMutexGuard<()>) -> Result<()> {
        #[cfg(test)]
        if let Some(records) = self.record_backend() {
            return records.recover(_gate).await;
        }
        crate::config::legacy::recover(&self.cfg_path).await
    }

    pub(super) async fn prepare_config_disk(
        &self,
        mutation: ConfigMutation,
        next: &Config,
    ) -> Result<PreparedDisk> {
        #[cfg(test)]
        if let Some(records) = self.record_backend() {
            return records.prepare(mutation, next).map(PreparedDisk::Records);
        }
        crate::config::legacy::prepare(
            next,
            &self.cfg_path,
            mutation.writes_root(),
            mutation.writes_library(),
        )
        .await
        .map(PreparedDisk::Inline)
    }
}

// Selectable only in fixtures until all stores and offline migration are ready.
#[cfg(test)]
pub(super) mod records;
