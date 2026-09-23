use super::*;
use serde_json::Value;

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
fn reads_the_shape_the_endpoint_speaks() {
    let s = parse(&serde_json::from_str(REAL).unwrap(), "pro".into());

    assert!(s.ok);
    assert_eq!(s.windows.len(), 3, "null windows are absent, not zero");
    assert_eq!(s.windows[0].key, "claude_session");
    assert_eq!(s.windows[0].pct, 52.0);
    assert_eq!(s.windows[1].key, "claude_week");
    assert_eq!(s.windows[1].pct, 55.0);
    assert!(s.windows[0].resets_in.unwrap() > 0);
}

/// Keep monetary utilization separate from rate-limit percentages.
/// Convert `monthly_limit` from cents: 10000 represents a $100 limit.
#[test]
fn spend_is_money_and_not_a_rate_limit() {
    let s = parse(&serde_json::from_str(REAL).unwrap(), String::new());

    let rates = s.windows.iter().filter(|w| w.key != "claude_spend");
    assert!(rates
        .clone()
        .all(|w| w.unit == Unit::Pct && w.amount.is_none()));
    assert!(rates.map(|w| w.pct).all(|p| p != 20.93 && p != 21.0));

    let m = s.windows.last().unwrap();
    assert_eq!(m.key, "claude_spend");
    assert_eq!(m.unit, Unit::Usd);
    assert_eq!(m.limit, Some(100.0));
    assert!((m.amount.unwrap() - 20.93).abs() < 0.01);
}

/// Keep the spending row as a percentage when the budget amount is unavailable.
#[test]
fn spend_without_a_readable_budget_stays_a_percentage() {
    let v: Value = serde_json::from_str(
        r#"{"five_hour":{"utilization":1},"extra_usage":{"is_enabled":true},
                "spend":{"percent":21}}"#,
    )
    .unwrap();

    let m = parse(&v, String::new()).windows.pop().unwrap();
    assert_eq!(m.key, "claude_spend");
    assert_eq!(m.unit, Unit::Pct);
    assert_eq!(m.pct, 21.0);
    assert!(m.amount.is_none());
}

#[test]
fn the_unit_is_on_the_wire() {
    let s = parse(&serde_json::from_str(REAL).unwrap(), String::new());
    let json = serde_json::to_string(s.windows.last().unwrap()).unwrap();

    assert!(json.contains(r#""unit":"usd""#), "{json}");
    assert!(json.contains(r#""amount":20.93"#), "{json}");
    assert!(serde_json::to_string(&s.windows[0])
        .unwrap()
        .contains(r#""unit":"pct""#));
}

/// Omit the spending row when extra usage is disabled.
#[test]
fn spend_off_is_not_spend_zero() {
    let v: Value = serde_json::from_str(
        r#"{"five_hour":{"utilization":1},
                "extra_usage":{"is_enabled":false,"monthly_limit":10000,"utilization":0}}"#,
    )
    .unwrap();

    assert!(parse(&v, String::new())
        .windows
        .iter()
        .all(|w| w.key != "claude_spend"));
}

/// Treat body utilization values as percentages regardless of magnitude.
/// Converting 0.8 as a fraction would incorrectly report 80% instead of 0.8%.
#[test]
fn utilization_is_a_percentage_not_a_fraction() {
    let v: Value = serde_json::from_str(r#"{"five_hour":{"utilization":0.8}}"#).unwrap();
    assert_eq!(parse(&v, String::new()).windows[0].pct, 0.8);
}

/// Recognize model-specific weekly windows by their key prefix.
/// Do not restrict parsing to a fixed list of model names.
#[test]
fn per_model_weeks_come_through_named() {
    let v: Value = serde_json::from_str(
        r#"{"seven_day_opus":{"utilization":30},"seven_day_cowork":{"utilization":5}}"#,
    )
    .unwrap();

    let s = parse(&v, String::new());
    let keys: Vec<_> = s.windows.iter().map(|w| w.key.as_str()).collect();
    assert!(keys.contains(&"claude_week_opus") && keys.contains(&"claude_week_cowork"));
    assert_eq!(
        s.windows
            .iter()
            .find(|w| w.key == "claude_week_opus")
            .unwrap()
            .label,
        "week (opus)"
    );
}

#[test]
fn remaining_over_limit_is_inverted() {
    let v: Value = serde_json::from_str(r#"{"seven_day":{"remaining":250,"limit":1000}}"#).unwrap();
    assert_eq!(parse(&v, String::new()).windows[0].pct, 75.0);
}

/// Report an unrecognized payload as an error with no usage values.
/// Do not interpret missing data as zero usage.
#[test]
fn unknown_payload_is_not_zero_percent() {
    let v: Value = serde_json::from_str(r#"{"something_else":{"nope":1}}"#).unwrap();
    let s = parse(&v, String::new());
    assert!(!s.ok);
    assert!(s.windows.is_empty());
    assert!(s.error.is_some());
}

#[test]
fn rfc3339_matches_a_known_instant() {
    // 2026-07-26T00:00:00Z, checked against `date -u -d @1785024000`.
    assert_eq!(epoch_from_rfc3339("2026-07-26T00:00:00Z"), Some(1785024000));
    assert_eq!(epoch_from_rfc3339("1970-01-01T00:00:00Z"), Some(0));
    assert_eq!(epoch_from_rfc3339("not a date"), None);
}

/// Parse fractional seconds and explicit UTC offsets.
#[test]
fn rfc3339_handles_fractions_and_offsets() {
    let z = epoch_from_rfc3339("2026-07-26T00:00:00Z").unwrap();
    assert_eq!(
        epoch_from_rfc3339("2026-07-26T00:00:00.621619+00:00"),
        Some(z)
    );
    assert_eq!(epoch_from_rfc3339("2026-07-26T00:00:00+00:00"), Some(z));
    // Midnight five hours west of UTC is 05:00 UTC.
    assert_eq!(
        epoch_from_rfc3339("2026-07-26T00:00:00-05:00"),
        Some(z + 5 * 3600)
    );
    // Reject unsupported time-zone syntax instead of assuming UTC.
    assert_eq!(epoch_from_rfc3339("2026-07-26T00:00:00+0500"), None);
}

#[test]
fn rfc3339_rejects_out_of_range_fields_and_trailing_zone_text() {
    for bad in [
        "2026-13-01T00:00:00Z",
        "2026-02-29T00:00:00Z",
        "2024-02-30T00:00:00Z",
        "2026-07-26T24:00:00Z",
        "2026-07-26T00:60:00Z",
        "2026-07-26T00:00:00+24:00",
        "2026-07-26T00:00:00+00:60",
        "2026-07-26T00:00:00+00:00garbage",
    ] {
        assert_eq!(epoch_from_rfc3339(bad), None, "accepted {bad}");
    }
    assert!(epoch_from_rfc3339("2024-02-29T00:00:00Z").is_some());
}

/// Represent purchased and used credits as monetary values in the shared usage format.
#[test]
fn credits_are_a_balance_in_money() {
    let v: Value = serde_json::from_str(
        r#"{"data":{"total_credits":25.0,"total_usage":10.0,"total_usage_bkt":0}}"#,
    )
    .unwrap();

    let s = parse_credits(&v);
    assert!(s.ok);
    let w = &s.windows[0];
    assert_eq!(w.key, "openrouter_balance");
    assert_eq!(w.unit, Unit::Usd);
    assert_eq!(w.amount, Some(10.0));
    assert_eq!(w.limit, Some(25.0));
    assert_eq!(w.pct, 40.0);
    // Purchased credits have no scheduled reset.
    assert!(w.resets_in.is_none());
}

/// Report an account with no purchased credits as fully used.
/// Reporting zero usage would incorrectly suggest available credit.
#[test]
fn no_credits_is_spent_rather_than_untouched() {
    let v: Value = serde_json::from_str(r#"{"data":{"total_credits":0,"total_usage":0}}"#).unwrap();
    assert_eq!(parse_credits(&v).windows[0].pct, 100.0);
}

#[test]
fn an_unknown_credits_payload_is_not_a_full_wallet() {
    let v: Value = serde_json::from_str(r#"{"data":{"nope":1}}"#).unwrap();
    let s = parse_credits(&v);
    assert!(!s.ok);
    assert!(s.windows.is_empty());
}

#[test]
fn credits_require_the_current_nested_payload() {
    let v: Value = serde_json::from_str(r#"{"total_credits":10.0,"total_usage":3.5}"#).unwrap();
    let s = parse_credits(&v);
    assert!(!s.ok);
    assert!(s.windows.is_empty());
}

#[test]
fn openai_primary_and_secondary_windows_stay_separate_from_anthropic() {
    let v: Value = serde_json::from_str(
            r#"{"plan_type":"pro","rate_limit":{"primary_window":{"used_percent":12.5,"limit_window_seconds":18000,"reset_at":4102444800},"secondary_window":{"used_percent":42,"limit_window_seconds":604800,"reset_at":4102448400}}}"#,
        )
        .unwrap();

    let s = parse_openai(&v);
    assert!(s.ok);
    assert_eq!(s.plan, "pro");
    assert_eq!(s.windows[0].key, "openai_session");
    assert_eq!(s.windows[0].label, "session");
    assert_eq!(s.windows[0].pct, 12.5);
    assert_eq!(s.windows[1].key, "openai_week");
    assert_eq!(s.windows[1].label, "weekly");
    assert_eq!(s.windows[1].pct, 42.0);
    assert!(s.windows.iter().all(|w| w.resets_in.is_some()));
}

#[test]
fn openai_primary_weekly_window_is_not_called_session() {
    let v: Value = serde_json::from_str(
            r#"{"plan_type":"free","rate_limit":{"primary_window":{"used_percent":12.5,"limit_window_seconds":604800,"reset_at":4102444800}}}"#,
        )
        .unwrap();

    let s = parse_openai(&v);
    assert!(s.ok);
    assert_eq!(s.windows.len(), 1);
    assert_eq!(s.windows[0].key, "openai_week");
    assert_eq!(s.windows[0].label, "weekly");
}

#[test]
fn unknown_openai_payload_is_not_an_empty_usage_window() {
    let s = parse_openai(&serde_json::json!({"rate_limit": {}}));
    assert!(!s.ok);
    assert!(s.windows.is_empty());
}
