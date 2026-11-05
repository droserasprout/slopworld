//! Recover surviving tmux panes: probe metadata, resolve fallbacks, then attach readers.

use crate::clock::unix_ms;
use crate::config::HostTerminalCfg;
use crate::session::*;
use futures::StreamExt;

/// Startup must not infer worker loss after an unavailable identity query.
/// Other malformed pane metadata keeps the existing skip-and-inspect behavior.
#[derive(Debug)]
struct WorkerMetadataUnavailable;

impl std::fmt::Display for WorkerMetadataUnavailable {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str("worker recovery metadata unavailable")
    }
}

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
    identity: AdoptionIdentity,
    host_project: String,
    host_path: String,
    activity: Option<crate::activity::Activity>,
}

/// Resolve metadata precedence once, before live-row creation or repair.
/// An identified worker without recoverable settings must be retired, never
/// reinterpreted as a host or an ordinary temporary agent.
enum AdoptionIdentity {
    Worker {
        session: Box<SessionCfg>,
        lifetime: AgentLifetime,
    },
    InvalidWorker,
    TemporaryAgent(Box<SessionCfg>),
    Host(SessionRole),
    Agent,
}

impl AdoptionIdentity {
    fn resolve(cfg: &Config, probe: &AdoptionProbe) -> Self {
        if let Some(metadata) = &probe.worker {
            return match recovered_worker_cfg(cfg, &probe.name, metadata) {
                Some(session) => Self::Worker {
                    session: Box::new(session),
                    lifetime: if metadata.durable {
                        AgentLifetime::Durable
                    } else {
                        AgentLifetime::Temporary
                    },
                },
                None => Self::InvalidWorker,
            };
        }
        if let Some(session) = &probe.temporary_session {
            return Self::TemporaryAgent(Box::new(session.clone()));
        }
        if probe.host {
            return Self::Host(if probe.saved_host.is_some() {
                SessionRole::HostShell
            } else {
                SessionRole::HostAction
            });
        }
        Self::Agent
    }

    fn role(&self) -> SessionRole {
        match self {
            Self::Worker { lifetime, .. } => SessionRole::Agent(*lifetime),
            Self::Host(role) => *role,
            _ => SessionRole::Agent(AgentLifetime::Temporary),
        }
    }

    fn is_host(&self) -> bool {
        matches!(self, Self::Host(_))
    }

    fn conflicts_with(&self, saved: &SessionCfg) -> bool {
        match self {
            Self::Worker { session, .. } => !saved.worker || session.state_id != saved.state_id,
            Self::InvalidWorker | Self::TemporaryAgent(_) | Self::Host(_) => true,
            Self::Agent => saved.worker,
        }
    }

    fn retain_after_failed_retirement(&self) -> bool {
        matches!(self, Self::Host(_) | Self::TemporaryAgent(_))
    }
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
        let worker = worker.context(WorkerMetadataUnavailable)?;
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
                session.daemon_token = None;
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
        let identity = AdoptionIdentity::resolve(cfg, &probe);
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
            identity,
            host_project,
            host_path,
            activity,
        }
    }

    /// Recover missing live rows and attach readers to surviving tmux panes.
    pub(in crate::session) async fn adopt_orphans(self: &Arc<Self>, cfg: &Config) -> Result<bool> {
        // Caller holds the session boundary across probing, live updates, and attachment.
        let names = self.tmux.list_checked().await?;
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
                    Ok(probe) => Ok(Some(probe)),
                    Err(error) if error.is::<WorkerMetadataUnavailable>() => {
                        Err(error.context(format!("cannot recover pane {name}")))
                    }
                    Err(error) => {
                        tracing::warn!(session = %name, "cannot recover pane metadata: {error:#}");
                        Ok(None)
                    }
                }
            })
            .buffer_unordered(8)
            .collect::<Vec<_>>()
            .await;
        // Resolve the whole probe batch before any adoption or retirement. A
        // failed worker query must abort checked startup before task-loss recovery.
        let probes = probes.into_iter().collect::<Result<Vec<_>>>()?;
        let decisions = probes
            .into_iter()
            .flatten()
            .map(|probe| Self::decide_orphan(cfg, probe, &self.activity_cache));

        let mut adopted = false;
        for decision in decisions {
            let conflict = cfg
                .session(&decision.probe.name)
                .is_some_and(|saved| decision.identity.conflicts_with(saved));
            if conflict || matches!(decision.identity, AdoptionIdentity::InvalidWorker) {
                // Name-based authorization resolves through saved config. Never attach a
                // surviving process or retain its credentials under a different identity.
                let _terminal = self
                    .terminal_boundary(&decision.probe.name)
                    .write_owned()
                    .await;
                self.invalidate_session(&decision.probe.name).await;
                if let Err(error) = self.tmux.kill(&decision.probe.name).await {
                    if decision.identity.retain_after_failed_retirement() {
                        // Failed retirement must retain the actual process identity. Otherwise
                        // the configured placeholder would authorize the conflicting process.
                        self.live.write().await.insert(
                            decision.probe.name.clone(),
                            Self::new_adopted_live(cfg, &decision),
                        );
                    }
                    return Err(error.context(format!(
                        "cannot stop conflicting pane {}",
                        decision.probe.name
                    )));
                }
                continue;
            }
            adopted |= self.commit_adoption(cfg, decision).await;
        }
        Ok(adopted)
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
                        if decision.identity.is_host() {
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
        if decision.identity.is_host() {
            // The saved host catalog alone owns persistence; a viewer stays temporary.
            if let Some(path) = &decision.probe.current_path {
                self.remember_host_path(name, path).await;
            }
        }
        created
    }

    /// Build worker settings or reconstruct a host/viewer session from saved metadata.
    fn adopted_session_cfg(cfg: &Config, decision: &AdoptionDecision) -> SessionCfg {
        match &decision.identity {
            AdoptionIdentity::Worker { session, .. }
            | AdoptionIdentity::TemporaryAgent(session) => return session.as_ref().clone(),
            _ => {}
        }

        let mut session = SessionCfg {
            name: decision.probe.name.clone(),
            label: decision
                .probe
                .saved_host
                .as_ref()
                .and_then(|tab| tab.label.clone()),
            project: decision.host_project.clone(),
            command: if decision.identity.is_host() {
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
        if !matches!(decision.identity, AdoptionIdentity::Worker { .. })
            && let Some(reader) = &decision.probe.reader
        {
            l.launch = LaunchContext::from(reader);
        }
        l.role = decision.identity.role();
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
        match &decision.identity {
            AdoptionIdentity::TemporaryAgent(session) => {
                l.cfg = session.as_ref().clone();
                l.role = decision.identity.role();
                l.host_path.clear();
            }
            AdoptionIdentity::Worker { session, .. } => {
                // Worker metadata overrides a stale host-catalog identity.
                l.cfg = session.as_ref().clone();
                l.launch = LaunchContext::default();
                l.role = decision.identity.role();
                l.host_path.clear();
            }
            AdoptionIdentity::Host(_) if l.role.is_host() => {
                if !decision.host_project.is_empty() {
                    l.cfg.project = decision.host_project.clone();
                }
                if !decision.host_path.is_empty() {
                    l.host_path = decision.host_path.clone();
                }
            }
            _ => {}
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
    // A successful probe can still describe an incomplete worker. Preserve its
    // worker marker so adoption retires it rather than falling back to another role.
    if metadata.parent.is_empty() || metadata.task_id.is_empty() {
        return None;
    }
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
    session.daemon_token = None;
    Some(session)
}

#[cfg(test)]
#[path = "adoption_tests.rs"]
mod tests;
