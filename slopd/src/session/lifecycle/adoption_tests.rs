use super::recovered_worker_cfg;
use crate::config::{Config, SessionCfg};

#[test]
fn recovered_worker_does_not_invent_settings_from_its_parent() {
    let cfg = Config {
        sessions: vec![SessionCfg {
            name: "parent".into(),
            project: "repo".into(),
            command: "codex".into(),
            sandbox: vec!["gpu".into()],
            ..Default::default()
        }],
        ..Default::default()
    };
    let worker = recovered_worker_cfg(
        &cfg,
        "parent-worker",
        &crate::tmux::WorkerMetadata {
            session: None,
            project: Some("repo".into()),
            worktree: Some("worker-worktree".into()),
            parent: "parent".into(),
            task_id: "task-7".into(),
            durable: false,
            state_id: Some("1111111111114111".into()),
        },
    );

    assert!(worker.is_none());
}

#[test]
fn recovered_activity_is_kept_only_for_durable_workers() {
    use super::{AdoptionDecision, AdoptionProbe, SessionRuntime, State};

    let cfg = Config::default();
    for durable in [false, true] {
        let metadata = crate::tmux::WorkerMetadata {
            session: Some(Box::new(SessionCfg::default())),
            project: None,
            worktree: None,
            parent: "parent".into(),
            task_id: "task-7".into(),
            durable,
            state_id: Some("1111111111114111".into()),
        };
        let decision = AdoptionDecision {
            worker_session: recovered_worker_cfg(&cfg, "worker", &metadata),
            probe: AdoptionProbe {
                name: "worker".into(),
                worker: Some(metadata),
                saved_host: None,
                host: false,
                tmux_host: None,
                activity: None,
                current_path: None,
                reader: None,
            },
            host_project: String::new(),
            host_path: String::new(),
            activity: Some(crate::activity::Activity {
                state: State::Idle,
                state_since: 123,
            }),
        };
        let live = SessionRuntime::new_adopted_live(&cfg, &decision);
        assert_eq!(!live.role.is_durable_agent(), !durable);
        if durable {
            assert_eq!(live.state, State::Idle);
            assert_eq!(live.state_since, 123);
            assert_eq!(live.last_change, 0);
        } else {
            assert_eq!(live.state, State::Working);
            assert!(live.state_since > 123);
            assert_eq!(live.last_change, live.state_since);
        }
    }
}

#[tokio::test]
async fn reconciliation_keeps_attached_worker_identity_without_reprobing_tmux_metadata() {
    use super::*;
    let socket = crate::test_support::TmuxSocket::new();
    let session = SessionCfg {
        name: "worker".into(),
        worker: true,
        parent: "parent".into(),
        task_id: "task".into(),
        ..Default::default()
    };
    let cfg = Config {
        sessions: vec![session.clone()],
        ..Default::default()
    };
    let manager = crate::session::test_manager_with_socket(cfg.clone(), socket.path.clone());
    manager
        .tmux
        .spawn(
            "worker",
            "/tmp",
            120,
            34,
            &["sleep".into(), "60".into()],
            false,
        )
        .await
        .unwrap();
    // Metadata is a recovery source; an attached reader already owns live identity.
    let stale_identity = crate::storage_id::draft_identity();
    manager
        .tmux
        .set_worker_metadata("worker", "parent", "task", true, &stale_identity)
        .await
        .unwrap();
    manager
        .tmux
        .set_worker_worktree("worker", "stale-project", "stale-worktree")
        .await
        .unwrap();
    let mut row = Live::new(session.clone(), TitleCapture::default());
    row.capture.emu = Some(Arc::new(Mutex::new(crate::emu::SessionEmu::new(120, 34))));
    manager.live.write().await.insert("worker".into(), row);
    for _ in 0..3 {
        assert!(!manager.session_operation(manager.adopt_orphans(&cfg)).await);
        let live = manager.live.read().await;
        assert_eq!(live["worker"].cfg.state_id, session.state_id);
        assert_eq!(live["worker"].cfg.project, session.project);
        assert_eq!(live["worker"].cfg.worktree, session.worktree);
    }
}

#[tokio::test]
async fn recovering_worker_without_current_metadata_retires_the_pane() {
    use super::*;
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    let socket = crate::test_support::TmuxSocket::new();
    let cfg = Config::default();
    let manager = crate::session::test_manager_with_socket(cfg.clone(), socket.path.clone());
    manager
        .tmux
        .spawn(
            "worker",
            "/tmp",
            120,
            34,
            &["sleep".into(), "60".into()],
            false,
        )
        .await
        .unwrap();
    manager
        .tmux
        .set_worker_metadata("worker", "parent", "task", false, "invalid-state")
        .await
        .unwrap();
    assert!(!manager.session_operation(manager.adopt_orphans(&cfg)).await);
    assert!(!manager.tmux.exists("worker").await);
    assert!(!manager.live.read().await.contains_key("worker"));
}

#[test]
fn recovered_worker_prefers_its_saved_settings_and_oneshot_snapshot() {
    let snapshot = SessionCfg {
        name: "worker".into(),
        command: "worker-command".into(),
        sandbox: vec!["worker-preset".into()],
        state_id: "1111111111111111".into(),
        ..Default::default()
    };
    let metadata = crate::tmux::WorkerMetadata {
        session: Some(Box::new(snapshot.clone())),
        project: None,
        worktree: None,
        parent: "parent".into(),
        task_id: "task".into(),
        durable: false,
        state_id: Some(snapshot.state_id.clone()),
    };
    let mut cfg = Config::default();
    let recovered = recovered_worker_cfg(&cfg, "worker", &metadata).unwrap();
    assert_eq!(recovered.command, "worker-command");
    assert_eq!(recovered.sandbox, ["worker-preset"]);
    let mut saved = snapshot;
    saved.command = "saved-command".into();
    cfg.sessions.push(saved);
    assert_eq!(
        recovered_worker_cfg(&cfg, "worker", &metadata)
            .unwrap()
            .command,
        "saved-command"
    );
}

#[tokio::test]
async fn surviving_worker_cannot_replace_a_different_saved_identity() {
    use super::*;
    let socket = crate::test_support::TmuxSocket::new();
    let saved = SessionCfg {
        name: "worker".into(),
        worker: true,
        state_id: "2222222222222222".into(),
        autostart: false,
        ..Default::default()
    };
    let mut cfg = Config {
        sessions: vec![saved.clone()],
        ..Default::default()
    };
    cfg.settings.daemon.token = "root".into();
    let manager = crate::session::test_manager_with_socket(cfg.clone(), socket.path.clone());
    manager
        .tmux
        .spawn(
            "worker",
            "/tmp",
            80,
            24,
            &["sleep".into(), "60".into()],
            false,
        )
        .await
        .unwrap();
    manager
        .tmux
        .set_worker_metadata("worker", "parent", "task", true, "1111111111111111")
        .await
        .unwrap();
    manager
        .live
        .write()
        .await
        .insert("worker".into(), Live::new(saved, TitleCapture::default()));
    let token = manager
        .config_authority
        .grants_for_test()
        .write()
        .await
        .mint(crate::grant::Grant {
            grantor: "worker".into(),
            sessions: ["worker".into()].into(),
            level: crate::grant::Level::Rw,
            revoked: Arc::new(std::sync::atomic::AtomicBool::new(false)),
        });
    assert!(
        manager
            .config_authority
            .resolve_cap(Some(&token))
            .await
            .is_some()
    );
    manager.session_operation(manager.adopt_orphans(&cfg)).await;
    assert!(
        manager
            .config_authority
            .resolve_cap(Some(&token))
            .await
            .is_none()
    );
    assert!(!manager.tmux.exists("worker").await);
    let live = manager.live.read().await;
    assert_eq!(live["worker"].cfg.state_id, "2222222222222222");
    assert!(live["worker"].capture.emu.is_none());
}

#[tokio::test]
async fn surviving_host_cannot_be_adopted_as_a_configured_agent_at_startup() {
    use super::*;
    let socket = crate::test_support::TmuxSocket::new();
    let cfg = Config {
        sessions: vec![SessionCfg {
            name: "reused".into(),
            autostart: false,
            ..Default::default()
        }],
        ..Default::default()
    };
    let manager = crate::session::test_manager_with_socket(cfg, socket.path.clone());
    manager
        .tmux
        .spawn(
            "reused",
            "/tmp",
            80,
            24,
            &["sleep".into(), "60".into()],
            true,
        )
        .await
        .unwrap();
    assert!(manager.live.read().await.is_empty());
    assert!(manager.tmux.is_host("reused").await);
    manager.sync_from_config().await;
    assert!(!manager.tmux.exists("reused").await);
    let live = manager.live.read().await;
    assert!(!live["reused"].role.is_host());
    assert!(live["reused"].capture.emu.is_none());
}

#[test]
fn adoption_restores_reader_context_without_putting_it_in_agent_configuration() {
    use super::*;
    let metadata = crate::tmux::ReaderMetadata {
        intent: "view".into(),
        label: "custom label".into(),
        original_label: "file.rs".into(),
        project: "repo".into(),
        worktree: "checkout".into(),
        path: "/tmp/file.rs".into(),
        key: "key".into(),
        scope: "scope".into(),
        pinned: true,
        line: 19,
    };
    let decision = AdoptionDecision {
        probe: AdoptionProbe {
            name: "reader".into(),
            worker: None,
            saved_host: None,
            host: true,
            tmux_host: None,
            activity: None,
            current_path: None,
            reader: Some(metadata.clone()),
        },
        worker_session: None,
        host_project: String::new(),
        host_path: "/tmp".into(),
        activity: None,
    };
    let cfg = Config::default();
    let row = SessionRuntime::new_adopted_live(&cfg, &decision);
    assert_eq!(row.launch, LaunchContext::from(&metadata));
    assert_eq!(row.cfg.label.as_deref(), Some("custom label"));
    assert_eq!(row.cfg.project, "repo");
    assert_eq!(row.cfg.worktree, "checkout");
    assert_eq!(row.role, SessionRole::HostAction);
    assert!(cfg.sessions.is_empty() && cfg.host_terminals.is_empty());
    let worker_decision = AdoptionDecision {
        worker_session: Some(SessionCfg::default()),
        ..decision
    };
    let worker = SessionRuntime::new_adopted_live(&cfg, &worker_decision);
    assert_eq!(
        worker.launch,
        LaunchContext::default(),
        "worker recovery must ignore stale reader metadata"
    );

    assert!(
        serde_json::to_value(&row.cfg)
            .unwrap()
            .get("reader_path")
            .is_none()
    );
}

#[test]
fn host_adoption_uses_saved_catalog_for_lifetime_and_never_restores_agent_activity() {
    use super::*;
    for (saved, expected) in [
        (false, SessionRole::HostAction),
        (true, SessionRole::HostShell),
    ] {
        let cfg = Config::default();
        let decision = SessionRuntime::decide_orphan(
            &cfg,
            AdoptionProbe {
                name: "host".into(),
                worker: None,
                host: true,
                saved_host: saved.then(|| crate::config::HostTerminalCfg {
                    name: "host".into(),
                    path: "/tmp".into(),
                    ..Default::default()
                }),
                tmux_host: Some(("repo".into(), "/tmp".into())),
                activity: Some(crate::activity::Activity {
                    state: State::Idle,
                    state_since: 123,
                }),
                current_path: None,
                reader: None,
            },
            &crate::session::test_manager(cfg.clone()).activity_cache,
        );
        let row = SessionRuntime::new_adopted_live(&cfg, &decision);
        assert_eq!(row.role, expected);
        assert_eq!(row.host_path, "/tmp");
        assert_eq!(row.state, State::Working);
        assert_ne!(row.state_since, 123);
    }
}

#[test]
fn worker_recovery_replaces_stale_host_role_and_directory() {
    use super::*;
    let cfg = Config::default();
    let manager = crate::session::test_manager(cfg.clone());
    for (durable, lifetime) in [
        (false, AgentLifetime::Temporary),
        (true, AgentLifetime::Durable),
    ] {
        let metadata = crate::tmux::WorkerMetadata {
            session: Some(Box::new(SessionCfg::default())),
            project: None,
            worktree: None,
            parent: "parent".into(),
            task_id: "task".into(),
            durable,
            state_id: Some("1111111111114111".into()),
        };
        let decision = SessionRuntime::decide_orphan(
            &cfg,
            AdoptionProbe {
                name: "worker".into(),
                worker: Some(metadata),
                host: false,
                saved_host: Some(crate::config::HostTerminalCfg {
                    name: "worker".into(),
                    ..Default::default()
                }),
                tmux_host: None,
                activity: None,
                current_path: None,
                reader: None,
            },
            &manager.activity_cache,
        );
        for stale in [SessionRole::HostShell, SessionRole::HostAction] {
            let mut row = Live::new(SessionCfg::default(), TitleCapture::default());
            row.role = stale;
            row.host_path = "/stale".into();
            manager.repair_adopted_live(&mut row, &decision);
            assert_eq!(row.role, SessionRole::Agent(lifetime));
            assert!(row.cfg.worker);
            assert_eq!(row.cfg.state_id, "1111111111114111");
            assert!(row.host_path.is_empty());
        }
    }
}
