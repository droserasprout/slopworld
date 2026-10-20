//! Accepted configuration and credential publication for the application.
//! Session adapters retain identity guards and validate live constraints; transactions
//! own persistence. Readers share this owner's single accepted configuration lock.
use crate::config::Config;
use anyhow::Result;
use std::sync::{
    Arc,
    atomic::{AtomicU64, Ordering},
};
use tokio::sync::{RwLock, broadcast};

/// Internal notifications for rechecking connected clients’ credentials.
#[derive(Debug, Clone)]
pub(crate) enum AuthChange {
    GrantsRevoked,
    RootTokenChanged,
}
/// One accepted settings lock, persisted grants, and credential invalidation epochs.
pub(crate) struct ConfigAuthority {
    config: Arc<RwLock<Config>>,
    grants: RwLock<crate::grant::Grants>,
    auth_generation: AtomicU64,
    root_generation: AtomicU64,
    changes: broadcast::Sender<AuthChange>,
}

impl ConfigAuthority {
    pub(crate) fn new(config: Config, grants: crate::grant::Grants) -> Self {
        let (changes, _) = broadcast::channel(16);
        Self {
            config: Arc::new(RwLock::new(config)),
            grants: RwLock::new(grants),
            auth_generation: AtomicU64::new(0),
            root_generation: AtomicU64::new(0),
            changes,
        }
    }

    pub(crate) async fn read_config(&self) -> tokio::sync::RwLockReadGuard<'_, Config> {
        self.config.read().await
    }

    pub(crate) fn daemon_settings(&self) -> crate::config::DaemonSettings {
        crate::config::DaemonSettings::new(self.config.clone())
    }

    /// Caller retains the transaction gate and session identity guard through publication.
    pub(crate) async fn publish_config(
        &self,
        change: crate::config::transactions::ConfigChange,
    ) -> String {
        let token = change.new.daemon.token.clone();
        let mut config = self.config.write().await;
        *config = change.new;
        if change.root_token_changed {
            self.invalidate_auth(AuthChange::RootTokenChanged);
        }
        token
    }

    #[cfg(test)]
    pub(crate) async fn write(&self) -> tokio::sync::RwLockWriteGuard<'_, Config> {
        self.config.write().await
    }

    pub(crate) fn root_generation(&self) -> u64 {
        self.root_generation.load(Ordering::Acquire)
    }

    pub(crate) fn auth_generation(&self) -> u64 {
        self.auth_generation.load(Ordering::Acquire)
    }

    pub(crate) fn auth_changes(&self) -> broadcast::Receiver<AuthChange> {
        self.changes.subscribe()
    }

    fn invalidate_auth(&self, change: AuthChange) {
        if matches!(change, AuthChange::RootTokenChanged) {
            self.root_generation.fetch_add(1, Ordering::AcqRel);
        }
        self.auth_generation.fetch_add(1, Ordering::AcqRel);
        drop(self.changes.send(change));
    }

    /// Admission samples the credential epoch before and after resolution. A token
    /// cloned before a concurrent rotation must not authorize the newer epoch.
    pub(crate) async fn resolve_cap(&self, presented: Option<&str>) -> Option<crate::grant::Cap> {
        let root = self.config.read().await.daemon.token.clone();
        self.grants.read().await.resolve(presented, &root)
    }

    /// Session adapter supplies identities validated under its operation guard.
    /// Retain that guard through minting, revocation, or session invalidation.
    pub(crate) async fn mint_grant(
        &self,
        grant: crate::grant::Grant,
        grantor_state_id: String,
        session_state_ids: std::collections::BTreeMap<String, String>,
    ) -> Result<String> {
        self.grants
            .write()
            .await
            .mint_persisted(grant, grantor_state_id, session_state_ids)
    }
    /// Startup only, after session adoption and before client admission.
    pub(crate) async fn prune_stale_grants(
        &self,
        state_ids: &std::collections::HashMap<String, String>,
        host_sessions: &std::collections::HashSet<String>,
    ) -> Result<bool> {
        self.grants
            .write()
            .await
            .prune_stale(state_ids, host_sessions)
    }
    pub(crate) async fn grant_count(&self) -> usize {
        self.grants.read().await.count()
    }
    #[cfg(test)]
    pub(crate) fn grants_for_test(&self) -> &RwLock<crate::grant::Grants> {
        &self.grants
    }
    pub(crate) async fn revoke_grants(&self, grantor: &str) -> Result<()> {
        let mut grants = self.grants.write().await;
        let count = grants.count();
        let result = grants.try_revoke_grantor(grantor);
        let changed = grants.count() < count;
        drop(grants);
        if changed {
            self.invalidate_auth(AuthChange::GrantsRevoked);
        }
        result.map(|_| ())
    }

    pub(crate) async fn invalidate_session_grants(&self, name: &str) {
        let mut grants = self.grants.write().await;
        let count = grants.count();
        if let Err(error) = grants.try_invalidate_session(name) {
            tracing::error!("persisting grants invalidated with session {name}: {error:#}");
        }
        let changed = grants.count() < count;
        drop(grants);
        if changed {
            self.invalidate_auth(AuthChange::GrantsRevoked);
        }
    }
}

#[cfg(test)]
#[path = "config_authority_tests.rs"]
mod tests;
