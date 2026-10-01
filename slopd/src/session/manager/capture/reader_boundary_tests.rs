use super::*;

#[tokio::test]
async fn failed_startup_attachment_cleans_up_under_the_existing_terminal_writer() {
    let socket = crate::test_support::TmuxSocket::new();
    let manager = crate::session::test_manager_with_socket(Config::default(), socket.path.clone());
    let mut row = Live::new(
        SessionCfg {
            name: "target".into(),
            ..Default::default()
        },
        TitleCapture::default(),
    );
    row.process_running = true;
    row.set_state(State::Working);
    manager.live.write().await.insert("target".into(), row);

    // No tmux pane exists, forcing the real control handshake to fail. Repeat
    // to verify cleanup releases capture ownership and permits another attempt.
    for _ in 0..2 {
        let previous_run = manager.live.read().await["target"].run_id;
        tokio::time::timeout(
            Duration::from_secs(10),
            manager.session_operation(async {
                // This is the lock nesting held by start_inner during start_reader.
                let terminal = manager.terminal_boundary("target").write_owned().await;
                manager.start_reader("target", &terminal).await.unwrap_err();
            }),
        )
        .await
        .expect("startup failure recursively acquired its terminal writer");
        let live = manager.live.read().await;
        let row = &live["target"];
        assert_eq!(row.state, State::Down);
        assert!(!row.process_running);
        assert!(row.run_id > previous_run);
        assert!(row.capture.emu.is_none());
        assert!(row.capture.reader_token.is_none());
        assert!(row.capture.reader.is_none());
    }
    // Delayed cleanup from the failed control task must also release the global
    // boundary instead of preventing unrelated lifecycle work forever.
    tokio::time::timeout(Duration::from_secs(1), manager.session_operation(async {}))
        .await
        .unwrap();
}

#[derive(Clone, Copy, Debug)]
enum Recovery {
    Adoption,
    Disconnect,
    Rename,
}

#[tokio::test]
async fn resize_waits_for_adoption_disconnect_and_rename_attachment() {
    for recovery in [Recovery::Adoption, Recovery::Disconnect, Recovery::Rename] {
        let socket = crate::test_support::TmuxSocket::new();
        let session = SessionCfg {
            name: "target".into(),
            autostart: false,
            ..Default::default()
        };
        let cfg = Config {
            sessions: vec![session.clone()],
            ..Default::default()
        };
        let manager = crate::session::test_manager_with_socket(cfg.clone(), socket.path.clone());
        let name = if matches!(recovery, Recovery::Rename) {
            "old"
        } else {
            "target"
        };
        manager
            .tmux
            .spawn(name, "/tmp", 80, 24, &["sleep".into(), "60".into()], false)
            .await
            .unwrap();
        let token = Arc::new(());
        let mut row = Live::new(session, TitleCapture::default());
        row.cfg.name = name.into();
        row.cols = 80;
        row.rows = 24;
        row.process_running = true;
        row.set_state(State::Working);
        if !matches!(recovery, Recovery::Adoption) {
            row.capture.emu = Some(Arc::new(Mutex::new(SessionEmu::new(80, 24))));
            row.capture.reader_token = Some(token.clone());
        }
        manager.live.write().await.insert(name.into(), row);
        let reached = Arc::new(tokio::sync::Notify::new());
        let release = Arc::new(tokio::sync::Notify::new());
        *manager.reader_attach_pause.lock().unwrap() = Some((reached.clone(), release.clone()));

        let attach = manager.session_operation(async {
            match recovery {
                Recovery::Adoption => {
                    manager.adopt_orphans(&cfg).await;
                }
                Recovery::Disconnect => {
                    manager.reader_ended("target".into(), token).await;
                }
                Recovery::Rename => {
                    let _old = manager.terminal_boundary("old").write_owned().await;
                    manager.tmux.rename("old", "target").await.unwrap();
                    manager.readopt("old", "target").await;
                }
            }
        });
        tokio::pin!(attach);
        tokio::time::timeout(Duration::from_secs(10), async {
            tokio::select! {
                () = reached.notified() => {}
                () = attach.as_mut() => panic!("{recovery:?} did not reach attachment"),
            }
        })
        .await
        .unwrap();

        // Attachment has sampled the old dimensions but has not installed its
        // mirror. A root socket resize must wait without the global boundary.
        let resize = async {
            let _terminal = manager.terminal_input_guard("target").await.unwrap();
            manager.resize("target", 100, 40).await.unwrap();
        };
        tokio::pin!(resize);
        assert!(futures::poll!(resize.as_mut()).is_pending());
        assert!(
            manager
                .terminal_boundary("target")
                .try_read_owned()
                .is_err(),
            "{recovery:?}"
        );
        drop(
            manager
                .terminal_boundary("unrelated")
                .try_read_owned()
                .unwrap(),
        );
        release.notify_one();
        tokio::time::timeout(Duration::from_secs(10), async {
            tokio::join!(attach.as_mut(), resize.as_mut());
        })
        .await
        .unwrap();
        // Repeating the accepted size must leave the mirror consistent too.
        manager.resize("target", 100, 40).await.unwrap();
        {
            let live = manager.live.read().await;
            let row = &live["target"];
            assert_eq!((row.cols, row.rows), (100, 40));
            let emu = row.capture.emu.as_ref().unwrap();
            assert_eq!(emu.lock().unwrap().render().lines.len(), 40);
        }
        manager.stop("target").await.unwrap();
    }
}
