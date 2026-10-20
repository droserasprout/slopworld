use super::*;
use crate::session::{AgentLifetime, SessionRole};

#[test]
fn teardown_retains_saved_roles_and_only_deletes_temporary_agent_state() {
    let manager = crate::session::test_manager(Config::default());
    for (role, saved, temporary_state) in [
        (SessionRole::Agent(AgentLifetime::Durable), true, false),
        (SessionRole::Agent(AgentLifetime::Temporary), false, true),
        (SessionRole::HostShell, true, false),
        (SessionRole::HostAction, false, false),
    ] {
        let token = Arc::new(());
        for (cause, retained) in [
            (DetachCause::Stop, saved),
            (
                DetachCause::ProcessExit {
                    reader_token: token.clone(),
                },
                saved,
            ),
            (DetachCause::ConfigRemoval, false),
            (DetachCause::Forget, false),
        ] {
            let mut live = HashMap::new();
            let mut row = Live::new(SessionCfg::default(), TitleCapture::default());
            row.role = role;
            row.run_id = 17;
            row.state = State::Working;
            row.capture.reader_token = Some(token.clone());
            live.insert("session".into(), row);

            let plan = manager
                .detach_live_locked(&mut live, "session", &cause)
                .unwrap();
            assert_eq!(live.contains_key("session"), retained, "{role:?}");
            assert_eq!(plan.remove_ephemeral_state, temporary_state, "{role:?}");
            assert_eq!(plan.remove_temp_project, !retained, "{role:?}");
            if retained {
                let row = &live["session"];
                assert_eq!(row.role, role);
                assert_eq!(row.state, State::Down);
                assert_eq!(row.run_id, 18);
                assert!(row.capture.reader_token.is_none());
            }
            // The old capture owner cannot remove a retained row or a replacement.
            assert!(
                manager
                    .detach_live_locked(
                        &mut live,
                        "session",
                        &DetachCause::ProcessExit {
                            reader_token: token.clone(),
                        }
                    )
                    .is_none()
            );
        }
    }
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
                &DetachCause::ProcessExit {
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
            &DetachCause::ProcessExit {
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
    let session = temporary_worker_task_session(&manager).await;
    let task_id = session.task_id.clone();
    let private = root.join("data/sessions").join(&session.state_id);
    std::fs::create_dir_all(&private).unwrap();
    std::fs::write(private.join("memory"), "worker state").unwrap();

    let unrelated = root.join("data/sessions/unrelated");
    std::fs::create_dir_all(&unrelated).unwrap();

    let mut row = Live::new(session, TitleCapture::default());
    row.role = SessionRole::Agent(AgentLifetime::Temporary);
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
    let cap = manager
        .config_authority
        .resolve_cap(Some(&token))
        .await
        .unwrap();
    let mut events = manager.application.subscribe();

    manager.stop("child").await.unwrap();

    assert!(!cap.is_valid());
    assert!(
        manager
            .config_authority
            .resolve_cap(Some(&token))
            .await
            .is_none()
    );
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
        manager
            .task_store()
            .task_for_async("host", &task_id)
            .await
            .unwrap()
            .unwrap()
            .note,
        saved.note
    );
    std::fs::remove_dir_all(manager.cfg_path.parent().unwrap()).unwrap();
}

async fn temporary_worker_task_session(manager: &Manager) -> SessionCfg {
    let identity = crate::storage_id::draft_identity();
    let task = manager
        .create_task_owned(
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
        .await
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

#[tokio::test]
async fn cancelled_stop_keeps_cleanup_and_identity_boundary_until_completion() {
    let socket = crate::test_support::TmuxSocket::new();
    let manager = crate::session::test_manager_with_socket(Config::default(), socket.path.clone());
    let mut row = Live::new(
        SessionCfg {
            name: "temporary".into(),
            project: "temporary-project".into(),
            ..Default::default()
        },
        TitleCapture::default(),
    );
    row.role = SessionRole::Agent(AgentLifetime::Temporary);
    manager.live.write().await.insert("temporary".into(), row);
    manager
        .temp
        .write()
        .await
        .insert("temporary-project".into(), ProjectCfg::default());
    let grants = manager.config_authority.grants_for_test().write().await;
    let task = {
        let manager = manager.clone();
        tokio::spawn(async move { manager.stop("temporary").await })
    };
    tokio::time::timeout(std::time::Duration::from_secs(5), async {
        while manager.live.read().await.contains_key("temporary") {
            tokio::task::yield_now().await;
        }
    })
    .await
    .unwrap();
    task.abort();
    assert!(task.await.unwrap_err().is_cancelled());
    assert!(manager.temp.read().await.contains_key("temporary-project"));
    drop(grants);
    tokio::time::timeout(
        std::time::Duration::from_secs(5),
        manager.session_operation(async {}),
    )
    .await
    .unwrap();
    assert!(!manager.temp.read().await.contains_key("temporary-project"));
    manager.stop("temporary").await.unwrap();
}

#[tokio::test]
async fn restart_rejects_runtime_only_viewers_and_workers_without_stopping_them() {
    let socket = crate::test_support::TmuxSocket::new();
    let manager = crate::session::test_manager_with_socket(Config::default(), socket.path.clone());
    for worker in [false, true] {
        let session = if worker {
            temporary_worker_task_session(&manager).await
        } else {
            SessionCfg {
                name: "viewer".into(),
                ..Default::default()
            }
        };
        let name = session.name.clone();
        let task = session.task_id.clone();
        let mut row = Live::new(session, TitleCapture::default());
        row.role = if !worker {
            SessionRole::HostAction
        } else {
            SessionRole::Agent(AgentLifetime::Temporary)
        };
        row.state = State::Working;
        manager.live.write().await.insert(name.clone(), row);
        manager
            .tmux
            .spawn(
                &name,
                "/tmp",
                80,
                24,
                &["sleep".into(), "60".into()],
                !worker,
            )
            .await
            .unwrap();
        assert!(
            manager
                .restart(&name)
                .await
                .unwrap_err()
                .to_string()
                .contains("cannot be restarted")
        );
        assert!(manager.tmux.exists(&name).await);
        assert_eq!(manager.live.read().await[&name].state, State::Working);
        if worker {
            assert_eq!(
                manager
                    .task_store()
                    .task_for_async("host", &task)
                    .await
                    .unwrap()
                    .unwrap()
                    .status,
                crate::tasks::Status::Queued
            );
        }
    }
}
