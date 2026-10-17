use super::*;
use crate::usage::{CLAUDE_SESSION, CLAUDE_WEEK, OPENAI_SESSION, Unit, Window, parse};

#[test]
fn optional_rows_are_discovered_before_polling_filters_and_survive_disabling() {
    let mut config = crate::config::Daemon::default();
    config.usage_items.insert(
        CLAUDE_SESSION.into(),
        crate::config::UsageItem {
            poll: true,
            interval_secs: None,
        },
    );
    let response = || {
        parse(
            &serde_json::json!({
                "five_hour": {"utilization": 10},
                "seven_day_opus": {"utilization": 20}
            }),
            "max".into(),
        )
    };
    let now = Instant::now();
    let mut poller = Poller::new("Anthropic", now);
    poller.snap = filter_snapshot(&mut poller, response(), &config, "anthropic", now);
    let published = merge([("anthropic", &poller.snap)], &config);
    assert!(
        published
            .catalog
            .iter()
            .any(|entry| entry.key == "claude_week_opus" && !entry.default_poll)
    );
    assert!(
        !published
            .windows
            .iter()
            .any(|window| window.key == "claude_week_opus")
    );
    assert!(
        !published
            .rows
            .iter()
            .any(|row| row.key == "claude_week_opus" && row.poll)
    );

    config.usage_items.insert(
        "claude_week_opus".into(),
        crate::config::UsageItem {
            poll: true,
            interval_secs: None,
        },
    );
    poller.snap = filter_snapshot(&mut poller, response(), &config, "anthropic", now);
    assert!(
        merge([("anthropic", &poller.snap)], &config)
            .rows
            .iter()
            .any(|row| row.key == "claude_week_opus" && row.poll && row.window.is_some())
    );

    poller.snap.credentials_missing = true;
    let missing = merge([("anthropic", &poller.snap)], &config);
    assert!(
        missing
            .rows
            .iter()
            .all(|row| row.provider != "anthropic" && !row.poll)
    );
    assert!(
        missing
            .catalog
            .iter()
            .any(|entry| entry.key == "claude_week_opus")
    );

    config.usage_items.clear();
    assert_eq!(poller.prepare(&config, "anthropic", now), None);
    let disabled = merge([("anthropic", &poller.snap)], &config);
    assert!(disabled.rows.iter().all(|row| !row.poll));
    assert!(
        disabled
            .catalog
            .iter()
            .any(|entry| entry.key == "claude_week_opus")
    );
    assert!(!poller.clear(now));
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
    assert!(
        panic_snapshot
            .0
            .error
            .as_deref()
            .is_some_and(|error| error.contains("usage poll task failed"))
    );

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
    assert!(
        kept.error
            .as_deref()
            .is_some_and(|error| error.contains("shape slopd does not know"))
    );
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
    // Keep the normal cadence above the Anthropic floor.
    config.daemon.usage_poll_secs = 600;
    let mut pollers = UsagePollers::new();
    pollers.providers[0].poller.interval = Some(600);
    pollers.providers[0].poller.settle(
        Snapshot::failed(&Snapshot::default(), "offline"),
        Some(900),
        600,
        Instant::now(),
    );
    let due = pollers.providers[0].poller.due;
    let first = crate::session::test_manager(config.clone());
    pollers.poll_once(&first).await;
    assert_eq!(pollers.providers[0].poller.due, due);
    assert_eq!(pollers.providers[0].poller.fails, 1);
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
    assert_eq!(pollers.providers[0].poller.due, due);
    assert_eq!(pollers.providers[0].poller.fails, 1);
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

#[test]
fn credential_renewal_shortens_only_an_established_failure_delay() {
    let fixture = std::env::temp_dir().join(format!("credential-stamp-{}", uuid::Uuid::new_v4()));
    std::fs::create_dir(&fixture).unwrap();
    let path = fixture.join("credentials");
    std::fs::write(&path, "credentials").unwrap();
    let file = std::fs::File::options().write(true).open(&path).unwrap();
    let first = SystemTime::UNIX_EPOCH + Duration::from_secs(1000);
    let renewed = first + Duration::from_secs(60);
    file.set_modified(first).unwrap();
    let config = crate::config::Daemon {
        claude_credentials: path.to_str().unwrap().into(),
        ..Default::default()
    };
    let now = Instant::now();
    let mut poller = Poller::new("Anthropic", now);
    poller.settle(
        Snapshot::failed(&Snapshot::default(), "expired"),
        None,
        600,
        now,
    );
    let deadline = poller.due;
    let mut stamp = None;
    watch_anthropic_stamp(&config, &mut poller, &mut stamp, now);
    assert_eq!(
        poller.due, deadline,
        "initial discovery must not bypass backoff"
    );
    assert_eq!(poller.fails, 1);
    watch_anthropic_stamp(&config, &mut poller, &mut stamp, now);
    assert_eq!(
        poller.due, deadline,
        "unchanged credentials preserve backoff"
    );
    file.set_modified(renewed).unwrap();
    watch_anthropic_stamp(&config, &mut poller, &mut stamp, now);
    assert_eq!(poller.due, now);
    assert_eq!(poller.fails, 0);
    poller.settle(
        Snapshot {
            ok: true,
            ..Default::default()
        },
        None,
        600,
        now,
    );
    file.set_modified(renewed + Duration::from_secs(60))
        .unwrap();
    watch_anthropic_stamp(&config, &mut poller, &mut stamp, now);
    assert_eq!(
        poller.due, deadline,
        "successful polling keeps its cadence after renewal"
    );
    std::fs::remove_dir_all(fixture).unwrap();
}

#[tokio::test]
async fn missing_credentials_hide_rows_and_restored_credentials_recover_them() {
    let Some(root) = crate::test_support::isolated() else {
        return;
    };
    let path = root.join("claude.json");
    let mut config = crate::config::Daemon {
        claude_credentials: path.to_str().unwrap().into(),
        ..Default::default()
    };
    config.usage_items.insert(
        CLAUDE_SESSION.into(),
        crate::config::UsageItem {
            poll: true,
            interval_secs: None,
        },
    );
    fn fetch_ok(
        _: super::super::providers::ProviderCredentials,
    ) -> Result<super::super::providers::ProviderResponse, super::super::providers::PollErr> {
        Ok(super::super::providers::ProviderResponse {
            body: serde_json::json!({"five_hour":{"utilization":37}}),
            plan: None,
        })
    }
    fn fetch_offline(
        _: super::super::providers::ProviderCredentials,
    ) -> Result<super::super::providers::ProviderResponse, super::super::providers::PollErr> {
        Err(super::super::providers::PollErr::new("offline"))
    }
    let mut provider = Provider {
        source: "anthropic",
        poller: Poller::new("Anthropic", Instant::now()),
        read: read_anthropic,
        fetch: fetch_ok,
        parse: parse_anthropic,
    };
    async fn poll(provider: &mut Provider, config: &crate::config::Daemon) -> Snapshot {
        provider.poller.due = Instant::now();
        poll_one(provider, config, Instant::now(), 300).await;
        merge([("anthropic", &provider.poller.snap)], config)
    }
    let missing = poll(&mut provider, &config).await;
    assert!(missing.sources.is_empty());
    assert!(!missing.rows.iter().any(|row| row.provider == "anthropic"));
    assert!(
        missing
            .catalog
            .iter()
            .any(|row| row.provider == "anthropic")
    );

    let credentials = r#"{"claudeAiOauth":{"accessToken":"token"}}"#;
    std::fs::write(&path, credentials).unwrap();
    let good = poll(&mut provider, &config).await;
    assert!(good.ok);
    assert_eq!(good.windows.len(), 1);
    assert!(
        good.rows
            .iter()
            .any(|row| row.key == CLAUDE_SESSION && row.poll)
    );

    std::fs::remove_file(&path).unwrap();
    let removed = poll(&mut provider, &config).await;
    assert!(removed.windows.is_empty());
    assert!(!removed.rows.iter().any(|row| row.provider == "anthropic"));
    assert_eq!(provider.poller.snap.windows, good.windows);

    std::fs::write(&path, credentials).unwrap();
    provider.fetch = fetch_offline;
    let stale = poll(&mut provider, &config).await;
    assert_eq!(stale.windows, good.windows);
    assert_eq!(stale.failed_sources, ["anthropic"]);
    assert!(
        stale
            .rows
            .iter()
            .any(|row| row.key == CLAUDE_SESSION && row.stale)
    );

    provider.fetch = fetch_ok;
    let recovered = poll(&mut provider, &config).await;
    assert!(recovered.ok);
    assert!(recovered.failed_sources.is_empty());
}

#[tokio::test]
async fn default_configuration_does_not_poll_any_provider() {
    let manager = crate::session::test_manager(crate::config::Config::default());
    let mut pollers = UsagePollers::new();
    for provider in &mut pollers.providers {
        provider.read = |_| panic!("disabled polling must not read credentials");
    }
    assert_eq!(pollers.poll_once(&manager).await, LOOK);
    let published = manager.usage().await;
    assert!(published.sources.is_empty());
    assert!(published.windows.is_empty());
    assert!(published.rows.iter().all(|row| !row.poll));
}
