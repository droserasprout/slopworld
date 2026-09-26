use super::super::lifecycle::stop::{reset_process_state, DetachCause, ReaderDisposition};
use super::*;
use std::io::Write;
use std::os::fd::OwnedFd;
use std::os::unix::net::UnixStream;

fn test_frame(text: &str, hash: u64) -> Frame {
    Frame {
        lines: vec![text.into()],
        content_hash: hash,
        activity_hash: hash,
        history: 0,
        cx: 0,
        cy: 0,
        cursor_shape: 0,
        cursor_blink: false,
        app_mouse: false,
        app_drag: false,
        alt_screen: false,
        title: String::new(),
        bell: false,
    }
}

#[tokio::test]
async fn pending_instruction_discovery_is_consumed_once() {
    let manager = crate::session::test_manager(Config::default());
    let mut live = Live::new(
        SessionCfg {
            name: "agent".into(),
            ..Default::default()
        },
        TitleCapture::default(),
    );
    live.input.breadcrumbs = b"previously armed prompt".to_vec();
    live.input.breadcrumbs_pending = true;
    manager.live.write().await.insert("agent".into(), live);
    assert_eq!(
        manager.consume_breadcrumbs("agent", &[]).await.as_deref(),
        Some(b"previously armed prompt".as_slice())
    );
    let live = manager.live.read().await;
    assert!(!live["agent"].input.breadcrumbs_pending);
    assert_eq!(live["agent"].input.breadcrumbs, b"previously armed prompt");
}

#[tokio::test]
async fn breadcrumbs_are_consumed_once_for_a_delivered_prompt() {
    let cfg = Config::default();
    let manager = crate::session::test_manager(cfg);
    let mut live = Live::new(
        SessionCfg {
            name: "agent".into(),
            ..Default::default()
        },
        TitleCapture::default(),
    );
    live.input.breadcrumbs = b"{{ random_tip }}".to_vec();
    live.input.breadcrumbs_pending = true;
    manager.live.write().await.insert("agent".into(), live);

    assert_eq!(
        manager
            .consume_breadcrumbs("agent", &["tip".into()])
            .await
            .as_deref(),
        Some(b"tip".as_slice())
    );
    assert!(manager.consume_breadcrumbs("agent", &[]).await.is_none());
}

#[tokio::test]
async fn control_reader_splits_lines_and_trims_network_endings() {
    let (reader, mut writer) = UnixStream::pair().unwrap();
    let file = std::fs::File::from(OwnedFd::from(reader));
    let mut lines = spawn_control_reader(file);

    writer.write_all(b"first\r\nsecond\nlast").unwrap();
    drop(writer);

    assert_eq!(lines.recv().await.as_deref(), Some(b"first".as_slice()));
    assert_eq!(lines.recv().await.as_deref(), Some(b"second".as_slice()));
    assert_eq!(lines.recv().await.as_deref(), Some(b"last".as_slice()));
    assert_eq!(lines.recv().await, None);
}

#[tokio::test]
async fn control_line_receiver_reassembles_chunk_boundaries() {
    let (tx, rx) = mpsc::channel(4);
    let mut lines = ControlLineReceiver::new(rx);
    tx.send(b"first\r".to_vec()).await.unwrap();
    tx.send(b"\nsecond\nlast".to_vec()).await.unwrap();
    drop(tx);

    assert_eq!(lines.recv().await.as_deref(), Some(b"first".as_slice()));
    assert_eq!(lines.recv().await.as_deref(), Some(b"second".as_slice()));
    assert_eq!(lines.recv().await.as_deref(), Some(b"last".as_slice()));
    assert_eq!(lines.recv().await, None);
}

#[test]
fn bounded_control_sender_exits_when_receiver_is_cancelled() {
    let (tx, rx) = mpsc::channel(1);
    tx.try_send(vec![1]).unwrap();
    let sender = std::thread::spawn(move || tx.blocking_send(vec![2]).is_err());
    drop(rx);
    assert!(sender.join().unwrap());
}

#[tokio::test]
async fn control_line_receiver_resumes_long_line_after_cancelled_recv() {
    let (tx, rx) = mpsc::channel(4);
    let mut lines = ControlLineReceiver::new(rx);
    let chunk = vec![b'x'; CONTROL_QUEUE_CHUNK_BYTES];
    for _ in 0..3 {
        tx.send(chunk.clone()).await.unwrap();
    }

    // Like the control loop's timer branch, cancel recv while a partial line is pending.
    assert!(
        tokio::time::timeout(Duration::from_millis(10), lines.recv())
            .await
            .is_err()
    );
    assert_eq!(lines.pending.len(), 3 * CONTROL_QUEUE_CHUNK_BYTES);
    assert_eq!(lines.searched, lines.pending.len());

    tx.send(b"\r\nnext\nfinal\r".to_vec()).await.unwrap();
    drop(tx);
    assert_eq!(
        lines.recv().await.unwrap(),
        vec![b'x'; 3 * CONTROL_QUEUE_CHUNK_BYTES]
    );
    assert_eq!(lines.recv().await.as_deref(), Some(b"next".as_slice()));
    assert_eq!(lines.recv().await.as_deref(), Some(b"final".as_slice()));
    assert_eq!(lines.recv().await, None);
    assert_eq!(lines.recv().await, None);
}

#[tokio::test]
async fn control_attach_wait_returns_initial_output_after_success() {
    let (tx, rx) = mpsc::channel(4);
    let mut rx = ControlLineReceiver::new(rx);
    tx.send(b"%begin 1 2 0\n".to_vec()).await.unwrap();
    tx.send(b"%output %0 hello\n".to_vec()).await.unwrap();
    tx.send(b"%end 1 2 0\n".to_vec()).await.unwrap();

    let pending = wait_for_control_attach(&mut rx).await.unwrap();
    assert_eq!(
        pending,
        vec![b"%begin 1 2 0".to_vec(), b"%output %0 hello".to_vec()]
    );
}

#[tokio::test]
async fn control_attach_wait_reports_tmux_error_detail() {
    let (tx, rx) = mpsc::channel(4);
    let mut rx = ControlLineReceiver::new(rx);
    tx.send(b"%begin 1 2 0\n".to_vec()).await.unwrap();
    tx.send(b"can't find session: missing\n".to_vec())
        .await
        .unwrap();
    tx.send(b"%error 1 2 0\n".to_vec()).await.unwrap();

    let error = wait_for_control_attach(&mut rx).await.unwrap_err();
    assert_eq!(error.to_string(), "can't find session: missing");
}

#[tokio::test]
async fn stale_reader_cannot_mark_a_replacement_down() {
    let manager = crate::session::test_manager(Config::default());
    let current = Arc::new(());
    let stale = Arc::new(());
    let mut live = Live::new(
        SessionCfg {
            name: "agent".into(),
            ..Default::default()
        },
        TitleCapture::default(),
    );
    live.state = State::Working;
    live.capture.reader_token = Some(current.clone());
    live.capture.emu = Some(Arc::new(Mutex::new(SessionEmu::new(80, 24))));
    manager.live.write().await.insert("agent".into(), live);

    manager.mark_down("agent", &stale).await;

    let live = manager.live.read().await;
    assert_eq!(live["agent"].state, State::Working);
    assert!(live["agent"].capture.emu.is_some());
    assert!(live["agent"]
        .capture
        .reader_token
        .as_ref()
        .is_some_and(|token| Arc::ptr_eq(token, &current)));
}

#[tokio::test]
async fn current_reader_exit_resets_durable_process_state_once() {
    let manager = crate::session::test_manager(Config::default());
    let reader_token = Arc::new(());
    let mut live = Live::new(
        SessionCfg {
            name: "agent".into(),
            ..Default::default()
        },
        TitleCapture::default(),
    );
    live.state = State::Working;
    live.bell = true;
    live.input.auto_resume_pending = true;
    live.capture.emu = Some(Arc::new(Mutex::new(SessionEmu::new(80, 24))));
    live.capture.reader_token = Some(reader_token.clone());
    live.capture.reader = Some(tokio::spawn(std::future::pending()));
    live.input.sender = Some(mpsc::unbounded_channel().0);
    manager.live.write().await.insert("agent".into(), live);

    let plan = {
        let mut live = manager.live.write().await;
        manager.detach_live_locked(
            &mut live,
            "agent",
            DetachCause::ProcessExit { reader_token },
        )
    };
    assert!(matches!(
        plan.as_ref().map(|plan| &plan.reader),
        Some(ReaderDisposition::CompletingCurrent(_))
    ));

    {
        let live = manager.live.read().await;
        let live = &live["agent"];
        assert_eq!(live.state, State::Down);
        assert!(!live.bell);
        assert!(!live.input.auto_resume_pending);
        assert!(live.capture.emu.is_none());
        assert!(live.capture.reader_token.is_none());
        assert!(live.input.sender.is_none());
    }

    manager.execute_cleanup(plan.unwrap()).await;
    assert!(manager.live.read().await.contains_key("agent"));
}

#[test]
fn title_submission_reports_uncertain_editing_without_a_submission() {
    let mut composer = Composer::ready();
    let keys = vec!["Up".to_string()];
    let (submission, uncertain) = build_title_submission(&mut composer, &keys, false);
    assert!(submission.is_none());
    assert!(uncertain);
}

#[test]
fn title_submission_uses_all_literal_chunks_before_enter() {
    let mut composer = Composer::ready();
    let chunks = vec!["fix ".to_string(), "the parser".to_string()];
    assert!(build_title_submission(&mut composer, &chunks, true)
        .0
        .is_none());

    let enter = vec!["Enter".to_string()];
    let (submission, uncertain) = build_title_submission(&mut composer, &enter, false);
    let Some(Submission::Prompt(prompt)) = submission else {
        panic!("expected submitted prompt")
    };
    assert_eq!(prompt, "fix the parser");
    assert!(!uncertain);
}

#[tokio::test]
async fn applying_a_frame_updates_state_screen_and_bell_events() {
    let manager = crate::session::test_manager(Config::default());
    manager.live.write().await.insert(
        "agent".into(),
        Live::new(
            SessionCfg {
                name: "agent".into(),
                ..Default::default()
            },
            TitleCapture::default(),
        ),
    );
    let mut events = manager.events.subscribe();
    let frame = Frame {
        lines: vec!["hello".into()],
        content_hash: 0,
        activity_hash: 0,
        history: 0,
        cx: 2,
        cy: 0,
        cursor_shape: 1,
        cursor_blink: true,
        app_mouse: false,
        app_drag: false,
        alt_screen: false,
        title: "shell".into(),
        bell: true,
    };

    manager.apply_frame("agent", frame).await;
    let live = manager.live.read().await;
    let live = live.get("agent").unwrap();
    assert_eq!(live.state, State::Working);
    assert_eq!(live.seq, 1);
    assert!(live.bell);
    assert_eq!(
        live.screen
            .as_ref()
            .unwrap()
            .lines
            .iter()
            .map(AsRef::as_ref)
            .collect::<Vec<&str>>(),
        ["hello"]
    );

    assert!(
        matches!(events.try_recv(), Ok(event) if matches!(event.event(), Event::Screen { .. }))
    );
    assert!(
        matches!(events.try_recv(), Ok(event) if matches!(event.event(), Event::Sessions { .. }))
    );

    manager
        .apply_frame(
            "agent",
            Frame {
                lines: vec!["hello".into()],
                content_hash: 0,
                activity_hash: 0,
                history: 0,
                cx: 2,
                cy: 0,
                cursor_shape: 1,
                cursor_blink: true,
                app_mouse: false,
                app_drag: false,
                alt_screen: false,
                title: "shell".into(),
                bell: false,
            },
        )
        .await;
    assert_eq!(manager.live.read().await["agent"].seq, 1);
    assert!(events.try_recv().is_err());
}

#[tokio::test]
async fn metadata_only_frame_updates_the_screen_and_emits_no_content_change() {
    let manager = crate::session::test_manager(Config::default());
    manager.live.write().await.insert(
        "agent".into(),
        Live::new(
            SessionCfg {
                name: "agent".into(),
                ..Default::default()
            },
            TitleCapture::default(),
        ),
    );
    let mut events = manager.events.subscribe();
    let frame = |cx: u16, title: &str, app_mouse: bool| Frame {
        lines: vec!["same".into()],
        content_hash: 7,
        activity_hash: 7,
        history: 0,
        cx,
        cy: 0,
        cursor_shape: 2,
        cursor_blink: false,
        app_mouse,
        app_drag: false,
        alt_screen: false,
        title: title.into(),
        bell: false,
    };

    manager.apply_frame("agent", frame(1, "old", false)).await;
    while events.try_recv().is_ok() {}
    manager.apply_frame("agent", frame(4, "new", true)).await;

    let live = manager.live.read().await;
    let screen = live["agent"].screen.as_ref().unwrap();
    assert_eq!(live["agent"].seq, 2);
    assert_eq!((screen.cx, screen.cy), (4, 0));
    assert_eq!(screen.title, "new");
    assert!(screen.app_mouse);
    assert!(
        matches!(events.try_recv(), Ok(event) if matches!(event.event(), Event::Screen { .. }))
    );
    assert!(
        matches!(events.try_recv(), Ok(event) if matches!(event.event(), Event::Sessions { .. }))
    );
    assert!(events.try_recv().is_err());
}

#[tokio::test]
async fn cursor_only_frame_does_not_keep_a_quiet_session_working() {
    let manager = crate::session::test_manager(Config::default());
    manager.live.write().await.insert(
        "agent".into(),
        Live::new(
            SessionCfg {
                name: "agent".into(),
                ..Default::default()
            },
            TitleCapture::default(),
        ),
    );

    let mut emu = SessionEmu::new(80, 24);
    emu.feed(b"same\x1b[3 q");
    manager.apply_frame("agent", emu.render()).await;
    {
        let mut live = manager.live.write().await;
        live.get_mut("agent").unwrap().last_change = 0;
    }
    // TUIs change cursor visibility and style without changing their screen text.
    emu.feed(b"\x1b[?25l\x1b[6 q");
    manager.apply_frame("agent", emu.render()).await;
    {
        let live = manager.live.read().await;
        assert_eq!(live["agent"].state, State::Idle);
        assert_eq!(live["agent"].last_change, 0);
    }
    emu.feed(b"\x1b[?25h\x1b[3 q\x1b[5G");
    manager.apply_frame("agent", emu.render()).await;

    let live = manager.live.read().await;
    let live = live.get("agent").unwrap();
    assert_eq!(live.state, State::Idle);
    assert_eq!(live.last_change, 0);
    assert_eq!(live.seq, 3);
    assert_eq!(live.screen.as_ref().unwrap().cx, 4);
}

#[tokio::test]
async fn faint_prompt_particles_do_not_keep_a_quiet_session_working() {
    let manager = crate::session::test_manager(Config::default());
    let mut live = Live::new(
        SessionCfg {
            name: "agent".into(),
            ..Default::default()
        },
        TitleCapture::default(),
    );
    live.ephemeral = true;
    manager.live.write().await.insert("agent".into(), live);
    *manager.rules.compiled.write().await = vec![(
        State::Working,
        regex::Regex::new("esc to interrupt").unwrap(),
    )];
    let mut emu = SessionEmu::new(80, 24);
    // Captured prompt animation: background RGB 30, moving single-dot Braille
    // foreground RGB 13..28. Include a stale working match above the prompt.
    emu.feed(b"old output (esc to interrupt)\r\n\x1b[48;2;30;30;30m  Ask Codex to do anything");
    manager.apply_frame("agent", emu.render()).await;
    manager
        .live
        .write()
        .await
        .get_mut("agent")
        .unwrap()
        .last_change = 0;
    let mut events = manager.events.subscribe();
    for (dot, gray) in [('⠁', 13), ('⠈', 28), ('⢀', 20), (' ', 20)] {
        emu.feed(format!("\x1b[2;1H\x1b[38;2;{gray};{gray};{gray}m{dot}").as_bytes());
        manager.apply_frame("agent", emu.render()).await;
        let live = manager.live.read().await;
        assert_eq!(live["agent"].state, State::Idle);
        assert_eq!(live["agent"].last_change, 0);
    }
    let mut idle_announced = false;
    let mut screens = 0;
    while let Ok(event) = events.try_recv() {
        match event.event() {
            Event::Sessions { sessions } => {
                idle_announced |= sessions
                    .iter()
                    .any(|s| s.name == "agent" && s.state == State::Idle);
            }
            Event::Screen { .. } => screens += 1,
            _ => {}
        }
    }
    assert!(idle_announced, "sidebar must receive the idle transition");
    assert_eq!(screens, 4, "cosmetic animation must still be rendered");
    // Real output away from the decorative prompt resumes activity immediately.
    emu.feed(b"\x1b[Hnew output");
    manager.apply_frame("agent", emu.render()).await;
    let live = manager.live.read().await;
    assert_eq!(live["agent"].state, State::Working);
    assert_ne!(live["agent"].last_change, 0);
}

#[tokio::test]
async fn applying_a_frame_uses_the_emulator_content_hash() {
    let manager = crate::session::test_manager(Config::default());
    manager.live.write().await.insert(
        "agent".into(),
        Live::new(
            SessionCfg {
                name: "agent".into(),
                ..Default::default()
            },
            TitleCapture::default(),
        ),
    );
    let mut events = manager.events.subscribe();
    let frame = |content_hash| Frame {
        lines: vec!["same".into()],
        content_hash,
        activity_hash: content_hash,
        history: 0,
        cx: 0,
        cy: 0,
        cursor_shape: 0,
        cursor_blink: false,
        app_mouse: false,
        app_drag: false,
        alt_screen: false,
        title: String::new(),
        bell: false,
    };

    manager.apply_frame("agent", frame(11)).await;
    while events.try_recv().is_ok() {}
    manager.apply_frame("agent", frame(22)).await;

    assert_eq!(manager.live.read().await["agent"].seq, 2);
    assert!(
        matches!(events.try_recv(), Ok(event) if matches!(event.event(), Event::Screen { .. }))
    );
}

#[tokio::test]
async fn clearing_a_bell_is_idempotent_and_announces_the_change() {
    let manager = crate::session::test_manager(Config::default());
    let mut live = Live::new(
        SessionCfg {
            name: "agent".into(),
            ..Default::default()
        },
        TitleCapture::default(),
    );
    live.bell = true;
    manager.live.write().await.insert("agent".into(), live);
    let mut events = manager.events.subscribe();

    manager.clear_bell("agent").await;
    assert!(!manager.live.read().await["agent"].bell);
    assert!(
        matches!(events.try_recv(), Ok(event) if matches!(event.event(), Event::Sessions { .. }))
    );
    manager.clear_bell("agent").await;
    assert!(events.try_recv().is_err());
}

#[tokio::test]
async fn capture_does_not_commit_after_run_replacement() {
    let manager = crate::session::test_manager(Config::default());
    manager.live.write().await.insert(
        "agent".into(),
        Live::new(
            SessionCfg {
                name: "agent".into(),
                ..Default::default()
            },
            TitleCapture::default(),
        ),
    );
    let rules = manager.rules.compiled.write().await;
    let task = tokio::spawn({
        let manager = manager.clone();
        async move { manager.apply_frame("agent", test_frame("old", 1)).await }
    });
    tokio::task::yield_now().await;
    manager.live.write().await.get_mut("agent").unwrap().run_id += 1;
    drop(rules);
    task.await.unwrap();

    let live = manager.live.read().await;
    assert_eq!(live["agent"].seq, 0);
    assert!(live["agent"].screen.is_none());
}

#[tokio::test]
async fn capture_does_not_revive_a_stopped_session() {
    let manager = crate::session::test_manager(Config::default());
    manager.live.write().await.insert(
        "agent".into(),
        Live::new(
            SessionCfg {
                name: "agent".into(),
                ..Default::default()
            },
            TitleCapture::default(),
        ),
    );
    manager.apply_frame("agent", test_frame("old", 1)).await;

    let rules = manager.rules.compiled.write().await;
    let task = tokio::spawn({
        let manager = manager.clone();
        async move { manager.apply_frame("agent", test_frame("stale", 2)).await }
    });
    tokio::task::yield_now().await;
    reset_process_state(manager.live.write().await.get_mut("agent").unwrap());
    drop(rules);
    task.await.unwrap();

    let live = manager.live.read().await;
    assert_eq!(live["agent"].state, State::Down);
    assert_eq!(live["agent"].seq, 1);
    assert!(live["agent"].screen.is_none());
}

#[tokio::test]
async fn capture_retries_after_competing_idle_decay() {
    let manager = crate::session::test_manager(Config::default());
    let mut live = Live::new(
        SessionCfg {
            name: "agent".into(),
            ..Default::default()
        },
        TitleCapture::default(),
    );
    live.ephemeral = true;
    manager.live.write().await.insert("agent".into(), live);
    manager.apply_frame("agent", test_frame("old", 1)).await;
    let mut events = manager.events.subscribe();

    let rules = manager.rules.compiled.write().await;
    let mut capture = Box::pin(manager.apply_frame("agent", test_frame("new", 2)));
    assert!(futures::poll!(capture.as_mut()).is_pending());
    manager
        .live
        .write()
        .await
        .get_mut("agent")
        .unwrap()
        .set_state(State::Idle);
    drop(rules);
    capture.await;

    let live = manager.live.read().await;
    assert_eq!(live["agent"].state, State::Working);
    assert_eq!(live["agent"].seq, 2);
    assert_eq!(live["agent"].plain.as_ref(), "new");
    assert!(
        matches!(events.try_recv(), Ok(event) if matches!(event.event(), Event::Screen { .. }))
    );
}

#[tokio::test]
async fn rule_cache_is_reused_by_text_and_invalidated_by_revision() {
    let manager = crate::session::test_manager(Config::default());
    manager.live.write().await.insert(
        "agent".into(),
        Live::new(
            SessionCfg {
                name: "agent".into(),
                ..Default::default()
            },
            TitleCapture::default(),
        ),
    );
    *manager.rules.compiled.write().await =
        vec![(State::Waiting, regex::Regex::new("same").unwrap())];
    manager.apply_frame("agent", test_frame("same", 1)).await;
    {
        let live = manager.live.read().await;
        assert_eq!(live["agent"].rule_cache.as_ref().unwrap().revision, 0);
        assert_eq!(
            live["agent"].rule_cache.as_ref().unwrap().matched,
            Some(State::Waiting)
        );
    }

    *manager.rules.compiled.write().await =
        vec![(State::Working, regex::Regex::new("same").unwrap())];
    manager
        .rules
        .revision
        .fetch_add(1, std::sync::atomic::Ordering::AcqRel);
    manager.apply_frame("agent", test_frame("same", 1)).await;

    let live = manager.live.read().await;
    assert_eq!(live["agent"].state, State::Working);
    assert_eq!(live["agent"].rule_cache.as_ref().unwrap().revision, 1);
    assert_eq!(
        live["agent"].rule_cache.as_ref().unwrap().matched,
        Some(State::Working)
    );
}

#[tokio::test]
async fn capture_retries_when_rules_reload_wins_before_commit() {
    let manager = crate::session::test_manager(Config::default());
    manager.live.write().await.insert(
        "agent".into(),
        Live::new(
            SessionCfg {
                name: "agent".into(),
                ..Default::default()
            },
            TitleCapture::default(),
        ),
    );
    *manager.rules.compiled.write().await =
        vec![(State::Waiting, regex::Regex::new("same").unwrap())];

    // Keep the snapshot's locks occupied while the frame finishes its old classification.
    // The reload then wins the commit boundary deterministically.
    let old_rules = manager.rules.compiled.read().await;
    let live_read = manager.live.read().await;
    let task = tokio::spawn({
        let manager = manager.clone();
        async move { manager.apply_frame("agent", test_frame("same", 1)).await }
    });
    for _ in 0..3 {
        tokio::task::yield_now().await;
    }

    let (ready_tx, ready_rx) = tokio::sync::oneshot::channel();
    let reload = tokio::spawn({
        let manager = manager.clone();
        async move {
            let mut rules = manager.rules.compiled.write().await;
            *rules = vec![(State::Working, regex::Regex::new("same").unwrap())];
            manager
                .rules
                .revision
                .fetch_add(1, std::sync::atomic::Ordering::AcqRel);
            let _ = ready_tx.send(());
        }
    });
    drop(old_rules);
    ready_rx.await.unwrap();
    drop(live_read);
    reload.await.unwrap();
    task.await.unwrap();

    let live = manager.live.read().await;
    assert_eq!(live["agent"].state, State::Working);
    assert_eq!(live["agent"].rule_cache.as_ref().unwrap().revision, 1);
    assert_eq!(
        live["agent"].rule_cache.as_ref().unwrap().matched,
        Some(State::Working)
    );
}
