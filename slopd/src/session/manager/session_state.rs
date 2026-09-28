//! Classify terminal activity, schedule idle decay, and persist state transitions.
//! Session projection lives in views.rs; polling deadlines live in maintenance.rs.

use super::super::*;
use crate::clock::unix_ms;

pub(super) const IDLE_MS: u64 = 10_000;
pub(super) const HOST_METADATA_POLL_MS: u64 = 2_000;

/// Down and uncaptured sessions need no timer. New frames need one bookkeeping pass.
pub(super) fn classification_deadline(live: &Live) -> Option<u64> {
    if live.state == State::Down || live.screen.is_none() {
        return None;
    }
    if live.seq != live.retick_seq {
        return Some(0);
    }
    matches!(live.state, State::Working | State::Waiting)
        .then(|| live.last_change.saturating_add(IDLE_MS))
}

struct RetickSnapshot {
    name: String,
    run_id: u64,
    seq: u64,
    state: State,
    last_change: u64,
}

/// Meaningful terminal activity starts a ten-second Working interval.
pub(super) fn classify_activity(changed: bool, last_change: u64, now: u64) -> State {
    if changed || now.saturating_sub(last_change) < IDLE_MS {
        State::Working
    } else {
        State::Idle
    }
}

impl Manager {
    /// Copy due inputs under the live lock; commit later rechecks their identity.
    async fn retick_snapshots(&self, now: u64) -> Vec<RetickSnapshot> {
        let live = self.live.read().await;
        live.iter()
            .filter_map(|(n, l)| {
                if !classification_deadline(l).is_some_and(|due| due <= now) {
                    return None;
                }
                Some(RetickSnapshot {
                    name: n.clone(),
                    run_id: l.run_id,
                    seq: l.seq,
                    state: l.state,
                    last_change: l.last_change,
                })
            })
            .collect()
    }

    /// Return whether the session list changed and any activity record to persist.
    /// The live lock is released before the caller performs persistence or publication.
    async fn commit_retick(
        &self,
        previous: &RetickSnapshot,
        state: State,
    ) -> (bool, Option<(State, u64)>) {
        let mut live = self.live.write().await;
        if let Some(l) = live.get_mut(&previous.name) {
            // A newer frame, run, or state supersedes this activity sample.
            if l.run_id != previous.run_id || l.seq != previous.seq || l.state != previous.state {
                return (false, None);
            }
            l.retick_seq = previous.seq;
            if l.set_state(state) {
                let activity = (!l.ephemeral).then_some((l.state, l.state_since));
                return (true, activity);
            }
        }
        (false, None)
    }

    /// Poll external state, classify due snapshots, then persist and announce committed changes.
    pub async fn retick(self: &Arc<Self>) {
        let _perf = crate::perf::timer("retick");
        let started = crate::perf::enabled().then(std::time::Instant::now);
        self.reload_if_due().await;

        let now = unix_ms();
        let has_hosts = self
            .live
            .read()
            .await
            .values()
            .any(|l| l.host && l.state != State::Down);
        let host_poll_due = has_hosts
            && now.saturating_sub(
                self.host_metadata
                    .checked
                    .load(std::sync::atomic::Ordering::Acquire),
            ) >= HOST_METADATA_POLL_MS;
        if host_poll_due {
            self.host_metadata
                .checked
                .store(now, std::sync::atomic::Ordering::Release);
            self.start_host_metadata_poll().await;
        }

        let snapshot = self.retick_snapshots(now).await;

        let classified = snapshot.len();
        let mut dirty_list = false;
        for previous in snapshot {
            let state = classify_activity(false, previous.last_change, now);
            let (changed, activity) = self.commit_retick(&previous, state).await;
            dirty_list |= changed;
            if let Some((state, state_since)) = activity {
                self.persist_activity(&previous.name, state, state_since)
                    .await;
            }
        }

        if dirty_list {
            self.announce_sessions().await;
        }
        crate::perf::count("retick-classified", classified as u64);
        if let Some(started) = started {
            tracing::debug!(
                target: "slopd::perf",
                lane = "retick",
                elapsed_us = started.elapsed().as_micros() as u64,
                classified,
                host_poll = host_poll_due,
                "daemon retick"
            );
        }
        drop(_perf);
        crate::perf::maybe_report();
    }
}

impl Manager {
    // Update the disk fallback cache and tmux metadata independently so one failure
    // does not prevent the other recovery source from being updated.

    pub(super) async fn clear_activity(&self, name: &str) {
        if let Err(error) = self.activity_cache.clear(name) {
            tracing::warn!(
                target: "slopd::activity",
                session = %name,
                %error,
                "could not clear session activity cache"
            );
        }
        if let Err(error) = self.tmux.clear_activity(name).await {
            tracing::debug!(
                target: "slopd::activity",
                session = %name,
                %error,
                "could not clear tmux session activity"
            );
        }
    }

    pub(super) async fn persist_activity(&self, name: &str, state: State, state_since: u64) {
        if let Err(error) = self.activity_cache.remember(name, state, state_since) {
            tracing::warn!(
                target: "slopd::activity",
                session = %name,
                %error,
                "could not persist session activity"
            );
        }
        if let Err(error) = self.tmux.set_activity(name, state, state_since).await {
            tracing::warn!(
                target: "slopd::activity",
                session = %name,
                %error,
                "could not persist tmux session activity"
            );
        }
    }
}

#[cfg(test)]
#[path = "session_state_tests.rs"]
mod tests;
