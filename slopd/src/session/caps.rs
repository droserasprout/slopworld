//! Session identity validation and operation guards for application credentials.

use super::*;

impl SessionRuntime {
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

    pub(crate) async fn terminal_cap_ok(
        &self,
        cap: &crate::grant::Cap,
        session: &str,
        need: crate::grant::Level,
    ) -> bool {
        self.cap_ok(cap, session, need).await
            || self
                .worker_access(cap, session)
                .await
                .is_some_and(|access| access.terminal)
    }

    pub(crate) async fn worker_lifecycle_ok(&self, cap: &crate::grant::Cap, session: &str) -> bool {
        self.cap_ok(cap, session, crate::grant::Level::Rw).await
            || self
                .worker_access(cap, session)
                .await
                .is_some_and(|access| access.lifecycle)
    }

    /// Resolve a direct relationship by stable identity under the request boundary.
    /// Manual grants remain independent; a missing legacy parent ID grants nothing.
    pub(crate) async fn worker_access(
        &self,
        cap: &crate::grant::Cap,
        name: &str,
    ) -> Option<crate::config::WorkerAccess> {
        if !cap.is_valid() || self.is_host(name).await {
            return None;
        }
        let child = self.session_cfg(name).await?;
        if !child.worker || child.parent_id.is_empty() {
            return None;
        }
        let principal = cap.principal()?;
        if self.is_host(principal).await {
            return None;
        }
        let parent = self.session_cfg(principal).await?;
        (parent.state_id == child.parent_id).then_some(child.parent_access)
    }

    pub(crate) async fn worker_diagnostics_ok(&self, cap: &crate::grant::Cap, name: &str) -> bool {
        self.cap_ok(cap, name, crate::grant::Level::Ro).await
            || self.worker_access(cap, name).await.is_some()
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
            Some(session) if session.role.is_host() => String::new(),
            Some(session) => session.cfg.state_id.clone(),
            None => cfg
                .session(&grantor)
                .map(|session| session.state_id.clone())
                .unwrap_or_default(),
        };
        if grantor_state_id.is_empty()
            && !live
                .get(&grantor)
                .is_some_and(|session| session.role.is_host())
        {
            bail!("session {grantor} has no stable identity for a grant");
        }
        let mut scope = std::collections::HashSet::new();
        let mut session_state_ids = std::collections::BTreeMap::new();
        for s in sessions {
            if live.get(&s).map(|l| l.role.is_host()).unwrap_or(false) {
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
        self.config_authority
            .mint_grant(
                crate::grant::Grant {
                    grantor,
                    sessions: scope,
                    level,
                    revoked: Default::default(),
                },
                grantor_state_id,
                session_state_ids,
            )
            .await
    }

    pub async fn revoke_grants(&self, grantor: &str) -> Result<()> {
        self.session_operation(self.config_authority.revoke_grants(grantor))
            .await
    }

    /// Revoke whole grants owned by or targeting a disappearing session.
    pub async fn invalidate_session(&self, name: &str) {
        self.session_operation(self.config_authority.invalidate_session_grants(name))
            .await
    }
}

#[cfg(test)]
#[path = "caps_tests.rs"]
mod tests;
