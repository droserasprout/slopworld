//! Recover surviving tmux panes: probe metadata, resolve fallbacks, then attach readers.

use crate::clock::unix_ms;
use crate::config::HostTerminalCfg;
use crate::session::*;

/// Raw tmux observations and the matching saved host entry.
struct AdoptionProbe {
    name: String,
    worker: Option<crate::tmux::WorkerMetadata>,
    saved_host: Option<HostTerminalCfg>,
    host: bool,
    tmux_host: Option<(String, String)>,
    activity: Option<crate::activity::Activity>,
    current_path: Option<String>,
    reader: Option<crate::tmux::ReaderMetadata>,
}

/// Original observations plus resolved recovery inputs for the live table.
struct AdoptionDecision {
    probe: AdoptionProbe,
    worker_session: Option<SessionCfg>,
    host_project: String,
    host_path: String,
    activity: Option<crate::activity::Activity>,
}

impl Manager {
    /// Read pane metadata, querying host details only for non-worker host panes.
    async fn probe_orphan(&self, cfg: &Config, name: String) -> AdoptionProbe {
        let saved_host = cfg
            .host_terminals
            .iter()
            .find(|tab| tab.name == name)
            .cloned();

        // Fetch independent metadata together; worker identity takes precedence over host markers.
        let (worker, activity, reader) = tokio::join!(
            self.tmux.worker_metadata(&name),
            self.tmux.activity(&name),
            self.tmux.reader_metadata(&name),
        );
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
            reader,
        }
    }

    /// Prefer tmux metadata, falling back to saved host entries and disk activity.
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
            probe,
            worker_session,
            host_project,
            host_path,
            activity,
        }
    }

    /// Recover missing live rows and attach readers to surviving tmux panes.
    pub(in crate::session::manager) async fn adopt_orphans(self: &Arc<Self>, cfg: &Config) -> bool {
        // Caller holds the session boundary across probing, live updates, and attachment.
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

    /// Create or repair the live row before restoring dimensions and capture.
    async fn commit_adoption(self: &Arc<Self>, cfg: &Config, decision: AdoptionDecision) -> bool {
        let name = &decision.probe.name;
        let (created, needs_size) = {
            let mut live = self.live.write().await;
            let (row, created) = match live.entry(name.clone()) {
                std::collections::hash_map::Entry::Vacant(entry) => {
                    tracing::info!(
                        "adopting tmux session {name} as a temporary {}",
                        if decision.probe.host {
                            "host terminal"
                        } else {
                            "agent"
                        }
                    );
                    (entry.insert(Self::new_adopted_live(cfg, &decision)), true)
                }
                std::collections::hash_map::Entry::Occupied(entry) => {
                    let row = entry.into_mut();
                    self.repair_adopted_live(row, &decision);
                    (row, false)
                }
            };
            (created, row.capture.emu.is_none())
        };

        // Recover missing dimensions outside the live lock, then attach and request a redraw.
        if needs_size {
            self.refresh_readerless_size(name).await;
        }
        self.attach_adopted_reader(name).await;
        if decision.probe.host {
            // The saved host catalog alone owns persistence; a viewer stays temporary.
            if let Some(path) = &decision.probe.current_path {
                self.remember_host_path(name, path).await;
            }
        }
        created
    }

    /// Build worker settings or reconstruct a host/viewer session from saved metadata.
    fn adopted_session_cfg(cfg: &Config, decision: &AdoptionDecision) -> SessionCfg {
        if let Some(session) = &decision.worker_session {
            return session.clone();
        }

        let mut session = SessionCfg {
            name: decision.probe.name.clone(),
            label: decision
                .probe
                .saved_host
                .as_ref()
                .and_then(|tab| tab.label.clone()),
            project: decision.host_project.clone(),
            command: if decision.probe.host {
                cfg.defaults.shell.clone()
            } else {
                String::new()
            },
            ..Default::default()
        };
        // Viewer metadata restores its label, source, and pin state.
        if let Some(reader) = &decision.probe.reader {
            session.label = Some(reader.label.clone());
            session.reader_label = if reader.original_label.is_empty() {
                reader.label.clone()
            } else {
                reader.original_label.clone()
            };
            session.project = reader.project.clone();
            session.worktree = reader.worktree.clone();
            session.intent = reader.intent.clone();
            session.reader_path = reader.path.clone();
            session.reader_key = reader.key.clone();
            session.reader_scope = reader.scope.clone();
            session.reader_pinned = reader.pinned;
            session.reader_line = reader.line;
        }
        session
    }

    /// Initialize a recovered row, preserving durable activity age.
    fn new_adopted_live(cfg: &Config, decision: &AdoptionDecision) -> Live {
        let now = unix_ms();
        let mut l = Live::new(
            Self::adopted_session_cfg(cfg, decision),
            TitleCapture::default(),
        );
        l.ephemeral = decision
            .probe
            .worker
            .as_ref()
            .map(|metadata| !metadata.durable)
            .unwrap_or(true);
        l.host = decision.probe.host;
        l.persistent_host = decision.probe.host && decision.probe.saved_host.is_some();
        l.host_path = decision.host_path.clone();
        l.state = State::Working;
        l.last_change = now;
        l.state_since = now;
        if !l.ephemeral
            && let Some(activity) = decision.activity
        {
            l.state = activity.state;
            l.state_since = activity.state_since;
            // Preserve displayed age; restart Working decay without a prior frame.
            l.last_change = if activity.state == State::Working {
                now
            } else {
                0
            };
        }
        l
    }

    /// Repair worker/host identity, then restore activity for a durable Down row.
    fn repair_adopted_live(&self, l: &mut Live, decision: &AdoptionDecision) {
        if let (Some(metadata), Some(session)) = (
            decision.probe.worker.as_ref(),
            decision.worker_session.clone(),
        ) {
            // Worker metadata overrides a stale host-catalog identity.
            l.cfg = session;
            l.ephemeral = !metadata.durable;
            l.host = false;
            l.persistent_host = false;
            l.host_path.clear();
        }
        if decision.probe.host && l.host {
            if !decision.host_project.is_empty() {
                l.cfg.project = decision.host_project.clone();
            }
            if !decision.host_path.is_empty() {
                l.host_path = decision.host_path.clone();
            }
        }
        self.restore_activity(l, decision.activity);
    }

    /// Attach capture and request a redraw only when a new reader starts.
    async fn attach_adopted_reader(self: &Arc<Self>, name: &str) {
        match self.spawn_reader(name).await {
            Ok(true) => {
                let m = self.clone();
                let name = name.to_owned();
                tokio::spawn(async move { m.nudge_redraw(&name).await });
            }
            Ok(false) => {}
            Err(error) => {
                tracing::warn!("could not attach reader for adopted session {name}: {error:#}")
            }
        }
    }

    /// Restore saved activity only for durable rows still marked Down.
    fn restore_activity(&self, l: &mut Live, activity: Option<crate::activity::Activity>) {
        if l.ephemeral || l.state != State::Down {
            return;
        }
        let Some(activity) = activity else { return };
        l.state = activity.state;
        l.state_since = activity.state_since;
        // Restart Working decay from now while preserving the displayed state age.
        l.last_change = if activity.state == State::Working {
            unix_ms()
        } else {
            0
        };
    }

    /// Sample tmux dimensions; apply only if a reader has not appeared while awaiting.
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

/// Inherit parent settings, then restore worker identity without reusing credentials.
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
