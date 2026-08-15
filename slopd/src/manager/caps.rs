//! Capability and grant checks.

use super::super::*;

impl Manager {
    pub async fn resolve_cap(&self, presented: Option<&str>) -> Option<crate::grant::Cap> {
        let root = self.cfg.read().await.daemon.token.clone();
        self.grants.read().await.resolve(presented, &root)
    }

    pub async fn cap_ok(
        &self,
        cap: &crate::grant::Cap,
        session: &str,
        need: crate::grant::Level,
    ) -> bool {
        let is_host = self
            .live
            .read()
            .await
            .get(session)
            .map(|l| l.host)
            .unwrap_or(false);
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
        if !self.session_known(&grantor).await {
            bail!("no such session to grant to: {grantor}");
        }
        let live = self.live.read().await;
        let mut scope = std::collections::HashSet::new();
        for s in sessions {
            if live.get(&s).map(|l| l.host).unwrap_or(false) {
                bail!("the host terminal cannot be granted: {s}");
            }
            if !live.contains_key(&s) && self.config().await.session(&s).is_none() {
                bail!("no such session to grant: {s}");
            }
            scope.insert(s);
        }
        drop(live);
        Ok(self.grants.write().await.mint(crate::grant::Grant {
            grantor,
            sessions: scope,
            level,
        }))
    }

    pub async fn revoke_grants(&self, grantor: &str) {
        self.grants.write().await.revoke_grantor(grantor);
    }

    pub async fn grant_count(&self) -> usize {
        self.grants.read().await.count()
    }
}
