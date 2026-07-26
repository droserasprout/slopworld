//! What is left of the subscription, polled from Anthropic and handed to the mod
//! as two numbers it can draw like a resource count.
//!
//! The agents are a colony that eats quota, so quota is the one resource this
//! game has left. Nothing on the host caches it - `stats-cache.json` is aggregate
//! tokens and days stale, and the transcripts carry no rate-limit fields - so the
//! numbers come from the same place Claude Code's own `/usage` gets them, using
//! the OAuth access token Claude Code leaves in `~/.claude/.credentials.json`.
//!
//! That file is read fresh on every poll and never copied anywhere: the token
//! expires, Claude Code refreshes it behind us, and re-reading is how we follow.
//! slopd does not do the refresh dance itself - the agents are logged in, so
//! something else already keeps that file current, and a daemon that could mint
//! tokens is a daemon worth stealing.
//!
//! Failure is a state, not an error: a snapshot carries whatever it managed to
//! read plus the reason it got no further, and the readout says so rather than
//! going blank. The mod is deliberately told a *list* of windows rather than two
//! named ones, so a plan with different limits draws whatever it has.

use std::path::PathBuf;
use std::sync::Arc;
use std::time::{Duration, SystemTime, UNIX_EPOCH};

use serde::Serialize;
use serde_json::Value;

use crate::session::Manager;

/// Where the numbers come from. Undocumented and subject to change under us,
/// which is why `parse` is written to survive not recognising what it gets and
/// why `SLOPD_USAGE_URL` can point this somewhere else - at a stub while
/// developing the readout, or at the endpoint's next address without waiting for
/// a build.
const USAGE_URL: &str = "https://api.anthropic.com/api/oauth/usage";

fn usage_url() -> String {
    std::env::var("SLOPD_USAGE_URL").unwrap_or_else(|_| USAGE_URL.to_string())
}

/// The beta header Claude Code's own OAuth calls carry. Without it the endpoint
/// treats the bearer as an API key and refuses it.
const OAUTH_BETA: &str = "oauth-2025-04-20";

const TIMEOUT: Duration = Duration::from_secs(20);

/// One rate-limit window: how much of it is gone, and when it comes back.
#[derive(Debug, Clone, Serialize, PartialEq)]
pub struct Window {
    /// Stable identifier the mod keys its icon and ordering off.
    pub key: String,
    /// What to call it on screen.
    pub label: String,
    /// Percent of the window consumed, 0-100.
    pub pct: f32,
    /// Seconds until it resets, if the endpoint said. Counted down by the mod
    /// against its own clock, so a stale snapshot still reads sensibly.
    pub resets_in: Option<u64>,
}

/// The whole picture as of one poll. Serialised straight onto the wire.
#[derive(Debug, Clone, Serialize, PartialEq, Default)]
pub struct Snapshot {
    /// Whether the last poll got usable numbers. False leaves `windows` as
    /// whatever the last good poll held, so the readout goes stale rather than
    /// empty while the network is out.
    pub ok: bool,
    /// Why not, in a sentence fit for a tooltip.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub error: Option<String>,
    /// Subscription tier, straight from the credentials file: "pro", "max", ...
    pub plan: String,
    /// Unix millis of the last successful poll, so the mod can age the numbers.
    pub fetched_ms: u64,
    pub windows: Vec<Window>,
}

impl Snapshot {
    fn failed(prev: &Snapshot, why: impl std::fmt::Display) -> Snapshot {
        Snapshot {
            ok: false,
            error: Some(why.to_string()),
            ..prev.clone()
        }
    }
}

fn now_ms() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_millis() as u64)
        .unwrap_or(0)
}

/// The bits of `~/.claude/.credentials.json` this needs. Read per poll and
/// dropped immediately after the request; the token never reaches a log line,
/// the config file or the wire.
struct Creds {
    token: String,
    plan: String,
}

fn read_creds(path: &PathBuf) -> anyhow::Result<Creds> {
    let text = std::fs::read_to_string(path)
        .map_err(|e| anyhow::anyhow!("{}: {e}", path.display()))?;
    let v: Value = serde_json::from_str(&text)?;
    let o = &v["claudeAiOauth"];

    let token = o["accessToken"]
        .as_str()
        .ok_or_else(|| anyhow::anyhow!("no OAuth token in {}", path.display()))?
        .to_string();

    // Expiry is advisory here: a stale token is a 401 we report like any other,
    // and saying so plainly beats a poll that silently does nothing.
    if let Some(exp) = o["expiresAt"].as_u64() {
        if exp < now_ms() {
            anyhow::bail!("Claude login expired; run `claude auth` on the host");
        }
    }

    Ok(Creds {
        token,
        plan: o["subscriptionType"].as_str().unwrap_or_default().to_string(),
    })
}

/// The blocking half: one GET, on whatever thread the caller gives it.
fn fetch(creds: &Creds) -> anyhow::Result<Value> {
    let mut res = ureq::get(usage_url())
        .config()
        .timeout_global(Some(TIMEOUT))
        .build()
        .header("Authorization", format!("Bearer {}", creds.token))
        .header("anthropic-beta", OAUTH_BETA)
        .header("User-Agent", concat!("slopd/", env!("CARGO_PKG_VERSION")))
        .call()
        .map_err(|e| match e {
            // 401 is the one worth naming: it means the token in the file is no
            // longer good, which is a thing the user fixes rather than waits out.
            ureq::Error::StatusCode(401) => {
                anyhow::anyhow!("Claude rejected the login (401); is the host still signed in?")
            }
            ureq::Error::StatusCode(c) => anyhow::anyhow!("usage endpoint returned {c}"),
            other => anyhow::anyhow!("{other}"),
        })?;

    Ok(res.body_mut().read_json::<Value>()?)
}

/// Everything that knows what the payload looks like lives here.
///
/// The endpoint is nobody's published API, so this recognises rather than
/// assumes: anything unrecognised leaves an empty list and a snapshot that says
/// so. Being wrong should read as "no numbers", never as "0% used".
///
/// The windows are picked out by family rather than by name. The payload carries
/// one `five_hour` and a whole row of `seven_day*` - opus, sonnet, cowork and
/// several that are null on any given plan - so matching the prefix takes
/// whichever ones this account actually has and picks up the next one for free.
/// The money is deliberately not in here: `extra_usage` and `spend` also carry a
/// `utilization`, and a bar that silently changed from quota to dollars would be
/// the worst kind of wrong.
fn parse(v: &Value, plan: String) -> Snapshot {
    let mut windows = Vec::new();

    // Object order out of serde_json is alphabetical, which happens to be the
    // order these want reading in: five_hour, then seven_day, then its variants.
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
                resets_in: resets_in(w),
            });
        }
    }

    if windows.is_empty() {
        tracing::debug!("unrecognised usage payload: {v}");
        return Snapshot {
            ok: false,
            error: Some("Anthropic answered in a shape slopd does not know".into()),
            plan,
            fetched_ms: now_ms(),
            windows,
        };
    }

    Snapshot {
        ok: true,
        error: None,
        plan,
        fetched_ms: now_ms(),
        windows,
    }
}

/// Which rate-limit window a top-level key is, as (wire key, label). None for
/// everything else in the payload, which is most of it.
fn family(name: &str) -> Option<(String, String)> {
    if name == "five_hour" {
        return Some(("session".into(), "session".into()));
    }

    let rest = name.strip_prefix("seven_day")?;
    match rest.strip_prefix('_') {
        // Plain seven_day: the weekly limit itself.
        None => Some(("week".into(), "week".into())),
        // seven_day_opus, seven_day_sonnet, and whatever comes next.
        Some(model) => Some((
            format!("week_{model}"),
            format!("week ({})", model.replace('_', " ")),
        )),
    }
}

/// How much of a window is gone, as a percentage.
///
/// `utilization` is a percentage in this payload - 52.0 means 52% - which is
/// worth stating because the same figure rides the API's response headers as a
/// fraction. Guessing between the two by size is what a previous cut did, and it
/// turns a window that is genuinely 0.8% spent into one that reads 80%.
fn percent(w: &Value) -> Option<f32> {
    for k in ["utilization", "used_pct", "percent_used"] {
        if let Some(p) = w[k].as_f64() {
            return Some(p.clamp(0.0, 100.0) as f32);
        }
    }

    let remaining = w["remaining"].as_f64()?;
    let limit = w["limit"].as_f64().filter(|l| *l > 0.0)?;
    Some((((limit - remaining) / limit) * 100.0).clamp(0.0, 100.0) as f32)
}

/// Seconds until the window resets. Absolute instants are converted here so the
/// mod never has to parse a date - it counts down from whenever it heard.
fn resets_in(w: &Value) -> Option<u64> {
    for k in ["resets_in_seconds", "resetsInSeconds"] {
        if let Some(s) = w[k].as_u64() {
            return Some(s);
        }
    }

    for k in ["resets_at", "resetsAt", "reset_at"] {
        // Epoch seconds.
        if let Some(at) = w[k].as_u64() {
            return Some(at.saturating_sub(now_ms() / 1000));
        }
        // RFC3339, which is what the JSON has carried in practice. Parsed by
        // hand rather than pulling in chrono for one field: the format is fixed
        // width and always UTC here.
        if let Some(s) = w[k].as_str() {
            if let Some(at) = epoch_from_rfc3339(s) {
                return Some(at.saturating_sub(now_ms() / 1000));
            }
        }
    }
    None
}

/// `2026-07-26T09:59:59.621619+00:00` -> epoch seconds.
///
/// Written out rather than pulled in: chrono for one field is a dependency the
/// daemon would carry forever. The three parts that vary are all handled, since
/// this endpoint uses all three - fractional seconds (thrown away, the countdown
/// is drawn in minutes), a trailing `Z`, and a numeric offset, which is applied
/// rather than assumed to be zero. It has been `+00:00` every time so far, and
/// silently reading a `-05:00` as UTC would put the reset five hours out.
fn epoch_from_rfc3339(s: &str) -> Option<u64> {
    let b = s.as_bytes();
    if b.len() < 19 || b[4] != b'-' || b[7] != b'-' || b[10] != b'T' {
        return None;
    }
    let n = |a: usize, z: usize| s[a..z].parse::<i64>().ok();
    let (y, mo, d) = (n(0, 4)?, n(5, 7)?, n(8, 10)?);
    let (h, mi, sec) = (n(11, 13)?, n(14, 16)?, n(17, 19)?);

    // Days since the epoch, by the civil-from-days algorithm: no leap-second
    // nonsense and no dependency.
    let y = if mo <= 2 { y - 1 } else { y };
    let era = if y >= 0 { y } else { y - 399 } / 400;
    let yoe = y - era * 400;
    let mp = (mo + 9) % 12;
    let doy = (153 * mp + 2) / 5 + d - 1;
    let doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
    let days = era * 146_097 + doe - 719_468;

    u64::try_from(days * 86_400 + h * 3_600 + mi * 60 + sec - offset_secs(s)?).ok()
}

/// Seconds to subtract to get UTC: 0 for `Z` or a missing zone, and the signed
/// offset otherwise. None for a zone this cannot read, which fails the whole
/// timestamp rather than quietly placing the reset in the wrong hour.
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
    if rest.len() < 5 || rest.as_bytes()[2] != b':' {
        return None;
    }
    let h = rest[0..2].parse::<i64>().ok()?;
    let m = rest[3..5].parse::<i64>().ok()?;
    Some(sign * (h * 3_600 + m * 60))
}

/// Polls for as long as the daemon lives, pushing a `usage` event whenever the
/// picture changes. Started from main once, and quiet in the log unless
/// something is wrong: this runs every minute forever.
pub fn spawn(m: Arc<Manager>) -> tokio::task::JoinHandle<()> {
    tokio::spawn(async move {
        loop {
            let cfg = m.config().await;
            let d = &cfg.daemon;

            if !d.usage {
                // Off is a setting that can be turned back on without a restart,
                // so this sleeps rather than returns.
                tokio::time::sleep(Duration::from_secs(30)).await;
                continue;
            }

            let path = PathBuf::from(crate::config::expand(&d.claude_credentials));
            let prev = m.usage().await;
            let was_ok = prev.ok;

            // Both halves are blocking: a file read and a TLS round trip.
            let next = tokio::task::spawn_blocking(move || match read_creds(&path) {
                Err(e) => Snapshot::failed(&prev, e),
                Ok(creds) => match fetch(&creds) {
                    Err(e) => Snapshot::failed(&prev, e),
                    Ok(body) => parse(&body, creds.plan),
                },
            })
            .await
            .unwrap_or_default();

            // Quiet in the steady state - this runs every minute forever - but
            // loud on either edge, because "is the readout live?" is otherwise a
            // question only the game can answer.
            match (&next.error, was_ok) {
                (Some(e), true) => tracing::warn!("usage poll failed: {e}"),
                (Some(e), false) => tracing::debug!("usage poll: {e}"),
                (None, false) => tracing::info!(
                    "usage: {}",
                    next.windows
                        .iter()
                        .map(|w| format!("{} {:.0}%", w.key, w.pct))
                        .collect::<Vec<_>>()
                        .join(", ")
                ),
                (None, true) => {}
            }
            m.set_usage(next).await;

            tokio::time::sleep(Duration::from_secs(d.usage_poll_secs.max(10))).await;
        }
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Trimmed from what the endpoint actually answered on a Pro account, down
    /// to the keys this reads plus the ones it has to step over.
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
        assert_eq!(s.windows.len(), 2, "null windows are absent, not zero");
        assert_eq!(s.windows[0].key, "session");
        assert_eq!(s.windows[0].pct, 52.0);
        assert_eq!(s.windows[1].key, "week");
        assert_eq!(s.windows[1].pct, 55.0);
        assert!(s.windows[0].resets_in.unwrap() > 0);
    }

    /// The money in that payload has a `utilization` too, and a bar that turned
    /// into dollars without saying so would be worse than no bar.
    #[test]
    fn spend_is_not_a_rate_limit() {
        let s = parse(&serde_json::from_str(REAL).unwrap(), String::new());
        assert!(s.windows.iter().all(|w| w.pct != 20.93 && w.pct != 21.0));
    }

    /// Utilization is a percentage here - the same figure is a fraction in the
    /// API's response headers, and guessing between them by size reads a window
    /// that is 0.8% spent as 80%.
    #[test]
    fn utilization_is_a_percentage_not_a_fraction() {
        let v: Value = serde_json::from_str(r#"{"five_hour":{"utilization":0.8}}"#).unwrap();
        assert_eq!(parse(&v, String::new()).windows[0].pct, 0.8);
    }

    /// The per-model weekly windows are null on this plan and populated on
    /// others, so they are matched by family rather than by a list of names.
    #[test]
    fn per_model_weeks_come_through_named() {
        let v: Value = serde_json::from_str(
            r#"{"seven_day_opus":{"utilization":30},"seven_day_cowork":{"utilization":5}}"#,
        )
        .unwrap();

        let s = parse(&v, String::new());
        let keys: Vec<_> = s.windows.iter().map(|w| w.key.as_str()).collect();
        assert!(keys.contains(&"week_opus") && keys.contains(&"week_cowork"));
        assert_eq!(
            s.windows.iter().find(|w| w.key == "week_opus").unwrap().label,
            "week (opus)"
        );
    }

    #[test]
    fn remaining_over_limit_is_inverted() {
        let v: Value =
            serde_json::from_str(r#"{"seven_day":{"remaining":250,"limit":1000}}"#).unwrap();
        assert_eq!(parse(&v, String::new()).windows[0].pct, 75.0);
    }

    /// The failure that matters: an unrecognised payload has to read as "no
    /// numbers", never as a colony sitting comfortably at zero.
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

    /// The three ways this endpoint has spelled the same instant. Fractional
    /// seconds are dropped, `Z` and `+00:00` agree, and a real offset moves it.
    #[test]
    fn rfc3339_handles_fractions_and_offsets() {
        let z = epoch_from_rfc3339("2026-07-26T00:00:00Z").unwrap();
        assert_eq!(epoch_from_rfc3339("2026-07-26T00:00:00.621619+00:00"), Some(z));
        assert_eq!(epoch_from_rfc3339("2026-07-26T00:00:00+00:00"), Some(z));
        // Midnight five hours west of UTC is 05:00 UTC.
        assert_eq!(
            epoch_from_rfc3339("2026-07-26T00:00:00-05:00"),
            Some(z + 5 * 3600)
        );
        // A zone this cannot read fails the timestamp rather than guessing UTC.
        assert_eq!(epoch_from_rfc3339("2026-07-26T00:00:00+0500"), None);
    }

    /// A failed poll keeps the last good numbers and says why, because a readout
    /// that empties itself every time the wifi hiccups is worse than a stale one.
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
}
