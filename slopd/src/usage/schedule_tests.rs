use super::*;
use crate::usage::{CLAUDE_SESSION, CLAUDE_WEEK, parse};

fn session_snapshot(pct: u32) -> Snapshot {
    parse(
        &serde_json::json!({"five_hour": {"utilization": pct}}),
        "max".into(),
    )
}

#[test]
fn absent_anthropic_rows_do_not_set_the_provider_interval() {
    let mut daemon = crate::config::Daemon {
        usage_poll_secs: 120,
        ..Default::default()
    };
    daemon.usage_items.insert(
        "claude_session".into(),
        crate::config::UsageItem {
            poll: true,
            interval_secs: Some(600),
        },
    );
    daemon.usage_items.insert(
        "claude_week_opus".into(),
        crate::config::UsageItem {
            poll: true,
            interval_secs: Some(10),
        },
    );

    let mut poller = Poller::new("Anthropic", Instant::now());
    // Before the first answer, every configured row is eligible to request an initial value.
    assert_eq!(
        provider_interval(&daemon, "anthropic", &poller),
        RATE_LIMIT_FLOOR_SECS
    );

    poller.snap = parse(
        &serde_json::from_str(r#"{"five_hour":{"utilization":50}}"#).unwrap(),
        "max".into(),
    );
    // The optional Opus window was absent, so it cannot make the shared request poll faster.
    assert_eq!(provider_interval(&daemon, "anthropic", &poller), 600);
}

#[test]
fn backoff_doubles_and_caps() {
    assert_eq!(backoff(60, 1, None), 60);
    assert_eq!(backoff(60, 2, None), 120);
    assert_eq!(backoff(60, 4, None), 480);
    // The shift saturates rather than overflowing on a long outage.
    assert_eq!(backoff(60, 30, None), BACKOFF_CAP);
}

/// `Retry-After` overrides exponential backoff and its upper limit.
/// The delay must still meet the minimum rate-limit interval.
#[test]
fn retry_after_beats_the_guess() {
    assert_eq!(backoff(60, 1, Some(900)), 900);
    assert_eq!(backoff(60, 1, Some(30)), RATE_LIMIT_FLOOR_SECS);
    assert_eq!(backoff(60, 1, Some(0)), RATE_LIMIT_FLOOR_SECS);
    assert_eq!(backoff(60, 9, Some(7200)), 7200);
}

#[test]
fn polling_failure_backoff_recovers_and_disabling_resets_schedules() {
    let mut poller = Poller::new("Anthropic", Instant::now());
    poller.snap = session_snapshot(42);
    for (attempt, delay) in [(1, 60), (2, 120)] {
        let before = Instant::now();
        poller.settle(Snapshot::failed(&poller.snap, "offline"), None, 60, before);
        assert_eq!(poller.fails, attempt);
        assert_eq!(poller.snap.windows[0].pct.to_bits(), 42.0_f32.to_bits());
        assert_eq!(poller.snap.error, Some("offline".into()));
        assert_eq!(poller.due, before + Duration::from_secs(delay));
    }
    poller.settle(session_snapshot(55), None, 60, Instant::now());
    assert_eq!(poller.fails, 0);
    assert!(poller.snap.ok);
    assert!(poller.snap.error.is_none());
    assert_eq!(poller.snap.windows[0].pct.to_bits(), 55.0_f32.to_bits());
    poller.item_due.insert(
        CLAUDE_SESSION.into(),
        Instant::now() + Duration::from_secs(600),
    );
    assert!(poller.clear(Instant::now()));
    assert_eq!(poller.snap, Snapshot::default());
    assert!(poller.item_due.is_empty());
    assert!(poller.due <= Instant::now());
    assert!(!poller.clear(Instant::now()));
}

#[test]
fn row_schedule_retains_values_until_due_and_expires_missing_windows() {
    let mut daemon = crate::config::Daemon::default();
    daemon.usage_items.insert(
        CLAUDE_SESSION.into(),
        crate::config::UsageItem {
            poll: true,
            interval_secs: None,
        },
    );
    let mut poller = Poller::new("Anthropic", Instant::now());
    let now = Instant::now();
    let interval = Duration::from_secs(item_interval(&daemon, "anthropic", CLAUDE_SESSION));
    poller.snap = filter_snapshot(&mut poller, session_snapshot(10), &daemon, "anthropic", now);
    assert_eq!(poller.item_due[CLAUDE_SESSION], now + interval);
    let early = filter_snapshot(
        &mut poller,
        session_snapshot(90),
        &daemon,
        "anthropic",
        now + Duration::from_secs(1),
    );
    assert_eq!(early.windows[0].pct.to_bits(), 10.0_f32.to_bits());
    let missing = Snapshot {
        ok: true,
        ..Default::default()
    };
    let early_missing = filter_snapshot(
        &mut poller,
        missing.clone(),
        &daemon,
        "anthropic",
        now + Duration::from_secs(1),
    );
    assert_eq!(early_missing.windows, poller.snap.windows);
    let expired = filter_snapshot(&mut poller, missing, &daemon, "anthropic", now + interval);
    assert!(expired.windows.is_empty());
    let refreshed = filter_snapshot(
        &mut poller,
        session_snapshot(90),
        &daemon,
        "anthropic",
        now + interval,
    );
    assert_eq!(refreshed.windows[0].pct.to_bits(), 90.0_f32.to_bits());
    assert_eq!(poller.item_due[CLAUDE_SESSION], now + interval + interval);
}

#[test]
fn shorter_intervals_refresh_immediately_and_disabled_rows_clear_schedules() {
    let mut daemon = crate::config::Daemon::default();
    daemon.usage_items.insert(
        CLAUDE_SESSION.into(),
        crate::config::UsageItem {
            poll: true,
            interval_secs: Some(3600),
        },
    );
    let mut poller = Poller::new("Anthropic", Instant::now());
    let now = Instant::now();
    poller.snap = filter_snapshot(&mut poller, session_snapshot(10), &daemon, "anthropic", now);
    daemon
        .usage_items
        .get_mut(CLAUDE_SESSION)
        .unwrap()
        .interval_secs = Some(300);
    let refreshed = filter_snapshot(&mut poller, session_snapshot(90), &daemon, "anthropic", now);
    assert_eq!(refreshed.windows[0].pct.to_bits(), 90.0_f32.to_bits());
    assert_eq!(
        poller.item_due[CLAUDE_SESSION],
        now + Duration::from_secs(300)
    );
    let failed = Snapshot::failed(&poller.snap, "offline");
    assert_eq!(
        filter_snapshot(&mut poller, failed.clone(), &daemon, "anthropic", now),
        failed
    );
    daemon.usage_items.get_mut(CLAUDE_SESSION).unwrap().poll = false;
    let disabled = filter_snapshot(&mut poller, session_snapshot(99), &daemon, "anthropic", now);
    assert!(disabled.windows.is_empty());
    assert!(poller.item_due.is_empty());
}

#[test]
fn only_explicitly_enabled_windows_participate_in_provider_cadence() {
    let mut daemon = crate::config::Daemon {
        usage_poll_secs: 300,
        ..Default::default()
    };
    daemon.usage_items.insert(
        CLAUDE_SESSION.into(),
        crate::config::UsageItem {
            poll: true,
            interval_secs: Some(900),
        },
    );
    let mut poller = Poller::new("Anthropic", Instant::now());
    assert_eq!(provider_interval(&daemon, "anthropic", &poller), 900);
    poller.snap = parse(
        &serde_json::json!({"five_hour":{"utilization":10},"seven_day":{"utilization":20}}),
        "max".into(),
    );
    assert_eq!(provider_interval(&daemon, "anthropic", &poller), 900);
    daemon.usage_items.insert(
        CLAUDE_WEEK.into(),
        crate::config::UsageItem {
            poll: true,
            interval_secs: None,
        },
    );
    assert_eq!(provider_interval(&daemon, "anthropic", &poller), 300);
    daemon.usage_items.insert(
        CLAUDE_WEEK.into(),
        crate::config::UsageItem {
            poll: false,
            interval_secs: None,
        },
    );
    assert_eq!(provider_interval(&daemon, "anthropic", &poller), 900);
}
