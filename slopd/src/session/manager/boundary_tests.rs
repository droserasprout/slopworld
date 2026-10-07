use crate::config::{Config, Daemon, SessionCfg};
use crate::grant::Level;
use std::sync::Arc;
use std::time::Duration;

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
        () = worktree.as_mut() => panic!("worktree guard finished before release"),
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
        settings: crate::config::Settings {
            daemon: Daemon {
                token: "root".into(),
                ..Default::default()
            },
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
    replacement.sessions[0].state_id = crate::storage_id::draft_identity();
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
        () = request.as_mut() => panic!("shared request ended early"),
        _ = started_wait => {},
    }
    let replace = manager.replace_workspace_fixture(&replacement);
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
            settings: crate::config::Settings {
                daemon: Daemon {
                    token: "root".into(),
                    ..Default::default()
                },
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
        replacement.sessions[1].state_id = crate::storage_id::draft_identity();
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
            () = request.as_mut() => panic!("request finished before release"),
            _ = check_done => {},
        }
        let replace = manager.replace_workspace_fixture(&replacement);
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

#[test]
fn named_locks_reuse_live_entries_and_prune_expired_names() {
    fn check<T: Default>() {
        let locks = Default::default();
        let first = super::named_lock::<T>(&locks, "first");
        assert!(std::sync::Arc::ptr_eq(
            &first,
            &super::named_lock(&locks, "first")
        ));
        let second = super::named_lock(&locks, "second");
        assert!(!std::sync::Arc::ptr_eq(&first, &second));
        drop(first);
        let replacement = super::named_lock(&locks, "replacement");
        assert!(!locks.lock().unwrap().contains_key("first"));
        assert!(std::sync::Arc::ptr_eq(
            &second,
            &super::named_lock(&locks, "second")
        ));
        drop((second, replacement));
        let _last = super::named_lock(&locks, "last");
        assert_eq!(locks.lock().unwrap().len(), 1);
    }
    check::<tokio::sync::RwLock<()>>();
    check::<tokio::sync::Mutex<()>>();
}

#[tokio::test]
async fn owned_operations_keep_the_authorized_request_context() {
    let manager = crate::session::test_manager(Config::default());
    manager
        .session_request(async {
            let current = manager.clone();
            manager
                .owned_session_operation(async move {
                    assert!(current.session_request_active());
                    assert!(current.session_write_operation_active());
                })
                .await;
        })
        .await;
    manager
        .session_read_request(async {
            let current = manager.clone();
            manager
                .owned_session_read_operation(async move {
                    assert!(current.session_request_active());
                    assert!(!current.session_write_operation_active());
                })
                .await;
        })
        .await;
}

#[tokio::test]
async fn cancelled_owned_shared_operation_retains_session_and_worktree_guards() {
    let manager = crate::session::test_manager(Config::default());
    let entered = Arc::new(tokio::sync::Barrier::new(2));
    let release = Arc::new(tokio::sync::Notify::new());
    let request = tokio::spawn({
        let current = manager.clone();
        let entered = entered.clone();
        let release = release.clone();
        async move {
            let operation = current.clone();
            current
                .owned_session_read_operation(async move {
                    let _worktrees = operation.worktrees.mutation.lock().await;
                    entered.wait().await;
                    release.notified().await;
                })
                .await;
        }
    });
    tokio::time::timeout(Duration::from_secs(5), entered.wait())
        .await
        .unwrap();
    request.abort();
    drop(request.await);
    manager.session_boundary.try_write().unwrap_err();
    drop(manager.session_boundary.try_read().unwrap());
    manager.worktrees.mutation.try_lock().unwrap_err();
    release.notify_one();
    let guard = tokio::time::timeout(Duration::from_secs(5), manager.session_boundary.write())
        .await
        .unwrap();
    drop(guard);
    drop(manager.worktrees.mutation.try_lock().unwrap());
}
