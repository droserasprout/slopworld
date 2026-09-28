//! Session views, activity classification and persistence, and the retick loop.

use super::super::*;
use crate::clock::unix_ms;

// Working sessions become idle after this long without meaningful activity.
pub(super) const IDLE_MS: u64 = 10_000;
// Refresh host cwd/process metadata independently of frame classification.
pub(super) const HOST_METADATA_POLL_MS: u64 = 2_000;

/// Compiled activity rules and the revision used to invalidate classification caches.
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
    let mut lines = text.lines().rev();
    let mut line = lines.find(|line| !line.trim().is_empty())?;
    for index in 0..TAIL_LINES {
        for (state, re) in rules {
            if re.is_match(line) {
                return Some(*state);
            }
        }
        if index + 1 == TAIL_LINES {
            break;
        }
        let Some(next) = lines.next() else {
            break;
        };
        line = next;
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

fn classify_match(matched: Option<State>, changed: bool, last_change: u64) -> State {
    if let Some(state) = matched {
        match state {
            // A prompt remains a prompt until the agent changes it or the user answers it.
            // An explicit idle rule is likewise authoritative.
            State::Waiting | State::Idle => return state,
            // Some agent interfaces retain a working status line after output stops.
            // Apply the fallback classifier's activity timeout to these matches.
            // Otherwise, the agent could remain in Working state indefinitely.
            State::Working if !changed && unix_ms().saturating_sub(last_change) >= IDLE_MS => {
                return State::Idle;
            }
            State::Working => return State::Working,
            State::Down => {}
        }
    }
    if changed || unix_ms().saturating_sub(last_change) < IDLE_MS {
        State::Working
    } else {
        State::Idle
    }
}

impl Manager {
    pub async fn views(&self) -> Vec<SessionView> {
        let cfg = self.config().await;
        let worktrees = self.worktree_view_index().await;
        let live = self.live.read().await;
        let temp = self.temp.read().await;
        let mut out: Vec<SessionView> = live
            .values()
            .map(|l| {
                let p = cfg.project_of(&l.cfg).or_else(|| temp.get(&l.cfg.project));
                // An unassigned host shell uses a temporary project to hold its working directory.
                // Exclude that project from sidebar grouping so the shell stays with the top host-terminal rows.
                let display_project = if l.host && temp.contains_key(&l.cfg.project) {
                    String::new()
                } else {
                    l.cfg.project.clone()
                };
                SessionView {
                    worktree: l.cfg.worktree.clone(),
                    worktree_name: worktrees
                        .get(&l.cfg.worktree)
                        .map(|w| w.name.clone())
                        .unwrap_or_default(),
                    name: l.cfg.name.clone(),
                    label: l.cfg.label.clone().unwrap_or_default(),
                    intent: l.cfg.intent.clone(),
                    reader: SessionReaderView {
                        path: l.cfg.reader_path.clone(),
                        key: l.cfg.reader_key.clone(),
                        scope: l.cfg.reader_scope.clone(),
                        pinned: l.cfg.reader_pinned,
                        line: l.cfg.reader_line,
                    },
                    project: display_project,
                    dir: if l.host && !l.host_path.trim().is_empty() {
                        l.host_path.clone()
                    } else {
                        worktrees
                            .get(&l.cfg.worktree)
                            .filter(|w| p.is_some_and(|p| !p.id.is_empty() && w.project_id == p.id))
                            .map(|w| w.path.clone())
                            .unwrap_or_else(|| p.map(|p| p.dir.clone()).unwrap_or_default())
                    },
                    launch: SessionLaunchView {
                        command: l.cfg.command.clone(),
                        command_preset: cfg.command_name(&l.cfg),
                        cmd: l.cfg.cmd.clone(),
                        sandbox: l.cfg.sandbox.clone(),
                        persistent_tmp: l.cfg.persistent_tmp,
                        agent: cfg.command_of(&l.cfg),
                        network: p.map(|p| cfg.network_of(&l.cfg, p)).unwrap_or_default(),
                        dns: p.map(|p| cfg.dns_of(&l.cfg, p)).unwrap_or_default(),
                        limits: p.map(|p| cfg.limits_of(&l.cfg, p)).unwrap_or(l.cfg.limits),
                        mounts: p.map(|p| p.mounts.clone()).unwrap_or_default(),
                        // A worker's lifecycle is task-owned even if an older config file still
                        // carries the parent's flags. Report the effective policy, not stale data.
                        autostart: l.cfg.autostart && !l.cfg.worker,
                        auto_resume: l.cfg.auto_resume && !l.cfg.worker,
                    },
                    worker: SessionWorkerView {
                        enabled: l.cfg.worker,
                        parent: l.cfg.parent.clone(),
                        task_id: l.cfg.task_id.clone(),
                        durable: l.cfg.worker && !l.ephemeral,
                    },
                    ephemeral: l.ephemeral,
                    host: l.host,
                    runtime: SessionRuntimeView {
                        auto_resume_pending: l.input.auto_resume_pending,
                        state: l.state,
                        alive: l.state != State::Down,
                        cols: l.cols,
                        rows: l.rows,
                        process_running: l.process_running,
                        last_change: l.last_change,
                        state_since: l.state_since,
                        title: l
                            .cfg
                            .label
                            .clone()
                            .filter(|label| !label.trim().is_empty())
                            .or_else(|| l.title.override_title.clone())
                            .or_else(|| l.screen.as_ref().map(|s| s.title.clone()))
                            .unwrap_or_default(),
                        bell: l.bell,
                        run_id: l.run_id,
                        seq: l.seq,
                    },
                }
            })
            .collect();
        out.sort_by(|a, b| a.name.cmp(&b.name));
        out
    }

    pub(super) async fn announce_sessions(&self) {
        self.emit(Event::Sessions {
            sessions: self.views().await,
        });
    }

    pub(super) async fn classify_with_cache(
        &self,
        changed: bool,
        last_change: u64,
        text: &str,
        cache: Option<&RuleCache>,
    ) -> Classification {
        // Hold the rules read lock even for cache hits to keep the revision and cached result consistent.
        // Reuse cached results for unchanged text to avoid regex processing.
        // Configuration publication replaces both values together under the write lock.
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
            state: classify_match(matched, changed, last_change),
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

        // Classification awaits the rules lock, so snapshot before releasing the live lock.
        let snapshot: Vec<RetickSnapshot> = {
            let live = self.live.read().await;
            let rules_revision = self.rules.revision.load(Ordering::Acquire);
            live.iter()
                .filter(|(_, l)| l.state != State::Down)
                .filter_map(|(n, l)| {
                    let _ = l.screen.as_ref()?;
                    let cache_current = l.rule_cache.as_ref().is_some_and(|cache| {
                        cache.revision == rules_revision && cache.text.as_ref() == l.plain.as_ref()
                    });
                    let decay_due = matches!(l.state, State::Working | State::Waiting)
                        && !l.rule_cache.as_ref().is_some_and(|cache| {
                            matches!(cache.matched, Some(State::Waiting | State::Idle))
                        })
                        && now >= l.last_change.saturating_add(IDLE_MS);
                    if l.seq == l.retick_seq && cache_current && !decay_due {
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
        };

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
            let mut activity = None;
            let mut live = self.live.write().await;
            if let Some(l) = live.get_mut(&previous.name) {
                // Output, stop/start, or another classification can supersede this snapshot
                // while the rules lock is awaited. Never mark newer frames as classified.
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
                    continue;
                }
                l.rule_cache = Some(RuleCache {
                    revision: classification.rules_revision,
                    text: previous.plain.clone(),
                    matched: classification.matched,
                });
                l.retick_seq = previous.seq;
                if l.set_state(classification.state) {
                    dirty_list = true;
                    if !l.ephemeral {
                        activity = Some((l.state, l.state_since));
                    }
                }
            }
            drop(live);
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
    // Activity persistence in the daemon cache and tmux.

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
