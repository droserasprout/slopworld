//! What is left of the subscriptions, polled from whoever sells them.
//!
//! Anthropic first, and nothing on the host caches it - `stats-cache.json` is aggregate
//! tokens and days stale, the transcripts carry no rate-limit fields - so the numbers come
//! from where Claude Code's own `/usage` gets them, with the OAuth token in
//! `~/.claude/.credentials.json`. Read fresh per poll and never copied: the token expires
//! hourly and something else refreshes it.
//!
//! OpenRouter is the second, and it is money rather than a window: credits bought less
//! credits spent, which is what the `pi` agent draws down. Same shape on the wire, told
//! apart by `unit` the way the extra-usage budget already was.
//!
//! Failure is a state rather than an error: a snapshot carries what it read plus the reason
//! it got no further. The mod is told a *list* of windows, so a plan with different limits
//! draws whatever it has - and so two sellers are one readout rather than two.

use std::path::PathBuf;
use std::sync::Arc;
use std::time::{Duration, Instant, SystemTime, UNIX_EPOCH};

use serde::Serialize;
use serde_json::Value;

use crate::session::Manager;

/// Undocumented and subject to change under us, which is why `parse` survives not
/// recognising what it gets and why `SLOPD_USAGE_URL` can point this elsewhere.
const USAGE_URL: &str = "https://api.anthropic.com/api/oauth/usage";

fn usage_url() -> String {
    std::env::var("SLOPD_USAGE_URL").unwrap_or_else(|_| USAGE_URL.to_string())
}

/// Published, unlike the one above, and answering the same question in money:
/// `{"data":{"total_credits":n,"total_usage":n}}`.
const CREDITS_URL: &str = "https://openrouter.ai/api/v1/credits";

fn credits_url() -> String {
    std::env::var("SLOPD_CREDITS_URL").unwrap_or_else(|_| CREDITS_URL.to_string())
}

/// The name the `pi` preset forwards, so a machine already running that agent has the key
/// where this can read it without a second copy in `config.toml`.
const KEY_ENV: &str = "OPENROUTER_API_KEY";

/// The beta header Claude Code's own OAuth calls carry. Without it the endpoint
/// treats the bearer as an API key and refuses it.
const OAUTH_BETA: &str = "oauth-2025-04-20";

const TIMEOUT: Duration = Duration::from_secs(20);

/// Rides on the wire rather than being worked out from the key: the one thing this
/// must never do is let a percentage and a sum of money look alike.
#[derive(Debug, Clone, Copy, Serialize, PartialEq, Default)]
#[serde(rename_all = "lowercase")]
pub enum Unit {
    /// Percent of a window spent, which is every rate limit.
    #[default]
    Pct,
    /// Dollars spent, which is only ever the extra-usage budget.
    Usd,
}

/// Every rate-limit window is one of these, and so is the extra-usage budget -
/// same shape, told apart by `unit`.
#[derive(Debug, Clone, Serialize, PartialEq)]
pub struct Window {
    pub key: String,
    pub label: String,
    /// 0-100, always present: a budget's percentage is true whether or not its dollars
    /// could be read.
    pub pct: f32,
    pub unit: Unit,
    /// Absent when the payload gave no figure to put a `$` on, which leaves the row a
    /// percentage.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub amount: Option<f32>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub limit: Option<f32>,
    /// Counted down by the mod against its own clock, so a stale snapshot still reads
    /// sensibly.
    pub resets_in: Option<u64>,
}

#[derive(Debug, Clone, Serialize, PartialEq, Default)]
pub struct Snapshot {
    /// False leaves `windows` as whatever the last good poll held, so the readout goes
    /// stale rather than empty while the network is out.
    pub ok: bool,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub error: Option<String>,
    /// Straight from the credentials file: "pro", "max", ...
    pub plan: String,
    /// Unix millis of the last successful poll, so the mod can age the numbers.
    pub fetched_ms: u64,
    /// Which sellers are switched on, whether or not they answered. The mod draws a row per
    /// resource it *expects*, so a source that is down leaves its icons on the line with no
    /// number instead of taking them off the screen - which is the one thing a readout in the
    /// corner must not do, an icon that comes and goes being harder to read than an empty one.
    #[serde(skip_serializing_if = "Vec::is_empty")]
    pub sources: Vec<String>,
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

/// Read per poll and dropped immediately after the request; the token never
/// reaches a log line, the config file or the wire.
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

    // Expiry is advisory here: a stale token is a 401 we report like any other.
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

/// The wait is carried rather than worked out: on a 429 the endpoint's own
/// `Retry-After` is the only thing anyone has been told about its limits.
struct PollErr {
    msg: String,
    /// From `Retry-After`, or the floor a 429 gets when it does not say. None for
    /// failures that are nobody's rate limit.
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

/// What a 429 with no `Retry-After` is treated as asking for. A rate limit retried
/// a minute later is usually just another rate limit.
const RATE_LIMIT_FLOOR: u64 = 300;

fn fetch(creds: &Creds) -> Result<Value, PollErr> {
    let mut res = ureq::get(usage_url())
        .config()
        .timeout_global(Some(TIMEOUT))
        // Statuses read rather than raised: ureq's `StatusCode` error has thrown the response
        // away by the time we see it, and with it a 429's `Retry-After`.
        .http_status_as_error(false)
        .build()
        .header("Authorization", format!("Bearer {}", creds.token))
        .header("anthropic-beta", OAUTH_BETA)
        .header("User-Agent", concat!("slopd/", env!("CARGO_PKG_VERSION")))
        .call()
        .map_err(PollErr::new)?;

    let status = res.status().as_u16();
    match status {
        // The only failure made worse by retrying at the usual rate.
        429 => {
            return Err(PollErr {
                msg: "Anthropic is rate-limiting usage checks (429)".into(),
                retry_after: Some(retry_after(&res).unwrap_or(RATE_LIMIT_FLOOR)),
            })
        }
        // 401 means the token in the file is no longer good, which is a thing the user
        // fixes rather than waits out.
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

/// Blank names slopd's own environment rather than a file: that is where the `pi` preset
/// forwards `OPENROUTER_API_KEY` from, so the machine that can already run the agent can
/// answer this without the key being written down a second time. A file is read fresh per
/// poll and trimmed, and neither road copies, logs or writes the key back.
fn read_key(file: &str) -> anyhow::Result<String> {
    if file.trim().is_empty() {
        let key = std::env::var(KEY_ENV).unwrap_or_default();
        if key.trim().is_empty() {
            anyhow::bail!("no ${KEY_ENV} in slopd's environment; export it or name a key file");
        }
        return Ok(key.trim().to_string());
    }

    let path = crate::config::expand(file);
    let text = std::fs::read_to_string(&path).map_err(|e| anyhow::anyhow!("{path}: {e}"))?;
    let key = text.trim();
    if key.is_empty() {
        anyhow::bail!("{path} holds no key");
    }
    Ok(key.to_string())
}

/// Same shape as `fetch`, and deliberately not folded together with it: what the two
/// endpoints share is four lines of status handling, and what they do not is every header
/// and every error sentence.
fn fetch_credits(key: &str) -> Result<Value, PollErr> {
    let mut res = ureq::get(credits_url())
        .config()
        .timeout_global(Some(TIMEOUT))
        .http_status_as_error(false)
        .build()
        .header("Authorization", format!("Bearer {key}"))
        .header("User-Agent", concat!("slopd/", env!("CARGO_PKG_VERSION")))
        .call()
        .map_err(PollErr::new)?;

    let status = res.status().as_u16();
    match status {
        429 => {
            return Err(PollErr {
                msg: "OpenRouter is rate-limiting credit checks (429)".into(),
                retry_after: Some(retry_after(&res).unwrap_or(RATE_LIMIT_FLOOR)),
            })
        }
        // Something the user fixes rather than waits out, and the one failure worth naming
        // the key in: this is the only thing here that could be a stale copy in a file.
        401 | 403 => {
            return Err(PollErr::new(format!(
                "OpenRouter rejected the key ({status}); is it still good?"
            )))
        }
        s if !(200..300).contains(&s) => {
            return Err(PollErr::new(format!("credits endpoint returned {s}")))
        }
        _ => {}
    }

    res.body_mut().read_json::<Value>().map_err(PollErr::new)
}

/// One row, in money. `/credits` says `total_credits` and `total_usage`; the older
/// `/auth/key` says `limit` and `usage`, with a null limit for a key that has none. Both
/// figures are wanted - a balance is a subtraction - and anything else reads as "no
/// numbers", for the reason `parse` does.
fn parse_credits(v: &Value) -> Snapshot {
    // The payload nests under `data`, but a proxy pointed at by `SLOPD_CREDITS_URL` may
    // not, so the object itself is tried too.
    let d = if v["data"].is_object() { &v["data"] } else { v };

    let used = num(d, &["total_usage", "usage", "used"]);
    let credits = num(d, &["total_credits", "credits", "limit"]);

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
        fetched_ms: now_ms(),
        windows: vec![Window {
            key: "balance".into(),
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

/// The first of these keys that carries a number, the endpoint having renamed both of
/// them once already.
fn num(v: &Value, keys: &[&str]) -> Option<f64> {
    keys.iter().find_map(|k| v[*k].as_f64())
}

/// Seconds only; the header's HTTP-date form has never turned up here, and the backoff
/// behind this answers one we cannot read. Clamped: a daemon that stops polling until next
/// week has to be restarted by hand.
fn retry_after<T>(res: &ureq::http::Response<T>) -> Option<u64> {
    let raw = res.headers().get("retry-after")?.to_str().ok()?;
    Some(raw.trim().parse::<u64>().ok()?.min(6 * 3600))
}

/// The endpoint is nobody's published API, so this recognises rather than assumes: anything
/// unrecognised leaves an empty list, being wrong having to read as "no numbers" and never as
/// "0% used". Windows are picked out by family rather than name - one `five_hour` and a row
/// of `seven_day*`, several null on any plan. Money comes through a door of its own, because
/// `extra_usage` and `spend` carry a `utilization` too.
fn parse(v: &Value, plan: String) -> Snapshot {
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
            fetched_ms: now_ms(),
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
        fetched_ms: now_ms(),
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

/// A percentage here - 52.0 means 52% - where the same figure rides the API's response
/// headers as a fraction. Guessing between them by size reads 0.8% spent as 80%.
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
            return Some(at.saturating_sub(now_ms() / 1000));
        }
        // RFC3339, parsed by hand rather than pulling in chrono for one field.
        if let Some(s) = w[k].as_str() {
            if let Some(at) = epoch_from_rfc3339(s) {
                return Some(at.saturating_sub(now_ms() / 1000));
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
    if rest.len() < 5 || rest.as_bytes()[2] != b':' {
        return None;
    }
    let h = rest[0..2].parse::<i64>().ok()?;
    let m = rest[3..5].parse::<i64>().ok()?;
    Some(sign * (h * 3_600 + m * 60))
}

/// The shortest window reported runs five hours, so half an hour behind is still worth
/// drawing. Also bounds how long a fixed login goes unnoticed, the loop only looking at the
/// config between sleeps.
const BACKOFF_CAP: u64 = 1800;

/// A 429 answered at the rate that earned it is a daemon feeding its own rate limit. The
/// first failure still retries at the normal interval. `asked` overrides the cap rather than
/// being clamped by it: a limit the far end named is not a guess.
fn backoff(base: u64, fails: u32, asked: Option<u64>) -> u64 {
    let grown = base
        .saturating_mul(1u64 << fails.saturating_sub(1).min(16))
        .min(BACKOFF_CAP);
    grown.max(asked.unwrap_or(0))
}

fn human(secs: u64) -> String {
    if secs >= 3600 {
        format!("{}h{:02}m", secs / 3600, (secs % 3600) / 60)
    } else if secs >= 60 {
        format!("{}m", secs / 60)
    } else {
        format!("{secs}s")
    }
}

/// One seller, on its own clock. Two of these rather than one loop asking both: a source
/// that is rate-limiting us must not slow the other one down, and a key that has gone bad
/// must not take the other's numbers off the screen.
struct Poller {
    who: &'static str,
    snap: Snapshot,
    /// Any poll that comes back with numbers puts it back to zero.
    fails: u32,
    /// When this source is next due. Now, on the first lap and whenever it is switched back
    /// on, so a setting turned on in the GUI answers rather than serving out a backoff.
    due: Instant,
}

impl Poller {
    fn new(who: &'static str) -> Self {
        Poller {
            who,
            snap: Snapshot::default(),
            fails: 0,
            due: Instant::now(),
        }
    }

    /// Switched off. Its rows go and the other source's stay, which is the whole point of
    /// there being two of these. Answers whether anything actually changed.
    fn clear(&mut self) -> bool {
        self.fails = 0;
        self.due = Instant::now();
        if self.snap == Snapshot::default() {
            return false;
        }
        self.snap = Snapshot::default();
        true
    }

    /// The delay, the two log edges and the wait written into the error the mod draws.
    fn settle(&mut self, mut next: Snapshot, asked: Option<u64>, base: u64) {
        let was_ok = self.snap.ok;

        let delay = if next.ok {
            self.fails = 0;
            base
        } else {
            self.fails = self.fails.saturating_add(1);
            backoff(base, self.fails, asked)
        };

        // Loud on either edge, because "is the readout live?" is otherwise a question
        // only the game can answer.
        match (&next.error, was_ok) {
            (Some(e), true) => tracing::warn!(
                "{} usage poll failed: {e}; next try in {}",
                self.who,
                human(delay)
            ),
            (Some(e), false) => {
                tracing::debug!("{} usage poll: {e}; next try in {}", self.who, human(delay))
            }
            (None, false) => tracing::info!(
                "{} usage: {}",
                self.who,
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

        // The readout draws this string, and "429" without "and I am not asking again for
        // ten minutes" reads as a daemon that has hung.
        if let Some(msg) = next.error.take() {
            next.error = Some(format!("{msg} - next try in {}", human(delay)));
        }

        self.snap = next;
        self.due = Instant::now() + Duration::from_secs(delay);
    }
}

/// One list for the mod, which draws resources rather than sellers. `ok` is *every* live
/// source being current, because a row nobody could tell from a live one is the one thing
/// this must never draw; the errors are joined so the tooltip says which half is out. A
/// source that is off contributes nothing at all, so switching one off is not a failure.
fn merge(parts: [&Snapshot; 2]) -> Snapshot {
    let mut out = Snapshot {
        ok: true,
        ..Default::default()
    };
    let mut errors = Vec::new();
    let mut any = false;

    for p in parts {
        if p.windows.is_empty() && p.error.is_none() {
            continue;
        }
        any = true;
        out.ok &= p.ok;
        out.fetched_ms = out.fetched_ms.max(p.fetched_ms);
        if out.plan.is_empty() {
            out.plan = p.plan.clone();
        }
        out.windows.extend(p.windows.iter().cloned());
        if let Some(e) = &p.error {
            errors.push(e.clone());
        }
    }

    // Nothing on: the default snapshot, which is what the readout draws nothing from.
    if !any {
        return Snapshot::default();
    }
    if !errors.is_empty() {
        out.error = Some(errors.join("; "));
    }
    out
}

/// How long a lap can be even with both sources due much later: what turns a source on is
/// `config.toml`, and this loop only looks at it between sleeps.
const LOOK: Duration = Duration::from_secs(30);

/// Started from main once, and quiet in the log unless something is wrong: this
/// runs every minute forever.
pub fn spawn(m: Arc<Manager>) -> tokio::task::JoinHandle<()> {
    tokio::spawn(async move {
        let mut anth = Poller::new("Anthropic");
        let mut cred = Poller::new("OpenRouter");
        let mut creds_stamp: Option<SystemTime> = None;

        loop {
            let cfg = m.config().await;
            let d = &cfg.daemon;
            let base = d.usage_poll_secs.max(10);
            let now = Instant::now();
            let mut moved = false;

            let creds_path = PathBuf::from(crate::config::expand(&d.claude_credentials));

            // A login renewed on the host is the one thing that can turn "expired" back into
            // numbers, and the backoff a dead token earned is up to half an hour long - so the
            // file is watched rather than waited out. A stat per lap, against a token read
            // only when a poll is actually due.
            let stamp = std::fs::metadata(&creds_path)
                .and_then(|md| md.modified())
                .ok();
            if stamp != creds_stamp {
                // Only while it is failing, and never on the first lap: this cuts a backoff
                // short rather than being a second way to ask. Claude Code rewrites that file
                // whenever it refreshes a token, and a healthy poller answering every rewrite
                // would be a poll rate set by another program.
                if creds_stamp.is_some() && anth.fails > 0 {
                    anth.fails = 0;
                    anth.due = now;
                }
                creds_stamp = stamp;
            }

            if !d.usage {
                // Off is a setting that can be turned back on without a restart, so this
                // drops the rows rather than returning.
                moved |= anth.clear();
            } else if anth.due <= now {
                let path = creds_path;
                let prev = anth.snap.clone();
                // Both halves are blocking: a file read and a TLS round trip.
                let (next, asked) = tokio::task::spawn_blocking(move || match read_creds(&path) {
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

                anth.settle(next, asked, base);
                moved = true;
            }

            if !d.openrouter {
                moved |= cred.clear();
            } else if cred.due <= now {
                let file = d.openrouter_key_file.clone();
                let prev = cred.snap.clone();
                let (next, asked) = tokio::task::spawn_blocking(move || match read_key(&file) {
                    Err(e) => (Snapshot::failed(&prev, e), None),
                    Ok(key) => match fetch_credits(&key) {
                        Err(e) => {
                            let asked = e.retry_after;
                            (Snapshot::failed(&prev, e), asked)
                        }
                        Ok(body) => (parse_credits(&body), None),
                    },
                })
                .await
                .unwrap_or_default();

                cred.settle(next, asked, base);
                moved = true;
            }

            // One event for both, and only when something actually moved: `set_usage`
            // re-announces to every client.
            if moved {
                let mut out = merge([&anth.snap, &cred.snap]);
                // Said here rather than in `merge`, which knows about snapshots and not about
                // switches. A seller that is on but has never answered is exactly the case
                // this is for, so it goes on the list whatever its snapshot holds.
                if d.usage {
                    out.sources.push("anthropic".into());
                }
                if d.openrouter {
                    out.sources.push("openrouter".into());
                }
                m.set_usage(out).await;
            }

            let due = [(d.usage, anth.due), (d.openrouter, cred.due)]
                .into_iter()
                .filter(|(on, _)| *on)
                .map(|(_, at)| at)
                .min();

            let wait = due
                .map(|at| at.saturating_duration_since(Instant::now()))
                .unwrap_or(LOOK)
                .min(LOOK)
                // A poll that came back instantly must not spin the loop on a due time
                // already in the past.
                .max(Duration::from_secs(1));

            tokio::time::sleep(wait).await;
        }
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Trimmed from what the endpoint answered on a Pro account.
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

    /// The money has a `utilization` too, so it must never come through as a rate
    /// limit. `monthly_limit` is cents: 10000 is the $100 cap.
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
        assert_eq!(m.key, "spend");
        assert_eq!(m.unit, Unit::Pct);
        assert_eq!(m.pct, 21.0);
        assert!(m.amount.is_none());
    }

    #[test]
    fn backoff_doubles_and_caps() {
        assert_eq!(backoff(60, 1, None), 60);
        assert_eq!(backoff(60, 2, None), 120);
        assert_eq!(backoff(60, 4, None), 480);
        // The shift saturates rather than overflowing on a long outage.
        assert_eq!(backoff(60, 30, None), BACKOFF_CAP);
    }

    /// `Retry-After` beats both the doubling and the cap, but may not make the poll
    /// faster than it asked.
    #[test]
    fn retry_after_beats_the_guess() {
        assert_eq!(backoff(60, 1, Some(900)), 900);
        assert_eq!(backoff(60, 1, Some(30)), 60);
        assert_eq!(backoff(60, 9, Some(7200)), 7200);
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
            .all(|w| w.key != "spend"));
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
        assert_eq!(w.key, "balance");
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

    /// The older key endpoint, and its null limit - a figure this cannot subtract from,
    /// which is said rather than drawn as a full bar.
    #[test]
    fn a_key_with_no_limit_is_no_numbers() {
        let v: Value = serde_json::from_str(r#"{"data":{"usage":3.5,"limit":null}}"#).unwrap();
        let s = parse_credits(&v);
        assert!(!s.ok);
        assert!(s.windows.is_empty());
        assert!(s.error.unwrap().contains("no credit limit"));

        let named: Value = serde_json::from_str(r#"{"data":{"usage":3.5,"limit":10.0}}"#).unwrap();
        assert_eq!(parse_credits(&named).windows[0].amount, Some(3.5));
    }

    #[test]
    fn an_unknown_credits_payload_is_not_a_full_wallet() {
        let v: Value = serde_json::from_str(r#"{"data":{"nope":1}}"#).unwrap();
        let s = parse_credits(&v);
        assert!(!s.ok);
        assert!(s.windows.is_empty());
    }

    /// The mod draws resources, not sellers: two sources are one list.
    #[test]
    fn sources_merge_into_one_list() {
        let a = parse(&serde_json::from_str(REAL).unwrap(), "max".into());
        let c = parse_credits(
            &serde_json::from_str(r#"{"data":{"total_credits":25,"total_usage":10}}"#).unwrap(),
        );

        let m = merge([&a, &c]);
        assert!(m.ok);
        assert_eq!(m.windows.len(), a.windows.len() + 1);
        assert_eq!(m.windows.last().unwrap().key, "balance");
        // The plan is the subscription's, and only one source has one.
        assert_eq!(m.plan, "max");
        assert!(m.error.is_none());
    }

    /// One source failing dims the readout and says which, and never empties the other's
    /// numbers. A source switched off contributes nothing and is not a failure.
    #[test]
    fn one_source_down_keeps_the_others_rows() {
        let a = parse(&serde_json::from_str(REAL).unwrap(), "max".into());
        let c = Snapshot::failed(&Snapshot::default(), "OpenRouter rejected the key (401)");

        let m = merge([&a, &c]);
        assert!(!m.ok);
        assert_eq!(m.windows.len(), a.windows.len());
        assert!(m.error.unwrap().contains("OpenRouter"));

        // Off on both sides is the snapshot the readout draws nothing from.
        assert_eq!(
            merge([&Snapshot::default(), &Snapshot::default()]),
            Snapshot::default()
        );
        // And one of them off leaves the other exactly as it was.
        let only = merge([&a, &Snapshot::default()]);
        assert!(only.ok);
        assert_eq!(only.windows, a.windows);
    }

    /// A readout that empties itself every time the wifi hiccups is worse than a stale
    /// one.
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
