//! Session views, state classification, and the manager retick loop.

use super::super::*;

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
                    autostart: l.cfg.autostart,
                    auto_resume: l.cfg.auto_resume,
                    worker: l.cfg.worker,
                    parent: l.cfg.parent.clone(),
                    task_id: l.cfg.task_id.clone(),
                    durable: l.cfg.worker && !l.ephemeral,
                    ephemeral: l.ephemeral,
                    host: l.host,
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
                    seq: l.seq,
                }
            })
            .collect();
        out.sort_by(|a, b| a.name.cmp(&b.name));
        out
    }

    pub(super) async fn classify(&self, changed: bool, last_change: u64, text: &str) -> State {
        if let Some(state) = match_rules(&self.rules.read().await, text) {
            return state;
        }
        if changed || now_ms().saturating_sub(last_change) < IDLE_MS {
            State::Working
        } else {
            State::Idle
        }
    }

    pub(super) async fn classify_initial(&self, previous: State, text: &str) -> State {
        if let Some(state) = match_rules(&self.rules.read().await, text) {
            return state;
        }
        if previous != State::Down {
            previous
        } else {
            State::Working
        }
    }

    pub async fn retick(self: &Arc<Self>) {
        self.reload_if_due().await;

        let host_paths_changed = self.refresh_host_paths().await;

        let now = now_ms();

        // Classification awaits the rules lock, so snapshot before releasing the live lock.
        let snapshot: Vec<(String, u64, String)> = {
            let live = self.live.read().await;
            live.iter()
                .filter(|(_, l)| l.state != State::Down)
                .filter_map(|(n, l)| {
                    let _ = l.screen.as_ref()?;
                    let idle_due = match l.state {
                        State::Working | State::Waiting => {
                            now.saturating_sub(l.state_since) >= IDLE_MS
                        }
                        State::Idle | State::Down => false,
                    };
                    if l.seq == l.retick_seq && !idle_due {
                        return None;
                    }
                    Some((n.clone(), l.last_change, l.plain.clone()))
                })
                .collect()
        };

        let mut dirty_list = false;
        for (name, last_change, plain) in snapshot {
            let state = self.classify(false, last_change, &plain).await;
            let mut activity = None;
            let mut live = self.live.write().await;
            if let Some(l) = live.get_mut(&name) {
                l.retick_seq = l.seq;
                if l.set_state(state) {
                    dirty_list = true;
                    if !l.ephemeral {
                        activity = Some((l.state, l.state_since));
                    }
                }
            }
            drop(live);
            if let Some((state, state_since)) = activity {
                self.persist_activity(&name, state, state_since).await;
            }
        }

        if dirty_list || host_paths_changed {
            let _ = self.events.send(Event::Sessions {
                sessions: self.views().await,
            });
        }
    }
}
