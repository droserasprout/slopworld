//! Recover surviving tmux panes: probe metadata, resolve fallbacks, then attach readers.

use crate::clock::unix_ms;
use crate::config::HostTerminalCfg;
use crate::session::*;
use futures::StreamExt;

/// Raw tmux observations and the matching saved host entry.
struct AdoptionProbe {
    name: String,
    worker: Option<crate::tmux::WorkerMetadata>,
    temporary_session: Option<SessionCfg>,
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

impl SessionRuntime {
    /// Read pane metadata, querying host details only for non-worker host panes.
    async fn probe_orphan(&self, cfg: &Config, name: String) -> Result<AdoptionProbe> {
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
        let temporary_session = if worker.is_none() {
            let mut session = self.tmux.temporary_agent_config(&name).await?;
            if let Some(session) = &mut session {
                crate::config::validate_state_id(&session.state_id)?;
                anyhow::ensure!(
                    !session.worker,
                    "temporary agent metadata contains worker identity"
                );
                session.name.clone_from(&name);
                session.autostart = false;
                session.auto_resume = false;
                session.worker_token = None;
            }
            session
        } else {
            None
        };
        let host = worker.is_none()
            && temporary_session.is_none()
            && (self.tmux.is_host(&name).await || saved_host.is_some());
        let (tmux_host, current_path) = if host {
            tokio::join!(
                self.tmux.host_metadata(&name),
                self.tmux.current_path(&name)
            )
        } else {
            (None, None)
        };

        Ok(AdoptionProbe {
            name,
            worker,
            temporary_session,
            saved_host,
            host,
            tmux_host,
            activity: activity
                .map(|(state, state_since)| crate::activity::Activity { state, state_since }),
            current_path,
            reader,
        })
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
            .and_then(|metadata| recovered_worker_cfg(cfg, &probe.name, metadata));
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
    pub(in crate::session) async fn adopt_orphans(self: &Arc<Self>, cfg: &Config) -> bool {
        // Caller holds the session boundary across probing, live updates, and attachment.
        let names = self.tmux.list().await;
        // Attached readers already own authoritative live identity and metadata.
        // Probe only missing or readerless rows; retain recovery after disconnect.
        let names = {
            let live = self.live.read().await;
            names
                .into_iter()
                .filter(|name| live.get(name).is_none_or(|row| row.capture.emu.is_none()))
                .collect::<Vec<_>>()
        };
        // Each probe launches several tmux clients. Bound recovery bursts as well
        // as avoiding repeated probing during ordinary configuration edits.
        let probes = futures::stream::iter(names)
            .map(|name| async move {
                match self.probe_orphan(cfg, name.clone()).await {
                    Ok(probe) => Some(probe),
                    Err(error) => {
                        tracing::warn!(session = %name, "cannot recover pane metadata: {error:#}");
                        None
                    }
                }
            })
            .buffer_unordered(8)
            .collect::<Vec<_>>()
            .await;
        let decisions = probes
            .into_iter()
            .flatten()
            .map(|probe| Self::decide_orphan(cfg, probe, &self.activity_cache));

        let mut adopted = false;
        for decision in decisions {
            let invalid_worker =
                decision.probe.worker.is_some() && decision.worker_session.is_none();
            let conflict = cfg.session(&decision.probe.name).is_some_and(|saved| {
                decision.probe.host
                    || decision.probe.temporary_session.is_some()
                    || decision.probe.worker.as_ref().is_some_and(|metadata| {
                        !saved.worker
                            || metadata.state_id.as_deref() != Some(saved.state_id.as_str())
                    })
            });
            if conflict || invalid_worker {
                // Name-based authorization resolves through saved config. Never attach a
                // surviving process or retain its credentials under a different identity.
                let _terminal = self
                    .terminal_boundary(&decision.probe.name)
                    .write_owned()
                    .await;
                self.invalidate_session(&decision.probe.name).await;
                if let Err(error) = self.tmux.kill(&decision.probe.name).await {
                    if decision.probe.host || decision.probe.temporary_session.is_some() {
                        // Failed retirement must retain the actual process identity. Otherwise
                        // the configured placeholder would authorize the conflicting process.
                        self.live.write().await.insert(
                            decision.probe.name.clone(),
                            Self::new_adopted_live(cfg, &decision),
                        );
                    }
                    tracing::warn!(
                        "cannot stop conflicting pane {}: {error:#}",
                        decision.probe.name
                    );
                }
                continue;
            }
            adopted |= self.commit_adoption(cfg, decision).await;
        }
        adopted
    }

    /// Create or repair the live row before restoring dimensions and capture.
    async fn commit_adoption(self: &Arc<Self>, cfg: &Config, decision: AdoptionDecision) -> bool {
        let name = &decision.probe.name;
        // Recovery may replace stale worker/host identity. Exclude root input
        // and resize through row publication and attachment, as startup does.
        let terminal = self.terminal_boundary(name).write_owned().await;
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
        self.attach_adopted_reader(name, &terminal).await;
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
        if let Some(session) = &decision.probe.temporary_session {
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
            session.project = reader.project.clone();
            session.worktree = reader.worktree.clone();
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
        if decision.worker_session.is_none()
            && let Some(reader) = &decision.probe.reader
        {
            l.launch = LaunchContext::from(reader);
        }
        l.role = if let Some(metadata) = &decision.probe.worker {
            SessionRole::Agent(if metadata.durable {
                AgentLifetime::Durable
            } else {
                AgentLifetime::Temporary
            })
        } else if decision.probe.host {
            if decision.probe.saved_host.is_some() {
                SessionRole::HostShell
            } else {
                SessionRole::HostAction
            }
        } else {
            SessionRole::Agent(AgentLifetime::Temporary)
        };
        l.host_path = decision.host_path.clone();
        l.state = State::Working;
        l.last_change = now;
        l.activity_at = Some(tokio::time::Instant::now());
        l.state_since = now;
        if l.role.is_durable_agent()
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
            l.activity_at = (activity.state == State::Working).then(tokio::time::Instant::now);
        }
        l
    }

    /// Repair worker/host identity, then restore activity for a durable Down row.
    fn repair_adopted_live(&self, l: &mut Live, decision: &AdoptionDecision) {
        if let Some(session) = &decision.probe.temporary_session {
            l.cfg = session.clone();
            l.role = SessionRole::Agent(AgentLifetime::Temporary);
            l.host_path.clear();
        }
        if let (Some(metadata), Some(session)) = (
            decision.probe.worker.as_ref(),
            decision.worker_session.clone(),
        ) {
            // Worker metadata overrides a stale host-catalog identity.
            l.cfg = session;
            l.launch = LaunchContext::default();
            l.role = SessionRole::Agent(if metadata.durable {
                AgentLifetime::Durable
            } else {
                AgentLifetime::Temporary
            });
            l.host_path.clear();
        }
        if decision.probe.host && l.role.is_host() {
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
    async fn attach_adopted_reader(
        self: &Arc<Self>,
        name: &str,
        terminal: &tokio::sync::OwnedRwLockWriteGuard<()>,
    ) {
        match self.spawn_reader_with_terminal(name, terminal).await {
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
        if !l.role.is_durable_agent() || l.state != State::Down {
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
        l.activity_at = (activity.state == State::Working).then(tokio::time::Instant::now);
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

/// Restore the worker settings, then recover identity without reusing credentials.
pub(in crate::session) fn recovered_worker_cfg(
    cfg: &Config,
    name: &str,
    metadata: &crate::tmux::WorkerMetadata,
) -> Option<SessionCfg> {
    let state_id = metadata
        .state_id
        .as_ref()
        .filter(|id| crate::config::validate_state_id(id).is_ok())?;
    let mut session = cfg
        .session(name)
        .filter(|saved| metadata.state_id.as_deref() == Some(saved.state_id.as_str()))
        .cloned()
        .or_else(|| metadata.session.as_deref().cloned())?;
    session.name = name.to_string();
    if let Some(project) = &metadata.project {
        session.project = project.clone();
    }
    session.worktree = metadata.worktree.clone().unwrap_or_default();
    session.state_id = state_id.clone();
    session.worker = true;
    session.parent = metadata.parent.clone();
    session.task_id = metadata.task_id.clone();
    session.autostart = false;
    session.auto_resume = false;
    session.worker_token = None;
    Some(session)
}

#[cfg(test)]
#[path = "adoption_tests.rs"]
mod tests;
