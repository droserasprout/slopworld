//! Recovery of tmux sessions that outlived the daemon configuration.

use crate::clock::unix_ms;
use crate::config::HostTerminalCfg;
use crate::session::*;

struct AdoptionProbe {
    name: String,
    worker: Option<crate::tmux::WorkerMetadata>,
    saved_host: Option<HostTerminalCfg>,
    host: bool,
    tmux_host: Option<(String, String)>,
    activity: Option<crate::activity::Activity>,
    current_path: Option<String>,
}

struct AdoptionDecision {
    name: String,
    worker: Option<crate::tmux::WorkerMetadata>,
    saved_host: Option<HostTerminalCfg>,
    worker_session: Option<SessionCfg>,
    host: bool,
    host_project: String,
    host_path: String,
    activity: Option<crate::activity::Activity>,
    current_path: Option<String>,
}

impl Manager {
    async fn probe_orphan(&self, cfg: &Config, name: String) -> AdoptionProbe {
        let saved_host = cfg
            .host_terminals
            .iter()
            .find(|tab| tab.name == name)
            .cloned();

        // Worker identity and cached activity are independent observations. Keep the tmux
        // calls concurrent, but make host metadata conditional on the worker/host decision.
        let (worker, activity) =
            tokio::join!(self.tmux.worker_metadata(&name), self.tmux.activity(&name),);
        let host = worker.is_none() && (self.tmux.is_host(&name).await || saved_host.is_some());
        let (tmux_host, current_path) = if host {
            tokio::join!(
                self.tmux.host_metadata(&name),
                self.tmux.current_path(&name)
            )
        } else {
            (None, None)
        };

        AdoptionProbe {
            name,
            worker,
            saved_host,
            host,
            tmux_host,
            activity: activity
                .map(|(state, state_since)| crate::activity::Activity { state, state_since }),
            current_path,
        }
    }

    fn decide_orphan(
        cfg: &Config,
        probe: AdoptionProbe,
        activity_cache: &crate::activity::ActivityCache,
    ) -> AdoptionDecision {
        let worker_session = probe
            .worker
            .as_ref()
            .map(|metadata| recovered_worker_cfg(cfg, &probe.name, metadata));
        let host_project = probe
            .tmux_host
            .as_ref()
            .and_then(|(project, _)| (!project.is_empty()).then_some(project.clone()))
            .or_else(|| probe.saved_host.as_ref().map(|tab| tab.project.clone()))
            .unwrap_or_default();
        let host_path = probe
            .tmux_host
            .as_ref()
            .and_then(|(_, path)| (!path.is_empty()).then_some(path.clone()))
            .or_else(|| probe.saved_host.as_ref().map(|tab| tab.path.clone()))
            .unwrap_or_default();
        let activity = probe.activity.or_else(|| activity_cache.get(&probe.name));

        AdoptionDecision {
            name: probe.name,
            worker: probe.worker,
            saved_host: probe.saved_host,
            worker_session,
            host: probe.host,
            host_project,
            host_path,
            activity,
            current_path: probe.current_path,
        }
    }

    /// Reconstruct each tmux session missing from the current configuration.
    /// Workers use tmux metadata owned by the daemon. Host shells use their host marker and catalog entry.
    /// Attach the reader after reconstructing the live row.
    /// Recovered panes then use the normal session state and resize operations.
    pub(in crate::session::manager) async fn adopt_orphans(self: &Arc<Self>, cfg: &Config) -> bool {
        // Reconciliation calls this within session_operation. Probes can yield.
        // Keep decisions, live-row changes, and reader attachment ordered within the same session boundary.
        let names = self.tmux.list().await;
        let probes =
            futures::future::join_all(names.into_iter().map(|name| self.probe_orphan(cfg, name)))
                .await;
        let decisions = probes
            .into_iter()
            .map(|probe| Self::decide_orphan(cfg, probe, &self.activity_cache));

        let mut adopted = false;
        for decision in decisions {
            adopted |= self.commit_adoption(cfg, decision).await;
        }
        adopted
    }

    async fn commit_adoption(self: &Arc<Self>, cfg: &Config, decision: AdoptionDecision) -> bool {
        let AdoptionDecision {
            name,
            worker,
            saved_host,
            worker_session,
            host,
            host_project,
            host_path,
            activity,
            current_path,
        } = decision;
        let mut created = false;
        let needs_size = {
            let mut live = self.live.write().await;
            if !live.contains_key(&name) {
                tracing::info!(
                    "adopting tmux session {name} as a temporary {}",
                    if host { "host terminal" } else { "agent" }
                );
                let now = unix_ms();
                let mut l = Live::new(
                    worker_session.clone().unwrap_or_else(|| SessionCfg {
                        name: name.clone(),
                        label: saved_host.as_ref().and_then(|tab| tab.label.clone()),
                        project: host_project.clone(),
                        command: if host {
                            cfg.defaults.shell.clone()
                        } else {
                            String::new()
                        },
                        ..Default::default()
                    }),
                    TitleCapture::default(),
                );
                l.ephemeral = worker
                    .as_ref()
                    .map(|metadata| !metadata.durable)
                    .unwrap_or(true);
                l.host = host;
                l.persistent_host = host && saved_host.is_some();
                l.host_path = host_path.clone();
                l.state = State::Working;
                l.last_change = now;
                l.state_since = now;
                if !l.ephemeral {
                    if let Some(activity) = activity {
                        l.state = activity.state;
                        l.state_since = activity.state_since;
                        // Persisted timestamps remain epoch milliseconds. Runtime decay
                        // starts from the adoption sample because no pre-restart frame can
                        // prove that the restored pane changed after the timestamp.
                        l.last_change = if activity.state == State::Working {
                            now
                        } else {
                            0
                        };
                    }
                }
                live.insert(name.clone(), l);
                created = true;
            } else if let Some(l) = live.get_mut(&name) {
                if let (Some(metadata), Some(session)) = (worker.as_ref(), worker_session.clone()) {
                    // A stale in-memory host row can exist when a daemon reload adopts a
                    // worker name that was also present in an old host-terminal catalog.
                    l.cfg = session;
                    l.ephemeral = !metadata.durable;
                    l.host = false;
                    l.persistent_host = false;
                    l.host_path.clear();
                }
                if host && l.host {
                    if !host_project.is_empty() {
                        l.cfg.project = host_project.clone();
                    }
                    if !host_path.is_empty() {
                        l.host_path = host_path.clone();
                    }
                }
                self.restore_activity(l, activity);
            }
            live.get(&name).is_some_and(|l| l.capture.emu.is_none())
        };

        // An established reader already has its dimensions.
        // Query tmux only for a newly adopted session or one without a reader.
        // Release the live-table lock before awaiting the result.
        if needs_size {
            self.refresh_readerless_size(&name).await;
        }
        match self.spawn_reader(&name).await {
            Ok(true) => {
                let m = self.clone();
                let name = name.clone();
                tokio::spawn(async move { m.nudge_redraw(&name).await });
            }
            Ok(false) => {}
            Err(error) => {
                tracing::warn!("could not attach reader for adopted session {name}: {error:#}")
            }
        }
        if host {
            // Only the saved catalog owns durable host tabs. Adopting a viewer must not turn
            // its project association into persistence.
            if let Some(path) = current_path {
                self.remember_host_path(&name, &path).await;
            }
        }
        created
    }

    fn restore_activity(&self, l: &mut Live, activity: Option<crate::activity::Activity>) {
        if l.ephemeral || l.state != State::Down {
            return;
        }
        let Some(activity) = activity else { return };
        l.state = activity.state;
        l.state_since = activity.state_since;
        // Working classification uses elapsed time since pane activity.
        // After a restart, the daemon has no previous frame for comparison.
        // Establish a new last-change sample through the first live frame or the ten-second activity timeout.
        // Preserve the displayed state age set above.
        l.last_change = if activity.state == State::Working {
            unix_ms()
        } else {
            0
        };
    }

    async fn refresh_readerless_size(&self, name: &str) {
        let Some((cols, rows)) = self.tmux.size(name).await else {
            return;
        };
        let mut live = self.live.write().await;
        if let Some(l) = live.get_mut(name).filter(|l| l.capture.emu.is_none()) {
            l.cols = cols;
            l.rows = rows;
        }
    }
}

pub(in crate::session::manager) fn recovered_worker_cfg(
    cfg: &Config,
    name: &str,
    metadata: &crate::tmux::WorkerMetadata,
) -> SessionCfg {
    let mut session = cfg.session(&metadata.parent).cloned().unwrap_or_default();
    session.name = name.to_string();
    if let Some(project) = &metadata.project {
        session.project = project.clone();
    }
    session.worktree = metadata.worktree.clone().unwrap_or_default();
    session.state_id = metadata
        .state_id
        .clone()
        .filter(|state_id| crate::config::validate_state_id(state_id).is_ok())
        .unwrap_or_else(|| uuid::Uuid::new_v4().to_string());
    session.worker = true;
    session.parent = metadata.parent.clone();
    session.task_id = metadata.task_id.clone();
    session.autostart = false;
    session.auto_resume = false;
    session.worker_token = None;
    session
}

#[cfg(test)]
#[path = "adoption_tests.rs"]
mod tests;
