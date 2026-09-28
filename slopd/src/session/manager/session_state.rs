//! Match terminal activity, schedule reclassification, and commit retick transitions.
//! Session projection and publication live in views.rs; polling deadlines live in maintenance.rs.

use super::super::*;
use crate::clock::unix_ms;

// Working sessions become idle after this long without meaningful activity.
pub(super) const IDLE_MS: u64 = 10_000;
// Refresh host cwd/process metadata independently of frame classification.
pub(super) const HOST_METADATA_POLL_MS: u64 = 2_000;

/// Compiled activity rules and their cache revision.
/// Writers update both under `compiled`; the atomic revision also lets live-state checks
/// detect rule changes without acquiring the rules lock.
pub(crate) struct ActivityRules {
    pub(super) compiled: RwLock<Vec<(State, Regex)>>,
    pub(super) revision: AtomicU64,
}

impl ActivityRules {
    pub(super) fn new(compiled: Vec<(State, Regex)>) -> Self {
        Self {
            compiled: RwLock::new(compiled),
            revision: AtomicU64::new(0),
        }
    }
}

// Limit classification to the tail ending at the last nonblank terminal line.
pub(super) const TAIL_LINES: usize = 12;

fn match_rules(rules: &[(State, Regex)], text: &str) -> Option<State> {
    // Use the lowest matching line. Configuration order resolves ties on that line.
    // Skip only trailing blank rows when locating the screen's end.
    // Each blank row within the tail counts toward TAIL_LINES.
    for line in text
        .lines()
        .rev()
        .skip_while(|line| line.trim().is_empty())
        .take(TAIL_LINES)
    {
        for (state, re) in rules {
            if re.is_match(line) {
                return Some(*state);
            }
        }
    }
    None
}

pub(super) fn compile_rules(cfg: &Config) -> Vec<(State, Regex)> {
    cfg.state_rules
        .iter()
        .filter_map(|r| {
            let state = match r.state.as_str() {
                "waiting" => State::Waiting,
                "working" => State::Working,
                "idle" => State::Idle,
                other => {
                    tracing::warn!("state_rule has unknown state {other:?}, ignoring");
                    return None;
                }
            };
            match Regex::new(&r.pattern) {
                Ok(re) => Some((state, re)),
                Err(e) => {
                    tracing::warn!("bad state_rule pattern {:?}: {e}", r.pattern);
                    None
                }
            }
        })
        .collect()
}

/// Next classification deadline in epoch milliseconds: zero means reclassify immediately.
/// `None` means the current session needs no classification until its inputs change.
/// Maintenance wake scheduling and retick selection share this cache and decay policy.
pub(super) fn classification_deadline(live: &Live, rules_revision: u64) -> Option<u64> {
    if live.state == State::Down || live.screen.is_none() {
        return None;
    }
    let cache_current = live.rule_cache.as_ref().is_some_and(|cache| {
        cache.revision == rules_revision && cache.text.as_ref() == live.plain.as_ref()
    });
    if live.seq != live.retick_seq || !cache_current {
        return Some(0);
    }
    let can_decay = matches!(live.state, State::Working | State::Waiting)
        && !live
            .rule_cache
            .as_ref()
            .is_some_and(|cache| matches!(cache.matched, Some(State::Waiting | State::Idle)));
    can_decay.then(|| live.last_change.saturating_add(IDLE_MS))
}

/// Classification inputs and the identity checks required before committing their result.
struct RetickSnapshot {
    name: String,
    run_id: u64,
    seq: u64,
    state: State,
    rules_revision: u64,
    last_change: u64,
    plain: Arc<String>,
    rule_cache: Option<RuleCache>,
}

/// Apply decay at `now` (epoch milliseconds); `changed` means meaningful terminal activity.
fn classify_match(matched: Option<State>, changed: bool, last_change: u64, now: u64) -> State {
    if let Some(state) = matched {
        match state {
            // Waiting and idle matches remain authoritative while the matching text remains.
            State::Waiting | State::Idle => return state,
            // Some agent interfaces retain a working status line after output stops.
            // Apply the fallback classifier's activity timeout to these matches.
            // Otherwise, the agent could remain in Working state indefinitely.
            State::Working if !changed && now.saturating_sub(last_change) >= IDLE_MS => {
                return State::Idle;
            }
            State::Working => return State::Working,
            State::Down => {}
        }
    }
    if changed || now.saturating_sub(last_change) < IDLE_MS {
        State::Working
    } else {
        State::Idle
    }
}

impl Manager {
    pub(super) async fn classify_with_cache(
        &self,
        changed: bool,
        last_change: u64,
        text: &str,
        cache: Option<&RuleCache>,
    ) -> Classification {
        // Even cache hits take the rules lock: publication updates rules and revision together.
        // Cache only the text match; activity decay must be evaluated on every call.
        let rules = self.rules.compiled.read().await;
        let current_revision = self
            .rules
            .revision
            .load(std::sync::atomic::Ordering::Acquire);
        let matched = cache
            .filter(|cache| cache.revision == current_revision && cache.text.as_str() == text)
            .map(|cache| cache.matched)
            .unwrap_or_else(|| match_rules(&rules, text));
        Classification {
            state: classify_match(matched, changed, last_change, unix_ms()),
            rules_revision: current_revision,
            matched,
        }
    }

    #[cfg(test)]
    pub(super) async fn classify(&self, changed: bool, last_change: u64, text: &str) -> State {
        self.classify_with_cache(changed, last_change, text, None)
            .await
            .state
    }

    #[cfg(test)]
    pub(super) async fn classify_initial(&self, previous: State, text: &str) -> State {
        let classification = self.classify_with_cache(false, 0, text, None).await;
        classification
            .matched
            .unwrap_or(if previous != State::Down {
                previous
            } else {
                State::Working
            })
    }

    /// Copy due inputs under the live lock, then release it before awaiting classification.
    async fn retick_snapshots(&self, now: u64) -> Vec<RetickSnapshot> {
        let live = self.live.read().await;
        let rules_revision = self.rules.revision.load(Ordering::Acquire);
        live.iter()
            .filter_map(|(n, l)| {
                if !classification_deadline(l, rules_revision).is_some_and(|due| due <= now) {
                    return None;
                }
                Some(RetickSnapshot {
                    name: n.clone(),
                    run_id: l.run_id,
                    seq: l.seq,
                    state: l.state,
                    rules_revision,
                    last_change: l.last_change,
                    plain: l.plain.clone(),
                    rule_cache: l.rule_cache.clone(),
                })
            })
            .collect()
    }

    /// Return whether the session list changed and any activity record to persist.
    /// The live lock is released before the caller performs persistence or publication.
    async fn commit_retick(
        &self,
        previous: &RetickSnapshot,
        classification: Classification,
    ) -> (bool, Option<(State, u64)>) {
        let mut live = self.live.write().await;
        if let Some(l) = live.get_mut(&previous.name) {
            // Output, lifecycle changes, or rule publication may supersede the snapshot
            // while classification or this write lock is awaited. Reject the entire
            // result, including cache and retick_seq updates, when its inputs are stale.
            if l.run_id != previous.run_id
                || l.seq != previous.seq
                || l.state != previous.state
                || self
                    .rules
                    .revision
                    .load(std::sync::atomic::Ordering::Acquire)
                    != previous.rules_revision
                || classification.rules_revision != previous.rules_revision
            {
                return (false, None);
            }
            l.rule_cache = Some(RuleCache {
                revision: classification.rules_revision,
                text: previous.plain.clone(),
                matched: classification.matched,
            });
            l.retick_seq = previous.seq;
            if l.set_state(classification.state) {
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
            let classification = self
                .classify_with_cache(
                    false,
                    previous.last_change,
                    previous.plain.as_str(),
                    previous.rule_cache.as_ref(),
                )
                .await;
            let (changed, activity) = self.commit_retick(&previous, classification).await;
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
