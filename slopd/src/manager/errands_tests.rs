use super::*;

#[tokio::test]
async fn errands_require_host_or_settings_before_allocating_and_copy_template_once() {
    let cfg = Config {
        projects: vec![ProjectCfg {
            name: "repo".into(),
            dir: "/tmp".into(),
            ..Default::default()
        }],
        ..Default::default()
    };
    let manager = crate::session::test_manager(cfg.clone());
    let mut item = LibraryItemCfg {
        name: "review".into(),
        project: "repo".into(),
        ..Default::default()
    };
    let want = RunWhere::default();
    let error = manager
        .create_errand_session(&cfg, &item, &want, false, false, "")
        .await
        .unwrap_err();
    assert!(error.to_string().contains("Host or an agent template"));
    assert!(manager.live.read().await.is_empty());
    assert!(manager.temp.read().await.is_empty());
    let template: AgentTemplate = serde_json::from_value(serde_json::json!({
            "name": "offline", "defaults": {"cmd":"review-agent", "network":"none", "limits":{"memory_mb":256},
                "autostart":true,"auto_resume":true,"persistent_tmp":true,
                "sandbox":["captured"], "sandbox_presets":[{"name":"captured","ro":["/usr"]}]}
        })).unwrap();
    manager.templates.write().await.create(template).unwrap();
    item.agent_template = "offline".into();
    let name = manager
        .create_errand_session(&cfg, &item, &want, false, false, "")
        .await
        .unwrap();
    {
        let live = manager.live.read().await;
        let session = &live[&name].cfg;
        assert_eq!(session.network, crate::config::NetworkMode::None);
        assert_eq!(session.limits.memory_mb, Some(256));
        assert_eq!(session.cmd.as_deref(), Some("review-agent"));
        assert_eq!(session.sandbox_snapshots[0].ro, ["/usr"]);
        assert!(session.persistent_tmp);
        assert!(!session.autostart && !session.auto_resume);
    }
    manager.templates.write().await.templates.clear();
    assert_eq!(
        manager.live.read().await[&name].cfg.limits.memory_mb,
        Some(256)
    );
    assert!(manager
        .create_errand_session(&cfg, &item, &want, false, false, "")
        .await
        .is_err());
    item.agent_template.clear();
    item.name = "host-command".into();
    let host = manager
        .create_errand_session(&cfg, &item, &want, true, false, "")
        .await
        .unwrap();
    assert!(manager.live.read().await[&host].host);
}

#[tokio::test]
async fn missing_clone_source_does_not_reserve_a_temporary_errand() {
    let cfg = Config::default();
    let manager = crate::session::test_manager(cfg.clone());
    let item = LibraryItemCfg {
        name: "review".into(),
        link: LibraryItemLink::Temp,
        ..Default::default()
    };

    let error = manager
        .create_errand_session(&cfg, &item, &RunWhere::default(), false, false, "gone")
        .await
        .unwrap_err();
    assert!(error
        .to_string()
        .contains("no such session to clone sandbox from: gone"));
    assert!(manager.live.read().await.is_empty());
    assert!(manager.temp.read().await.is_empty());
    assert!(manager.config().await.host_terminals.is_empty());
    std::fs::remove_dir_all(manager.cfg_path.parent().unwrap()).unwrap();
}

#[tokio::test]
async fn host_with_clone_source_fails_before_persistence_or_allocation() {
    let cfg = Config {
        projects: vec![ProjectCfg {
            name: "repo".into(),
            dir: "/tmp".into(),
            ..Default::default()
        }],
        ..Default::default()
    };
    let manager = crate::session::test_manager(cfg.clone());
    let item = LibraryItemCfg {
        name: "review".into(),
        project: "repo".into(),
        ..Default::default()
    };

    let error = manager
        .create_errand_session(&cfg, &item, &RunWhere::default(), true, true, "source")
        .await
        .unwrap_err();
    assert!(error
        .to_string()
        .contains("choose host execution or agent settings"));
    assert!(manager.live.read().await.is_empty());
    assert!(manager.temp.read().await.is_empty());
    assert!(manager.config().await.host_terminals.is_empty());
    assert!(!manager.cfg_path.exists());
    std::fs::remove_dir_all(manager.cfg_path.parent().unwrap()).unwrap();
}

#[tokio::test]
async fn host_persistence_failure_leaves_no_live_or_temporary_state() {
    let cfg = Config {
        projects: vec![ProjectCfg {
            name: "repo".into(),
            dir: "/tmp".into(),
            ..Default::default()
        }],
        ..Default::default()
    };
    let manager = crate::session::test_manager(cfg.clone());
    let item = LibraryItemCfg {
        name: "review".into(),
        project: "repo".into(),
        ..Default::default()
    };
    let parent = manager.cfg_path.parent().unwrap();
    std::fs::remove_dir_all(parent).unwrap();
    std::fs::write(parent, "blocks config directory").unwrap();

    assert!(manager
        .create_errand_session(&cfg, &item, &RunWhere::default(), true, true, "")
        .await
        .is_err());
    assert!(manager.live.read().await.is_empty());
    assert!(manager.temp.read().await.is_empty());
    assert!(manager.config().await.host_terminals.is_empty());
    assert!(!manager.cfg_path.exists());
    std::fs::remove_file(parent).unwrap();
}

#[tokio::test]
async fn temporary_clone_copies_agent_limits_within_session_boundary() {
    let cfg = Config {
        projects: vec![ProjectCfg {
            name: "repo".into(),
            dir: "/tmp".into(),
            ..Default::default()
        }],
        sessions: vec![SessionCfg {
            name: "source".into(),
            project: "repo".into(),
            network: crate::config::NetworkMode::None,
            persistent_tmp: true,
            limits: crate::config::Limits {
                memory_mb: Some(256),
                ..Default::default()
            },
            ..Default::default()
        }],
        ..Default::default()
    };
    let manager = crate::session::test_manager(cfg.clone());
    let item = LibraryItemCfg {
        name: "review".into(),
        link: LibraryItemLink::Temp,
        ..Default::default()
    };
    let (release, wait) = tokio::sync::oneshot::channel::<()>();
    let guarded = manager.session_read_operation(async { wait.await.unwrap() });
    tokio::pin!(guarded);
    assert!(futures::poll!(guarded.as_mut()).is_pending());
    let want = RunWhere::default();
    let create = manager.create_errand_session(&cfg, &item, &want, false, false, "source");
    tokio::pin!(create);
    assert!(futures::poll!(create.as_mut()).is_pending());
    assert!(manager.live.read().await.is_empty());
    assert!(manager.temp.read().await.is_empty());
    release.send(()).unwrap();
    guarded.await;

    let name = create.await.unwrap();
    let live = manager.live.read().await;
    let row = &live[&name];
    assert!(row.ephemeral);
    assert!(!row.host);
    assert_eq!(row.cfg.network, crate::config::NetworkMode::None);
    assert_eq!(row.cfg.limits.memory_mb, Some(256));
    assert!(row.cfg.persistent_tmp);
    assert!(manager.temp.read().await[&row.cfg.project].temp);
    std::fs::remove_dir_all(manager.cfg_path.parent().unwrap()).unwrap();
}

#[tokio::test]
async fn invalid_temporary_clone_settings_do_not_allocate_a_project() {
    let cfg = Config {
        projects: vec![ProjectCfg {
            name: "repo".into(),
            dir: "/tmp".into(),
            ..Default::default()
        }],
        sessions: vec![SessionCfg {
            name: "source".into(),
            project: "repo".into(),
            sandbox: vec!["missing-preset".into()],
            ..Default::default()
        }],
        ..Default::default()
    };
    let manager = crate::session::test_manager(cfg.clone());
    let item = LibraryItemCfg {
        name: "review".into(),
        link: LibraryItemLink::Temp,
        ..Default::default()
    };

    let error = manager
        .create_errand_session(&cfg, &item, &RunWhere::default(), false, false, "source")
        .await
        .unwrap_err();
    assert!(error.to_string().contains("unknown sandbox preset"));
    assert!(manager.live.read().await.is_empty());
    assert!(manager.temp.read().await.is_empty());
    std::fs::remove_dir_all(manager.cfg_path.parent().unwrap()).unwrap();
}

#[tokio::test]
async fn persistent_host_errand_saves_tab_and_live_metadata() {
    let cfg = Config {
        projects: vec![ProjectCfg {
            name: "repo".into(),
            dir: "/tmp".into(),
            ..Default::default()
        }],
        ..Default::default()
    };
    let manager = crate::session::test_manager(cfg.clone());
    let item = LibraryItemCfg {
        name: "shell".into(),
        project: "repo".into(),
        ..Default::default()
    };

    let name = manager
        .create_errand_session(&cfg, &item, &RunWhere::default(), true, true, "")
        .await
        .unwrap();
    let saved: Config =
        toml::from_str(&std::fs::read_to_string(&manager.cfg_path).unwrap()).unwrap();
    assert_eq!(saved.host_terminals.len(), 1);
    assert_eq!(saved.host_terminals[0].name, name);
    assert_eq!(saved.host_terminals[0].project, "repo");
    assert_eq!(saved.host_terminals[0].path, "/tmp");
    let live = manager.live.read().await;
    assert!(live[&name].host && live[&name].persistent_host);
    assert_eq!(live[&name].host_path, "/tmp");
    assert!(manager.temp.read().await.is_empty());
    std::fs::remove_dir_all(manager.cfg_path.parent().unwrap()).unwrap();
}
