use super::*;

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
        live.ephemeral = true;
        manager.live.write().await.insert("agent".into(), live);
        let emu = Arc::new(Mutex::new(SessionEmu::new(80, 24)));
        let mut events = manager.events.subscribe();
        let mut schedule = DrawSchedule::default();
        assert!(matches!(
            manager
                .handle_control_line(&emu, b"%output %1 hello".to_vec())
                .await,
            ControlLine::Output
        ));
        let now = Instant::now();
        schedule.output(now, watched);
        assert!(schedule.fire(now + FAST_TICK, watched));
        manager
            .handle_control_tick(
                "agent",
                &emu,
                watched,
                &mut schedule,
                &Arc::new(AtomicBool::new(false)),
                &Arc::new(tokio::sync::Notify::new()),
            )
            .await;

        assert!(manager.screen("agent").await.is_some());
        assert!(manager.live.read().await["agent"].plain.contains("hello"));
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
    live.ephemeral = true;
    manager.live.write().await.insert("agent".into(), live);
    let emu = Arc::new(Mutex::new(SessionEmu::new(80, 24)));
    emu.lock().unwrap().feed(b"final output");
    let mut changes = manager.signals.watchers_changed.subscribe();
    let mut other_reader = manager.signals.watchers_changed.subscribe();
    let mut schedule = DrawSchedule::default();
    let now = Instant::now();
    schedule.output(now, false);
    assert!(schedule.fire(now + FAST_TICK, false));
    let copying = Arc::new(AtomicBool::new(false));
    let clipboard_wake = Arc::new(tokio::sync::Notify::new());

    let rules = manager.rules.compiled.write().await;
    let mut tick = Box::pin(manager.handle_control_tick(
        "agent",
        &emu,
        false,
        &mut schedule,
        &copying,
        &clipboard_wake,
    ));
    assert!(futures::poll!(tick.as_mut()).is_pending());
    // No receiver is waiting on changed() while frame publication is suspended.
    let watch = manager.watching("agent");
    drop(rules);
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
    assert!(schedule.fire(Instant::now(), manager.watched("agent")));

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
        schedule.output(now, watched);
        for millis in 1..32 {
            schedule.output(now + Duration::from_millis(millis), watched);
            assert_eq!(schedule.deadline(), Some(now + FAST_TICK));
        }
    }
}

#[test]
fn watched_output_uses_the_existing_draw_beat() {
    let now = Instant::now();
    let mut schedule = DrawSchedule::default();
    schedule.output(now, true);
    assert_eq!(schedule.deadline(), Some(now + FAST_TICK));
    assert!(schedule.fire(now + FAST_TICK, true));

    // Output arriving near the next beat should not start a fresh 16 ms wait.
    let nearly_due = now + FAST_TICK + Duration::from_millis(14);
    schedule.output(nearly_due, true);
    assert_eq!(schedule.deadline(), Some(now + FAST_TICK * 2));
    assert!(schedule.fire(now + FAST_TICK * 2, true));

    // A quiet pane is ready for an immediate capture without a recurring timer.
    let after_idle = now + Duration::from_millis(100);
    schedule.output(after_idle, true);
    assert_eq!(schedule.deadline(), Some(after_idle));
    assert!(schedule.fire(after_idle, true));
    assert_eq!(schedule.deadline(), None);
}

#[test]
fn clean_reader_has_no_recurring_deadline() {
    let now = Instant::now();
    let mut schedule = DrawSchedule::default();
    assert_eq!(schedule.deadline(), None);

    schedule.output(now, false);
    assert_eq!(schedule.deadline(), Some(now + FAST_TICK));
    assert!(schedule.fire(now + FAST_TICK, false));
    assert_eq!(schedule.deadline(), None);
}

#[test]
fn unwatched_output_keeps_the_render_limit_without_polling_clean_panes() {
    let now = Instant::now();
    let mut schedule = DrawSchedule::default();
    schedule.output(now, false);
    assert!(schedule.fire(now + FAST_TICK, false));

    let next = now + Duration::from_millis(20);
    schedule.output(next, false);
    assert_eq!(schedule.deadline(), Some(now + FAST_TICK + SLOW_TICK));
    assert!(!schedule.fire(now + Duration::from_millis(100), false));
    assert_eq!(schedule.deadline(), Some(now + FAST_TICK + SLOW_TICK));
    assert!(schedule.fire(now + FAST_TICK + SLOW_TICK, false));
    assert_eq!(schedule.deadline(), None);
}

#[test]
fn a_new_subscription_wakes_dirty_or_pending_work() {
    let now = Instant::now();
    let mut schedule = DrawSchedule::default();
    schedule.wake(now, true);
    assert!(schedule.fire(now, true));

    schedule.output(now, false);
    schedule.wake(now + Duration::from_millis(1), true);
    assert_eq!(schedule.deadline(), Some(now + Duration::from_millis(1)));
    assert!(schedule.fire(now + Duration::from_millis(1), true));
}
