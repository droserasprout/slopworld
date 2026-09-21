//! Polls provider usage with fresh credentials, narrow parsing, and snapshot-preserving failures; unknown responses yield no figures rather than false zeroes.

use std::collections::HashMap;
use std::path::PathBuf;
use std::sync::Arc;
use std::time::{Duration, Instant, SystemTime, UNIX_EPOCH};

use serde::Serialize;

use crate::session::Manager;

mod parsing;
mod providers;
#[cfg(test)]
use parsing::{parse, parse_credits, parse_openai};
use parsing::{parse_anthropic, parse_openai_response, parse_openrouter};
use providers::{
    fetch_anthropic, fetch_openai_provider, fetch_openrouter, read_anthropic, read_openai,
    read_openrouter, FetchProvider, ParseProvider, PrepareProvider, ReadProvider, RATE_LIMIT_FLOOR,
};

pub const CLAUDE_SESSION: &str = "claude_session";
pub const CLAUDE_WEEK: &str = "claude_week";
pub const CLAUDE_SPEND: &str = "claude_spend";
pub const OPENAI_SESSION: &str = "openai_session";
pub const OPENAI_WEEK: &str = "openai_week";
pub const OPENROUTER_BALANCE: &str = "openrouter_balance";

#[cfg(test)]
use providers::{
    cached_anthropic_retry, expiry_error, fresh_anthropic_cache, parse_retry_after, read_creds,
    save_anthropic_cache, save_anthropic_rate_limit,
};

/// Rides on the wire rather than being worked out from the key: the one thing this
/// must never do is let a percentage and a sum of money look alike.
#[derive(Debug, Clone, Copy, PartialEq, Default)]
pub enum Unit {
    /// Percent of a window spent, which is every rate limit.
    #[default]
    Pct,
    /// Dollars spent, which is only ever the extra-usage budget.
    Usd,
}

crate::wire_enum!(Unit, {
    Unit::Pct => crate::shared::protocol::enums::usage_unit::PCT,
    Unit::Usd => crate::shared::protocol::enums::usage_unit::USD,
});

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

/// A daemon-owned usage row definition. The mod may choose icons and wording, but never
/// reconstructs provider/default-poll/rank policy from a key prefix.
#[derive(Debug, Clone, Serialize, PartialEq)]
pub struct CatalogEntry {
    pub key: String,
    pub label: String,
    pub provider: String,
    pub unit: Unit,
    pub rank: i32,
    pub default_poll: bool,
}

pub fn catalog() -> Vec<CatalogEntry> {
    [
        (
            CLAUDE_SESSION,
            "Claude session",
            "anthropic",
            Unit::Pct,
            0,
            true,
        ),
        (
            CLAUDE_WEEK,
            "Claude weekly",
            "anthropic",
            Unit::Pct,
            1,
            true,
        ),
        (
            OPENAI_SESSION,
            "OpenAI session",
            "openai",
            Unit::Pct,
            2,
            true,
        ),
        (OPENAI_WEEK, "OpenAI weekly", "openai", Unit::Pct, 3, true),
        (
            OPENROUTER_BALANCE,
            "OpenRouter balance",
            "openrouter",
            Unit::Usd,
            4,
            false,
        ),
        (
            CLAUDE_SPEND,
            "Claude balance",
            "anthropic",
            Unit::Pct,
            5,
            true,
        ),
    ]
    .into_iter()
    .map(
        |(key, label, provider, unit, rank, default_poll)| CatalogEntry {
            key: key.into(),
            label: label.into(),
            provider: provider.into(),
            unit,
            rank,
            default_poll,
        },
    )
    .collect()
}

#[derive(Debug, Clone, Serialize, PartialEq)]
pub struct UsageRow {
    pub key: String,
    pub label: String,
    pub provider: String,
    pub unit: Unit,
    pub rank: i32,
    pub poll: bool,
    pub stale: bool,
    /// Absent is a real state: the provider is enabled but has not supplied a usable value.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub window: Option<Window>,
}

#[derive(Debug, Clone, Serialize, PartialEq)]
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
    /// Enabled sources whose last poll failed. Kept separate from `ok`: the latter describes
    /// the merged readout, while the mod needs to dim only the seller that is stale.
    #[serde(skip_serializing_if = "Vec::is_empty")]
    pub failed_sources: Vec<String>,
    pub windows: Vec<Window>,
    pub catalog: Vec<CatalogEntry>,
    pub rows: Vec<UsageRow>,
}

impl Snapshot {
    fn failed(prev: &Snapshot, why: impl std::fmt::Display) -> Snapshot {
        Snapshot {
            ok: false,
            error: Some(why.to_string()),
            ..prev.clone()
        }
    }

    /// Compare only readout fields; fetched time ages values, while sources and failures
    /// preserve placeholder rows and provider-local stale state.
    pub fn same_readout(&self, other: &Snapshot) -> bool {
        self.ok == other.ok
            && self.error == other.error
            && self.plan == other.plan
            && self.failed_sources == other.failed_sources
            && self.windows == other.windows
            && self.sources == other.sources
            && self.catalog == other.catalog
            && self.rows == other.rows
    }
}

impl Default for Snapshot {
    fn default() -> Self {
        Self {
            ok: false,
            error: None,
            plan: String::new(),
            fetched_ms: 0,
            sources: Vec::new(),
            failed_sources: Vec::new(),
            windows: Vec::new(),
            catalog: catalog(),
            rows: Vec::new(),
        }
    }
}

fn now_ms() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_millis() as u64)
        .unwrap_or(0)
}

/// The shortest window reported runs five hours, so half an hour behind is still worth
/// drawing. Also bounds how long a fixed login goes unnoticed, the loop only looking at the
/// config between sleeps.
const BACKOFF_CAP: u64 = 1800;

/// A 429 answered at the rate that earned it is a daemon feeding its own rate limit. The
/// endpoint has returned `Retry-After: 0` while continuing to reject requests, so every
/// rate-limited failure gets the same five-minute floor. A longer server answer still wins.
fn backoff(base: u64, fails: u32, asked: Option<u64>) -> u64 {
    let grown = base
        .saturating_mul(1u64 << fails.saturating_sub(1).min(16))
        .min(BACKOFF_CAP);
    grown
        .max(asked.unwrap_or(0))
        .max(if asked.is_some() { RATE_LIMIT_FLOOR } else { 0 })
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
    /// Provider responses contain several windows, so each returned window keeps its own
    /// schedule even though the request itself is shared.
    item_due: HashMap<String, Instant>,
}

impl Poller {
    fn new(who: &'static str) -> Self {
        Poller {
            who,
            snap: Snapshot::default(),
            fails: 0,
            due: Instant::now(),
            item_due: HashMap::new(),
        }
    }

    /// Switched off. Its rows go and the other source's stay, which is the whole point of
    /// there being two of these. Answers whether anything actually changed.
    fn clear(&mut self) -> bool {
        self.fails = 0;
        self.due = Instant::now();
        self.item_due.clear();
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

struct Provider<'a> {
    source: &'static str,
    enabled: fn(&crate::config::Daemon) -> bool,
    poller: &'a mut Poller,
    read: ReadProvider,
    fetch: FetchProvider,
    parse: ParseProvider,
    /// Most providers need no work between laps. Anthropic alone watches its credential mtime.
    prepare: Option<PrepareProvider>,
}

fn anthropic_enabled(d: &crate::config::Daemon) -> bool {
    provider_enabled(d, "anthropic")
}

fn openrouter_enabled(d: &crate::config::Daemon) -> bool {
    provider_enabled(d, "openrouter")
}

fn openai_enabled(d: &crate::config::Daemon) -> bool {
    provider_enabled(d, "openai")
}

fn source_for_key(key: &str) -> Option<&'static str> {
    if key.starts_with("claude_") {
        Some("anthropic")
    } else if key.starts_with("openrouter_") {
        Some("openrouter")
    } else if key.starts_with("openai_") {
        Some("openai")
    } else {
        None
    }
}

fn default_source_enabled(source: &str) -> bool {
    matches!(source, "anthropic" | "openai")
}

fn catalog_entry(key: &str) -> Option<CatalogEntry> {
    catalog().into_iter().find(|entry| entry.key == key)
}

fn default_poll(key: &str, source: &str) -> bool {
    catalog_entry(key)
        .map(|entry| entry.default_poll)
        .unwrap_or_else(|| default_source_enabled(source))
}

fn item_enabled(d: &crate::config::Daemon, source: &str, key: &str) -> bool {
    if source_for_key(key) != Some(source) {
        return false;
    }
    d.usage_items
        .get(key)
        .map(|item| item.poll)
        .unwrap_or_else(|| default_poll(key, source))
}

/// Once a table exists for a provider, turning every row off also turns the provider off. This
/// avoids keeping credentials and network polling alive for a table that visibly has no rows.
fn provider_enabled(d: &crate::config::Daemon, source: &str) -> bool {
    let rows: Vec<_> = d
        .usage_items
        .iter()
        .filter(|(key, _)| source_for_key(key) == Some(source))
        .collect();
    if rows.is_empty() {
        default_source_enabled(source)
    } else {
        rows.iter().any(|(_, item)| item.poll)
    }
}

fn minimum_poll_secs(source: &str) -> u64 {
    if source == "anthropic" {
        RATE_LIMIT_FLOOR
    } else {
        10
    }
}

fn item_interval(d: &crate::config::Daemon, source: &str, key: &str) -> u64 {
    d.usage_items
        .get(key)
        .and_then(|item| item.interval_secs)
        .filter(|seconds| *seconds > 0)
        .unwrap_or(d.usage_poll_secs)
        .max(minimum_poll_secs(source))
}

/// Providers answer several windows in one request. Poll at the fastest enabled row interval;
/// each row's toggle is still applied to the merged snapshot, while a slow row never makes a
/// faster row wait for it. Once the provider has answered, rows absent from that answer cannot
/// make the shared request faster (for example, an optional Claude model window may be null).
fn provider_interval(d: &crate::config::Daemon, source: &str, poller: &Poller) -> u64 {
    let has_answer = poller.snap.fetched_ms > 0;
    d.usage_items
        .iter()
        .filter(|(key, item)| {
            source_for_key(key) == Some(source)
                && item.poll
                && (!has_answer || poller.snap.windows.iter().any(|w| w.key == **key))
        })
        .map(|(key, _)| item_interval(d, source, key))
        .min()
        .unwrap_or(d.usage_poll_secs.max(minimum_poll_secs(source)))
}

fn resolved_catalog(windows: &[Window]) -> Vec<CatalogEntry> {
    let mut entries = catalog();
    for window in windows {
        if entries.iter().any(|entry| entry.key == window.key) {
            continue;
        }
        let provider = source_for_key(&window.key).unwrap_or("unknown");
        let rank = entries.iter().map(|entry| entry.rank).max().unwrap_or(0) + 1;
        entries.push(CatalogEntry {
            key: window.key.clone(),
            label: window.label.clone(),
            provider: provider.into(),
            unit: window.unit,
            rank,
            default_poll: true,
        });
    }
    entries.sort_by_key(|entry| entry.rank);
    entries
}

fn resolve_rows(snapshot: &Snapshot, d: &crate::config::Daemon) -> Vec<UsageRow> {
    let mut rows = Vec::new();
    for entry in &snapshot.catalog {
        // A partial usage table disables the provider as a whole. Do not leave the other
        // catalog entries looking pollable when their implicit defaults outlive that switch.
        let poll = provider_enabled(d, &entry.provider)
            && d.usage_items
                .get(&entry.key)
                .map(|item| item.poll)
                .unwrap_or(entry.default_poll);
        let window = snapshot
            .windows
            .iter()
            .find(|window| window.key == entry.key)
            .cloned();
        // A successful provider response is also an applicability answer: if it returned one
        // of its windows, an omitted catalog entry is not a pending value. In particular, the
        // OpenAI free plan reports its weekly window in `primary_window` and does not have a
        // session window. Keep placeholders only while that provider has supplied no usable
        // windows at all (usually because its first poll failed).
        if window.is_none()
            && snapshot
                .windows
                .iter()
                .any(|window| source_for_key(&window.key) == Some(entry.provider.as_str()))
        {
            continue;
        }
        let stale = snapshot
            .failed_sources
            .iter()
            .any(|source| source == &entry.provider);
        rows.push(UsageRow {
            key: entry.key.clone(),
            label: window
                .as_ref()
                .map(|window| window.label.clone())
                .unwrap_or_else(|| entry.label.clone()),
            provider: entry.provider.clone(),
            unit: window
                .as_ref()
                .map(|window| window.unit)
                .unwrap_or(entry.unit),
            rank: entry.rank,
            poll,
            stale,
            window,
        });
    }
    rows.sort_by_key(|row| row.rank);
    rows
}

fn filter_snapshot(
    poller: &mut Poller,
    mut snapshot: Snapshot,
    d: &crate::config::Daemon,
    source: &str,
    now: Instant,
) -> Snapshot {
    if !snapshot.ok {
        return snapshot;
    }

    poller
        .item_due
        .retain(|key, _| item_enabled(d, source, key));

    let previous = poller.snap.windows.clone();
    let mut seen = Vec::new();
    let mut windows = Vec::new();
    for window in snapshot.windows.drain(..) {
        if !item_enabled(d, source, &window.key) {
            continue;
        }

        let interval = item_interval(d, source, &window.key);
        let due = poller.item_due.entry(window.key.clone()).or_insert(now);
        if *due > now + Duration::from_secs(interval) {
            *due = now;
        }
        if *due <= now {
            *due = now + Duration::from_secs(interval);
            windows.push(window.clone());
        } else if let Some(old) = previous.iter().find(|old| old.key == window.key) {
            windows.push(old.clone());
        }
        seen.push(window.key);
    }

    // An optional window may disappear from a valid response. Keep its last value until that
    // row's own interval is due, then let it disappear instead of showing an old figure forever.
    for old in previous {
        if seen.iter().any(|key| key == &old.key) || !item_enabled(d, source, &old.key) {
            continue;
        }
        if poller.item_due.get(&old.key).is_some_and(|due| *due > now) {
            windows.push(old);
        }
    }
    snapshot.windows = windows;
    snapshot
}

fn preserve_parse_failure(previous: &Snapshot, parsed: Snapshot) -> Snapshot {
    if parsed.ok {
        parsed
    } else {
        Snapshot::failed(
            previous,
            parsed
                .error
                .unwrap_or_else(|| "provider returned no usable usage data".into()),
        )
    }
}

/// A login renewed on the host is the one thing that can turn "expired" back into numbers, and
/// the backoff a dead token earned is up to half an hour long - so the file is watched rather
/// than waited out. This is an optional provider hook because the other credential sources do
/// not have Claude's shared-file renewal behavior.
fn watch_anthropic_stamp(
    d: &crate::config::Daemon,
    poller: &mut Poller,
    creds_stamp: &mut Option<SystemTime>,
    now: Instant,
) {
    let creds_path = PathBuf::from(crate::config::expand(&d.claude_credentials));
    let stamp = std::fs::metadata(&creds_path)
        .and_then(|md| md.modified())
        .ok();
    if stamp != *creds_stamp {
        // Only while it is failing, and never on the first lap: this cuts a backoff short rather
        // than being a second way to ask. Claude Code rewrites that file whenever it refreshes a
        // token, and a healthy poller answering every rewrite would be a poll rate set by another
        // program.
        if creds_stamp.is_some() && poller.fails > 0 {
            poller.fails = 0;
            poller.due = now;
        }
        *creds_stamp = stamp;
    }
}

fn settle_join(
    previous: &Snapshot,
    result: Result<(Snapshot, Option<u64>), tokio::task::JoinError>,
) -> (Snapshot, Option<u64>) {
    match result {
        Ok(result) => result,
        Err(error) => (
            Snapshot::failed(previous, format!("usage poll task failed: {error}")),
            None,
        ),
    }
}

/// Read, fetch, parse and settle one provider. The descriptor supplies the provider-specific
/// functions; all scheduling, failure backoff and snapshot preservation stay here.
async fn poll_one(
    provider: &mut Provider<'_>,
    d: &crate::config::Daemon,
    now: Instant,
    base: u64,
) -> bool {
    if provider.poller.due > now {
        return false;
    }

    let prev = provider.poller.snap.clone();
    let config = d.clone();
    let read = provider.read;
    let fetch = provider.fetch;
    let parse = provider.parse;
    let (next, asked) = settle_join(
        &provider.poller.snap,
        tokio::task::spawn_blocking(move || match read(&config) {
            Err(e) => (Snapshot::failed(&prev, e), None),
            Ok(creds) => match fetch(creds) {
                Err(e) => {
                    let asked = e.retry_after;
                    (Snapshot::failed(&prev, e), asked)
                }
                Ok(response) => (preserve_parse_failure(&prev, parse(response)), None),
            },
        })
        .await,
    );

    let next = filter_snapshot(provider.poller, next, d, provider.source, now);
    provider.poller.settle(next, asked, base);
    true
}

/// One list for the mod, which draws resources rather than sellers. `ok` is *every* live
/// source being current, because a row nobody could tell from a live one is the one thing
/// this must never draw; the errors are joined so the tooltip says which half is out. A
/// source that is off contributes nothing at all, so switching one off is not a failure.
fn merge<'a>(
    parts: impl IntoIterator<Item = (&'a str, &'a Snapshot)>,
    d: &crate::config::Daemon,
) -> Snapshot {
    let mut out = Snapshot {
        ok: true,
        ..Default::default()
    };
    let mut errors = Vec::new();
    let mut any = false;

    for (source, p) in parts {
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
        if !p.ok {
            out.failed_sources.push(source.to_string());
        }
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
    out.catalog = resolved_catalog(&out.windows);
    out.rows = resolve_rows(&out, d);
    out
}

/// How long a lap can be even with both sources due much later: what turns a source on is
/// `config.toml`, and this loop only looks at it between sleeps.
const LOOK: Duration = Duration::from_secs(30);

/// Started from main once, and quiet in the log unless something is wrong: this
/// runs every minute forever.
struct UsagePollers {
    anth: Poller,
    cred: Poller,
    openai: Poller,
    creds_stamp: Option<SystemTime>,
}

impl UsagePollers {
    fn new() -> Self {
        Self {
            anth: Poller::new("Anthropic"),
            cred: Poller::new("OpenRouter"),
            openai: Poller::new("OpenAI"),
            creds_stamp: None,
        }
    }

    async fn poll_once(&mut self, m: &Arc<Manager>) -> Duration {
        let cfg = m.config().await;
        let d = &cfg.daemon;
        let now = Instant::now();
        let mut moved = false;
        let UsagePollers {
            anth,
            cred,
            openai,
            creds_stamp,
        } = self;

        let mut providers = [
            Provider {
                source: "anthropic",
                enabled: anthropic_enabled,
                poller: anth,
                read: read_anthropic,
                fetch: fetch_anthropic,
                parse: parse_anthropic,
                prepare: Some(watch_anthropic_stamp),
            },
            Provider {
                source: "openrouter",
                enabled: openrouter_enabled,
                poller: cred,
                read: read_openrouter,
                fetch: fetch_openrouter,
                parse: parse_openrouter,
                prepare: None,
            },
            Provider {
                source: "openai",
                enabled: openai_enabled,
                poller: openai,
                read: read_openai,
                fetch: fetch_openai_provider,
                parse: parse_openai_response,
                prepare: None,
            },
        ];

        for provider in &mut providers {
            if let Some(prepare) = provider.prepare {
                prepare(d, provider.poller, creds_stamp, now);
            }

            if !(provider.enabled)(d) {
                // Off is a setting that can be turned back on without a restart, so this
                // drops the rows rather than returning.
                moved |= provider.poller.clear();
                continue;
            }

            // A row toggle should disappear from the wire immediately, even when this
            // provider is between network polls. Its next enabled poll will repopulate it.
            let before = provider.poller.snap.windows.len();
            provider
                .poller
                .item_due
                .retain(|key, _| item_enabled(d, provider.source, key));
            provider
                .poller
                .snap
                .windows
                .retain(|window| item_enabled(d, provider.source, &window.key));
            moved |= before != provider.poller.snap.windows.len();

            let interval = provider_interval(d, provider.source, provider.poller);
            // A newly shortened interval should take effect on the next loop rather than
            // waiting out the old, longer due time. Increasing it naturally takes effect
            // after the next poll.
            if provider.poller.due > now + Duration::from_secs(interval) {
                provider.poller.due = now;
            }

            moved |= poll_one(provider, d, now, interval).await;
        }

        // One event for both, and only when something actually moved: `set_usage`
        // re-announces to every client.
        if moved {
            let mut out = merge(
                providers
                    .iter()
                    .map(|provider| (provider.source, &provider.poller.snap)),
                d,
            );
            // Said here rather than in `merge`, which knows about snapshots and not about
            // switches. A seller that is on but has never answered is exactly the case
            // this is for, so it goes on the list whatever its snapshot holds.
            out.sources.extend(
                providers
                    .iter()
                    .filter(|provider| (provider.enabled)(d))
                    .map(|provider| provider.source.into()),
            );
            out.catalog = resolved_catalog(&out.windows);
            out.rows = resolve_rows(&out, d);
            m.set_usage(out).await;
        }

        let due = providers
            .iter()
            .filter(|provider| (provider.enabled)(d))
            .map(|provider| provider.poller.due)
            .min();

        due.map(|at| at.saturating_duration_since(Instant::now()))
            .unwrap_or(LOOK)
            .min(LOOK)
            // A poll that came back instantly must not spin the loop on a due time
            // already in the past.
            .max(Duration::from_secs(1))
    }
}

pub fn spawn(m: Arc<Manager>) -> tokio::task::JoinHandle<()> {
    tokio::spawn(async move {
        let mut pollers = UsagePollers::new();
        loop {
            let wait = pollers.poll_once(&m).await;
            tokio::time::sleep(wait).await;
        }
    })
}

#[cfg(test)]
mod tests {
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

    /// The file has no atomic writer any more: inside a sandbox the rename fails with `EBUSY`
    /// and Claude Code truncates the host's inode in place instead, so a poll can read a file
    /// mid-write. Half-written is a *parse* error, and one retry is what keeps a microsecond
    /// window from costing half an hour of a wrong readout.
    #[test]
    fn a_half_written_credentials_file_is_read_again_rather_than_failed() {
        let path = std::env::temp_dir().join(format!("slopd-torn-{}.json", std::process::id()));
        let whole = r#"{"claudeAiOauth":{"accessToken":"t","subscriptionType":"max"}}"#;

        // The state O_TRUNC leaves behind, before the write lands.
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

        // And a file that is still not JSON once the window has passed is a real error, not
        // something to keep quiet about.
        std::fs::write(&path, "half a {").unwrap();
        assert!(read_creds(&path).is_err());

        // A missing file is not retried at all - it is an IO error, and it will still be
        // missing in 50ms.
        std::fs::remove_file(&path).unwrap();
        assert!(read_creds(&path).is_err());
    }

    /// Both stamps are epoch milliseconds. Written as a real pair off a live credentials
    /// file, because the failure this guards is a unit slip and a synthetic `100 < 200`
    /// would survive one.
    #[test]
    fn expiry_reads_milliseconds() {
        // Issued 15:10 UTC-3, eight hours to run.
        let exp = 1_786_327_807_534;
        let refresh = 1_788_638_739_534;

        assert!(expiry_error(Some(exp), Some(refresh), exp - 1).is_none());
        assert!(expiry_error(Some(exp), Some(refresh), exp + 1).is_some());

        // The slip that started this: seconds either side of the comparison, and a token
        // three hours dead reads as good until the year 58,000.
        assert!(expiry_error(Some(exp), Some(refresh), exp / 1000).is_none());
        assert!(expiry_error(Some(exp * 1000), Some(refresh), exp + 10_800_000).is_none());
    }

    /// The whole point of telling them apart: an access token the host will renew on its
    /// own must not send anyone to `claude auth`.
    #[test]
    fn a_renewable_token_is_not_a_logged_out_host() {
        let now = 1_786_327_807_534;
        let stale = now - 1;
        let live_refresh = now + 30 * 86_400_000;

        let renew = expiry_error(Some(stale), Some(live_refresh), now).unwrap();
        assert!(renew.contains("needs renewing"));
        assert!(!renew.contains("claude auth"));

        let relogin = expiry_error(Some(stale), Some(stale), now).unwrap();
        assert!(relogin.contains("claude auth"));

        // No refresh stamp at all is not a dead one.
        assert!(!expiry_error(Some(stale), None, now)
            .unwrap()
            .contains("claude auth"));

        // Nothing said about expiry is not an expired token.
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

    /// `Retry-After` beats both the doubling and the cap, but may not make the poll
    /// faster than the rate-limit floor.
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

    /// The mod draws resources, not sellers: three sources are one list.
    #[test]
    fn sources_merge_into_one_list() {
        let a = parse(&serde_json::from_str(REAL).unwrap(), "max".into());
        let c = parse_credits(
            &serde_json::from_str(r#"{"data":{"total_credits":25,"total_usage":10}}"#).unwrap(),
        );

        let o = parse_openai(
            &serde_json::from_str(r#"{"rate_limit":{"primary_window":{"used_percent":20}}}"#)
                .unwrap(),
        );
        let m = merge(
            [("anthropic", &a), ("openrouter", &c), ("openai", &o)],
            &crate::config::Daemon::default(),
        );
        assert!(m.ok);
        assert_eq!(m.windows.len(), a.windows.len() + 2);
        assert_eq!(m.windows.last().unwrap().key, "openai_session");
        // The plan is the subscription's, and only one source has one.
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
        let canceled =
            tokio::spawn(async { std::future::pending::<(Snapshot, Option<u64>)>().await });
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

    /// Turning a seller off is a change the mod has to hear about even when every figure it
    /// was already drawing stayed put: it draws a row per *expected* source, so a snapshot
    /// that differs only in `sources` must not be swallowed as "nothing moved". `fetched_ms`
    /// is the other way round - it moves every lap and is not worth a broadcast on its own.
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

    /// One source failing dims the readout and says which, and never empties the other's
    /// numbers. A source switched off contributes nothing and is not a failure.
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

        // Off on both sides is the snapshot the readout draws nothing from.
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
        // And one of them off leaves the other exactly as it was.
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
        let refreshed =
            filter_snapshot(&mut poller, session_snapshot(90), &daemon, "anthropic", now);
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
        let disabled =
            filter_snapshot(&mut poller, session_snapshot(99), &daemon, "anthropic", now);
        assert!(disabled.windows.is_empty());
        assert!(poller.item_due.is_empty());
    }
}
