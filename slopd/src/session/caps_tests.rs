use crate::config::{Config, Daemon, SessionCfg};
use crate::grant::{Cap, Level};
use crate::session::test_manager;
use crate::session::{AgentLifetime, SessionRole};

fn config() -> Config {
    Config {
        settings: crate::config::Settings {
            daemon: Daemon {
                token: "root".into(),
                ..Default::default()
            },
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
        manager.config_authority.resolve_cap(Some("root")).await,
        Some(Cap::Root)
    ));
    assert!(
        manager
            .config_authority
            .resolve_cap(Some("wrong"))
            .await
            .is_none()
    );

    let token = manager
        .mint_grant("grantor".into(), vec!["target".into()], Level::Ro)
        .await
        .unwrap();
    assert_eq!(manager.config_authority.grant_count().await, 1);
    let cap = manager
        .config_authority
        .resolve_cap(Some(&token))
        .await
        .unwrap();
    assert!(cap.allows("target", false, Level::Ro));
    assert!(!cap.allows("target", false, Level::Rw));
    assert!(!cap.allows("grantor", false, Level::Ro));
    assert!(manager.cap_ok(&cap, "target", Level::Ro).await);
    assert!(!manager.cap_ok(&cap, "target", Level::Rw).await);

    manager.revoke_grants("grantor").await.unwrap();
    assert_eq!(manager.config_authority.grant_count().await, 0);
    assert!(!manager.cap_ok(&cap, "target", Level::Ro).await);
    assert!(
        manager
            .config_authority
            .resolve_cap(Some(&token))
            .await
            .is_none()
    );
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

#[tokio::test]
async fn config_replacement_invalidates_grants_without_a_live_row() {
    for level in [Level::Ro, Level::Rw] {
        for name in ["grantor", "target"] {
            let manager = test_manager(config());
            let token = manager
                .mint_grant("grantor".into(), vec!["target".into()], level)
                .await
                .unwrap();
            let cap = manager
                .config_authority
                .resolve_cap(Some(&token))
                .await
                .unwrap();
            let mut cfg = manager.config().await;
            cfg.sessions
                .iter_mut()
                .find(|s| s.name == name)
                .unwrap()
                .state_id = crate::storage_id::draft_identity();
            cfg.sessions.sort_by_key(|session| session.name == name);
            manager.replace_workspace_fixture(&cfg).await.unwrap();
            assert!(!manager.cap_ok(&cap, "target", level).await);
        }
    }
}
#[tokio::test]
async fn durable_stop_preserves_grants_but_ephemeral_removal_invalidates_them() {
    for level in [Level::Ro, Level::Rw] {
        for subject in ["grantor", "target"] {
            let manager = test_manager(config());
            manager.sync_from_config().await;
            let token = manager
                .mint_grant("grantor".into(), vec!["target".into()], level)
                .await
                .unwrap();
            let cap = manager
                .config_authority
                .resolve_cap(Some(&token))
                .await
                .unwrap();
            manager.stop(subject).await.unwrap();
            assert!(manager.cap_ok(&cap, "target", level).await);
            assert!(manager.start(subject).await.is_err()); // No command in this fixture.
            assert!(manager.cap_ok(&cap, "target", level).await);
            manager.live.write().await.get_mut(subject).unwrap().role =
                SessionRole::Agent(AgentLifetime::Temporary);
            manager.stop(subject).await.unwrap();
            manager.sync_from_config().await;
            assert!(!manager.cap_ok(&cap, "target", level).await);
        }
    }
}

#[tokio::test]
async fn only_root_rotation_advances_the_root_credential_generation() {
    let manager = test_manager(config());
    let generation = manager.config_authority.root_generation();
    manager
        .mint_grant("grantor".into(), vec!["target".into()], Level::Ro)
        .await
        .unwrap();
    manager.revoke_grants("grantor").await.unwrap();
    assert_eq!(manager.config_authority.root_generation(), generation);
    manager
        .configuration()
        .patch_config(serde_json::json!({"daemon":{"token":"<redacted>"}}))
        .await
        .unwrap();
    assert_eq!(manager.config_authority.root_generation(), generation);
    manager
        .configuration()
        .patch_config(serde_json::json!({"daemon":{"token":"new-root"}}))
        .await
        .unwrap();
    assert_ne!(manager.config_authority.root_generation(), generation);
    assert!(
        manager
            .config_authority
            .resolve_cap(Some("root"))
            .await
            .is_none()
    );
    assert!(matches!(
        manager.config_authority.resolve_cap(Some("new-root")).await,
        Some(Cap::Root)
    ));
}

#[tokio::test]
async fn relationship_roles_follow_stable_identity_and_keep_manual_grants_independent() {
    use crate::config::WorkerAccess;
    let mut cfg = config();
    cfg.sessions[0].state_id = "1111111111111111".into();
    cfg.sessions[1].worker = true;
    cfg.sessions[1].parent = "grantor".into();
    cfg.sessions[1].parent_id = "1111111111111111".into();
    let manager = test_manager(cfg);
    let cap = Cap::Scoped(crate::grant::Grant {
        grantor: "grantor".into(),
        sessions: ["grantor".into()].into(),
        level: Level::Rw,
        revoked: Default::default(),
    });
    for bits in 0..16 {
        let role = WorkerAccess {
            followup: bits & 1 != 0,
            cancel: bits & 2 != 0,
            terminal: bits & 4 != 0,
            lifecycle: bits & 8 != 0,
        };
        manager.config_authority.write().await.sessions[1].parent_access = role;
        assert_eq!(manager.worker_access(&cap, "target").await, Some(role));
        assert!(manager.worker_diagnostics_ok(&cap, "target").await);
        assert_eq!(
            manager.terminal_cap_ok(&cap, "target", Level::Rw).await,
            role.terminal
        );
        assert_eq!(
            manager.worker_lifecycle_ok(&cap, "target").await,
            role.lifecycle
        );
        assert!(
            !manager.cap_ok(&cap, "target", Level::Rw).await,
            "relationship is not a generic write grant"
        );
        assert!(!manager.terminal_cap_ok(&cap, "unrelated", Level::Ro).await);
    }
    // Renaming preserves the relationship; replacing a parent at the same name does not.
    manager.config_authority.write().await.sessions[0].name = "renamed".into();
    let mut renamed = cap.clone();
    if let Cap::Scoped(grant) = &mut renamed {
        grant.grantor = "renamed".into();
    }
    assert_eq!(
        manager.worker_access(&renamed, "target").await,
        Some(WorkerAccess::all())
    );
    manager.config_authority.write().await.sessions[0].state_id = "2222222222222222".into();
    assert_eq!(manager.worker_access(&renamed, "target").await, None);
    // A manual read grant remains a read grant, even without relationship authority.
    if let Cap::Scoped(grant) = &mut renamed {
        grant.sessions.insert("target".into());
        grant.level = Level::Ro;
    }
    assert!(manager.terminal_cap_ok(&renamed, "target", Level::Ro).await);
    assert!(!manager.terminal_cap_ok(&renamed, "target", Level::Rw).await);
    manager.config_authority.write().await.sessions[1]
        .parent_id
        .clear();
    assert_eq!(manager.worker_access(&renamed, "target").await, None);
    if let Cap::Scoped(grant) = &renamed {
        grant
            .revoked
            .store(true, std::sync::atomic::Ordering::Release);
    }
    assert!(!manager.worker_diagnostics_ok(&renamed, "target").await);
}
