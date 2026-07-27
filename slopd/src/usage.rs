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

/// What a row's number *is*, and so what the readout writes beside its icon.
///
/// It rides on the wire rather than being worked out from the key, because the
/// one thing this must never do is let a percentage and a sum of money look
/// alike. A client that does not know a unit has a number it cannot label, and
/// that is the honest answer.
#[derive(Debug, Clone, Copy, Serialize, PartialEq, Default)]
#[serde(rename_all = "lowercase")]
pub enum Unit {
    /// Percent of a window spent, which is every rate limit.
    #[default]
    Pct,
    /// Dollars spent, which is only ever the extra-usage budget.
    Usd,
}

/// One row of the readout: how much of something is gone, and when it comes
/// back. Every rate-limit window is one of these, and so is the extra-usage
/// budget - the same shape, told apart by `unit`.
#[derive(Debug, Clone, Serialize, PartialEq)]
pub struct Window {
    /// Stable identifier the mod keys its icon and ordering off.
    pub key: String,
    /// What to call it on screen.
    pub label: String,
    /// Percent of the window consumed, 0-100. Always present: a budget's
    /// percentage is true whether or not its dollars could be read.
    pub pct: f32,
    /// What `pct` counts, and what the mod draws next to the number.
    pub unit: Unit,
    /// Money spent so far, when `unit` is `usd`. Absent when the payload gave
    /// no figure to put a `$` on, which leaves the row a percentage.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub amount: Option<f32>,
    /// What `amount` is out of, for the tooltip.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub limit: Option<f32>,
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
    let text =
        std::fs::read_to_string(path).map_err(|e| anyhow::anyhow!("{}: {e}", path.display()))?;
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
        plan: o["subscriptionType"]
            .as_str()
            .unwrap_or_default()
            .to_string(),
    })
}

/// Why a poll failed, and how long the far end asked us to leave it alone.
///
/// The wait is carried rather than worked out, because on a 429 the endpoint's
/// own `Retry-After` is the only thing anyone has ever been told about this
/// endpoint's limits. Everything else this daemon knows about them is a guess.
struct PollErr {
    msg: String,
    /// Seconds, from `Retry-After` or from the floor a 429 gets when it does
    /// not say. None for failures that are nobody's rate limit.
    retry_after: Option<u64>,
}

impl PollErr {
    fn new(msg: impl std::fmt::Display) -> Self {
        PollErr {
            msg: msg.to_string(),
            retry_after: None,
        }
    }
}

impl std::fmt::Display for PollErr {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str(&self.msg)
    }
}

/// What a 429 with no `Retry-After` is treated as having asked for. A rate
/// limit retried a minute later is usually just another rate limit, and the
/// numbers this polls for move in hours.
const RATE_LIMIT_FLOOR: u64 = 300;

/// The blocking half: one GET, on whatever thread the caller gives it.
fn fetch(creds: &Creds) -> Result<Value, PollErr> {
    let mut res = ureq::get(usage_url())
        .config()
        .timeout_global(Some(TIMEOUT))
        // Statuses are read here rather than raised as errors. ureq's
        // `StatusCode` error has thrown the response away by the time we see
        // it, and with it the one header a 429 is worth having.
        .http_status_as_error(false)
        .build()
        .header("Authorization", format!("Bearer {}", creds.token))
        .header("anthropic-beta", OAUTH_BETA)
        .header("User-Agent", concat!("slopd/", env!("CARGO_PKG_VERSION")))
        .call()
        .map_err(PollErr::new)?;

    let status = res.status().as_u16();
    match status {
        // Too many polls, or too many from this account. The only failure that
        // is made worse by retrying at the usual rate.
        429 => {
            return Err(PollErr {
                msg: "Anthropic is rate-limiting usage checks (429)".into(),
                retry_after: Some(retry_after(&res).unwrap_or(RATE_LIMIT_FLOOR)),
            })
        }
        // 401 is the one worth naming: it means the token in the file is no
        // longer good, which is a thing the user fixes rather than waits out.
        401 => {
            return Err(PollErr::new(
                "Claude rejected the login (401); is the host still signed in?",
            ))
        }
        s if !(200..300).contains(&s) => {
            return Err(PollErr::new(format!("usage endpoint returned {s}")))
        }
        _ => {}
    }

    res.body_mut().read_json::<Value>().map_err(PollErr::new)
}

/// `Retry-After` in seconds, clamped to something a person would sit through.
///
/// The header's other form is an HTTP date, which is deliberately not parsed:
/// it has never turned up here, and the exponential backoff behind this is a
/// perfectly good answer for a header we cannot read. A number this cannot
/// believe - a broken header asking for a year - is capped rather than
/// honoured, because a daemon that stops polling until next week is one that
/// has to be restarted by hand to come back.
fn retry_after<T>(res: &ureq::http::Response<T>) -> Option<u64> {
    let raw = res.headers().get("retry-after")?.to_str().ok()?;
    Some(raw.trim().parse::<u64>().ok()?.min(6 * 3600))
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
/// The money comes through a door of its own (`spend`), because `extra_usage`
/// and `spend` also carry a `utilization` and a row that silently changed from
/// quota to dollars would be the worst kind of wrong. Through that door it
/// arrives labelled instead: `unit` says which it is.
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
                unit: Unit::Pct,
                amount: None,
                limit: None,
                resets_in: resets_in(w),
            });
        }
    }

    // Emptiness is judged on the rate limits alone, and before the money is
    // added: a payload this does not recognise has to read as "no numbers" even
    // if something in it happened to be spend-shaped.
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

    // Last, because it is the one row that is not a rate limit and the readout
    // draws them in the order they arrive.
    windows.extend(spend(v));

    Snapshot {
        ok: true,
        error: None,
        plan,
        fetched_ms: now_ms(),
        windows,
    }
}

/// The extra-usage budget, as a row like any other.
///
/// Deliberately not part of `family`. `extra_usage` and `spend` both carry a
/// `utilization`, and letting either through the rate-limit path is exactly how
/// a quota row quietly becomes a dollar row. Coming through here it arrives
/// carrying its unit, so the readout writes a `$` on purpose rather than a `%`
/// by accident.
///
/// `monthly_limit` is read as minor units - 10000 is the $100 cap, not a $10,000
/// one. Every dollar figure this payload names outright says so in the name
/// (`limit_dollars`, `used_dollars`), so a bare integer sitting beside a
/// percentage is cents; and where the two can be checked against each other they
/// agree, since 20.93% of $100 is the $21 that `spend.percent` reports. A budget
/// whose size cannot be read at all still leaves a row, without an amount: the
/// percentage of it that is gone is true whatever it is worth, and a row with no
/// figure to put a `$` on stays a percentage.
fn spend(v: &Value) -> Option<Window> {
    let e = &v["extra_usage"];
    if e.is_null() {
        return None;
    }

    // Off is not the same as nothing spent: an account that never opted in has
    // no budget to draw, and a row reading $0 would imply it had one.
    if e["is_enabled"].as_bool() == Some(false) {
        return None;
    }

    // `spend.percent` is the same number rounded, and stands in if the budget
    // itself stops reporting one.
    let pct = percent(e).or_else(|| {
        v["spend"]["percent"]
            .as_f64()
            .map(|p| p.clamp(0.0, 100.0) as f32)
    })?;

    let limit = budget(e);

    Some(Window {
        key: "spend".into(),
        label: "extra usage".into(),
        pct,
        unit: if limit.is_some() {
            Unit::Usd
        } else {
            Unit::Pct
        },
        amount: limit.map(|l| l * pct / 100.0),
        limit,
        // Monthly, and the payload does not say when. Nothing beats a countdown
        // to a date this invented.
        resets_in: None,
    })
}

/// What the extra-usage budget is worth, in dollars. Named figures first, and
/// the bare `monthly_limit` read as cents.
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

/// The longest this will wait between polls, however badly things are going.
///
/// Chosen against what a stale readout costs rather than against the endpoint:
/// the shortest window this reports runs five hours, so half an hour behind is
/// a number still worth drawing - and the tooltip says how old it is. It also
/// bounds how long a fixed login or a flipped setting goes unnoticed, since the
/// loop only looks at the config between sleeps.
const BACKOFF_CAP: u64 = 1800;

/// How long to wait after a failed poll: the configured interval, doubled once
/// per consecutive failure, capped - and never less than the endpoint asked for.
///
/// The doubling is the point. A 429 answered by polling at exactly the rate
/// that earned it is a daemon feeding its own rate limit, and this one polls
/// forever; the first failure still retries at the normal interval, because one
/// dropped packet should not slow the readout down. `asked` overrides the cap
/// rather than being clamped by it: a limit the far end named is the one number
/// here that is not a guess.
fn backoff(base: u64, fails: u32, asked: Option<u64>) -> u64 {
    let grown = base
        .saturating_mul(1u64 << fails.saturating_sub(1).min(16))
        .min(BACKOFF_CAP);
    grown.max(asked.unwrap_or(0))
}

/// A duration in the shape the tooltip and the log want it. Coarse: nothing
/// reading this cares about the seconds on a ten-minute wait.
fn human(secs: u64) -> String {
    if secs >= 3600 {
        format!("{}h{:02}m", secs / 3600, (secs % 3600) / 60)
    } else if secs >= 60 {
        format!("{}m", secs / 60)
    } else {
        format!("{secs}s")
    }
}

/// Polls for as long as the daemon lives, pushing a `usage` event whenever the
/// picture changes. Started from main once, and quiet in the log unless
/// something is wrong: this runs every minute forever.
pub fn spawn(m: Arc<Manager>) -> tokio::task::JoinHandle<()> {
    tokio::spawn(async move {
        // Consecutive failures, and so how far the interval has been turned up.
        // Any poll that comes back with numbers puts it back.
        let mut fails: u32 = 0;

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

            // Both halves are blocking: a file read and a TLS round trip. The
            // second value is what the far end asked us to wait, when it said.
            let (mut next, asked) = tokio::task::spawn_blocking(move || match read_creds(&path) {
                Err(e) => (Snapshot::failed(&prev, e), None),
                Ok(creds) => match fetch(&creds) {
                    Err(e) => {
                        let asked = e.retry_after;
                        (Snapshot::failed(&prev, e), asked)
                    }
                    Ok(body) => (parse(&body, creds.plan), None),
                },
            })
            .await
            .unwrap_or_default();

            let base = d.usage_poll_secs.max(10);
            let delay = if next.ok {
                fails = 0;
                base
            } else {
                fails = fails.saturating_add(1);
                backoff(base, fails, asked)
            };

            // Quiet in the steady state - this runs every minute forever - but
            // loud on either edge, because "is the readout live?" is otherwise a
            // question only the game can answer.
            match (&next.error, was_ok) {
                (Some(e), true) => {
                    tracing::warn!("usage poll failed: {e}; next try in {}", human(delay))
                }
                (Some(e), false) => {
                    tracing::debug!("usage poll: {e}; next try in {}", human(delay))
                }
                (None, false) => tracing::info!(
                    "usage: {}",
                    next.windows
                        .iter()
                        .map(|w| match (w.unit, w.amount) {
                            (Unit::Usd, Some(a)) => format!("{} ${a:.2}", w.key),
                            _ => format!("{} {:.0}%", w.key, w.pct),
                        })
                        .collect::<Vec<_>>()
                        .join(", ")
                ),
                (None, true) => {}
            }

            // The wait goes into the message rather than being left in the log:
            // the readout draws this string, and "429" without "and I am not
            // asking again for ten minutes" reads as a daemon that has hung.
            if let Some(msg) = next.error.take() {
                next.error = Some(format!("{msg} - next try in {}", human(delay)));
            }

            m.set_usage(next).await;

            tokio::time::sleep(Duration::from_secs(delay)).await;
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
        assert_eq!(s.windows.len(), 3, "null windows are absent, not zero");
        assert_eq!(s.windows[0].key, "session");
        assert_eq!(s.windows[0].pct, 52.0);
        assert_eq!(s.windows[1].key, "week");
        assert_eq!(s.windows[1].pct, 55.0);
        assert!(s.windows[0].resets_in.unwrap() > 0);
    }

    /// The money in that payload has a `utilization` too, so it may never come
    /// through as a rate limit - it comes through as its own row, last, saying
    /// what it is. `monthly_limit` is cents: 10000 is the $100 cap.
    #[test]
    fn spend_is_money_and_not_a_rate_limit() {
        let s = parse(&serde_json::from_str(REAL).unwrap(), String::new());

        let rates = s.windows.iter().filter(|w| w.key != "spend");
        assert!(rates
            .clone()
            .all(|w| w.unit == Unit::Pct && w.amount.is_none()));
        assert!(rates.map(|w| w.pct).all(|p| p != 20.93 && p != 21.0));

        let m = s.windows.last().unwrap();
        assert_eq!(m.key, "spend");
        assert_eq!(m.unit, Unit::Usd);
        assert_eq!(m.limit, Some(100.0));
        assert!((m.amount.unwrap() - 20.93).abs() < 0.01);
    }

    /// A budget whose size cannot be read still leaves a row: the share of it
    /// that is gone is true whatever it is worth. It just stays a percentage,
    /// because there is no figure to put a `$` on.
    #[test]
    fn spend_without_a_readable_budget_stays_a_percentage() {
        let v: Value = serde_json::from_str(
            r#"{"five_hour":{"utilization":1},"extra_usage":{"is_enabled":true},
                "spend":{"percent":21}}"#,
        )
        .unwrap();

        let m = parse(&v, String::new()).windows.pop().unwrap();
        assert_eq!(m.key, "spend");
        assert_eq!(m.unit, Unit::Pct);
        assert_eq!(m.pct, 21.0);
        assert!(m.amount.is_none());
    }

    /// One dropped packet does not slow the readout down; a run of them does,
    /// and the growth is bounded at both ends.
    #[test]
    fn backoff_doubles_and_caps() {
        assert_eq!(backoff(60, 1, None), 60);
        assert_eq!(backoff(60, 2, None), 120);
        assert_eq!(backoff(60, 4, None), 480);
        // The shift saturates rather than overflowing on a long outage.
        assert_eq!(backoff(60, 30, None), BACKOFF_CAP);
    }

    /// `Retry-After` is the only number here that is not a guess, so it beats
    /// both the doubling and the cap - but it may not make the poll *faster*
    /// than it was asked to be.
    #[test]
    fn retry_after_beats_the_guess() {
        assert_eq!(backoff(60, 1, Some(900)), 900);
        assert_eq!(backoff(60, 1, Some(30)), 60);
        assert_eq!(backoff(60, 9, Some(7200)), 7200);
    }

    /// The unit is what keeps a dollar row from reading as a percentage, so the
    /// name it goes onto the wire under is part of the protocol.
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

    /// Extra usage switched off has no budget to draw, and a row reading $0
    /// would say it had one.
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
            .all(|w| w.key != "spend"));
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
            s.windows
                .iter()
                .find(|w| w.key == "week_opus")
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
