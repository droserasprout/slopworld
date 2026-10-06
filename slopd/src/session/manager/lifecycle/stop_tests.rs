use super::*;

#[test]
fn stopping_host_viewers_forgets_them_but_keeps_saved_shells() {
    let manager = crate::session::test_manager(Config::default());
    for persistent in [false, true] {
        let mut live = HashMap::new();
        let mut row = Live::new(SessionCfg::default(), TitleCapture::default());
        row.ephemeral = true;
        row.host = true;
        row.persistent_host = persistent;
        live.insert("host".into(), row);

        let plan = manager
            .detach_live_locked(&mut live, "host", DetachCause::Stop)
            .unwrap();

        assert_eq!(live.contains_key("host"), persistent);
        assert!(!plan.remove_ephemeral_state);
    }
}

#[test]
fn config_removal_does_not_delete_durable_private_state() {
    let manager = crate::session::test_manager(Config::default());
    let mut live = HashMap::new();
    live.insert(
        "agent".into(),
        Live::new(
            SessionCfg {
                name: "agent".into(),
                ..Default::default()
            },
            TitleCapture::default(),
        ),
    );

    let plan = manager
        .detach_live_locked(&mut live, "agent", DetachCause::ConfigRemoval)
        .expect("durable live row should produce a cleanup plan");

    assert!(!plan.remove_ephemeral_state);
}

#[test]
fn config_removal_deletes_ephemeral_private_state() {
    let manager = crate::session::test_manager(Config::default());
    let mut live = HashMap::new();
    let mut ephemeral = Live::new(
        SessionCfg {
            name: "agent".into(),
            ..Default::default()
        },
        TitleCapture::default(),
    );
    ephemeral.ephemeral = true;
    live.insert("agent".into(), ephemeral);

    let plan = manager
        .detach_live_locked(&mut live, "agent", DetachCause::ConfigRemoval)
        .expect("ephemeral live row should produce a cleanup plan");

    assert!(plan.remove_ephemeral_state);
}

#[tokio::test]
async fn stale_reader_exit_cannot_detach_a_replacement_and_current_exit_does_not_abort_itself() {
    let manager = crate::session::test_manager(Config::default());
    let mut live = HashMap::new();
    let token = Arc::new(());
    let (release, wait) = tokio::sync::oneshot::channel();
    let (completed, completion) = tokio::sync::oneshot::channel();
    let mut row = Live::new(
        SessionCfg {
            name: "agent".into(),
            ..Default::default()
        },
        TitleCapture::default(),
    );
    row.run_id = 42;
    row.set_state(State::Working);
    row.process_running = true;
    row.input.auto_resume_pending = true;
    row.bell = true;
    row.capture.emu = Some(Arc::new(Mutex::new(SessionEmu::new(80, 24))));
    row.capture.reader_token = Some(token.clone());
    row.capture.reader = Some(tokio::spawn(async move {
        wait.await.unwrap();
        completed.send(()).unwrap();
    }));
    let (input, mut received) = mpsc::unbounded_channel();
    row.input.sender = Some(input);
    live.insert("agent".into(), row);

    assert!(
        manager
            .detach_live_locked(
                &mut live,
                "agent",
                DetachCause::ProcessExit {
                    reader_token: Arc::new(())
                }
            )
            .is_none()
    );
    let row = &live["agent"];
    assert_eq!(row.run_id, 42);
    assert_eq!(row.state, State::Working);
    assert!(row.capture.reader.is_some());
    assert!(row.input.sender.is_some());

    let plan = manager
        .detach_live_locked(
            &mut live,
            "agent",
            DetachCause::ProcessExit {
                reader_token: token,
            },
        )
        .unwrap();

    assert!(matches!(
        plan.reader,
        ReaderDisposition::CompletingCurrent(_)
    ));
    assert!(!plan.revoke_grants);
    let row = &live["agent"];
    assert_eq!(row.run_id, 43);
    assert_eq!(row.state, State::Down);
    assert!(!row.process_running);
    assert!(!row.input.auto_resume_pending);
    assert!(!row.bell);
    assert!(
        row.capture.emu.is_none()
            && row.capture.reader_token.is_none()
            && row.capture.reader.is_none()
    );
    assert!(received.recv().await.is_none());

    finish_reader(plan.reader);
    release.send(()).unwrap();
    tokio::time::timeout(Duration::from_secs(2), completion)
        .await
        .unwrap()
        .unwrap();
    std::fs::remove_dir_all(manager.cfg_path.parent().unwrap()).unwrap();
}

#[tokio::test]
async fn stopping_a_temporary_worker_revokes_authority_and_cleans_owned_state() {
    let Some(root) = crate::test_support::isolated() else {
        return;
    };
    let mut cfg = Config::default();
    cfg.daemon.token = "root-secret".into();
    let manager = crate::session::test_manager_with_socket(
        cfg,
        format!("lifecycle-{}", uuid::Uuid::new_v4()),
    );
    let session = temporary_worker_task_session(&manager);
    let task_id = session.task_id.clone();
    let private = root.join("state").join(&session.state_id);
    std::fs::create_dir_all(&private).unwrap();
    std::fs::write(private.join("memory"), "worker state").unwrap();

    let unrelated = root.join("state/unrelated");
    std::fs::create_dir_all(&unrelated).unwrap();

    let mut row = Live::new(session, TitleCapture::default());
    row.ephemeral = true;
    row.set_state(State::Working);
    let (started, running) = tokio::sync::oneshot::channel();
    let (cancelled, cancellation) = tokio::sync::oneshot::channel::<()>();
    row.capture.reader = Some(tokio::spawn(async move {
        let _cancelled = cancelled;
        started.send(()).unwrap();
        std::future::pending::<()>().await;
    }));
    running.await.unwrap();
    manager.live.write().await.insert("child".into(), row);
    manager.temp.write().await.insert(
        "scratch".into(),
        ProjectCfg {
            name: "scratch".into(),
            temp: true,
            ..Default::default()
        },
    );

    let token = manager
        .mint_grant(
            "child".into(),
            vec!["child".into()],
            crate::grant::Level::Rw,
        )
        .await
        .unwrap();
    let cap = manager.resolve_cap(Some(&token)).await.unwrap();
    let mut events = manager.events.subscribe();

    manager.stop("child").await.unwrap();

    assert!(!cap.is_valid());
    assert!(manager.resolve_cap(Some(&token)).await.is_none());
    assert!(!manager.live.read().await.contains_key("child"));
    assert!(!manager.temp.read().await.contains_key("scratch"));
    assert!(!private.exists());
    assert!(unrelated.exists());
    assert!(
        tokio::time::timeout(Duration::from_secs(2), cancellation)
            .await
            .unwrap()
            .is_err()
    );
    assert!(matches!(
        events.try_recv().unwrap().event(),
        Event::Sessions { sessions } if sessions.is_empty()
    ));

    let saved = crate::tasks::Tasks::load(&manager.cfg_path)
        .unwrap()
        .get("host", &task_id)
        .unwrap();
    assert_eq!(saved.status, crate::tasks::Status::Failed);
    assert_eq!(
        saved.note.as_deref(),
        Some("The daemon stopped worker session child.")
    );

    // Cleanup remains safe after the row, process and private directory have gone.
    manager.stop("child").await.unwrap();
    manager.forget("child").await;
    assert_eq!(
        manager.tasks.task_for("host", &task_id).unwrap().note,
        saved.note
    );
    std::fs::remove_dir_all(manager.cfg_path.parent().unwrap()).unwrap();
}

fn temporary_worker_task_session(manager: &Manager) -> SessionCfg {
    let identity = uuid::Uuid::new_v4().to_string();
    let task = manager
        .tasks
        .create_owned(
            crate::tasks::Participant {
                name: "host".into(),
                identity: "host".into(),
            },
            crate::tasks::Participant {
                name: "child".into(),
                identity: identity.clone(),
            },
            "work".into(),
            Some(crate::tasks::WorkerTask {
                session: "child".into(),
                parent: "parent".into(),
                durable: false,
            }),
        )
        .unwrap();
    SessionCfg {
        name: "child".into(),
        project: "scratch".into(),
        worker: true,
        task_id: task.id.clone(),
        state_id: identity,
        ..Default::default()
    }
}
