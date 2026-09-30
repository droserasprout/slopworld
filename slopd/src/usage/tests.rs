use super::*;

const REAL: &str = r#"{
        "five_hour": {"utilization": 52.0, "resets_at": "2100-07-26T09:59:59.621619+00:00",
                      "limit_dollars": null, "used_dollars": null},
        "seven_day": {"utilization": 55.0, "resets_at": "2100-07-26T19:59:59.621640+00:00"},
        "seven_day_oauth_apps": null,
        "seven_day_opus": null,
        "seven_day_sonnet": null,
        "limits": [],
        "extra_usage": {"is_enabled": true, "monthly_limit": 10000, "utilization": 20.93},
        "spend": {"percent": 21, "severity": "normal"},
        "member_dashboard_available": false
    }"#;

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

    let mut poller = Poller::new("Anthropic");
    // Before the first answer, every configured row is eligible to request an initial value.
    assert_eq!(
        provider_interval(&daemon, "anthropic", &poller),
        RATE_LIMIT_FLOOR
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
    assert_eq!(backoff(60, 1, Some(30)), RATE_LIMIT_FLOOR);
    assert_eq!(backoff(60, 1, Some(0)), RATE_LIMIT_FLOOR);
    assert_eq!(backoff(60, 9, Some(7200)), 7200);
}

/// Merge resource windows from three providers into one list for the mod.
#[test]
fn sources_merge_into_one_list() {
    let a = parse(&serde_json::from_str(REAL).unwrap(), "max".into());
    let c = parse_credits(
        &serde_json::from_str(r#"{"data":{"total_credits":25,"total_usage":10}}"#).unwrap(),
    );

    let o = parse_openai(
        &serde_json::from_str(r#"{"rate_limit":{"primary_window":{"used_percent":20}}}"#).unwrap(),
    );
    let m = merge(
        [("anthropic", &a), ("openrouter", &c), ("openai", &o)],
        &crate::config::Daemon::default(),
    );
    assert!(m.ok);
    assert_eq!(m.windows.len(), a.windows.len() + 2);
    assert_eq!(m.windows.last().unwrap().key, "openai_session");
    // Only one source supplies a subscription plan in this fixture.
    assert_eq!(m.plan, "max");
    assert!(m.error.is_none());
}

#[tokio::test]
async fn failed_poll_joins_keep_previous_usage_and_aggregate_as_stale() {
    let previous = Snapshot {
        ok: true,
        fetched_ms: 42,
        windows: vec![Window {
            key: CLAUDE_SESSION.into(),
            label: "session".into(),
            pct: 37.0,
            unit: Unit::Pct,
            amount: None,
            limit: None,
            resets_in: None,
        }],
        ..Default::default()
    };

    let panicked = tokio::spawn(async { panic!("provider worker panic") });
    let canceled = tokio::spawn(async { std::future::pending::<(Snapshot, Option<u64>)>().await });
    canceled.abort();

    let panic_snapshot = settle_join(&previous, panicked.await);
    let cancel_snapshot = settle_join(&previous, canceled.await);
    assert!(!panic_snapshot.0.ok);
    assert!(!cancel_snapshot.0.ok);
    assert_eq!(panic_snapshot.0.windows, previous.windows);
    assert_eq!(cancel_snapshot.0.windows, previous.windows);
    assert!(panic_snapshot
        .0
        .error
        .as_deref()
        .is_some_and(|error| error.contains("usage poll task failed")));

    let merged = merge(
        [
            ("anthropic", &panic_snapshot.0),
            ("openai", &cancel_snapshot.0),
        ],
        &crate::config::Daemon::default(),
    );
    assert!(!merged.ok);
    assert_eq!(merged.windows.len(), 2);
    assert_eq!(merged.failed_sources, ["anthropic", "openai"]);
    assert!(merged.error.is_some());
}

#[test]
fn valid_openai_response_omits_non_applicable_session_row() {
    let openai = parse_openai(
            &serde_json::from_str(
                r#"{"plan_type":"free","rate_limit":{"primary_window":{"used_percent":12.5,"limit_window_seconds":604800,"reset_at":4102444800}}}"#,
            )
            .unwrap(),
        );
    let merged = merge([("openai", &openai)], &crate::config::Daemon::default());

    assert!(merged
        .rows
        .iter()
        .any(|row| row.key == OPENAI_WEEK && row.window.is_some()));
    assert!(!merged.rows.iter().any(|row| row.key == OPENAI_SESSION));
}

/// A change to `sources` must trigger an update even when usage values stay the same.
/// The mod uses this list to determine which sources to display.
/// A change to `fetched_ms` alone must not trigger an update.
#[test]
fn a_source_switched_off_is_not_the_same_readout() {
    let base = Snapshot {
        ok: true,
        plan: "max".into(),
        fetched_ms: 1_000,
        sources: vec!["anthropic".into(), "openrouter".into()],
        windows: vec![Window {
            key: "five_hour".into(),
            label: "5h".into(),
            pct: 40.0,
            unit: Unit::Pct,
            amount: None,
            limit: None,
            resets_in: None,
        }],
        ..Default::default()
    };

    let mut aged = base.clone();
    aged.fetched_ms = 99_000;
    assert!(base.same_readout(&aged), "a newer poll of the same figures");

    let mut dropped = base.clone();
    dropped.sources = vec!["anthropic".into()];
    assert!(
        !base.same_readout(&dropped),
        "openrouter was switched off and the readout must be told"
    );
}

/// Report a failed source while preserving values from other sources.
/// A disabled source contributes no values and does not count as a failure.
#[test]
fn one_source_down_keeps_the_others_rows() {
    let a = parse(&serde_json::from_str(REAL).unwrap(), "max".into());
    let c = Snapshot::failed(&Snapshot::default(), "OpenRouter rejected the key (401)");

    let m = merge(
        [
            ("anthropic", &a),
            ("openrouter", &c),
            ("openai", &Snapshot::default()),
        ],
        &crate::config::Daemon::default(),
    );
    assert!(!m.ok);
    assert_eq!(m.windows.len(), a.windows.len());
    assert_eq!(m.failed_sources, vec!["openrouter"]);
    assert!(m.error.unwrap().contains("OpenRouter"));

    // With all sources disabled, return an empty snapshot.
    assert_eq!(
        merge(
            [
                ("anthropic", &Snapshot::default()),
                ("openrouter", &Snapshot::default()),
                ("openai", &Snapshot::default()),
            ],
            &crate::config::Daemon::default(),
        ),
        Snapshot::default()
    );
    // Disabled sources do not change values from the remaining source.
    let only = merge(
        [
            ("anthropic", &a),
            ("openrouter", &Snapshot::default()),
            ("openai", &Snapshot::default()),
        ],
        &crate::config::Daemon::default(),
    );
    assert!(only.ok);
    assert_eq!(only.windows, a.windows);
}

/// Preserve the last successful values when a network request fails.
#[test]
fn failure_keeps_the_last_good_numbers() {
    let good = parse(
        &serde_json::from_str(r#"{"five_hour":{"utilization":50}}"#).unwrap(),
        "max".into(),
    );
    let bad = Snapshot::failed(&good, "network unreachable");

    assert!(!bad.ok);
    assert_eq!(bad.windows, good.windows);
    assert_eq!(bad.plan, "max");
    assert_eq!(bad.error.unwrap(), "network unreachable");
}

#[test]
fn parser_failure_keeps_the_last_good_numbers() {
    let good = parse(
        &serde_json::from_str(r#"{"five_hour":{"utilization":50}}"#).unwrap(),
        "max".into(),
    );
    let parsed = parse(&serde_json::json!({"changed": true}), "pro".into());
    let kept = preserve_parse_failure(&good, parsed);

    assert!(!kept.ok);
    assert_eq!(kept.windows, good.windows);
    assert_eq!(kept.plan, "max");
    assert!(kept
        .error
        .as_deref()
        .is_some_and(|error| error.contains("shape slopd does not know")));
}
fn session_snapshot(pct: u32) -> Snapshot {
    parse(
        &serde_json::json!({"five_hour": {"utilization": pct}}),
        "max".into(),
    )
}

#[test]
fn polling_failure_backoff_recovers_and_disabling_resets_schedules() {
    let mut poller = Poller::new("Anthropic");
    poller.snap = session_snapshot(42);
    for (attempt, delay) in [(1, 60), (2, 120)] {
        let before = Instant::now();
        poller.settle(Snapshot::failed(&poller.snap, "offline"), None, 60);
        assert_eq!(poller.fails, attempt);
        assert_eq!(poller.snap.windows[0].pct.to_bits(), 42.0_f32.to_bits());
        assert_eq!(
            poller.snap.error,
            Some(format!("offline - next try in {}m", delay / 60))
        );
        assert!(poller.due >= before + Duration::from_secs(delay));
        assert!(poller.due <= Instant::now() + Duration::from_secs(delay));
    }
    poller.settle(session_snapshot(55), None, 60);
    assert_eq!(poller.fails, 0);
    assert!(poller.snap.ok);
    assert!(poller.snap.error.is_none());
    assert_eq!(poller.snap.windows[0].pct.to_bits(), 55.0_f32.to_bits());
    poller.item_due.insert(
        CLAUDE_SESSION.into(),
        Instant::now() + Duration::from_secs(600),
    );
    assert!(poller.clear());
    assert_eq!(poller.snap, Snapshot::default());
    assert!(poller.item_due.is_empty());
    assert!(poller.due <= Instant::now());
    assert!(!poller.clear());
}

#[test]
fn row_schedule_retains_values_until_due_and_expires_missing_windows() {
    let daemon = crate::config::Daemon::default();
    let mut poller = Poller::new("Anthropic");
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
    let mut poller = Poller::new("Anthropic");
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
fn implicit_enabled_windows_participate_in_provider_cadence() {
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
    let mut poller = Poller::new("Anthropic");
    assert_eq!(provider_interval(&daemon, "anthropic", &poller), 300);
    poller.snap = parse(
        &serde_json::json!({"five_hour":{"utilization":10},"seven_day":{"utilization":20}}),
        "max".into(),
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

#[tokio::test]
async fn polling_laps_preserve_failure_deadlines_and_publish_placeholder_toggles() {
    let mut config = crate::config::Config::default();
    config.daemon.usage_items.insert(
        OPENAI_SESSION.into(),
        crate::config::UsageItem {
            poll: false,
            interval_secs: None,
        },
    );
    for key in [CLAUDE_SESSION, CLAUDE_WEEK] {
        config.daemon.usage_items.insert(
            key.into(),
            crate::config::UsageItem {
                poll: true,
                interval_secs: Some(600),
            },
        );
    }
    // Keep the normal cadence above the Anthropic floor even with implicit rows.
    config.daemon.usage_poll_secs = 600;
    let mut pollers = UsagePollers::new();
    pollers.anth.interval = Some(600);
    pollers.anth.settle(
        Snapshot::failed(&Snapshot::default(), "offline"),
        Some(900),
        600,
    );
    let due = pollers.anth.due;
    let first = crate::session::test_manager(config.clone());
    pollers.poll_once(&first).await;
    assert_eq!(pollers.anth.due, due);
    assert_eq!(pollers.anth.fails, 1);
    assert!(
        first
            .usage()
            .await
            .rows
            .iter()
            .find(|row| row.key == CLAUDE_SESSION)
            .unwrap()
            .poll
    );

    // Toggle an empty row while another row keeps the provider enabled, and shorten
    // configuration cadence during backoff. Neither change requires an outbound poll.
    config
        .daemon
        .usage_items
        .get_mut(CLAUDE_SESSION)
        .unwrap()
        .poll = false;
    config
        .daemon
        .usage_items
        .get_mut(CLAUDE_WEEK)
        .unwrap()
        .interval_secs = Some(300);
    let changed = crate::session::test_manager(config);
    pollers.poll_once(&changed).await;
    assert_eq!(pollers.anth.due, due);
    assert_eq!(pollers.anth.fails, 1);
    let published = changed.usage().await;
    let session = published
        .rows
        .iter()
        .find(|row| row.key == CLAUDE_SESSION)
        .unwrap();
    assert!(!session.poll);
    assert!(session.window.is_none());
    assert!(session.stale);
    assert!(
        published
            .rows
            .iter()
            .find(|row| row.key == CLAUDE_WEEK)
            .unwrap()
            .poll
    );
}
