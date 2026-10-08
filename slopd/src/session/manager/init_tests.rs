use super::*;

#[tokio::test]
async fn initialization_retains_loaded_records_and_prunes_credentials_after_reconciliation() {
    let Some(root) = crate::test_support::isolated_with_env(|command, root| {
        // This test runs alone in a child process; never load the user's catalogs or tmux.
        for (key, value) in [
            ("SLOPD_TMUX_SOCKET", root.join("tmux")),
            ("SLOPD_CONFIG_ROOT", root.join("config")),
            ("SLOPD_DATA", root.join("data")),
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
    seed_startup_records(&cfg, &path, &data);
    std::fs::rename(path.with_file_name("grants.toml"), data.join("grants.toml")).unwrap();
    let binding = crate::storage::target::StorageBinding::resolved(&path).unwrap();
    let loaded = crate::storage::layout::load(&binding).await.unwrap();
    let kept_path = data
        .join("agents")
        .join(format!("{}.toml", cfg.sessions[0].state_id));
    // Manager construction must adopt the validated indexes, not reload a second
    // workspace snapshot after startup has already selected its configuration.
    std::fs::write(&kept_path, "broken external edit after load").unwrap();
    let manager = Manager::new(loaded).await.unwrap();

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

    manager
        .set_label("kept", "after startup".into())
        .await
        .unwrap();
    let saved: toml::Value = toml::from_str(&std::fs::read_to_string(kept_path).unwrap()).unwrap();
    assert_eq!(saved["future"].as_str(), Some("retained extension"));
    assert_eq!(saved["label"].as_str(), Some("after startup"));

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

fn seed_startup_records(cfg: &Config, path: &Path, data: &Path) {
    std::fs::create_dir_all(data.join("agents")).unwrap();
    std::fs::write(path, toml::to_string(&cfg.settings).unwrap()).unwrap();
    for (order, session) in cfg.sessions.iter().enumerate() {
        let mut record = toml::Value::try_from(session).unwrap();
        record
            .as_table_mut()
            .unwrap()
            .insert("future".into(), "retained extension".into());
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
}
