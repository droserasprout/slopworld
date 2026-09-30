use super::*;

#[tokio::test]
async fn cancelling_batch_cleanup_aborts_all_removed_readers() {
    let manager = crate::session::test_manager(Config::default());
    let mut completions = Vec::new();
    for name in ["first", "second"] {
        let (done, completion) = tokio::sync::oneshot::channel::<()>();
        let mut live = Live::new(
            SessionCfg {
                name: name.into(),
                ..Default::default()
            },
            TitleCapture::default(),
        );
        live.capture.reader = Some(tokio::spawn(async move {
            let _done = done;
            std::future::pending::<()>().await;
        }));
        manager.live.write().await.insert(name.into(), live);
        completions.push(completion);
    }

    // Keep cleanup from completing even if tmux activity clearing returns immediately.
    let _temp = manager.temp.write().await;
    let cfg = Config::default();
    let mut pruning = Box::pin(manager.prune_removed(&cfg));
    assert!(futures::poll!(pruning.as_mut()).is_pending());
    assert!(manager.live.read().await.is_empty());
    drop(pruning);

    for completion in completions {
        assert!(
            tokio::time::timeout(Duration::from_secs(1), completion)
                .await
                .expect("removed reader survived cancellation of batch cleanup")
                .is_err()
        );
    }
}

#[tokio::test]
async fn host_catalog_refresh_preserves_existing_non_host_identity() {
    let mut cfg = Config {
        host_terminals: ["shell", "worker"]
            .into_iter()
            .map(|name| crate::config::HostTerminalCfg {
                name: name.into(),
                label: Some("saved label".into()),
                path: "/tmp".into(),
                ..Default::default()
            })
            .collect(),
        ..Default::default()
    };
    let manager = crate::session::test_manager(cfg.clone());
    let worker = Live::new(
        SessionCfg {
            name: "worker".into(),
            worker: true,
            label: Some("worker label".into()),
            ..Default::default()
        },
        TitleCapture::default(),
    );
    manager.live.write().await.insert("worker".into(), worker);

    manager.upsert_host_terminals(&cfg).await;
    cfg.host_terminals[0].label = Some("updated label".into());
    cfg.host_terminals[0].path = "/".into();
    manager.upsert_host_terminals(&cfg).await;

    let live = manager.live.read().await;
    let shell = &live["shell"];
    assert!(shell.host && shell.persistent_host && shell.ephemeral);
    assert_eq!(shell.cfg.label.as_deref(), Some("updated label"));
    assert_eq!(shell.host_path, "/");
    let worker = &live["worker"];
    assert!(worker.cfg.worker);
    assert!(!worker.host && !worker.persistent_host);
    assert_eq!(worker.cfg.label.as_deref(), Some("worker label"));
}

#[tokio::test]
async fn configured_agent_never_inherits_a_former_host_row() {
    let session = SessionCfg {
        name: "shell".into(),
        state_id: uuid::Uuid::new_v4().to_string(),
        ..Default::default()
    };
    let cfg = Config {
        sessions: vec![session.clone()],
        ..Default::default()
    };
    let manager = crate::session::test_manager(cfg.clone());
    let mut host = Live::new(
        SessionCfg {
            name: "shell".into(),
            ..Default::default()
        },
        TitleCapture::default(),
    );
    host.host = true;
    host.ephemeral = true;
    manager.live.write().await.insert("shell".into(), host);
    {
        let live = manager.live.read().await;
        assert!(!keep_live_session(&cfg, "shell", &live["shell"]));
    }
    manager.upsert_sessions(&cfg).await;
    let live = manager.live.read().await;
    assert!(live["shell"].host);
    assert_ne!(live["shell"].cfg.state_id, session.state_id);
}
