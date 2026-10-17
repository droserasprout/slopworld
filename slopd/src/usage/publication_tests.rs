use super::*;
use crate::usage::{
    CLAUDE_SESSION, OPENAI_SESSION, OPENAI_WEEK, Window, parse, parse_credits, parse_openai,
};

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
fn poll_reporting_keeps_retry_wording_and_retained_values() {
    let mut snapshot = Snapshot {
        ok: false,
        error: Some("offline".into()),
        fetched_ms: 42,
        ..Default::default()
    };
    report_poll(
        "Anthropic",
        &mut snapshot,
        PollTransition {
            was_ok: true,
            delay: 120,
        },
    );
    assert_eq!(snapshot.error.as_deref(), Some("offline - next try in 2m"));
    assert_eq!(snapshot.fetched_ms, 42);
}

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
        &enabled_daemon(),
    );
    assert!(m.ok);
    assert_eq!(m.windows.len(), a.windows.len() + 2);
    assert_eq!(m.windows.last().unwrap().key, "openai_session");
    // Only one source supplies a subscription plan in this fixture.
    assert_eq!(m.plan, "max");
    assert!(m.error.is_none());
}

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
        &enabled_daemon(),
    );
    assert!(!m.ok);
    assert_eq!(m.windows.len(), a.windows.len());
    assert_eq!(m.failed_sources, vec!["openrouter"]);
    assert!(m.error.unwrap().contains("OpenRouter"));

    // Disabled sources do not change values from the remaining source.
    let only = merge(
        [
            ("anthropic", &a),
            ("openrouter", &Snapshot::default()),
            ("openai", &Snapshot::default()),
        ],
        &enabled_daemon(),
    );
    assert!(only.ok);
    assert_eq!(only.windows, a.windows);
}

#[test]
fn empty_snapshots_publish_enabled_placeholders_and_disabled_editable_rows() {
    let empty = Snapshot::default();
    let parts = [
        ("anthropic", &empty),
        ("openrouter", &empty),
        ("openai", &empty),
    ];
    let mut config = enabled_daemon();
    let pending = merge(parts, &config);
    assert!(!pending.ok);
    assert_eq!(pending.sources, ["anthropic", "openrouter", "openai"]);
    assert!(
        pending
            .rows
            .iter()
            .any(|row| row.key == CLAUDE_SESSION && row.poll)
    );
    assert!(
        pending
            .rows
            .iter()
            .any(|row| row.key == OPENAI_SESSION && row.poll)
    );
    assert!(pending.rows.iter().all(|row| row.window.is_none()));

    for row in &pending.rows {
        config.usage_items.insert(
            row.key.clone(),
            crate::config::UsageItem {
                poll: false,
                interval_secs: None,
            },
        );
    }
    let disabled = merge(parts, &config);
    assert!(!disabled.ok);
    assert!(disabled.sources.is_empty());
    assert_eq!(disabled.rows.len(), pending.rows.len());
    assert!(
        disabled
            .rows
            .iter()
            .all(|row| !row.poll && row.window.is_none())
    );
}

#[test]
fn merged_usage_preserves_each_providers_last_good_timestamp() {
    let good = Snapshot {
        ok: true,
        fetched_ms: 42,
        windows: vec![Window {
            key: CLAUDE_SESSION.into(),
            label: "session".into(),
            pct: 10.0,
            unit: Unit::Pct,
            amount: None,
            limit: None,
            resets_in: Some(100),
        }],
        ..Default::default()
    };
    let failed = Snapshot::failed(&good, "offline");
    let fresh = Snapshot {
        fetched_ms: 99,
        ..good.clone()
    };
    let merged = merge(
        [("anthropic", &failed), ("openai", &fresh)],
        &enabled_daemon(),
    );
    assert_eq!(merged.source_fetched_ms["anthropic"], 42);
    assert_eq!(merged.source_fetched_ms["openai"], 99);
}

#[test]
fn valid_openai_response_omits_non_applicable_session_row() {
    let openai = parse_openai(
            &serde_json::from_str(
                r#"{"plan_type":"free","rate_limit":{"primary_window":{"used_percent":12.5,"limit_window_seconds":604800,"reset_at":4102444800}}}"#,
            )
            .unwrap(),
        );
    let merged = merge([("openai", &openai)], &enabled_daemon());

    assert!(
        merged
            .rows
            .iter()
            .any(|row| row.key == OPENAI_WEEK && row.window.is_some())
    );
    assert!(!merged.rows.iter().any(|row| row.key == OPENAI_SESSION));
}

#[test]
fn missing_credentials_hide_only_the_affected_provider_and_keep_its_catalog() {
    let healthy = parse(&serde_json::from_str(REAL).unwrap(), "max".into());
    let missing = Snapshot {
        credentials_missing: true,
        ..Snapshot::failed(&Snapshot::default(), "missing credentials")
    };
    let merged = merge(
        [("anthropic", &healthy), ("openai", &missing)],
        &enabled_daemon(),
    );
    assert!(merged.ok);
    assert_eq!(merged.windows, healthy.windows);
    assert_eq!(merged.sources, ["anthropic"]);
    assert!(merged.failed_sources.is_empty());
    assert!(merged.error.is_none());
    assert!(!merged.rows.iter().any(|row| row.provider == "openai"));
    assert!(
        merged
            .rows
            .iter()
            .any(|row| row.provider == "anthropic" && row.poll)
    );
    assert!(merged.catalog.iter().any(|row| row.provider == "openai"));
}

fn enabled_daemon() -> crate::config::Daemon {
    let mut config = crate::config::Daemon::default();
    for entry in crate::usage::catalog() {
        config.usage_items.insert(
            entry.key,
            crate::config::UsageItem {
                poll: true,
                interval_secs: None,
            },
        );
    }
    config
}
