use super::*;

#[tokio::test]
async fn initialization_reconciles_sessions_before_pruning_persisted_credentials() {
    let Some(root) = crate::test_support::isolated() else {
        return;
    };
    let socket = root.join("tmux").to_str().unwrap().to_owned();
    // This test runs alone in a child process; never load the user's catalogs or tmux.
    for (key, value) in [
        ("SLOPD_TMUX_SOCKET", PathBuf::from(&socket)),
        ("SLOPD_PRESETS", root.join("presets")),
        ("SLOPD_JUKEBOX", root.join("jukebox")),
        ("XDG_RUNTIME_DIR", root.join("runtime")),
    ] {
        // SAFETY: this isolated current-thread test sets its environment before starting work.
        unsafe {
            std::env::set_var(key, value);
        }
    }
    let mut cfg = Config::default();
    cfg.daemon.token = "test-root-token".into();
    for name in ["kept", "replaced"] {
        cfg.sessions.push(SessionCfg {
            name: name.into(),
            ..Default::default()
        });
    }
    let seed = crate::session::test_manager_with_socket(cfg.clone(), socket.clone());
    *seed.auth.grants.write().await = crate::grant::Grants::load(&seed.cfg_path).unwrap();
    for session in &cfg.sessions {
        seed.live.write().await.insert(
            session.name.clone(),
            Live::new(session.clone(), TitleCapture::default()),
        );
    }
    let kept = seed
        .mint_grant("kept".into(), vec!["kept".into()], crate::grant::Level::Rw)
        .await
        .unwrap();
    let stale = seed
        .mint_grant(
            "replaced".into(),
            vec!["replaced".into()],
            crate::grant::Level::Rw,
        )
        .await
        .unwrap();
    let path = seed.cfg_path.clone();
    cfg.sessions[1].state_id = uuid::Uuid::new_v4().to_string();

    let manager = Manager::new(cfg.clone(), path.clone()).await.unwrap();

    let live = manager.live.read().await;
    assert_eq!(live.len(), 2);
    for session in &cfg.sessions {
        let row = &live[&session.name];
        assert_eq!(row.cfg.state_id, session.state_id);
        assert_eq!(row.state, State::Down);
        assert!(row.capture.reader.is_none());
    }
    drop(live);
    assert!(manager.resolve_cap(Some(&kept)).await.is_some());
    assert!(manager.resolve_cap(Some(&stale)).await.is_none());
    assert_eq!(manager.grant_count().await, 1);
    // Pruning must survive another daemon restart.
    assert_eq!(crate::grant::Grants::load(&path).unwrap().count(), 1);

    drop(manager);
    drop(seed);
    drop(
        tokio::process::Command::new("tmux")
            .args(["-S", &socket, "kill-server"])
            .output()
            .await,
    );
    assert!(
        !path.parent().unwrap().exists(),
        "fixture directory survived its owner"
    );
}
