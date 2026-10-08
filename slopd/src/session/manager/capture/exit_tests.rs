use super::*;

#[tokio::test]
async fn maintenance_recovers_reader_after_unavailable_status_without_config_change() {
    let socket = crate::test_support::TmuxSocket::new();
    let manager = crate::session::test_manager_with_socket(Config::default(), socket.path.clone());
    let (session, task) = task_owned_session(&manager).await;
    let token = Arc::new(());
    let mut live = Live::new(session, TitleCapture::default());
    live.set_state(State::Working);
    live.capture.reader_token = Some(token.clone());
    live.capture.emu = Some(Arc::new(Mutex::new(SessionEmu::new(80, 24))));
    manager.live.write().await.insert("worker".into(), live);
    manager.reader_ended("worker".into(), token).await;
    manager
        .tmux
        .spawn(
            "worker",
            "/tmp",
            80,
            24,
            &["sleep".into(), "2147483647".into()],
            false,
        )
        .await
        .unwrap();

    // Maintenance must retry without a config edit or an explicit spawn_reader call.
    let recovered = tokio::time::timeout(Duration::from_secs(6), async {
        loop {
            manager.retick().await;
            if manager.live.read().await["worker"].capture.emu.is_some() {
                break;
            }
            tokio::time::sleep(manager.maintenance_delay().await).await;
        }
    })
    .await;
    let status = manager
        .tasks
        .task_for_async(crate::tasks::HOST, &task.id)
        .await
        .unwrap()
        .unwrap()
        .status;
    manager.stop("worker").await.unwrap();
    assert!(
        recovered.is_ok(),
        "maintenance never retried the disconnected reader"
    );
    assert_eq!(status, crate::tasks::Status::Queued);
}

async fn task_owned_session(manager: &Manager) -> (SessionCfg, crate::tasks::Task) {
    let mut session = SessionCfg {
        name: "worker".into(),
        worker: true,
        ..Default::default()
    };
    let task = manager
        .create_task_owned(
            crate::tasks::Participant {
                name: crate::tasks::HOST.into(),
                identity: crate::tasks::HOST.into(),
            },
            crate::tasks::Participant {
                name: session.name.clone(),
                identity: session.state_id.clone(),
            },
            "review".into(),
            Some(crate::tasks::WorkerTask {
                session: session.name.clone(),
                parent: crate::tasks::HOST.into(),
                durable: true,
            }),
        )
        .await
        .unwrap();
    session.task_id = task.id.clone();
    (session, task)
}

#[tokio::test]
async fn repeated_unavailable_status_schedules_a_bounded_retry_and_stop_cancels_it() {
    let socket = crate::test_support::TmuxSocket::new();
    let manager = crate::session::test_manager_with_socket(Config::default(), socket.path.clone());
    let (session, task) = task_owned_session(&manager).await;
    let mut live = Live::new(session, TitleCapture::default());
    live.state = State::Working;
    live.capture.retry_at = Some(Instant::now());
    manager.live.write().await.insert("worker".into(), live);
    for _ in 0..2 {
        manager.retry_capture_if_due().await;
        let due = manager.live.read().await["worker"]
            .capture
            .retry_at
            .unwrap();
        assert!(due > Instant::now());
        manager.retry_capture_if_due().await;
        assert_eq!(
            manager.live.read().await["worker"].capture.retry_at,
            Some(due)
        );
        assert_eq!(
            manager
                .tasks
                .task_for_async(crate::tasks::HOST, &task.id)
                .await
                .unwrap()
                .unwrap()
                .status,
            crate::tasks::Status::Queued
        );
        manager
            .live
            .write()
            .await
            .get_mut("worker")
            .unwrap()
            .capture
            .retry_at = Some(Instant::now());
    }
    manager.stop("worker").await.unwrap();
    manager.retry_capture_if_due().await;
    let live = manager.live.read().await;
    assert_eq!(live["worker"].state, State::Down);
    assert!(live["worker"].capture.retry_at.is_none());
    assert!(live["worker"].capture.reader_token.is_none());
}

#[tokio::test]
async fn retry_waiting_for_lifecycle_cannot_attach_to_a_replacement() {
    let socket = crate::test_support::TmuxSocket::new();
    let manager = crate::session::test_manager_with_socket(Config::default(), socket.path.clone());
    let mut live = Live::new(
        SessionCfg {
            name: "agent".into(),
            ..Default::default()
        },
        TitleCapture::default(),
    );
    live.capture.retry_at = Some(Instant::now());
    manager.live.write().await.insert("agent".into(), live);
    let boundary = manager.session_boundary.write().await;
    let retry = manager.retry_capture_if_due();
    tokio::pin!(retry);
    assert!(futures::poll!(retry.as_mut()).is_pending());
    let replacement = Live::new(
        SessionCfg {
            name: "agent".into(),
            ..Default::default()
        },
        TitleCapture::default(),
    );
    let identity = replacement.cfg.state_id.clone();
    manager
        .live
        .write()
        .await
        .insert("agent".into(), replacement);
    drop(boundary);
    retry.await;
    let live = manager.live.read().await;
    assert_eq!(live["agent"].cfg.state_id, identity);
    assert!(live["agent"].capture.retry_at.is_none());
    assert!(live["agent"].capture.reader_token.is_none());
}

#[tokio::test]
async fn failed_existing_attachment_releases_capture_and_allows_retry() {
    let socket = crate::test_support::TmuxSocket::new();
    let manager = crate::session::test_manager_with_socket(Config::default(), socket.path.clone());
    let (session, task) = task_owned_session(&manager).await;
    let mut live = Live::new(session, TitleCapture::default());
    live.set_state(State::Working);
    live.process_running = true;
    let run = live.run_id;
    manager.live.write().await.insert("worker".into(), live);

    // No pane exists: the real tmux control handshake must fail, repeatedly,
    // without leaving an emulator that makes the next attempt skip attachment.
    for _ in 0..2 {
        manager
            .session_operation(manager.spawn_reader("worker"))
            .await
            .unwrap_err();
        let live = manager.live.read().await;
        let current = &live["worker"];
        assert!(current.capture.emu.is_none());
        assert!(current.capture.reader_token.is_none());
        assert!(current.capture.reader.is_none());
        assert_eq!(current.run_id, run);
        assert!(current.process_running);
        assert_eq!(current.state, State::Working);
    }
    manager
        .tmux
        .spawn(
            "worker",
            "/tmp",
            80,
            24,
            &["sleep".into(), "2147483647".into()],
            false,
        )
        .await
        .unwrap();
    manager
        .live
        .write()
        .await
        .get_mut("worker")
        .unwrap()
        .capture
        .retry_at = Some(Instant::now());
    manager.retick().await;
    assert!(manager.live.read().await["worker"].capture.emu.is_some());
    assert!(
        manager.live.read().await["worker"]
            .capture
            .retry_at
            .is_none()
    );
    assert_eq!(manager.live.read().await["worker"].run_id, run);
    assert_eq!(
        manager
            .tasks
            .task_for_async(crate::tasks::HOST, &task.id)
            .await
            .unwrap()
            .unwrap()
            .status,
        crate::tasks::Status::Queued
    );
    manager.stop("worker").await.unwrap();
}

#[tokio::test]
async fn unavailable_pane_status_releases_reader_without_failing_task_or_replacement() {
    let socket = crate::test_support::TmuxSocket::new();
    let manager = crate::session::test_manager_with_socket(Config::default(), socket.path.clone());
    let (session, task) = task_owned_session(&manager).await;
    let token = Arc::new(());
    let mut live = Live::new(session, TitleCapture::default());
    live.set_state(State::Working);
    live.process_running = true;
    live.capture.reader_token = Some(token.clone());
    live.capture.emu = Some(Arc::new(Mutex::new(SessionEmu::new(80, 24))));
    let run = live.run_id;
    manager.live.write().await.insert("worker".into(), live);

    // With no server both the status query and checked listing fail. Neither
    // failure proves process exit, but the completed capture must be released.
    manager.reader_ended("worker".into(), token.clone()).await;
    {
        let live = manager.live.read().await;
        let current = &live["worker"];
        assert!(current.capture.emu.is_none());
        assert!(current.capture.reader_token.is_none());
        assert_eq!(current.run_id, run);
        assert!(current.process_running);
        assert_eq!(current.state, State::Working);
    }
    manager
        .tmux
        .spawn(
            "worker",
            "/tmp",
            80,
            24,
            &["sleep".into(), "2147483647".into()],
            false,
        )
        .await
        .unwrap();
    assert!(manager.spawn_reader("worker").await.unwrap());
    let replacement = manager.live.read().await["worker"]
        .capture
        .reader_token
        .clone()
        .unwrap();
    // Delayed or duplicate cleanup from the old reader cannot clear its replacement.
    assert!(!manager.release_completed_reader("worker", &token).await);
    manager.reader_ended("worker".into(), token).await;
    {
        let live = manager.live.read().await;
        assert!(reader_owned_by(&live["worker"], &replacement));
        assert!(live["worker"].capture.emu.is_some());
        assert!(live["worker"].capture.reader.is_some());
    }
    assert_eq!(
        manager
            .tasks
            .task_for_async(crate::tasks::HOST, &task.id)
            .await
            .unwrap()
            .unwrap()
            .status,
        crate::tasks::Status::Queued
    );
    manager.stop("worker").await.unwrap();
}

#[tokio::test]
async fn disconnected_reader_reattaches_without_failing_live_worker_task() {
    let root = std::env::temp_dir().join(format!("slopd-reader-recovery-{}", uuid::Uuid::new_v4()));
    std::fs::create_dir_all(&root).unwrap();
    let manager = crate::session::test_manager_with_socket(
        Config::default(),
        root.join("socket").to_string_lossy().into_owned(),
    );
    manager
        .tmux
        .spawn(
            "worker",
            "/tmp",
            80,
            24,
            &[
                "bash".into(),
                "-c".into(),
                r"printf '\033[?2004hready> '; read -r unused".into(),
            ],
            false,
        )
        .await
        .unwrap();
    let (session, task) = task_owned_session(&manager).await;
    let token = Arc::new(());
    let mut live = Live::new(session, TitleCapture::default());
    live.set_state(State::Working);
    live.process_running = true;
    live.capture.reader_token = Some(token.clone());
    live.capture.emu = Some(Arc::new(Mutex::new(SessionEmu::new(80, 24))));
    let run = live.run_id;
    manager.live.write().await.insert("worker".into(), live);
    manager.reader_ended("worker".into(), token).await;
    assert_eq!(
        manager
            .tasks
            .task_for_async(crate::tasks::HOST, &task.id)
            .await
            .unwrap()
            .unwrap()
            .status,
        crate::tasks::Status::Queued
    );
    {
        let live = manager.live.read().await;
        assert_eq!(live["worker"].run_id, run);
        assert!(live["worker"].capture.reader_token.is_some());
        assert!(live["worker"].capture.emu.is_some());
        assert_ne!(live["worker"].state, State::Down);
    }
    manager.stop("worker").await.unwrap();
    drop(
        tokio::process::Command::new("tmux")
            .args(["-S", root.join("socket").to_str().unwrap(), "kill-server"])
            .status()
            .await,
    );
    std::fs::remove_dir_all(root).unwrap();
}

#[tokio::test]
async fn confirmed_worker_exit_preserves_private_evidence_and_task_exit_status() {
    use std::os::unix::fs::PermissionsExt;
    let Some(root) = crate::test_support::isolated() else {
        return;
    };
    let socket = root.join("socket");
    let manager = crate::session::test_manager_with_socket(
        Config::default(),
        socket.to_string_lossy().into_owned(),
    );
    manager
        .tmux
        .spawn(
            "worker",
            "/tmp",
            80,
            24,
            &["sleep".into(), "2147483647".into()],
            false,
        )
        .await
        .unwrap();
    manager.tmux.retain_exit("worker").await.unwrap();
    let (session, task) = task_owned_session(&manager).await;
    let exit_path = crate::sandbox::state_dir(&session)
        .unwrap()
        .join("exit.json");
    let token = Arc::new(());
    let mut live = Live::new(session, TitleCapture::default());
    live.set_state(State::Working);
    live.process_running = true;
    live.capture.reader_token = Some(token.clone());
    manager.live.write().await.insert("worker".into(), live);
    manager
        .tmux
        .start_command(
            "worker",
            "/tmp",
            &[
                "bash".into(),
                "-c".into(),
                "printf 'fatal worker evidence'; exit 7".into(),
            ],
        )
        .await
        .unwrap();
    tokio::time::timeout(Duration::from_secs(5), async {
        loop {
            if manager.tmux.pane_exit("worker").await.unwrap().is_some() {
                break;
            }
            tokio::time::sleep(Duration::from_millis(10)).await;
        }
    })
    .await
    .unwrap();
    manager.reader_ended("worker".into(), token.clone()).await;
    let record = std::fs::read(&exit_path).unwrap();
    let parsed: serde_json::Value = serde_json::from_slice(&record).unwrap();
    assert!(parsed["reason"].as_str().unwrap().contains("exit status 7"));
    assert_eq!(parsed["task_id"], task.id);
    assert!(
        parsed["screen"]
            .as_array()
            .unwrap()
            .iter()
            .any(|line| line.as_str().unwrap().contains("fatal worker evidence"))
    );
    assert_eq!(
        std::fs::metadata(&exit_path).unwrap().permissions().mode() & 0o777,
        0o600
    );
    let task = manager
        .tasks
        .task_for_async(crate::tasks::HOST, &task.id)
        .await
        .unwrap()
        .unwrap();
    assert_eq!(task.status, crate::tasks::Status::Failed);
    assert!(task.note.unwrap().contains("exit status 7"));
    assert_eq!(manager.live.read().await["worker"].state, State::Down);
    assert!(!manager.tmux.exists("worker").await);
    // A duplicate old reader completion must not rewrite the new run's evidence.
    manager.reader_ended("worker".into(), token).await;
    assert_eq!(std::fs::read(&exit_path).unwrap(), record);
    drop(
        tokio::process::Command::new("tmux")
            .args(["-S", socket.to_str().unwrap(), "kill-server"])
            .status()
            .await,
    );
}

#[tokio::test]
async fn adoption_finalizes_a_pane_that_exited_without_a_reader() {
    let Some(_root) = crate::test_support::isolated() else {
        return;
    };
    let socket = crate::test_support::TmuxSocket::new();
    let manager = crate::session::test_manager_with_socket(Config::default(), socket.path.clone());
    let (session, task) = task_owned_session(&manager).await;
    let exit_path = crate::sandbox::state_dir(&session)
        .unwrap()
        .join("exit.json");
    manager
        .tmux
        .spawn(
            "worker",
            "/tmp",
            80,
            24,
            &["sleep".into(), "2147483647".into()],
            false,
        )
        .await
        .unwrap();
    manager
        .tmux
        .set_worker_metadata(
            "worker",
            crate::tasks::HOST,
            &task.id,
            true,
            &session.state_id,
        )
        .await
        .unwrap();
    manager.tmux.retain_exit("worker").await.unwrap();
    manager
        .tmux
        .start_command(
            "worker",
            "/tmp",
            &[
                "bash".into(),
                "-c".into(),
                "printf 'offline exit evidence'; exit 9".into(),
            ],
        )
        .await
        .unwrap();
    tokio::time::timeout(Duration::from_secs(5), async {
        while manager.tmux.pane_exit("worker").await.unwrap().is_none() {
            tokio::time::sleep(Duration::from_millis(10)).await;
        }
    })
    .await
    .unwrap();

    // No reader was present for the death hook. Exercise the actual adoption
    // entry point, which must discover the exit after its control handshake.
    manager
        .session_operation(manager.adopt_orphans(&Config::default()))
        .await;
    tokio::time::timeout(Duration::from_secs(5), async {
        loop {
            if manager.live.read().await["worker"].state == State::Down {
                break;
            }
            tokio::time::sleep(Duration::from_millis(10)).await;
        }
    })
    .await
    .unwrap();
    assert!(!manager.tmux.exists("worker").await);
    assert_eq!(
        manager
            .tasks
            .task_for_async(crate::tasks::HOST, &task.id)
            .await
            .unwrap()
            .unwrap()
            .status,
        crate::tasks::Status::Failed
    );
    let record: serde_json::Value =
        serde_json::from_slice(&std::fs::read(exit_path).unwrap()).unwrap();
    assert!(record["reason"].as_str().unwrap().contains("exit status 9"));
    assert!(
        record["screen"]
            .as_array()
            .unwrap()
            .iter()
            .any(|line| line.as_str().unwrap().contains("offline exit evidence"))
    );
}

#[tokio::test]
async fn host_reader_exit_removes_tab_without_explicit_stop() {
    let Some(root) = crate::test_support::isolated() else {
        return;
    };
    let project_dir = root.join("project");
    std::fs::create_dir_all(&project_dir).unwrap();
    let socket = crate::test_support::TmuxSocket::new();
    let cfg = Config {
        projects: vec![ProjectCfg {
            name: "repo".into(),
            dir: project_dir.to_string_lossy().into_owned(),
            ..Default::default()
        }],
        ..Default::default()
    };
    let manager = crate::session::test_manager_with_socket(cfg, socket.path.clone());
    let item = LibraryItemCfg {
        name: "reader".into(),
        project: "repo".into(),
        kind: LibraryItemKind::Shell,
        host: true,
        command: Some("/bin/sh -c 'printf ready; read reply'".into()),
        ..Default::default()
    };
    let name = manager
        .run_errand(
            item,
            RunWhere {
                intent: "view".into(),
                reader_path: "/tmp/sample".into(),
                ..Default::default()
            },
            true,
            false,
            "",
        )
        .await
        .unwrap();
    tokio::time::timeout(Duration::from_secs(5), async {
        while !manager
            .tmux
            .capture(&name, 0)
            .await
            .unwrap()
            .lines
            .iter()
            .any(|line| line.contains("ready"))
        {
            tokio::time::sleep(Duration::from_millis(10)).await;
        }
    })
    .await
    .unwrap();
    let state_dir = crate::sandbox::state_dir(&manager.live.read().await[&name].cfg).unwrap();
    assert!(!state_dir.exists());
    manager
        .tmux
        .send_keys(&name, &["Enter".into()], false)
        .await
        .unwrap();
    tokio::time::timeout(Duration::from_secs(5), async {
        while manager.live.read().await.contains_key(&name) {
            tokio::time::sleep(Duration::from_millis(10)).await;
        }
    })
    .await
    .expect("exited host reader tab must disappear");
    assert!(!state_dir.exists(), "host exit created private storage");
    assert!(!manager.tmux.exists(&name).await);
}

#[tokio::test]
async fn disappeared_host_panes_do_not_allocate_private_storage() {
    let Some(_root) = crate::test_support::isolated() else {
        return;
    };
    let socket = crate::test_support::TmuxSocket::new();
    let manager = crate::session::test_manager_with_socket(Config::default(), socket.path.clone());
    // Keep the server alive so a checked listing proves the host pane is absent.
    manager
        .tmux
        .spawn(
            "keeper",
            "/tmp",
            80,
            24,
            &["sleep".into(), "2147483647".into()],
            false,
        )
        .await
        .unwrap();
    for persistent in [false, true] {
        let session = SessionCfg {
            name: "host-tab".into(),
            ..Default::default()
        };
        let state_dir = crate::sandbox::state_dir(&session).unwrap();
        let token = Arc::new(());
        let mut live = Live::new(session, TitleCapture::default());
        live.host = true;
        live.ephemeral = true;
        live.persistent_host = persistent;
        live.process_running = true;
        live.set_state(State::Working);
        live.capture.reader_token = Some(token.clone());
        manager.live.write().await.insert("host-tab".into(), live);
        manager.reader_ended("host-tab".into(), token.clone()).await;
        manager.reader_ended("host-tab".into(), token).await;
        assert!(!state_dir.exists(), "host exit created private storage");
        let live = manager.live.read().await;
        if persistent {
            assert_eq!(live["host-tab"].state, State::Down);
            assert!(!live["host-tab"].process_running);
        } else {
            assert!(!live.contains_key("host-tab"));
        }
    }
}
