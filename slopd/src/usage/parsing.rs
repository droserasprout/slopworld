//! Provider response parsing.

use crate::clock::unix_ms;
use serde_json::Value;

use super::{
    CLAUDE_SESSION, CLAUDE_SPEND, CLAUDE_WEEK, OPENAI_SESSION, OPENAI_WEEK, OPENROUTER_BALANCE,
    Snapshot, Unit, Window, providers::ProviderResponse,
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
            fetched_ms: unix_ms(),
            ..Default::default()
        };
    }

    Snapshot {
        ok: true,
        plan,
        fetched_ms: unix_ms(),
        windows,
        ..Default::default()
    }
}

pub(super) fn parse_anthropic(response: ProviderResponse) -> Snapshot {
    parse(&response.body, response.plan.unwrap_or_default())
}

#[expect(
    clippy::needless_pass_by_value,
    reason = "provider parser callbacks share an owned response signature"
)]
pub(super) fn parse_openrouter(response: ProviderResponse) -> Snapshot {
    parse_credits(&response.body)
}

#[expect(
    clippy::needless_pass_by_value,
    reason = "provider parser callbacks share an owned response signature"
)]
pub(super) fn parse_openai_response(response: ProviderResponse) -> Snapshot {
    parse_openai(&response.body)
}

/// Parse one monetary row. Require both figures to calculate the balance.
/// Return no values if the response is incomplete or unrecognized, as in `parse`.
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
                // This key has no spending limit. Show its value without implying a remaining balance.
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
        fetched_ms: unix_ms(),
        windows: vec![Window {
            key: OPENROUTER_BALANCE.into(),
            label: "balance".into(),
            // With no purchased credits, the remaining balance is zero.
            // Show 100% spent so an empty account does not appear full.
            pct: if credits > 0.0 {
                wire_number(((used / credits) * 100.0).clamp(0.0, 100.0))
            } else {
                100.0
            },
            unit: Unit::Usd,
            amount: Some(wire_number(used)),
            limit: Some(wire_number(credits)),
            // Credits are bought rather than granted, so there is no reset to count to.
            resets_in: None,
        }],
        ..Default::default()
    }
}

/// Parse known usage windows and extra-usage amounts.
/// Return an empty result for unknown response formats instead of incorrect zero values.
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

    // Validate rate-limit fields before adding monetary values.
    // An unrecognized response must produce no values, even if it contains apparent spending data.
    if windows.is_empty() {
        tracing::debug!("unrecognised usage payload: {v}");
        return Snapshot {
            ok: false,
            error: Some("Anthropic answered in a shape slopd does not know".into()),
            plan,
            fetched_ms: unix_ms(),
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
        fetched_ms: unix_ms(),
        windows,
        ..Default::default()
    }
}

/// Parse monetary usage separately from `family`.
/// Both `extra_usage` and `spend` contain `utilization`, but they are not rate-limit windows.
/// `monthly_limit` uses minor units: 10000 means $100.
/// Keep a row without an amount if its budget size is unavailable.
fn spend(v: &Value) -> Option<Window> {
    let e = &v["extra_usage"];
    if e.is_null() {
        return None;
    }

    // A disabled budget is different from zero spending. A $0 row would imply an active budget.
    if e["is_enabled"].as_bool() == Some(false) {
        return None;
    }

    // `spend.percent` is the same number rounded, and stands in if the budget stops
    // reporting one.
    let pct = percent(e).or_else(|| {
        v.get("spend")?
            .get("percent")?
            .as_f64()
            .map(|p| wire_number(p.clamp(0.0, 100.0)))
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
            return Some(wire_number(d));
        }
    }
    e["monthly_limit"]
        .as_f64()
        .map(|cents| wire_number(cents / 100.0))
}

/// None for everything else in the payload, which is most of it.
fn family(name: &str) -> Option<(String, String)> {
    if name == "five_hour" {
        return Some((CLAUDE_SESSION.into(), "session".into()));
    }

    if name == "seven_day" {
        return Some((CLAUDE_WEEK.into(), "week".into()));
    }
    let model = name
        .strip_prefix("seven_day_")
        .filter(|model| !model.is_empty())?;
    Some((
        format!("claude_week_{model}"),
        format!("week ({})", model.replace('_', " ")),
    ))
}

/// Read a percentage: 52.0 means 52%. Response headers use fractions instead.
/// Do not infer the unit from the value because that could convert 0.8% to 80%.
fn percent(w: &Value) -> Option<f32> {
    for k in ["utilization", "used_pct", "used_percent", "percent_used"] {
        if let Some(p) = w[k].as_f64() {
            return Some(wire_number(p.clamp(0.0, 100.0)));
        }
    }

    let remaining = w["remaining"].as_f64()?;
    let limit = w["limit"].as_f64().filter(|l| *l > 0.0)?;
    Some(wire_number(
        (((limit - remaining) / limit) * 100.0).clamp(0.0, 100.0),
    ))
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
            return Some(at.saturating_sub(unix_ms() / 1000));
        }
        // RFC3339, parsed by hand rather than pulling in chrono for one field.
        if let Some(s) = w[k].as_str()
            && let Some(at) = epoch_from_rfc3339(s)
        {
            return Some(at.saturating_sub(unix_ms() / 1000));
        }
    }
    None
}

/// Normalize provider timestamps to whole Unix seconds; reject pre-epoch instants.
fn epoch_from_rfc3339(s: &str) -> Option<u64> {
    let at = time::OffsetDateTime::parse(s, &time::format_description::well_known::Rfc3339).ok()?;
    u64::try_from(at.unix_timestamp()).ok()
}

// The wire contract uses f32 for observational usage figures, not accounting arithmetic.
#[expect(
    clippy::cast_possible_truncation,
    reason = "usage display fields deliberately round provider f64 values to the wire contract's f32 precision"
)]
fn wire_number(value: f64) -> f32 {
    value as f32
}

#[cfg(test)]
#[path = "parsing_tests.rs"]
mod tests;
