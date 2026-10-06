use super::recovered_worker_cfg;
use crate::config::{Config, SessionCfg};

#[test]
fn recovered_worker_uses_parent_settings_and_task_identity() {
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
            project: Some("repo".into()),
            worktree: Some("worker-worktree".into()),
            parent: "parent".into(),
            task_id: "task-7".into(),
            durable: false,
            state_id: Some("11111111-1111-4111-8111-111111111111".into()),
        },
    );

    assert!(worker.worker);
    assert_eq!(worker.parent, "parent");
    assert_eq!(worker.task_id, "task-7");
    assert_eq!(worker.state_id, "11111111-1111-4111-8111-111111111111");
    assert_eq!(worker.project, "repo");
    assert_eq!(worker.worktree, "worker-worktree");
    assert_eq!(worker.command, "codex");
    assert_eq!(worker.sandbox, ["gpu"]);
    assert!(!worker.autostart);
    assert!(!worker.auto_resume);
}

#[test]
fn recovered_activity_is_kept_only_for_durable_workers() {
    use super::{AdoptionDecision, AdoptionProbe, Manager, State};

    let cfg = Config::default();
    for durable in [false, true] {
        let metadata = crate::tmux::WorkerMetadata {
            project: None,
            worktree: None,
            parent: "parent".into(),
            task_id: "task-7".into(),
            durable,
            state_id: Some("11111111-1111-4111-8111-111111111111".into()),
        };
        let decision = AdoptionDecision {
            worker_session: Some(recovered_worker_cfg(&cfg, "worker", &metadata)),
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
                state: State::Waiting,
                state_since: 123,
            }),
        };
        let live = Manager::new_adopted_live(&cfg, &decision);
        assert_eq!(live.ephemeral, !durable);
        if durable {
            assert_eq!(live.state, State::Waiting);
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
    let stale_identity = uuid::Uuid::new_v4().to_string();
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
async fn recovering_worker_without_an_identity_uses_the_guarded_allocator() {
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
    assert!(manager.session_operation(manager.adopt_orphans(&cfg)).await);
    let live = manager.live.read().await;
    assert!(crate::storage_id::valid(&live["worker"].cfg.state_id));
}
