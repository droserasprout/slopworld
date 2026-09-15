//! Session views, state classification, and the manager retick loop.

use super::super::*;

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
            // Some agent TUIs leave their working status line on screen after they stop
            // producing output. Let that stale match decay with the same activity clock as
            // the fallback classifier, or it can keep an agent working forever.
            State::Working if !changed && now_ms().saturating_sub(last_change) >= IDLE_MS => {
                return State::Idle;
            }
            State::Working => return State::Working,
            State::Down => {}
        }
    }
    if changed || now_ms().saturating_sub(last_change) < IDLE_MS {
        State::Working
    } else {
        State::Idle
    }
}

impl Manager {
    pub async fn views(&self) -> Vec<SessionView> {
        let cfg = self.config().await;
        let live = self.live.read().await;
        let temp = self.temp.read().await;
        let mut out: Vec<SessionView> = live
            .values()
            .map(|l| {
                let p = cfg.project_of(&l.cfg).or_else(|| temp.get(&l.cfg.project));
                // An unassigned host shell uses a disposable project only to own its
                // working directory. Keep that implementation detail out of sidebar
                // grouping so it remains in the top host-terminal rows.
                let display_project = if l.host && temp.contains_key(&l.cfg.project) {
                    String::new()
                } else {
                    l.cfg.project.clone()
                };
                SessionView {
                    name: l.cfg.name.clone(),
                    label: l.cfg.label.clone().unwrap_or_default(),
                    project: display_project,
                    dir: if l.host && !l.host_path.trim().is_empty() {
                        l.host_path.clone()
                    } else {
                        p.map(|p| p.dir.clone()).unwrap_or_default()
                    },
                    command: l.cfg.command.clone(),
                    command_preset: cfg.command_name(&l.cfg),
                    cmd: l.cfg.cmd.clone(),
                    sandbox: l.cfg.sandbox.clone(),
                    breadcrumbs: l.cfg.breadcrumbs.clone(),
                    slopworld_md: l.cfg.slopworld_md,
                    instructions_breadcrumb: l.cfg.instructions_breadcrumb,
                    persistent_tmp: l.cfg.persistent_tmp,
                    breadcrumb_yolo: l.cfg.breadcrumb_yolo,
                    breadcrumbs_pending: l.breadcrumbs_pending,
                    auto_resume_pending: l.auto_resume_pending,
                    agent: cfg.command_of(&l.cfg),
                    state: l.state,
                    alive: l.state != State::Down,
                    cols: l.cols,
                    rows: l.rows,
                    network: p.map(|p| cfg.network_of(&l.cfg, p)).unwrap_or_default(),
                    network_override: l.cfg.network,
                    dns: p.map(|p| cfg.dns_of(&l.cfg, p)).unwrap_or_default(),
                    dns_override: l.cfg.dns.clone(),
                    limits: p.map(|p| cfg.limits_of(&l.cfg, p)).unwrap_or(l.cfg.limits),
                    limits_override: l.cfg.limits,
                    mounts: l.cfg.mounts.clone(),
                    // A worker's lifecycle is task-owned even if an older config file still
                    // carries the parent's flags. Report the effective policy, not stale data.
                    autostart: l.cfg.autostart && !l.cfg.worker,
                    auto_resume: l.cfg.auto_resume && !l.cfg.worker,
                    worker: l.cfg.worker,
                    parent: l.cfg.parent.clone(),
                    task_id: l.cfg.task_id.clone(),
                    durable: l.cfg.worker && !l.ephemeral,
                    ephemeral: l.ephemeral,
                    host: l.host,
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
        // Keep even cache hits under the rules read lock. This makes the revision and the
        // cached result one coherent snapshot while still avoiding regex work for unchanged
        // text; a config publish replaces both together under the write lock.
        let rules = self.rules.read().await;
        let current_revision = self
            .rules_revision
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

        let now = now_ms();
        let has_hosts = self
            .live
            .read()
            .await
            .values()
            .any(|l| l.host && l.state != State::Down);
        let host_poll_due = has_hosts
            && now.saturating_sub(
                self.host_metadata_checked
                    .load(std::sync::atomic::Ordering::Acquire),
            ) >= HOST_METADATA_POLL_MS;
        if host_poll_due {
            self.host_metadata_checked
                .store(now, std::sync::atomic::Ordering::Release);
            self.start_host_metadata_poll().await;
        }

        // Classification awaits the rules lock, so snapshot before releasing the live lock.
        let snapshot: Vec<RetickSnapshot> = {
            let live = self.live.read().await;
            let rules_revision = self.rules_revision.load(Ordering::Acquire);
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
                        .rules_revision
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

#[cfg(test)]
mod tests {
    use super::*;

    async fn quiet_session() -> (Arc<Manager>, crate::emu::SessionEmu) {
        let manager = crate::session::test_manager(Config::default());
        let mut live = Live::new(
            SessionCfg {
                name: "agent".into(),
                ..Default::default()
            },
            TitleCapture::default(),
        );
        live.ephemeral = true;
        manager.live.write().await.insert("agent".into(), live);
        let mut emu = crate::emu::SessionEmu::new(80, 24);
        emu.feed(b"old output");
        manager.apply_frame("agent", emu.render()).await;
        {
            let mut live = manager.live.write().await;
            let live = live.get_mut("agent").unwrap();
            live.last_change = 0;
            live.state_since = 0;
        }
        // Prevent unrelated polling from suspending retick before it reaches classification.
        manager
            .config_state
            .config_checked
            .store(u64::MAX, Ordering::Relaxed);
        manager
            .config_state
            .presets_checked
            .store(u64::MAX, Ordering::Relaxed);
        manager
            .config_state
            .jukebox_checked
            .store(u64::MAX, Ordering::Relaxed);
        manager
            .host_metadata_checked
            .store(u64::MAX, Ordering::Relaxed);
        (manager, emu)
    }

    #[tokio::test]
    async fn retick_does_not_overwrite_fresh_terminal_activity() {
        let (manager, mut emu) = quiet_session().await;
        let rules = manager.rules.write().await;
        let mut tick = Box::pin(manager.retick());
        assert!(futures::poll!(tick.as_mut()).is_pending());
        drop(rules);

        // The tick has captured the old idle deadline. Commit real output before resuming it.
        emu.feed(b"\r\nnew output");
        manager.apply_frame("agent", emu.render()).await;
        tick.await;

        {
            let live = manager.live.read().await;
            let live = &live["agent"];
            assert_eq!(live.state, State::Working);
            assert_ne!(
                live.retick_seq, live.seq,
                "new output was never classified by retick"
            );
        }
        manager.retick().await;
        let live = manager.live.read().await;
        assert_eq!(live["agent"].state, State::Working);
        assert_eq!(live["agent"].retick_seq, live["agent"].seq);
    }

    #[tokio::test]
    async fn retick_does_not_revive_a_stopped_session() {
        let (manager, _) = quiet_session().await;
        let rules = manager.rules.write().await;
        let mut tick = Box::pin(manager.retick());
        assert!(futures::poll!(tick.as_mut()).is_pending());
        manager
            .live
            .write()
            .await
            .get_mut("agent")
            .unwrap()
            .set_state(State::Down);
        drop(rules);
        tick.await;

        let live = manager.live.read().await;
        assert_eq!(live["agent"].state, State::Down);
        assert_eq!(live["agent"].retick_seq, 0);
    }

    #[tokio::test]
    async fn retick_does_not_classify_a_replacement_run_with_the_same_sequence() {
        let (manager, _) = quiet_session().await;
        let rules = manager.rules.write().await;
        let mut tick = Box::pin(manager.retick());
        assert!(futures::poll!(tick.as_mut()).is_pending());
        {
            let mut live = manager.live.write().await;
            let live = live.get_mut("agent").unwrap();
            live.run_id += 1;
            live.last_change = now_ms();
        }
        drop(rules);
        tick.await;

        let live = manager.live.read().await;
        assert_eq!(live["agent"].state, State::Working);
        assert_eq!(live["agent"].retick_seq, 0);
    }

    #[tokio::test]
    async fn retick_rejects_a_classification_from_before_rules_reload() {
        let (manager, _) = quiet_session().await;
        let mut rules = manager.rules.write().await;
        let mut tick = Box::pin(manager.retick());
        assert!(futures::poll!(tick.as_mut()).is_pending());
        *rules = vec![(State::Waiting, regex::Regex::new("old output").unwrap())];
        manager.rules_revision.fetch_add(1, Ordering::AcqRel);
        drop(rules);
        tick.await;

        let live = manager.live.read().await;
        assert_eq!(live["agent"].retick_seq, 0);
        drop(live);
        manager.retick().await;
        let live = manager.live.read().await;
        assert_eq!(live["agent"].state, State::Waiting);
        assert_eq!(live["agent"].retick_seq, live["agent"].seq);
    }
}
