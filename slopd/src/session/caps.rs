//! Session identity validation and operation guards for application credentials.

use super::*;

impl SessionRuntime {
    pub async fn cap_ok(
        &self,
        cap: &crate::credentials::Cap,
        session: &str,
        need: crate::credentials::Level,
    ) -> bool {
        // Callers serialize this check and its session operation with lifecycle changes.
        let is_host = self.is_host(session).await;
        cap.allows(session, is_host, need)
    }

    pub(crate) async fn terminal_cap_ok(
        &self,
        cap: &crate::credentials::Cap,
        session: &str,
        need: crate::credentials::Level,
    ) -> bool {
        self.cap_ok(cap, session, need).await
            || self
                .worker_access(cap, session)
                .await
                .is_some_and(|access| access.terminal)
    }

    pub(crate) async fn worker_lifecycle_ok(
        &self,
        cap: &crate::credentials::Cap,
        session: &str,
    ) -> bool {
        self.cap_ok(cap, session, crate::credentials::Level::Rw)
            .await
            || self
                .worker_access(cap, session)
                .await
                .is_some_and(|access| access.lifecycle)
    }

    /// Resolve current action scopes under the request boundary. Parent identity is
    /// captured, but permissions are never cached on a relationship or credential.
    pub(crate) async fn worker_access(
        &self,
        cap: &crate::credentials::Cap,
        name: &str,
    ) -> Option<crate::config::WorkerAccess> {
        if !cap.is_valid() || self.is_host(name).await {
            return None;
        }
        let target = self.session_cfg(name).await?;
        let principal = cap.principal()?;
        let caller = self.session_cfg(principal).await?;
        let own =
            target.worker && !target.parent_id.is_empty() && target.parent_id == caller.state_id;
        let project = !caller.project.is_empty() && caller.project == target.project;
        let p = caller.permissions;
        let access = crate::config::WorkerAccess {
            followup: p.followup.allows(own, project),
            cancel: p.cancel.allows(own, project),
            terminal: p.terminal.allows(own, project),
            lifecycle: p.lifecycle.allows(own, project),
        };
        (own || access.followup
            || access.cancel
            || access.terminal
            || access.lifecycle
            || p.delete.allows(own, project))
        .then_some(access)
    }

    pub(crate) async fn delete_agent_ok(&self, cap: &crate::credentials::Cap, name: &str) -> bool {
        if cap.may_create() {
            return true;
        }
        if !cap.is_valid() || self.is_host(name).await {
            return false;
        }
        let Some(principal) = cap.principal() else {
            return false;
        };
        let Some(caller) = self.session_cfg(principal).await else {
            return false;
        };
        let Some(target) = self.session_cfg(name).await else {
            return false;
        };
        caller.permissions.delete.allows(
            target.worker && !target.parent_id.is_empty() && target.parent_id == caller.state_id,
            principal != name && !caller.project.is_empty() && caller.project == target.project,
        )
    }

    pub(crate) async fn manage_worktrees_ok(&self, cap: &crate::credentials::Cap) -> bool {
        if cap.may_create() {
            return true;
        }
        if !cap.is_valid() {
            return false;
        }
        let Some(principal) = cap.principal() else {
            return false;
        };
        self.session_cfg(principal)
            .await
            .is_some_and(|s| s.permissions.worktrees)
    }

    pub(crate) async fn worker_diagnostics_ok(
        &self,
        cap: &crate::credentials::Cap,
        name: &str,
    ) -> bool {
        self.cap_ok(cap, name, crate::credentials::Level::Ro).await
            || self.worker_access(cap, name).await.is_some()
    }

    pub async fn session_known(&self, name: &str) -> bool {
        self.live.read().await.contains_key(name) || self.config().await.session(name).is_some()
    }

    #[cfg(test)]
    pub async fn mint_grant(
        &self,
        grantor: String,
        sessions: Vec<String>,
        level: crate::credentials::Level,
    ) -> Result<String> {
        self.session_operation(self.mint_grant_inner(grantor, sessions, level))
            .await
    }

    #[cfg(test)]
    async fn mint_grant_inner(
        &self,
        grantor: String,
        sessions: Vec<String>,
        level: crate::credentials::Level,
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
                crate::credentials::AgentCredential {
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

    #[cfg(test)]
    pub async fn revoke_grants(&self, grantor: &str) -> Result<()> {
        self.session_operation(self.config_authority.revoke_grants(grantor))
            .await
    }

    /// Revoke whole grants owned by or targeting a disappearing session.
    pub async fn invalidate_session(&self, name: &str) {
        self.session_operation(self.config_authority.invalidate_session_credentials(name))
            .await
    }
}

#[cfg(test)]
#[path = "caps_tests.rs"]
mod tests;

#[cfg(test)]
#[path = "permission_tests.rs"]
mod permission_tests;
