//! Poll provider usage with current credentials and validated response fields.
//! Preserve snapshots after failures. Unknown responses supply no values, instead of incorrect zero values.

use std::collections::HashMap;
use std::path::PathBuf;
use std::sync::Arc;
use std::time::{Duration, Instant, SystemTime};

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

/// Include units in the protocol so clients can distinguish percentages from monetary amounts without interpreting keys.
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

/// A rate-limit window or extra-usage budget. The `unit` field distinguishes their values.
#[derive(Debug, Clone, Serialize, PartialEq)]
pub struct Window {
    pub key: String,
    pub label: String,
    /// Percentage from 0 to 100. Always present, even when the monetary amount is unavailable.
    pub pct: f32,
    pub unit: Unit,
    /// Monetary amount, if supplied by the response. Otherwise, the row shows only a percentage.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub amount: Option<f32>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub limit: Option<f32>,
    /// Time until reset. The mod uses its own clock to update the countdown between snapshots.
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
    /// If false, `windows` retains the last successful poll results.
    /// This preserves previous values during network failures.
    pub ok: bool,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub error: Option<String>,
    /// The daemon reads this value from the credentials file. Examples include "pro" and "max".
    pub plan: String,
    /// Unix millis of the last successful poll, so the mod can age the numbers.
    pub fetched_ms: u64,
    /// Enabled providers, including those without responses.
    /// The mod keeps a row for each expected resource.
    /// Unavailable sources keep their icons without values so the display layout remains stable.
    #[serde(skip_serializing_if = "Vec::is_empty")]
    pub sources: Vec<String>,
    /// Enabled sources whose last poll failed.
    /// `ok` describes the combined display. This list lets the mod dim only providers with outdated data.
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

    /// Compare display fields. Fetch time indicates the age of values.
    /// Sources and failures preserve placeholder rows and identify outdated provider data.
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

/// Limit the calculated retry delay to half an hour.
/// This preserves periodic retries after credential problems without frequent requests during failures.
const BACKOFF_CAP: u64 = 1800;

/// Delay retries after 429 responses to avoid repeated rate-limit failures.
/// The endpoint has returned `Retry-After: 0` while still rejecting requests.
/// Use a minimum delay of five minutes. Use a longer delay if the server requests one.
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

/// Independent polling state for one provider.
/// Rate limits and credential failures for this provider must not delay other providers or remove their values.
struct Poller {
    who: &'static str,
    snap: Snapshot,
    /// Failure count. A poll that returns usable values resets it to zero.
    fails: u32,
    /// The next polling time for this source.
    /// Poll immediately at startup and when the user enables the source again.
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

    /// Clear this disabled source's rows without changing other sources.
    /// Return whether the state changed.
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
                "{} usage poll failed: {e}. Next try in {}.",
                self.who,
                human(delay)
            ),
            (Some(e), false) => {
                tracing::debug!(
                    "{} usage poll: {e}. Next try in {}.",
                    self.who,
                    human(delay)
                )
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

/// Providers return multiple windows per request. Poll at the shortest interval among enabled rows.
/// Apply each row's enabled setting to the combined snapshot.
/// After a provider responds, exclude absent rows when selecting the shortest interval.
/// For example, an optional Claude model window can be null.
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

/// Watch the credential file to detect host login renewal before a retry delay ends.
/// An expired token can cause a delay of up to half an hour.
/// This optional hook supports Claude's shared credential file. Other credential sources do not use this renewal mechanism.
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
        // End a retry delay early only after a failure and after the first file check.
        // Claude Code rewrites this file when it refreshes a token.
        // Do not let those writes control the polling rate when polls succeed.
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

/// Poll one provider and update its state. The descriptor supplies provider-specific functions.
/// Keep scheduling, retry delays, and snapshot preservation here.
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

/// Combine provider snapshots into one resource list for the mod.
/// Set `ok` only when every enabled source has current data.
/// Combine errors so the tooltip identifies unavailable sources.
/// Exclude disabled sources. Disabling a source is not a failure.
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
        out.error = Some(errors.join(". "));
    }
    out.catalog = resolved_catalog(&out.windows);
    out.rows = resolve_rows(&out, d);
    out
}

/// Maximum sleep before checking configuration again, even if provider polls are due later.
/// This lets the loop detect newly enabled sources.
const LOOK: Duration = Duration::from_secs(30);

/// Provider polling state for the background task started by main.
/// Log errors without logging each successful poll.
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
                // Clear disabled rows, but keep the loop active.
                // The user can enable the source again without a restart.
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

        // Send one combined event only when data changes. `set_usage` broadcasts to all clients.
        if moved {
            let mut out = merge(
                providers
                    .iter()
                    .map(|provider| (provider.source, &provider.poller.snap)),
                d,
            );
            // Add enabled sources here because merge handles snapshots without configuration flags.
            // Include each enabled provider even if it has never supplied a snapshot.
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
mod tests;
