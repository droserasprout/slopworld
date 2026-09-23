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
fn usage_sources_use_rows_without_provider_switches() {
    let mut daemon = crate::config::Daemon::default();
    assert!(provider_enabled(&daemon, "anthropic"));
    assert!(!provider_enabled(&daemon, "openrouter"));
    assert!(provider_enabled(&daemon, "openai"));

    daemon.usage_items.insert(
        OPENROUTER_BALANCE.into(),
        crate::config::UsageItem {
            poll: true,
            interval_secs: None,
        },
    );
    daemon.usage_items.insert(
        CLAUDE_SESSION.into(),
        crate::config::UsageItem {
            poll: false,
            interval_secs: None,
        },
    );
    assert!(provider_enabled(&daemon, "openrouter"));
    assert!(!provider_enabled(&daemon, "anthropic"));
    assert!(!item_enabled(&daemon, "anthropic", "claude_session"));
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
fn anthropic_cache_shares_successes_and_rate_limit_backoff() {
    let path = std::env::temp_dir().join(format!(
        "slopd-anthropic-cache-{}-{}.json",
        std::process::id(),
        now_ms()
    ));
    let body = serde_json::json!({"five_hour": {"utilization": 12}});

    save_anthropic_cache(&path, &body, None);
    assert_eq!(fresh_anthropic_cache(&path), Some(body));

    save_anthropic_rate_limit(&path, RATE_LIMIT_FLOOR);
    assert!(fresh_anthropic_cache(&path).is_none());
    assert!(cached_anthropic_retry(&path).is_some_and(|seconds| seconds > 0));

    std::fs::remove_file(path).unwrap();
}

/// Credential writes can truncate the existing file when a sandbox bind mount prevents atomic replacement.
/// A concurrent read can then encounter incomplete JSON.
/// Retry a parse failure once to avoid reporting a temporary write as a credential error.
#[test]
fn a_half_written_credentials_file_is_read_again_rather_than_failed() {
    let path = std::env::temp_dir().join(format!("slopd-torn-{}.json", std::process::id()));
    let whole = r#"{"claudeAiOauth":{"accessToken":"t","subscriptionType":"max"}}"#;

    // Simulate an empty file after O_TRUNC and before the writer supplies new contents.
    std::fs::write(&path, "").unwrap();
    let writer = {
        let path = path.clone();
        std::thread::spawn(move || {
            std::thread::sleep(std::time::Duration::from_millis(10));
            std::fs::write(&path, whole).unwrap();
        })
    };
    let got = read_creds(&path);
    writer.join().unwrap();
    assert_eq!(
        got.unwrap().token,
        "t",
        "the retry did not pick up the write"
    );

    // Report a parse error if the retry also reads invalid JSON.
    std::fs::write(&path, "half a {").unwrap();
    assert!(read_creds(&path).is_err());

    // Report a missing file as an I/O error without a retry.
    std::fs::remove_file(&path).unwrap();
    assert!(read_creds(&path).is_err());
}

/// Compare expiry timestamps in milliseconds since the Unix epoch.
/// Use realistic timestamp values to expose incorrect unit conversions.
#[test]
fn expiry_reads_milliseconds() {
    // Issued 15:10 UTC-3, eight hours to run.
    let exp = 1_786_327_807_534;
    let refresh = 1_788_638_739_534;

    assert!(expiry_error(Some(exp), Some(refresh), exp - 1).is_none());
    assert!(expiry_error(Some(exp), Some(refresh), exp + 1).is_some());

    // Incorrect conversion of either timestamp changes the expiry result.
    // These comparisons show why callers must supply consistent units.
    assert!(expiry_error(Some(exp), Some(refresh), exp / 1000).is_none());
    assert!(expiry_error(Some(exp * 1000), Some(refresh), exp + 10_800_000).is_none());
}

/// Do not request `claude auth` when the host can renew the access token.
#[test]
fn a_renewable_token_is_not_a_logged_out_host() {
    let now = 1_786_327_807_534;
    let stale = now - 1;
    let live_refresh = now + 30 * 86_400_000;

    let renew = expiry_error(Some(stale), Some(live_refresh), now).unwrap();
    assert!(renew.contains("needs renewal"));
    assert!(!renew.contains("claude auth"));

    let relogin = expiry_error(Some(stale), Some(stale), now).unwrap();
    assert!(relogin.contains("claude auth"));

    // An absent refresh expiry does not prove that the refresh token has expired.
    assert!(!expiry_error(Some(stale), None, now)
        .unwrap()
        .contains("claude auth"));

    // An absent access-token expiry does not prove that the access token has expired.
    assert!(expiry_error(None, Some(stale), now).is_none());
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

#[test]
fn retry_after_parses_seconds_and_http_dates() {
    let now = UNIX_EPOCH + Duration::from_secs(1_000_000);

    assert_eq!(parse_retry_after(" 42 ", now), Some(42));
    assert_eq!(parse_retry_after("0", now), Some(0));
    assert_eq!(
        parse_retry_after(
            &httpdate::fmt_http_date(now + Duration::from_secs(123)),
            now
        ),
        Some(123)
    );
    assert_eq!(
        parse_retry_after(&httpdate::fmt_http_date(now - Duration::from_secs(1)), now),
        Some(0)
    );
    assert_eq!(parse_retry_after("not a retry delay", now), None);
    assert_eq!(parse_retry_after("999999", now), Some(6 * 3600));
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

#[test]
fn resolved_rows_keep_disabled_and_missing_states_explicit() {
    let mut daemon = crate::config::Daemon::default();
    daemon.usage_items.insert(
        CLAUDE_SESSION.into(),
        crate::config::UsageItem {
            poll: false,
            interval_secs: None,
        },
    );

    let rows = resolve_rows(&Snapshot::default(), &daemon);
    let session = rows.iter().find(|row| row.key == CLAUDE_SESSION).unwrap();
    let balance = rows
        .iter()
        .find(|row| row.key == OPENROUTER_BALANCE)
        .unwrap();
    assert!(!session.poll);
    assert!(session.window.is_none());
    assert!(!balance.poll);
    assert!(balance.window.is_none());
}

#[test]
fn a_partial_provider_table_disables_implicit_rows() {
    let mut daemon = crate::config::Daemon::default();
    daemon.usage_items.insert(
        CLAUDE_SESSION.into(),
        crate::config::UsageItem {
            poll: false,
            interval_secs: None,
        },
    );

    let rows = resolve_rows(&Snapshot::default(), &daemon);
    for key in [CLAUDE_SESSION, CLAUDE_WEEK, CLAUDE_SPEND] {
        let row = rows.iter().find(|row| row.key == key).unwrap();
        assert!(!row.poll, "disabled provider leaves no pollable {key} row");
    }
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
        assert_eq!(poller.snap.windows[0].pct, 42.0);
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
    assert_eq!(poller.snap.windows[0].pct, 55.0);
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
    assert_eq!(early.windows[0].pct, 10.0);
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
    assert_eq!(refreshed.windows[0].pct, 90.0);
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
    assert_eq!(refreshed.windows[0].pct, 90.0);
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
