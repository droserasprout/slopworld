//! Daemon workspace storage and mutation serialization, constructed by application startup.
//! Transactions own persistence; session adapters retain live-operation guards before
//! taking the worktree gate, then the persistence gate. Endpoint publication stays
//! inside the configuration transaction so credentials follow committed settings.
use crate::config::transactions::{Transactions, records::Records};
use crate::storage::layout::LoadedStorage;
use std::path::{Path, PathBuf};
use std::sync::Arc;
#[cfg(test)]
use std::sync::Mutex;
#[cfg(test)]
use tokio::sync::Notify;

pub(crate) struct Workspace {
    pub(crate) configuration: Transactions,
    endpoint_path: PathBuf,
    pub(crate) worktrees: WorktreeState,
}

#[derive(Default)]
pub(crate) struct WorktreeState {
    pub(crate) mutation: tokio::sync::Mutex<()>,
    #[cfg(test)]
    pub(crate) relocation_pause: Mutex<Option<(Arc<Notify>, Arc<Notify>)>>,
}

impl Workspace {
    pub(crate) fn new(storage: LoadedStorage, endpoint_path: PathBuf) -> Self {
        let LoadedStorage {
            binding,
            stores,
            library_revision,
        } = storage;
        Self::from_transactions(
            Transactions::new(Records::new(binding, stores), library_revision),
            endpoint_path,
        )
    }

    pub(crate) fn from_transactions(configuration: Transactions, endpoint_path: PathBuf) -> Self {
        Self {
            configuration,
            endpoint_path,
            worktrees: WorktreeState::default(),
        }
    }

    /// Startup handoff; runtime receives locations and accepted reads, not transaction access.
    pub(crate) fn session_storage(self: &Arc<Self>) -> crate::session::RuntimeStorage {
        crate::session::RuntimeStorage {
            identities: super::WorkspaceIdentities::new(&self.configuration.records.binding),
            worktrees: self.configuration.records.worktree_catalog(),
            activity_cache: crate::activity::ActivityCache::load(crate::activity::cache_path(
                self.settings_path(),
            )),
            #[cfg(test)]
            workspace: self.clone(),
        }
    }

    pub(crate) fn settings_path(&self) -> &Path {
        &self.configuration.records.binding.settings
    }

    pub(crate) async fn update_endpoint(&self, token: &str) {
        if let Err(error) = crate::endpoint::update_token(&self.endpoint_path, token).await {
            tracing::warn!(
                "The daemon accepted the config but could not update the endpoint descriptor: {error:#}"
            );
        }
    }
}
