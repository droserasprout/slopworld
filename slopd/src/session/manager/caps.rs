//! Authorization state, credential invalidation, and capability checks.

use super::super::*;

/// Grants and the generation and notifications used to invalidate client credentials.
pub(crate) struct Authorization {
    pub(super) grants: RwLock<crate::grant::Grants>,
    generation: AtomicU64,
    changes: broadcast::Sender<AuthChange>,
}

impl Authorization {
    pub(super) fn new(grants: crate::grant::Grants) -> Self {
        let (changes, _) = broadcast::channel(16);
        Self {
            grants: RwLock::new(grants),
            generation: AtomicU64::new(0),
            changes,
        }
    }
}

impl Manager {
    pub(crate) fn auth_generation(&self) -> u64 {
        self.auth.generation.load(Ordering::Acquire)
    }

    pub(crate) fn auth_changes(&self) -> broadcast::Receiver<AuthChange> {
        self.auth.changes.subscribe()
    }

    pub(crate) fn invalidate_auth(&self, change: AuthChange) {
        self.auth.generation.fetch_add(1, Ordering::AcqRel);
        drop(self.auth.changes.send(change));
    }

    pub async fn resolve_cap(&self, presented: Option<&str>) -> Option<crate::grant::Cap> {
        let root = self.cfg.read().await.daemon.token.clone();
        self.auth.grants.read().await.resolve(presented, &root)
    }

    pub async fn cap_ok(
        &self,
        cap: &crate::grant::Cap,
        session: &str,
        need: crate::grant::Level,
    ) -> bool {
        // Callers serialize this check and its session operation with lifecycle changes.
        let is_host = self.is_host(session).await;
        cap.allows(session, is_host, need)
    }

    pub async fn session_known(&self, name: &str) -> bool {
        self.live.read().await.contains_key(name) || self.config().await.session(name).is_some()
    }

    pub async fn mint_grant(
        &self,
        grantor: String,
        sessions: Vec<String>,
        level: crate::grant::Level,
    ) -> Result<String> {
        self.session_operation(self.mint_grant_inner(grantor, sessions, level))
            .await
    }

    async fn mint_grant_inner(
        &self,
        grantor: String,
        sessions: Vec<String>,
        level: crate::grant::Level,
    ) -> Result<String> {
        if !self.session_known(&grantor).await {
            bail!("no such session to grant to: {grantor}");
        }
        let cfg = self.config().await;
        let live = self.live.read().await;
        let grantor_state_id = match live.get(&grantor) {
            Some(session) if session.host => String::new(),
            Some(session) => session.cfg.state_id.clone(),
            None => cfg
                .session(&grantor)
                .map(|session| session.state_id.clone())
                .unwrap_or_default(),
        };
        if grantor_state_id.is_empty() && !live.get(&grantor).is_some_and(|session| session.host) {
            bail!("session {grantor} has no stable identity for a grant");
        }
        let mut scope = std::collections::HashSet::new();
        let mut session_state_ids = std::collections::BTreeMap::new();
        for s in sessions {
            if live.get(&s).map(|l| l.host).unwrap_or(false) {
                bail!("the host terminal cannot be granted: {s}");
            }
            if !live.contains_key(&s) && cfg.session(&s).is_none() {
                bail!("no such session to grant: {s}");
            }
            let state_id = live
                .get(&s)
                .map(|session| session.cfg.state_id.clone())
                .or_else(|| cfg.session(&s).map(|session| session.state_id.clone()))
                .unwrap_or_default();
            if state_id.is_empty() {
                bail!("session {s} has no stable identity for a grant");
            }
            session_state_ids.insert(s.clone(), state_id);
            scope.insert(s);
        }
        drop(live);
        self.auth.grants.write().await.mint_persisted(
            crate::grant::Grant {
                grantor,
                sessions: scope,
                level,
                revoked: Default::default(),
            },
            grantor_state_id,
            session_state_ids,
        )
    }

    pub async fn revoke_grants(&self, grantor: &str) -> Result<()> {
        self.session_operation(self.revoke_grants_inner(grantor))
            .await
    }

    async fn revoke_grants_inner(&self, grantor: &str) -> Result<()> {
        let mut grants = self.auth.grants.write().await;
        let count = grants.count();
        let result = grants.try_revoke_grantor(grantor);
        let changed = grants.count() < count;
        drop(grants);
        if changed {
            self.invalidate_auth(crate::session::AuthChange::GrantsRevoked);
        }
        result.map(|_| ())
    }

    /// Revoke whole grants owned by or targeting a disappearing session.
    pub async fn invalidate_session(&self, name: &str) {
        self.session_operation(self.invalidate_session_inner(name))
            .await
    }

    async fn invalidate_session_inner(&self, name: &str) {
        let mut grants = self.auth.grants.write().await;
        let count = grants.count();
        if let Err(error) = grants.try_invalidate_session(name) {
            tracing::error!("persisting grants invalidated with session {name}: {error:#}");
        }
        let changed = grants.count() < count;
        drop(grants);
        if changed {
            self.invalidate_auth(crate::session::AuthChange::GrantsRevoked);
        }
    }

    pub async fn grant_count(&self) -> usize {
        self.auth.grants.read().await.count()
    }
}

#[cfg(test)]
#[path = "caps_tests.rs"]
mod tests;
