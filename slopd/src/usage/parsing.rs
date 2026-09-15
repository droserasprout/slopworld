//! Provider response parsing.

use serde_json::Value;

use super::{
    providers::ProviderResponse, Snapshot, Unit, Window, CLAUDE_SESSION, CLAUDE_SPEND, CLAUDE_WEEK,
    OPENAI_SESSION, OPENAI_WEEK, OPENROUTER_BALANCE,
};

pub(super) fn parse_openai(v: &Value) -> Snapshot {
    let limits = if v["rate_limit"].is_object() {
        &v["rate_limit"]
    } else {
        v
    };
    let mut windows = Vec::new();
    for (field, fallback_key, fallback_label) in [
        ("primary_window", OPENAI_SESSION, "session"),
        ("secondary_window", OPENAI_WEEK, "weekly"),
    ] {
        let w = &limits[field];
        let Some(pct) = percent(w) else { continue };
        let (key, label) = if w["limit_window_seconds"].as_u64() == Some(7 * 24 * 60 * 60) {
            (OPENAI_WEEK, "weekly")
        } else {
            (fallback_key, fallback_label)
        };
        windows.push(Window {
            key: key.into(),
            label: label.into(),
            pct,
            unit: Unit::Pct,
            amount: None,
            limit: None,
            resets_in: resets_in(w),
        });
    }

    let plan = v["plan_type"].as_str().unwrap_or_default().to_string();
    if windows.is_empty() {
        tracing::debug!("unrecognised OpenAI usage payload: {v}");
        return Snapshot {
            ok: false,
            error: Some("OpenAI answered in a shape slopd does not know".into()),
            plan,
            fetched_ms: super::now_ms(),
            ..Default::default()
        };
    }

    Snapshot {
        ok: true,
        plan,
        fetched_ms: super::now_ms(),
        windows,
        ..Default::default()
    }
}

pub(super) fn parse_anthropic(response: ProviderResponse) -> Snapshot {
    parse(&response.body, response.plan.unwrap_or_default())
}

pub(super) fn parse_openrouter(response: ProviderResponse) -> Snapshot {
    parse_credits(&response.body)
}

pub(super) fn parse_openai_response(response: ProviderResponse) -> Snapshot {
    parse_openai(&response.body)
}

/// One row, in money. Both figures are wanted - a balance is a subtraction - and anything
/// else reads as "no numbers", for the reason `parse` does.
pub(super) fn parse_credits(v: &Value) -> Snapshot {
    let d = v.get("data").and_then(Value::as_object);
    let used = d.and_then(|d| d.get("total_usage")).and_then(Value::as_f64);
    let credits = d
        .and_then(|d| d.get("total_credits"))
        .and_then(Value::as_f64);

    let (Some(used), Some(credits)) = (used, credits) else {
        tracing::debug!("unrecognised credits payload: {v}");
        return Snapshot {
            ok: false,
            error: Some(match used {
                // A key with no ceiling on it: the figure exists, there is just no balance
                // to count down. Said plainly rather than drawn as a full bar.
                Some(_) => "OpenRouter reports no credit limit on this key".into(),
                None => "OpenRouter answered in a shape slopd does not know".into(),
            }),
            ..Default::default()
        };
    };

    Snapshot {
        ok: true,
        error: None,
        plan: String::new(),
        fetched_ms: super::now_ms(),
        windows: vec![Window {
            key: OPENROUTER_BALANCE.into(),
            label: "balance".into(),
            // Nothing bought is nothing left, which is 100% spent. Zero would draw a full
            // account beside an empty one.
            pct: if credits > 0.0 {
                ((used / credits) * 100.0).clamp(0.0, 100.0) as f32
            } else {
                100.0
            },
            unit: Unit::Usd,
            amount: Some(used as f32),
            limit: Some(credits as f32),
            // Credits are bought rather than granted, so there is no reset to count to.
            resets_in: None,
        }],
        ..Default::default()
    }
}

/// Parse known window families and extra-usage money; unknown shapes produce an empty result rather than a false zero.
pub(super) fn parse(v: &Value, plan: String) -> Snapshot {
    let mut windows = Vec::new();

    // Object order out of serde_json is alphabetical, which happens to be the order
    // these want reading in.
    if let Some(obj) = v.as_object() {
        for (name, w) in obj {
            let Some((key, label)) = family(name) else {
                continue;
            };
            // A window the plan does not have is null, which is not an error.
            if w.is_null() {
                continue;
            }
            let Some(pct) = percent(w) else { continue };
            windows.push(Window {
                key,
                label,
                pct,
                unit: Unit::Pct,
                amount: None,
                limit: None,
                resets_in: resets_in(w),
            });
        }
    }

    // Judged on the rate limits alone and before the money is added: an unrecognised payload
    // has to read as "no numbers" even if something in it was spend-shaped.
    if windows.is_empty() {
        tracing::debug!("unrecognised usage payload: {v}");
        return Snapshot {
            ok: false,
            error: Some("Anthropic answered in a shape slopd does not know".into()),
            plan,
            fetched_ms: super::now_ms(),
            windows,
            ..Default::default()
        };
    }

    // Last, because it is the one row that is not a rate limit and the readout draws
    // them in arrival order.
    windows.extend(spend(v));

    Snapshot {
        ok: true,
        error: None,
        plan,
        fetched_ms: super::now_ms(),
        windows,
        ..Default::default()
    }
}

/// Not part of `family`: `extra_usage` and `spend` both carry a `utilization`, and letting
/// either through the rate-limit path is how a quota row becomes a dollar row.
/// `monthly_limit` is minor units - 10000 is the $100 cap. A budget whose size cannot be read
/// still leaves a row, without an amount.
fn spend(v: &Value) -> Option<Window> {
    let e = &v["extra_usage"];
    if e.is_null() {
        return None;
    }

    // Off is not the same as nothing spent: a row reading $0 would say the account
    // had a budget.
    if e["is_enabled"].as_bool() == Some(false) {
        return None;
    }

    // `spend.percent` is the same number rounded, and stands in if the budget stops
    // reporting one.
    let pct = percent(e).or_else(|| {
        v["spend"]["percent"]
            .as_f64()
            .map(|p| p.clamp(0.0, 100.0) as f32)
    })?;

    let limit = budget(e);

    Some(Window {
        key: CLAUDE_SPEND.into(),
        label: "extra usage".into(),
        pct,
        unit: if limit.is_some() {
            Unit::Usd
        } else {
            Unit::Pct
        },
        amount: limit.map(|l| l * pct / 100.0),
        limit,
        // Monthly, and the payload does not say when.
        resets_in: None,
    })
}

/// Named figures first, and the bare `monthly_limit` read as cents.
fn budget(e: &Value) -> Option<f32> {
    for k in ["monthly_limit_dollars", "limit_dollars"] {
        if let Some(d) = e[k].as_f64() {
            return Some(d as f32);
        }
    }
    e["monthly_limit"]
        .as_f64()
        .map(|cents| (cents / 100.0) as f32)
}

/// None for everything else in the payload, which is most of it.
fn family(name: &str) -> Option<(String, String)> {
    if name == "five_hour" {
        return Some((CLAUDE_SESSION.into(), "session".into()));
    }

    let rest = name.strip_prefix("seven_day")?;
    match rest.strip_prefix('_') {
        // Plain seven_day: the weekly limit itself.
        None => Some((CLAUDE_WEEK.into(), "week".into())),
        // seven_day_opus, seven_day_sonnet, and whatever comes next.
        Some(model) => Some((
            format!("claude_week_{model}"),
            format!("week ({})", model.replace('_', " ")),
        )),
    }
}

/// A percentage here - 52.0 means 52% - where the same figure rides the API's response
/// headers as a fraction. Guessing between them by size reads 0.8% spent as 80%.
fn percent(w: &Value) -> Option<f32> {
    for k in ["utilization", "used_pct", "used_percent", "percent_used"] {
        if let Some(p) = w[k].as_f64() {
            return Some(p.clamp(0.0, 100.0) as f32);
        }
    }

    let remaining = w["remaining"].as_f64()?;
    let limit = w["limit"].as_f64().filter(|l| *l > 0.0)?;
    Some((((limit - remaining) / limit) * 100.0).clamp(0.0, 100.0) as f32)
}

/// Absolute instants are converted here, so the mod never parses a date.
fn resets_in(w: &Value) -> Option<u64> {
    for k in ["resets_in_seconds", "resetsInSeconds"] {
        if let Some(s) = w[k].as_u64() {
            return Some(s);
        }
    }

    for k in ["resets_at", "resetsAt", "reset_at"] {
        // Epoch seconds.
        if let Some(at) = w[k].as_u64() {
            return Some(at.saturating_sub(super::now_ms() / 1000));
        }
        // RFC3339, parsed by hand rather than pulling in chrono for one field.
        if let Some(s) = w[k].as_str() {
            if let Some(at) = epoch_from_rfc3339(s) {
                return Some(at.saturating_sub(super::now_ms() / 1000));
            }
        }
    }
    None
}

/// `2026-07-26T09:59:59.621619+00:00` -> epoch seconds. All three forms this endpoint uses:
/// fractional seconds (dropped), a trailing `Z`, and a numeric offset, which is applied
/// rather than assumed zero.
fn epoch_from_rfc3339(s: &str) -> Option<u64> {
    let b = s.as_bytes();
    if b.len() < 19 || b[4] != b'-' || b[7] != b'-' || b[10] != b'T' {
        return None;
    }
    let n = |a: usize, z: usize| s[a..z].parse::<i64>().ok();
    let (y, mo, d) = (n(0, 4)?, n(5, 7)?, n(8, 10)?);
    let (h, mi, sec) = (n(11, 13)?, n(14, 16)?, n(17, 19)?);
    if !(1..=12).contains(&mo)
        || d < 1
        || d > days_in_month(y, mo)
        || !(0..=23).contains(&h)
        || !(0..=59).contains(&mi)
        || !(0..=60).contains(&sec)
    {
        return None;
    }

    // Days since the epoch, by the civil-from-days algorithm.
    let y = if mo <= 2 { y - 1 } else { y };
    let era = if y >= 0 { y } else { y - 399 } / 400;
    let yoe = y - era * 400;
    let mp = (mo + 9) % 12;
    let doy = (153 * mp + 2) / 5 + d - 1;
    let doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
    let days = era * 146_097 + doe - 719_468;

    u64::try_from(days * 86_400 + h * 3_600 + mi * 60 + sec - offset_secs(s)?).ok()
}

fn days_in_month(year: i64, month: i64) -> i64 {
    match month {
        2 if year % 4 == 0 && (year % 100 != 0 || year % 400 == 0) => 29,
        2 => 28,
        4 | 6 | 9 | 11 => 30,
        _ => 31,
    }
}

/// 0 for `Z` or a missing zone. None for a zone this cannot read, which fails the
/// whole timestamp rather than placing the reset in the wrong hour.
fn offset_secs(s: &str) -> Option<i64> {
    // Skip the date-time, and any fractional seconds after it.
    let zone = s[19..].trim_start_matches(|c: char| c == '.' || c.is_ascii_digit());

    if zone.is_empty() || zone == "Z" || zone == "z" {
        return Some(0);
    }

    let sign = match zone.as_bytes()[0] {
        b'+' => 1,
        b'-' => -1,
        _ => return None,
    };
    let rest = &zone[1..];
    if rest.len() != 5 || rest.as_bytes()[2] != b':' {
        return None;
    }
    let h = rest[0..2].parse::<i64>().ok()?;
    let m = rest[3..5].parse::<i64>().ok()?;
    if h > 23 || m > 59 {
        return None;
    }
    Some(sign * (h * 3_600 + m * 60))
}

#[cfg(test)]
mod tests {
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

    /// The money has a `utilization` too, so it must never come through as a rate
    /// limit. `monthly_limit` is cents: 10000 is the $100 cap.
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

    /// A budget whose size cannot be read still leaves a row; it just stays a
    /// percentage.
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

    /// Extra usage switched off has no budget to draw.
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

    /// The same figure is a fraction in the API's response headers, and guessing by
    /// size reads a window that is 0.8% spent as 80%.
    #[test]
    fn utilization_is_a_percentage_not_a_fraction() {
        let v: Value = serde_json::from_str(r#"{"five_hour":{"utilization":0.8}}"#).unwrap();
        assert_eq!(parse(&v, String::new()).windows[0].pct, 0.8);
    }

    /// Null on this plan and populated on others, so matched by family rather than by
    /// a list of names.
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
        let v: Value =
            serde_json::from_str(r#"{"seven_day":{"remaining":250,"limit":1000}}"#).unwrap();
        assert_eq!(parse(&v, String::new()).windows[0].pct, 75.0);
    }

    /// An unrecognised payload has to read as "no numbers", never as a colony sitting
    /// comfortably at zero.
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

    /// The three ways this endpoint has spelled the same instant.
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
        // A zone this cannot read fails the timestamp rather than guessing UTC.
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

    /// Credits bought less credits spent, in money, on the same wire as a rate limit.
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
        // Bought rather than granted: there is nothing to count down to.
        assert!(w.resets_in.is_none());
    }

    /// Nothing bought is nothing left. Zero percent spent would draw an empty account
    /// beside a full one.
    #[test]
    fn no_credits_is_spent_rather_than_untouched() {
        let v: Value =
            serde_json::from_str(r#"{"data":{"total_credits":0,"total_usage":0}}"#).unwrap();
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
}
