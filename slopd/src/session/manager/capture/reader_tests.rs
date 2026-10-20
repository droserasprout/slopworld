use super::*;
use crate::session::{AgentLifetime, SessionRole};

#[tokio::test]
async fn due_output_reaches_the_screen_for_watched_and_unwatched_panes() {
    for watched in [false, true] {
        let manager = crate::session::test_manager(Config::default());
        let mut live = Live::new(
            SessionCfg {
                name: "agent".into(),
                ..Default::default()
            },
            TitleCapture::default(),
        );
        live.role = SessionRole::Agent(AgentLifetime::Temporary);
        manager.live.write().await.insert("agent".into(), live);
        let emu = Arc::new(Mutex::new(SessionEmu::new(80, 24)));
        manager
            .live
            .write()
            .await
            .get_mut("agent")
            .unwrap()
            .capture
            .emu = Some(emu.clone());
        let mut events = manager.application.subscribe();
        let mut schedule = DrawSchedule::default();
        assert!(matches!(
            handle_control_line(&emu, b"%output %1 hello"),
            ControlLine::Output {
                redraw_clear: false
            }
        ));
        let now = Instant::now();
        schedule.output(now, watched, false);
        let action = schedule.fire(now + FAST_TICK, watched);
        assert_eq!(action, TickAction::Render { clipboard: watched });
        manager
            .handle_control_tick("agent", &emu, action, &ClipboardPump::default())
            .await;

        assert!(manager.screen("agent").await.is_some());
        assert!(
            manager
                .screen("agent")
                .await
                .unwrap()
                .lines
                .iter()
                .any(|line| line.contains("hello"))
        );
        assert!(
            matches!(events.try_recv(), Ok(event) if matches!(event.event(), Event::Screen { .. }))
        );
        assert!(!schedule.dirty);
        assert_eq!(schedule.deadline(), None);
    }
}

#[tokio::test]
async fn subscription_during_render_leaves_a_clean_tick_pending_for_each_reader() {
    let manager = crate::session::test_manager(Config::default());
    let mut live = Live::new(
        SessionCfg {
            name: "agent".into(),
            ..Default::default()
        },
        TitleCapture::default(),
    );
    live.role = SessionRole::Agent(AgentLifetime::Temporary);
    manager.live.write().await.insert("agent".into(), live);
    let emu = Arc::new(Mutex::new(SessionEmu::new(80, 24)));
    manager
        .live
        .write()
        .await
        .get_mut("agent")
        .unwrap()
        .capture
        .emu = Some(emu.clone());
    emu.lock().unwrap().feed(b"final output");
    let mut changes = manager.signals.watchers_changed.subscribe();
    let mut other_reader = manager.signals.watchers_changed.subscribe();
    let mut schedule = DrawSchedule::default();
    let now = Instant::now();
    schedule.output(now, false, false);
    let action = schedule.fire(now + FAST_TICK, false);
    assert_eq!(action, TickAction::Render { clipboard: false });
    let clipboard = ClipboardPump::default();

    let live_lock = manager.live.write().await;
    let mut tick = Box::pin(manager.handle_control_tick("agent", &emu, action, &clipboard));
    assert!(futures::poll!(tick.as_mut()).is_pending());
    // No receiver is waiting on changed() while frame publication is suspended.
    let watch = manager.watching("agent");
    drop(live_lock);
    tick.await;
    assert!(!schedule.dirty);
    assert_eq!(schedule.deadline(), None);

    // Both readers must retain the wake, even with no more terminal output.
    assert!(matches!(
        futures::poll!(Box::pin(changes.changed())),
        std::task::Poll::Ready(Ok(()))
    ));
    assert!(matches!(
        futures::poll!(Box::pin(other_reader.changed())),
        std::task::Poll::Ready(Ok(()))
    ));
    schedule.wake(Instant::now(), manager.watched("agent"));
    assert_eq!(
        schedule.fire(Instant::now(), manager.watched("agent")),
        TickAction::ClipboardOnly
    );

    drop(watch);
    assert!(matches!(
        futures::poll!(Box::pin(changes.changed())),
        std::task::Poll::Ready(Ok(()))
    ));
    assert!(!manager.watched("agent"));
}

#[test]
fn continuous_output_does_not_postpone_the_first_draw() {
    for watched in [false, true] {
        let now = Instant::now();
        let mut schedule = DrawSchedule::default();
        schedule.output(now, watched, false);
        for millis in 1..32 {
            schedule.output(now + Duration::from_millis(millis), watched, false);
            assert_eq!(schedule.deadline(), Some(now + FAST_TICK));
        }
    }
}

#[test]
fn pager_clear_waits_for_replacement_rows_without_stalling_a_stream() {
    let now = Instant::now();
    let mut schedule = DrawSchedule::default();
    schedule.output(now, true, false);
    assert_eq!(
        schedule.fire(now + FAST_TICK, true),
        TickAction::Render { clipboard: true }
    );

    let clear = now + Duration::from_millis(100);
    schedule.output(clear, true, true);
    assert_eq!(schedule.deadline(), Some(clear + REDRAW_QUIET));
    schedule.wake(clear + Duration::from_millis(1), true);
    assert_eq!(schedule.deadline(), Some(clear + REDRAW_QUIET));
    let next = clear + Duration::from_millis(1);
    schedule.output(next, true, false);
    assert_eq!(schedule.deadline(), Some(next + REDRAW_QUIET));

    for millis in 2..32 {
        schedule.output(clear + Duration::from_millis(millis), true, false);
    }
    assert_eq!(schedule.deadline(), Some(clear + REDRAW_LIMIT));
    assert_eq!(
        schedule.fire(clear + REDRAW_LIMIT, true),
        TickAction::Render { clipboard: true }
    );
    assert_eq!(schedule.deadline(), None);
}

#[test]
fn control_reader_recognizes_a_pager_clear_split_across_output_records() {
    let emu = Mutex::new(SessionEmu::new(80, 24));
    assert!(matches!(
        handle_control_line(&emu, b"%output %1 \\033[H\\033["),
        ControlLine::Output {
            redraw_clear: false
        }
    ));
    assert!(matches!(
        handle_control_line(&emu, b"%output %1 Jfirst row"),
        ControlLine::Output { redraw_clear: true }
    ));
    assert!(matches!(
        handle_control_line(&emu, b"%output %1 second row"),
        ControlLine::Output {
            redraw_clear: false
        }
    ));
}

#[test]
fn watched_output_uses_the_existing_draw_beat() {
    let now = Instant::now();
    let mut schedule = DrawSchedule::default();
    schedule.output(now, true, false);
    assert_eq!(schedule.deadline(), Some(now + FAST_TICK));
    assert_eq!(
        schedule.fire(now + FAST_TICK, true),
        TickAction::Render { clipboard: true }
    );

    // Output arriving near the next beat should not start a fresh 16 ms wait.
    let nearly_due = now + FAST_TICK + Duration::from_millis(14);
    schedule.output(nearly_due, true, false);
    assert_eq!(schedule.deadline(), Some(now + FAST_TICK * 2));
    assert_eq!(
        schedule.fire(now + FAST_TICK * 2, true),
        TickAction::Render { clipboard: true }
    );

    // A quiet pane is ready for an immediate capture without a recurring timer.
    let after_idle = now + Duration::from_millis(100);
    schedule.output(after_idle, true, false);
    assert_eq!(schedule.deadline(), Some(after_idle));
    assert_eq!(
        schedule.fire(after_idle, true),
        TickAction::Render { clipboard: true }
    );
    assert_eq!(schedule.deadline(), None);
}

#[test]
fn clean_reader_has_no_recurring_deadline() {
    let now = Instant::now();
    let mut schedule = DrawSchedule::default();
    assert_eq!(schedule.deadline(), None);

    schedule.output(now, false, false);
    assert_eq!(schedule.deadline(), Some(now + FAST_TICK));
    assert_eq!(
        schedule.fire(now + FAST_TICK, false),
        TickAction::Render { clipboard: false }
    );
    assert_eq!(schedule.deadline(), None);
}

#[test]
fn unwatched_output_keeps_the_render_limit_without_polling_clean_panes() {
    let now = Instant::now();
    let mut schedule = DrawSchedule::default();
    schedule.output(now, false, false);
    assert_eq!(
        schedule.fire(now + FAST_TICK, false),
        TickAction::Render { clipboard: false }
    );

    let next = now + Duration::from_millis(20);
    schedule.output(next, false, false);
    assert_eq!(schedule.deadline(), Some(now + FAST_TICK + SLOW_TICK));
    assert_eq!(
        schedule.fire(now + Duration::from_millis(100), false),
        TickAction::Idle
    );
    assert_eq!(schedule.deadline(), Some(now + FAST_TICK + SLOW_TICK));
    assert_eq!(
        schedule.fire(now + FAST_TICK + SLOW_TICK, false),
        TickAction::Render { clipboard: false }
    );
    assert_eq!(schedule.deadline(), None);
}

#[test]
fn a_new_subscription_wakes_dirty_or_pending_work() {
    let now = Instant::now();
    let mut schedule = DrawSchedule::default();
    schedule.wake(now, true);
    assert_eq!(schedule.fire(now, true), TickAction::ClipboardOnly);

    schedule.output(now, false, false);
    schedule.wake(now + Duration::from_millis(1), true);
    assert_eq!(schedule.deadline(), Some(now + Duration::from_millis(1)));
    assert_eq!(
        schedule.fire(now + Duration::from_millis(1), true),
        TickAction::Render { clipboard: true }
    );
}

#[test]
fn rendering_consumes_output_but_clipboard_wakes_do_not_advance_draw_time() {
    let now = Instant::now();
    let mut schedule = DrawSchedule::default();
    schedule.output(now, true, false);
    assert_eq!(
        schedule.fire(now + FAST_TICK, true),
        TickAction::Render { clipboard: true }
    );
    assert!(!schedule.dirty);
    schedule.wake(now + FAST_TICK + Duration::from_millis(1), true);
    assert_eq!(
        schedule.fire(now + FAST_TICK + Duration::from_millis(1), true),
        TickAction::ClipboardOnly
    );
    assert_eq!(schedule.last_drawn, Some(now + FAST_TICK));
    assert_eq!(schedule.deadline(), None);
    assert_eq!(schedule.fire(now + SLOW_TICK, false), TickAction::Idle);
}

#[test]
fn snapshot_seed_restores_primary_history_cursor_and_title() {
    let mut emu = SessionEmu::new(20, 2);
    seed_emulator(
        &mut emu,
        &crate::tmux::Screen {
            bracketed_paste: true,
            mouse: Default::default(),
            lines: vec!["history".into(), "first".into(), "second".into()],
            cx: 3,
            cy: 1,
            title: "restored title".into(),
            alt_screen: false,
        },
        2,
    );
    let frame = emu.render();
    assert!(!frame.alt_screen);
    assert_eq!((frame.cx, frame.cy), (3, 1));
    assert_eq!(frame.title, "restored title");
    assert!(frame.lines.join("\n").contains("first"));
    assert!(frame.lines.join("\n").contains("second"));
    let (grid, achieved, _, title) = emu.scroll_snapshot(1);
    assert_eq!(achieved, 1);
    let history = SessionEmu::frame_from_grid(&grid, 20, 2, title);
    assert!(history.lines[0].contains("history"));
}

#[test]
fn snapshot_seed_keeps_primary_history_out_of_alternate_screen() {
    let mut emu = SessionEmu::new(20, 2);
    seed_emulator(
        &mut emu,
        &crate::tmux::Screen {
            bracketed_paste: true,
            mouse: Default::default(),
            lines: vec!["history".into(), "alt first".into(), "alt second".into()],
            cx: 2,
            cy: 0,
            title: String::new(),
            alt_screen: true,
        },
        2,
    );
    let frame = emu.render();
    assert!(frame.alt_screen);
    assert_eq!((frame.cx, frame.cy), (2, 0));
    assert!(frame.lines.join("\n").contains("alt first"));
    assert!(frame.lines.join("\n").contains("alt second"));
    assert!(!frame.lines.join("\n").contains("history"));
    emu.feed(b"\x1b[?1049l");
    let primary = emu.render();
    assert!(!primary.alt_screen);
    assert!(primary.lines.join("\n").contains("history"));
    assert!(!primary.lines.join("\n").contains("alt first"));
}

#[tokio::test]
async fn surviving_pane_restores_mouse_reporting_without_new_application_output() {
    use crate::emu::{MouseAction, MouseInput};

    let socket = crate::test_support::TmuxSocket::new();
    let tmux = crate::tmux::Tmux::new(&socket.path);
    for (index, modes) in [
        "",
        "\\033[?1000h",
        "\\033[?1002h\\033[?1006h",
        "\\033[?1003h\\033[?1006h",
        "\\033[?1002h\\033[?1005h",
        "\\033[?1006h", // Encoding alone must not turn on tracking.
        "\\033[?1002h\\033[?1006h\\033[?1002l\\033[?1006l",
    ]
    .into_iter()
    .enumerate()
    {
        let name = format!("mouse-recovery-{index}");
        tmux.spawn(
            &name,
            "/tmp",
            120,
            24,
            &[
                "bash".into(),
                "-c".into(),
                format!("printf '\\033[?1049h{modes}mouse-ready'; read -r unused"),
            ],
            true,
        )
        .await
        .unwrap();
        let cap = tokio::time::timeout(Duration::from_secs(5), async {
            loop {
                let cap = tmux.capture(&name, 0).await.unwrap();
                if cap.lines.iter().any(|line| line.contains("mouse-ready")) {
                    break cap;
                }
                tokio::time::sleep(Duration::from_millis(20)).await;
            }
        })
        .await
        .unwrap();

        // A fresh mirror sees only the retained snapshot: the app is blocked on input.
        let mut recovered = SessionEmu::new(120, 24);
        seed_emulator(&mut recovered, &cap, 24);
        let mut original = SessionEmu::new(120, 24);
        original.feed(modes.replace("\\033", "\x1b").as_bytes());
        let original_frame = original.render();
        let recovered_frame = recovered.render();
        assert!(recovered_frame.alt_screen);
        assert_eq!(
            recovered_frame.app_mouse, original_frame.app_mouse,
            "{index}"
        );
        assert_eq!(recovered_frame.app_drag, original_frame.app_drag, "{index}");
        for (action_index, action) in [
            MouseAction::Press,
            MouseAction::Release,
            MouseAction::Drag,
            MouseAction::WheelUp,
            MouseAction::WheelDown,
        ]
        .into_iter()
        .enumerate()
        {
            let event = MouseInput {
                action,
                button: 0,
                col: 100, // Exercise UTF-8 coordinates as well as legacy and SGR reports.
                row: 2,
            };
            assert_eq!(
                recovered.mouse_report(&event),
                original.mouse_report(&event),
                "mode case {index}, action {action_index}"
            );
        }
    }
}

#[tokio::test]
async fn exit_and_eof_flush_output_received_before_the_last_draw() {
    for exit in [false, true] {
        let manager = crate::session::test_manager(Config::default());
        let emu = Arc::new(Mutex::new(SessionEmu::new(80, 24)));
        let mut row = Live::new(
            SessionCfg {
                name: "agent".into(),
                ..Default::default()
            },
            TitleCapture::default(),
        );
        row.role = SessionRole::Agent(AgentLifetime::Temporary);
        row.capture.emu = Some(emu.clone());
        manager.live.write().await.insert("agent".into(), row);
        let (tx, rx) = mpsc::channel(2);
        let mut pending = vec![b"%output %1 final output".to_vec()];
        if exit {
            pending.push(b"%exit".to_vec());
        }
        drop(tx);
        manager
            .run_control_loop("agent", emu, ControlLineReceiver::new(rx), pending)
            .await;
        assert!(
            manager
                .screen("agent")
                .await
                .unwrap()
                .lines
                .iter()
                .any(|line| line.contains("final output"))
        );
    }
}
