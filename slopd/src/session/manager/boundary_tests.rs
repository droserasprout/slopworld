use crate::config::{Config, Daemon, SessionCfg};
use crate::grant::Level;

#[tokio::test]
async fn shared_worktree_guard_allows_input_but_blocks_identity_changes() {
    let manager = crate::session::test_manager(Config::default());
    let (release, wait) = tokio::sync::oneshot::channel::<()>();
    let (started, started_wait) = tokio::sync::oneshot::channel();
    let worktree = manager.session_read_operation(async {
        started.send(()).unwrap();
        wait.await.unwrap();
    });
    tokio::pin!(worktree);
    tokio::select! {
        _ = worktree.as_mut() => panic!("worktree guard finished before release"),
        _ = started_wait => {},
    }

    // A terminal command can enter the shared boundary while checkout is active.
    manager.session_read_operation(async {}).await;
    let identity_change = manager.session_operation(async {});
    tokio::pin!(identity_change);
    assert!(futures::poll!(identity_change.as_mut()).is_pending());
    release.send(()).unwrap();
    worktree.await;
    identity_change.await;
}

#[tokio::test]
async fn shared_request_keeps_scoped_capability_valid_until_it_finishes() {
    let manager = crate::session::test_manager(Config {
        daemon: Daemon {
            token: "root".into(),
            ..Default::default()
        },
        sessions: vec![SessionCfg {
            name: "agent".into(),
            ..Default::default()
        }],
        ..Default::default()
    });
    let token = manager
        .mint_grant("agent".into(), vec!["agent".into()], Level::Rw)
        .await
        .unwrap();
    let cap = manager.resolve_cap(Some(&token)).await.unwrap();
    let mut replacement = manager.config().await;
    replacement.sessions[0].state_id = uuid::Uuid::new_v4().to_string();
    let text = toml::to_string(&replacement).unwrap();
    let (release, wait) = tokio::sync::oneshot::channel::<()>();
    let (started, started_wait) = tokio::sync::oneshot::channel();
    let request = manager.session_read_request(async {
        assert!(manager.cap_ok(&cap, "agent", Level::Rw).await);
        started.send(()).unwrap();
        wait.await.unwrap();
        assert!(manager.cap_ok(&cap, "agent", Level::Rw).await);
    });
    tokio::pin!(request);
    tokio::select! {
        _ = request.as_mut() => panic!("shared request ended early"),
        _ = started_wait => {},
    }
    let replace = manager.replace_config(&text);
    tokio::pin!(replace);
    assert!(futures::poll!(replace.as_mut()).is_pending());
    release.send(()).unwrap();
    request.await;
    replace.await.unwrap();
    assert!(!manager.cap_ok(&cap, "agent", Level::Rw).await);
}

#[tokio::test]
async fn replacement_waits_for_authorized_use_and_then_rejects_the_old_capability() {
    for level in [Level::Ro, Level::Rw] {
        let manager = crate::session::test_manager(Config {
            daemon: Daemon {
                token: "root".into(),
                ..Default::default()
            },
            sessions: ["grantor", "target"]
                .into_iter()
                .map(|name| SessionCfg {
                    name: name.into(),
                    ..Default::default()
                })
                .collect(),
            ..Default::default()
        });
        let token = manager
            .mint_grant("grantor".into(), vec!["target".into()], level)
            .await
            .unwrap();
        let cap = manager.resolve_cap(Some(&token)).await.unwrap();
        let original = manager.config().await;
        let mut replacement = original.clone();
        replacement.sessions[1].state_id = uuid::Uuid::new_v4().to_string();
        let text = toml::to_string(&replacement).unwrap();
        let (release, wait) = tokio::sync::oneshot::channel();
        let (checked, check_done) = tokio::sync::oneshot::channel();
        let request = manager.session_request(async {
            assert!(manager.cap_ok(&cap, "target", level).await);
            checked.send(()).unwrap();
            wait.await.unwrap();
            assert_eq!(
                manager.config().await.sessions[1].state_id,
                original.sessions[1].state_id
            );
        });
        tokio::pin!(request);
        tokio::select! {
            _ = request.as_mut() => panic!("request finished before release"),
            _ = check_done => {},
        }
        let replace = manager.replace_config(&text);
        tokio::pin!(replace);
        assert!(futures::poll!(replace.as_mut()).is_pending());
        assert!(manager.cap_ok(&cap, "target", level).await);
        release.send(()).unwrap();
        request.await;
        replace.await.unwrap();
        assert!(!manager.cap_ok(&cap, "target", level).await);
    }
}

#[tokio::test]
async fn cancelling_a_nested_owned_operation_retains_the_exclusive_boundary() {
    let manager = crate::session::test_manager(Config::default());
    let reached = std::sync::Arc::new(tokio::sync::Notify::new());
    let release = std::sync::Arc::new(tokio::sync::Notify::new());
    let finished = std::sync::Arc::new(tokio::sync::Notify::new());
    let caller = {
        let manager = manager.clone();
        let reached = reached.clone();
        let release = release.clone();
        let finished = finished.clone();
        tokio::spawn(async move {
            manager
                .session_operation(async {
                    manager
                        .owned_session_operation(async move {
                            reached.notify_one();
                            release.notified().await;
                            finished.notify_one();
                        })
                        .await;
                })
                .await;
        })
    };
    reached.notified().await;
    caller.abort();
    assert!(caller.await.unwrap_err().is_cancelled());
    let replacement = manager.session_operation(async {});
    tokio::pin!(replacement);
    assert!(futures::poll!(replacement.as_mut()).is_pending());
    release.notify_one();
    finished.notified().await;
    replacement.await;
}
