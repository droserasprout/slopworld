//! Provider retry and row cadence transitions. Callers supply monotonic time; polling owns I/O.
use super::Snapshot;
use super::providers::RATE_LIMIT_FLOOR_SECS;
use super::rows::{item_enabled, provider_enabled, resolved_catalog, source_for_key};
use std::{
    collections::HashMap,
    time::{Duration, Instant},
};

pub(super) struct PollTransition {
    pub(super) was_ok: bool,
    pub(super) delay: u64,
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
    grown.max(asked.unwrap_or(0)).max(if asked.is_some() {
        RATE_LIMIT_FLOOR_SECS
    } else {
        0
    })
}

/// Independent polling state for one provider.
/// Rate limits and credential failures for this provider must not delay other providers or remove their values.
pub(super) struct Poller {
    pub(super) who: &'static str,
    pub(super) snap: Snapshot,
    /// Failure count. A poll that returns usable values resets it to zero.
    pub(super) fails: u32,
    /// The next polling time for this source.
    /// Poll immediately at startup and when the user enables the source again.
    pub(super) due: Instant,
    /// Last effective cadence, independent of the failure retry deadline.
    pub(super) interval: Option<u64>,
    /// Provider responses contain several windows, so each returned window keeps its own
    /// schedule even though the request itself is shared.
    pub(super) item_due: HashMap<String, Instant>,
}

impl Poller {
    pub(super) fn new(who: &'static str, now: Instant) -> Self {
        Poller {
            who,
            snap: Snapshot::default(),
            fails: 0,
            due: now,
            interval: None,
            item_due: HashMap::new(),
        }
    }

    /// Clear this disabled source's rows without changing other sources.
    /// Return whether the state changed.
    pub(super) fn clear(&mut self, now: Instant) -> bool {
        self.fails = 0;
        self.due = now;
        self.interval = None;
        self.item_due.clear();
        if self.snap == Snapshot::default() {
            return false;
        }
        self.snap = Snapshot::default();
        true
    }

    /// Accept a result and return the transition needed for logging and retry wording.
    pub(super) fn settle(
        &mut self,
        next: Snapshot,
        asked: Option<u64>,
        base: u64,
        now: Instant,
    ) -> PollTransition {
        let was_ok = self.snap.ok;

        let delay = if next.ok {
            self.fails = 0;
            base
        } else {
            self.fails = self.fails.saturating_add(1);
            backoff(base, self.fails, asked)
        };

        self.snap = next;
        self.due = now + Duration::from_secs(delay);
        PollTransition { was_ok, delay }
    }

    /// Resolve row toggles and successful-poll cadence without erasing failure backoff.
    pub(super) fn prepare(
        &mut self,
        d: &crate::config::Daemon,
        source: &str,
        now: Instant,
    ) -> Option<u64> {
        if !provider_enabled(d, source) {
            self.clear(now);
            return None;
        }
        // A row toggle should disappear from the wire immediately, even when this
        // provider is between network polls. Its next enabled poll will repopulate it.
        self.item_due.retain(|key, _| item_enabled(d, source, key));
        self.snap
            .windows
            .retain(|window| item_enabled(d, source, &window.key));

        let interval = provider_interval(d, source, self);
        // Configuration may shorten a successful poll's cadence, but must never
        // erase exponential or upstream failure backoff.
        if self.fails == 0 && self.interval.is_some_and(|previous| interval < previous) {
            self.due = now;
        }
        self.interval = Some(interval);
        Some(interval)
    }
}

fn minimum_poll_secs(source: &str) -> u64 {
    if source == "anthropic" {
        RATE_LIMIT_FLOOR_SECS
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
    let entries = resolved_catalog(&poller.snap.windows);
    entries
        .iter()
        .filter(|entry| {
            entry.provider == source
                && item_enabled(d, source, &entry.key)
                && (poller.snap.fetched_ms == 0
                    || poller.snap.windows.iter().any(|w| w.key == entry.key))
        })
        .map(|entry| item_interval(d, source, &entry.key))
        // Configured dynamic rows may not have been discovered yet.
        .chain(
            d.usage_items
                .iter()
                .filter(|(key, item)| {
                    source_for_key(key) == Some(source) && item.poll && poller.snap.fetched_ms == 0
                })
                .map(|(key, _)| item_interval(d, source, key)),
        )
        .min()
        .unwrap_or(d.usage_poll_secs.max(minimum_poll_secs(source)))
}

pub(super) fn filter_snapshot(
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

#[cfg(test)]
#[path = "schedule_tests.rs"]
mod tests;
