//! Recovery of tmux sessions that outlived the daemon configuration.

use super::super::*;

impl Manager {
    /// Rebuild the smallest durable view of every tmux session the config does not currently
    /// describe. Workers use daemon-owned tmux metadata; host shells use their host marker and
    /// catalog entry. The reader is attached only after the live row has been reconstructed so
    /// a recovered pane follows the same state and resize path as a normal session.
    pub(super) async fn adopt_orphans(self: &Arc<Self>, cfg: &Config) -> bool {
        let mut adopted = false;
        for name in self.tmux.list().await {
            // One-shot workers are deliberately absent from config.toml. Recover their
            // daemon-owned identity from the metadata carried by the surviving tmux session.
            let worker = self.tmux.worker_metadata(&name).await;
            let worker_session = worker
                .as_ref()
                .map(|metadata| recovered_worker_cfg(cfg, &name, metadata));
            let saved_host = cfg.host_terminals.iter().find(|tab| tab.name == name);
            // Worker identity takes precedence over a stale host marker or host-terminal record.
            let host = worker.is_none() && (self.tmux.is_host(&name).await || saved_host.is_some());
            let tmux_host = host.then(|| self.tmux.host_metadata(&name));
            let tmux_host = match tmux_host {
                Some(future) => future.await,
                None => None,
            };
            let host_project = tmux_host
                .as_ref()
                .and_then(|(project, _)| (!project.is_empty()).then_some(project.clone()))
                .or_else(|| saved_host.map(|tab| tab.project.clone()))
                .unwrap_or_default();
            let host_path = tmux_host
                .as_ref()
                .and_then(|(_, path)| (!path.is_empty()).then_some(path.clone()))
                .or_else(|| saved_host.map(|tab| tab.path.clone()))
                .unwrap_or_default();
            let activity = self
                .tmux
                .activity(&name)
                .await
                .map(|(state, state_since)| crate::activity::Activity { state, state_since })
                .or_else(|| self.activity_cache.get(&name));
            let needs_size = {
                let mut live = self.live.write().await;
                if !live.contains_key(&name) {
                    tracing::info!(
                        "adopting tmux session {name} as a temporary {}",
                        if host { "host terminal" } else { "agent" }
                    );
                    let now = now_ms();
                    let mut l = Live::new(
                        worker_session.clone().unwrap_or_else(|| SessionCfg {
                            name: name.clone(),
                            label: saved_host.and_then(|tab| tab.label.clone()),
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
                    adopted = true;
                } else if let Some(l) = live.get_mut(&name) {
                    if let (Some(metadata), Some(session)) = (worker.as_ref(), worker_session) {
                        // A stale in-memory host row can exist when a daemon reload adopts a
                        // worker name that was also present in an old host-terminal catalog.
                        l.cfg = session;
                        l.ephemeral = !metadata.durable;
                        l.host = false;
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
                live.get(&name).is_some_and(|l| l.emu.is_none())
            };

            // An established reader already knows its dimensions. Query tmux only for a
            // newly adopted or reader-less session, and do not hold the live-table lock
            // across the subprocess await.
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
                let current_path = self.tmux.current_path(&name).await;
                if saved_host.is_none() {
                    let project = self
                        .live
                        .read()
                        .await
                        .get(&name)
                        .filter(|l| l.host)
                        .map(|l| l.cfg.project.clone())
                        .unwrap_or_default();
                    let path = current_path
                        .clone()
                        .filter(|path| !path.trim().is_empty())
                        .or_else(|| (!host_path.trim().is_empty()).then_some(host_path.clone()))
                        .unwrap_or_default();
                    // A nameless host shell is still a runtime-only errand. Project shells
                    // are the durable sidebar tabs; only those can restore grouping on reboot.
                    if !project.trim().is_empty() {
                        if let Err(error) =
                            self.remember_host_terminal(&name, &project, &path).await
                        {
                            tracing::warn!(
                                "could not save adopted host terminal {name}: {error:#}"
                            );
                        }
                    }
                }
                if let Some(path) = current_path {
                    self.remember_host_path(&name, &path).await;
                }
            }
        }
        adopted
    }

    fn restore_activity(&self, l: &mut Live, activity: Option<crate::activity::Activity>) {
        if l.ephemeral || l.state != State::Down {
            return;
        }
        let Some(activity) = activity else { return };
        l.state = activity.state;
        l.state_since = activity.state_since;
        // Working classification uses pane activity as a decay clock. A daemon restart has
        // no frame to compare against, so let the first live frame/ten-second decay establish
        // a fresh last-change sample while preserving the user-visible state age above.
        l.last_change = if activity.state == State::Working {
            now_ms()
        } else {
            0
        };
    }

    async fn refresh_readerless_size(&self, name: &str) {
        let Some((cols, rows)) = self.tmux.size(name).await else {
            return;
        };
        let mut live = self.live.write().await;
        if let Some(l) = live.get_mut(name).filter(|l| l.emu.is_none()) {
            l.cols = cols;
            l.rows = rows;
        }
    }
}

pub(super) fn recovered_worker_cfg(
    cfg: &Config,
    name: &str,
    metadata: &crate::tmux::WorkerMetadata,
) -> SessionCfg {
    let mut session = cfg.session(&metadata.parent).cloned().unwrap_or_default();
    session.name = name.to_string();
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
    if !session
        .sandbox
        .iter()
        .any(|preset| preset == super::workers::WORKER_SANDBOX)
    {
        session.sandbox.push(super::workers::WORKER_SANDBOX.into());
    }
    session
}

#[cfg(test)]
mod tests {
    use super::recovered_worker_cfg;
    use crate::config::{Config, SessionCfg};

    #[test]
    fn recovered_worker_uses_parent_settings_and_task_identity() {
        let cfg = Config {
            sessions: vec![SessionCfg {
                name: "parent".into(),
                project: "repo".into(),
                command: "codex".into(),
                sandbox: vec!["gpu".into()],
                ..Default::default()
            }],
            ..Default::default()
        };
        let worker = recovered_worker_cfg(
            &cfg,
            "parent-worker",
            &crate::tmux::WorkerMetadata {
                parent: "parent".into(),
                task_id: "task-7".into(),
                durable: false,
                state_id: Some("11111111-1111-4111-8111-111111111111".into()),
            },
        );

        assert!(worker.worker);
        assert_eq!(worker.parent, "parent");
        assert_eq!(worker.task_id, "task-7");
        assert_eq!(worker.state_id, "11111111-1111-4111-8111-111111111111");
        assert_eq!(worker.project, "repo");
        assert_eq!(worker.command, "codex");
        assert_eq!(worker.sandbox, ["gpu", "slopworld-worker"]);
        assert!(!worker.autostart);
        assert!(!worker.auto_resume);
    }
}
