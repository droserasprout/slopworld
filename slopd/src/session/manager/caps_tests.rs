use crate::config::{Config, Daemon, SessionCfg};
use crate::grant::{Cap, Level};
use crate::session::test_manager;

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

    manager.revoke_grants("grantor").await.unwrap();
    assert_eq!(manager.grant_count().await, 0);
    assert!(!manager.cap_ok(&cap, "target", Level::Ro).await);
    assert!(manager.resolve_cap(Some(&token)).await.is_none());
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
            let cap = manager.resolve_cap(Some(&token)).await.unwrap();
            let mut cfg = manager.config().await;
            cfg.sessions
                .iter_mut()
                .find(|s| s.name == name)
                .unwrap()
                .state_id = crate::storage_id::draft_identity();
            manager
                .replace_config(&toml::to_string(&cfg).unwrap())
                .await
                .unwrap();
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
            let cap = manager.resolve_cap(Some(&token)).await.unwrap();
            manager.stop(subject).await.unwrap();
            assert!(manager.cap_ok(&cap, "target", level).await);
            assert!(manager.start(subject).await.is_err()); // No command in this fixture.
            assert!(manager.cap_ok(&cap, "target", level).await);
            manager
                .live
                .write()
                .await
                .get_mut(subject)
                .unwrap()
                .ephemeral = true;
            manager.stop(subject).await.unwrap();
            manager.sync_from_config().await;
            assert!(!manager.cap_ok(&cap, "target", level).await);
        }
    }
}
