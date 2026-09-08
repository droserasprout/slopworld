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
        self.invalidate_auth(crate::session::AuthChange::GrantorRevoked(
            grantor.to_string(),
        ));
    }

    pub async fn grant_count(&self) -> usize {
        self.grants.read().await.count()
    }
}

#[cfg(test)]
mod tests {
    use crate::config::{Config, Daemon, SessionCfg};
    use crate::grant::{Cap, Level};
    use crate::session::test_manager;

    fn config() -> Config {
        Config {
            daemon: Daemon {
                token: "root".into(),
                ..Default::default()
            },
            sessions: vec![
                SessionCfg {
                    name: "grantor".into(),
                    ..Default::default()
                },
                SessionCfg {
                    name: "target".into(),
                    ..Default::default()
                },
            ],
            ..Default::default()
        }
    }

    #[tokio::test]
    async fn capabilities_resolve_and_grants_stay_scoped() {
        let manager = test_manager(config());
        assert!(manager.session_known("grantor").await);
        assert!(!manager.session_known("missing").await);
        assert!(matches!(
            manager.resolve_cap(Some("root")).await,
            Some(Cap::Root)
        ));
        assert!(manager.resolve_cap(Some("wrong")).await.is_none());

        let token = manager
            .mint_grant("grantor".into(), vec!["target".into()], Level::Ro)
            .await
            .unwrap();
        assert_eq!(manager.grant_count().await, 1);
        let cap = manager.resolve_cap(Some(&token)).await.unwrap();
        assert!(cap.allows("target", false, Level::Ro));
        assert!(!cap.allows("target", false, Level::Rw));
        assert!(!cap.allows("grantor", false, Level::Ro));
        assert!(manager.cap_ok(&cap, "target", Level::Ro).await);
        assert!(!manager.cap_ok(&cap, "target", Level::Rw).await);

        manager.revoke_grants("grantor").await;
        assert_eq!(manager.grant_count().await, 0);
    }

    #[tokio::test]
    async fn minting_requires_existing_grantor_and_targets() {
        let manager = test_manager(config());
        let missing_grantor = manager
            .mint_grant("missing".into(), vec![], Level::Ro)
            .await
            .unwrap_err()
            .to_string();
        assert!(missing_grantor.contains("no such session to grant to"));

        let missing_target = manager
            .mint_grant("grantor".into(), vec!["missing".into()], Level::Ro)
            .await
            .unwrap_err()
            .to_string();
        assert!(missing_target.contains("no such session to grant"));
    }
}
