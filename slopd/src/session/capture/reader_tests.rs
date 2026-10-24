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
                redraw_clear: false,
                ..
            }
        ));
        let now = Instant::now();
        schedule.output(now, watched, false);
        let action = schedule.fire(now + FAST_TICK, watched);
        assert_eq!(action, TickAction::Render { clipboard: watched });
        manager.handle_control_tick("agent", &emu, action).await;

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

    let live_lock = manager.live.write().await;
    let mut tick = Box::pin(manager.handle_control_tick("agent", &emu, action));
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
fn non_pager_output_after_a_clear_has_a_bounded_draw_deadline() {
    for repeated_clears in [false, true] {
        let now = Instant::now();
        let mut schedule = DrawSchedule::default();
        schedule.output(now, true, true);
        for millis in 1..=32 {
            let received = now + Duration::from_millis(millis);
            schedule.output(received, true, repeated_clears);
            // Even a prompt-shaped operation cannot bypass the non-pager bound.
            schedule.finish_pager_redraw(received, true);
            schedule.wake(received, true);
            assert!(schedule.deadline().unwrap() <= now + REDRAW_LIMIT);
            assert_eq!(
                schedule.fire(received, true),
                if millis == 32 {
                    TickAction::Render { clipboard: true }
                } else {
                    TickAction::Idle
                }
            );
        }
        // Once published, ordinary output resumes the display cadence.
        let drawn = now + REDRAW_LIMIT;
        for millis in 1..=16 {
            schedule.output(drawn + Duration::from_millis(millis), true, false);
            assert_eq!(schedule.deadline(), Some(drawn + FAST_TICK));
        }
        assert_eq!(
            schedule.fire(drawn + FAST_TICK, true),
            TickAction::Render { clipboard: true }
        );
    }
}

#[test]
fn pager_clear_waits_for_quiet_even_when_replacement_exceeds_32_ms() {
    let now = Instant::now();
    let mut schedule = DrawSchedule {
        pager: true,
        ..Default::default()
    };
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

    for millis in 2..80 {
        schedule.output(clear + Duration::from_millis(millis), true, false);
        assert_eq!(
            schedule.fire(clear + Duration::from_millis(millis), true),
            TickAction::Idle
        );
    }
    let settled = clear + Duration::from_millis(79) + REDRAW_QUIET;
    assert_eq!(schedule.deadline(), Some(settled));
    assert_eq!(
        schedule.fire(settled, true),
        TickAction::Render { clipboard: true }
    );
    assert_eq!(schedule.deadline(), None);

    // Once the burst settles, an ordinary stream keeps the normal draw beat.
    let next = settled + Duration::from_millis(1);
    schedule.output(next, true, false);
    for millis in 2..16 {
        schedule.output(settled + Duration::from_millis(millis), true, false);
    }
    assert_eq!(schedule.deadline(), Some(settled + FAST_TICK));
    assert_eq!(
        schedule.fire(settled + FAST_TICK, true),
        TickAction::Render { clipboard: true }
    );
}

#[test]
fn completed_pager_pages_publish_during_continuous_scrolling() {
    let now = Instant::now();
    let mut schedule = DrawSchedule {
        pager: true,
        ..Default::default()
    };
    let mut frames = 0;
    // Key repeat never leaves a 16 ms quiet interval between these pages.
    for page in 0..20 {
        let clear = now + Duration::from_millis(page * 8);
        schedule.output(clear, true, true);
        assert_eq!(schedule.fire(clear, true), TickAction::Idle);
        let complete = clear + Duration::from_millis(4);
        schedule.output(complete, true, false);
        schedule.finish_pager_redraw(complete, true);
        if matches!(schedule.fire(complete, true), TickAction::Render { .. }) {
            frames += 1;
        }
    }
    assert_eq!(frames, 10);
}

#[tokio::test]
async fn managed_pager_control_loop_publishes_completed_pages_before_new_clears() {
    let manager = crate::session::test_manager(Config::default());
    let emu = Arc::new(Mutex::new(SessionEmu::new(80, 24)));
    let mut row = Live::new(
        SessionCfg {
            name: "agent".into(),
            ..Default::default()
        },
        TitleCapture::default(),
    );
    row.role = SessionRole::HostAction;
    row.launch.intent = "diff".into();
    row.capture.emu = Some(emu.clone());
    manager.live.write().await.insert("agent".into(), row);
    let _watch = manager.watching("agent");
    let mut events = manager.application.subscribe();
    let (tx, rx) = mpsc::channel(1);
    // Both records are already in one transport chunk. A completion must publish
    // before queue draining can replace the page with the next erase.
    tx.send(b"%output %1 \\033[H\\033[Jcomplete page\\033[24;1H:\\033[K\n%output %1 \\033[H\\033[Jpartial next page\n".to_vec()).await.unwrap();
    let reader = tokio::spawn({
        let manager = manager.clone();
        async move {
            manager
                .run_control_loop("agent", emu, ControlLineReceiver::new(rx), vec![])
                .await;
        }
    });
    let screen = tokio::time::timeout(Duration::from_secs(5), async {
        loop {
            let event = events.recv().await.unwrap();
            if let Event::Screen { screen } = event.event() {
                break screen.clone();
            }
        }
    })
    .await
    .unwrap();
    assert!(screen.lines[0].contains("complete page"));
    drop(tx);
    tokio::time::timeout(Duration::from_secs(5), reader)
        .await
        .unwrap()
        .unwrap();
}

#[tokio::test]
async fn completed_pager_page_publishes_before_feeding_a_queued_clear() {
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
    let _watch = manager.watching("agent");
    let mut schedule = DrawSchedule {
        pager: true,
        ..Default::default()
    };
    assert!(process_control_line(
        &emu,
        b"%output %1 \\033[H\\033[Jcomplete page\\033[24;1H:\\033[K",
        &mut schedule,
        true,
    ));
    assert_eq!(schedule.redraw, RedrawState::Complete);
    let (tx, rx) = mpsc::channel(1);
    let next = b"%output %1 \\033[H\\033[Jpartial next page";
    tx.send([next.as_slice(), b"\n"].concat()).await.unwrap();
    let mut rx = ControlLineReceiver::new(rx);
    assert!(
        manager
            .draw_control_output("agent", &emu, &mut rx, &mut schedule)
            .await
    );
    let frame = manager.screen("agent").await.unwrap();
    assert!(frame.lines[0].contains("complete page"));
    assert_eq!(rx.recv().await.as_deref(), Some(next.as_slice()));
}

#[test]
fn subscription_wakes_and_repeated_clears_preserve_redraw_quiet() {
    let now = Instant::now();
    let mut schedule = DrawSchedule {
        pager: true,
        ..Default::default()
    };
    schedule.output(now, true, true);
    for millis in [8, 24, 40, 56] {
        let received = now + Duration::from_millis(millis);
        schedule.output(received, true, true);
        schedule.wake(received, true);
        assert_eq!(schedule.deadline(), Some(received + REDRAW_QUIET));
        assert_eq!(schedule.fire(received, true), TickAction::Idle);
    }
    let settled = schedule.deadline().unwrap();
    assert_eq!(
        schedule.fire(settled, true),
        TickAction::Render { clipboard: true }
    );
}

#[test]
fn an_unwatched_redraw_returns_to_the_background_draw_cadence() {
    let now = Instant::now();
    let mut schedule = DrawSchedule::default();
    schedule.output(now, true, false);
    schedule.fire(now + FAST_TICK, true);
    let clear = now + Duration::from_millis(100);
    schedule.output(clear, true, true);
    schedule.output(clear + Duration::from_millis(8), true, false);
    schedule.wake(clear + Duration::from_millis(9), false);
    let due = now + FAST_TICK + SLOW_TICK;
    assert_eq!(
        schedule.fire(due, false),
        TickAction::Render { clipboard: false }
    );
    assert_eq!(schedule.redraw, RedrawState::Streaming);
    assert_eq!(schedule.deadline(), None);
}

#[test]
fn pager_redraw_keeps_the_previous_page_across_delayed_output_records() {
    let now = Instant::now();
    let mut schedule = DrawSchedule {
        pager: true,
        ..Default::default()
    };
    let emu = Mutex::new(SessionEmu::new(20, 3));
    emu.lock()
        .unwrap()
        .feed(b"old first\r\nold second\r\nold third");
    let previous = emu.lock().unwrap().render();
    schedule.output(now, true, false);
    schedule.fire(now + FAST_TICK, true);

    let clear = now + Duration::from_millis(100);
    for (millis, record) in [
        (0, b"%output %1 \\033[H\\033[J".as_slice()),
        (8, b"%output %1 new first".as_slice()),
        (16, b"%output %1 \\015\\012new".as_slice()),
        (24, b"%output %1  second".as_slice()),
        (32, b"%output %1 \\015\\012new".as_slice()),
        (40, b"%output %1  third".as_slice()),
    ] {
        let received = clear + Duration::from_millis(millis);
        if millis > 0 {
            // The timer must not publish the clear or a partially replaced page
            // before the next record. No sleeps or scheduler races in this test.
            assert!(schedule.deadline().unwrap() > received);
            assert_eq!(schedule.fire(received, true), TickAction::Idle);
        }
        let ControlLine::Output { redraw_clear, .. } = handle_control_line(&emu, record) else {
            panic!("expected pager output");
        };
        schedule.output(received, true, redraw_clear);
    }

    let settled = clear + Duration::from_millis(40) + REDRAW_QUIET;
    assert_eq!(schedule.deadline(), Some(settled));
    assert_eq!(
        schedule.fire(settled, true),
        TickAction::Render { clipboard: true }
    );
    let replacement = emu.lock().unwrap().render();
    assert_ne!(replacement.content_hash, previous.content_hash);
    assert!(replacement.lines[0].contains("new first"));
    assert!(replacement.lines[1].contains("new second"));
    assert!(replacement.lines[2].contains("new third"));
}

#[tokio::test]
async fn expired_redraw_timer_consumes_queued_rows_before_publishing() {
    for split_chunks in [false, true] {
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
        let _watch = manager.watching("agent");
        emu.lock().unwrap().feed(b"old first\r\nold second");
        manager.render_and_broadcast("agent", &emu).await;
        let mut events = manager.application.subscribe();

        let mut schedule = DrawSchedule {
            pager: true,
            ..Default::default()
        };
        let clear = Instant::now() - Duration::from_millis(100);
        emu.lock().unwrap().feed(b"\x1b[H\x1b[J");
        schedule.output(clear, true, true);
        assert!(schedule.deadline().unwrap() < Instant::now());

        let (tx, rx) = mpsc::channel(2);
        let first = b"%output %1 new first\n";
        let second = b"%output %1 \\015\\012new second\n";
        if split_chunks {
            tx.send(first.to_vec()).await.unwrap();
            tx.send(second.to_vec()).await.unwrap();
        } else {
            tx.send([first.as_slice(), second.as_slice()].concat())
                .await
                .unwrap();
        }
        // Keep the stream open so this exercises the draw timer, not exit flush.
        let mut rx = ControlLineReceiver::new(rx);
        assert!(
            manager
                .draw_control_output("agent", &emu, &mut rx, &mut schedule)
                .await
        );
        // Draining newly queued rows starts a fresh quiet interval even though
        // the original clear happened long before the timer fired.
        assert!(schedule.dirty);
        assert!(schedule.deadline().unwrap() > Instant::now());
        assert!(events.try_recv().is_err());
        let screen = manager.screen("agent").await.unwrap();
        assert!(screen.lines[0].contains("old first"));
        assert!(screen.lines[1].contains("old second"));
        let action = schedule.fire(schedule.deadline().unwrap(), true);
        manager.handle_control_tick("agent", &emu, action).await;
        let screen = manager.screen("agent").await.unwrap();
        assert!(screen.lines[0].contains("new first"));
        assert!(screen.lines[1].contains("new second"));
        let mut frames = 0;
        while let Ok(event) = events.try_recv() {
            if let Event::Screen { screen } = event.event() {
                frames += 1;
                assert!(screen.lines[0].contains("new first"));
                assert!(screen.lines[1].contains("new second"));
            }
        }
        assert_eq!(frames, 1);
    }
}

#[tokio::test]
async fn queued_clear_reschedules_a_due_draw_until_replacement_settles() {
    let manager = crate::session::test_manager(Config::default());
    let _watch = manager.watching("agent");
    let emu = Mutex::new(SessionEmu::new(80, 24));
    let mut schedule = DrawSchedule::default();
    schedule.wake(Instant::now() - FAST_TICK, true);
    let (tx, rx) = mpsc::channel(1);
    tx.send(b"%output %1 \\033[H\\033[Jfirst row\n%output %1 \\015\\012partial".to_vec())
        .await
        .unwrap();
    let mut rx = ControlLineReceiver::new(rx);
    assert!(
        manager
            .draw_control_output("agent", &emu, &mut rx, &mut schedule)
            .await
    );
    assert!(schedule.dirty);
    assert!(schedule.deadline().unwrap() > Instant::now());
    assert!(manager.screen("agent").await.is_none());

    // Cancelling the nonblocking receive retains a split control record.
    tx.send(b" row\n".to_vec()).await.unwrap();
    assert_eq!(
        rx.recv().await.as_deref(),
        Some(b"%output %1 \\015\\012partial row".as_slice())
    );
}

#[test]
fn control_reader_recognizes_a_pager_clear_split_across_output_records() {
    let emu = Mutex::new(SessionEmu::new(80, 24));
    assert!(matches!(
        handle_control_line(&emu, b"%output %1 \\033[H\\033["),
        ControlLine::Output {
            redraw_clear: false,
            ..
        }
    ));
    assert!(matches!(
        handle_control_line(&emu, b"%output %1 Jfirst row"),
        ControlLine::Output {
            redraw_clear: true,
            ..
        }
    ));
    assert!(matches!(
        handle_control_line(&emu, b"%output %1 second row"),
        ControlLine::Output {
            redraw_clear: false,
            ..
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
        let _watch = manager.watching("agent");
        let (tx, rx) = mpsc::channel(2);
        let mut pending = vec![b"%output %1 \\033[H\\033[Jfinal output".to_vec()];
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

#[tokio::test]
async fn osc52_is_published_once_for_watched_current_readers_including_on_exit() {
    let manager = crate::session::test_manager(Config::default());
    let emu = Arc::new(Mutex::new(SessionEmu::new(80, 24)));
    let mut row = Live::new(
        SessionCfg {
            name: "agent".into(),
            ..Default::default()
        },
        TitleCapture::default(),
    );
    row.run_id = 7;
    row.capture.emu = Some(emu.clone());
    manager.live.write().await.insert("agent".into(), row);
    let mut events = manager.application.subscribe();
    emu.lock().unwrap().feed(b"\x1b]52;c;aGVsbG8=\x07");
    manager.publish_clipboard("agent", &emu).await;
    assert!(events.try_recv().is_err(), "unwatched output stays pending");
    let _watch = manager.watching("agent");
    manager
        .handle_control_tick("agent", &emu, TickAction::ClipboardOnly)
        .await;
    assert!(
        matches!(events.try_recv().unwrap().event(), Event::Clipboard { name, run_id: 7, text } if name == "agent" && text == "hello")
    );
    manager.publish_clipboard("agent", &emu).await;
    assert!(events.try_recv().is_err(), "writes are consumed once");
    emu.lock().unwrap().feed(b"\x1b]52;c;ZmluYWw=\x07");
    manager
        .flush_control_exit("agent", &emu, &DrawSchedule::default())
        .await;
    assert!(
        matches!(events.try_recv().unwrap().event(), Event::Clipboard { text, .. } if text == "final")
    );
    manager
        .flush_control_exit("agent", &emu, &DrawSchedule::default())
        .await;
    assert!(
        events.try_recv().is_err(),
        "repeated exit cleanup cannot replay a copy"
    );

    emu.lock().unwrap().feed(b"\x1b]52;c;c3RhbGU=\x07");
    manager
        .live
        .write()
        .await
        .get_mut("agent")
        .unwrap()
        .capture
        .emu = Some(Arc::new(Mutex::new(SessionEmu::new(80, 24))));
    manager.publish_clipboard("agent", &emu).await;
    manager
        .flush_control_exit("agent", &emu, &DrawSchedule::default())
        .await;
    assert!(
        events.try_recv().is_err(),
        "replaced readers cannot change the clipboard"
    );
}
