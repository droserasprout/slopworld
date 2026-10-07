use super::*;

#[tokio::test]
async fn initialization_reconciles_sessions_before_pruning_persisted_credentials() {
    let Some(root) = crate::test_support::isolated_with_env(|command, root| {
        // This test runs alone in a child process; never load the user's catalogs or tmux.
        for (key, value) in [
            ("SLOPD_TMUX_SOCKET", root.join("tmux")),
            ("SLOPD_PRESETS", root.join("presets")),
            ("SLOPD_CONFIG_ROOT", root.join("config")),
            ("SLOPD_DATA", root.join("data")),
            ("SLOPD_JUKEBOX", root.join("jukebox")),
            ("XDG_RUNTIME_DIR", root.join("runtime")),
        ] {
            command.env(key, value);
        }
    }) else {
        return;
    };
    let socket = root.join("tmux").to_str().unwrap().to_owned();
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
    cfg.sessions[1].state_id = crate::storage_id::draft_identity();

    // Seed current stores directly; initialization must not depend on a converter.
    let data = crate::paths::data_root();
    std::fs::create_dir_all(data.join("agents")).unwrap();
    std::fs::write(&path, toml::to_string(&cfg.settings).unwrap()).unwrap();
    for (order, session) in cfg.sessions.iter().enumerate() {
        let mut record = toml::Value::try_from(session).unwrap();
        record
            .as_table_mut()
            .unwrap()
            .insert("storage_order".into(), i64::try_from(order).unwrap().into());
        std::fs::write(
            data.join("agents")
                .join(format!("{}.toml", session.state_id)),
            toml::to_string(&record).unwrap(),
        )
        .unwrap();
    }
    std::fs::rename(path.with_file_name("grants.toml"), data.join("grants.toml")).unwrap();
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
    assert_eq!(
        crate::grant::Grants::load_data(&path, &crate::paths::data_root())
            .unwrap()
            .count(),
        1
    );

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
