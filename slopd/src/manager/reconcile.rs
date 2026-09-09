//! Configuration-to-live reconciliation.

use super::super::*;

/// Keeps the ordering of durable configuration reconciliation in one named owner. The manager
/// remains the public façade because callers must not be able to skip pruning, manifest updates,
/// or autostart ordering.
pub(super) struct ConfigReconciler;

impl ConfigReconciler {
    pub(super) async fn sync(manager: &Arc<Manager>) {
        let cfg = manager.config().await;
        let removed = manager.prune_removed(&cfg).await;

        for name in &removed {
            manager.revoke_grants(name).await;
        }

        manager.upsert_sessions(&cfg).await;
        manager.upsert_host_terminals(&cfg).await;
        manager.sync_manifests(&cfg).await;
        let titles_changed = manager.reconcile_title_settings(&cfg).await;
        manager.autostart(&cfg).await;
        manager.autostart_host_terminals(&cfg).await;

        let adopted = manager.adopt_orphans(&cfg).await;
        if titles_changed || adopted {
            manager.emit(Event::Sessions {
                sessions: manager.views().await,
            });
        }
    }
}

impl Manager {
    /// Keep generated project manifests in step with durable configuration before starting new
    /// panes. This is deliberately part of reconciliation, not generic manager state.
    pub(super) async fn sync_manifests(&self, cfg: &Config) {
        let views = self.views().await;
        for project in &cfg.projects {
            let dir = crate::config::expand(&project.dir);
            let path = std::path::Path::new(&dir);
            if !path.is_dir() {
                continue;
            }
            let enabled = cfg.daemon.experimental
                && cfg
                    .sessions
                    .iter()
                    .any(|session| session.project == project.name && session.slopworld_md);
            let result = if enabled {
                crate::manifest::prepare(path, cfg, project, &views).map(|_| ())
            } else {
                crate::manifest::remove(path)
            };
            if let Err(error) = result {
                tracing::warn!(
                    project = %project.name,
                    error = %error,
                    "could not synchronize generated SLOPWORLD.md"
                );
            }
        }
    }

    pub(super) async fn prune_removed(&self, cfg: &Config) -> Vec<String> {
        // Dropping a `Live` only detaches its reader; abort it so a removed session cannot keep
        // a control-mode attach open against a name nothing points at any more.
        let mut live = self.live.write().await;
        let mut removed = Vec::new();
        let mut worker_tasks = Vec::new();
        live.retain(|name, l| {
            let saved_host = l.host && cfg.host_terminals.iter().any(|tab| tab.name == *name);
            if (l.ephemeral && (!l.host || saved_host)) || cfg.session(name).is_some() {
                return true;
            }
            tracing::info!("agent {name} is gone from the config, ending its reader");
            if l.cfg.worker && !l.cfg.task_id.trim().is_empty() {
                worker_tasks.push((l.cfg.task_id.clone(), name.clone()));
            }
            if let Some(h) = l.reader.take() {
                h.abort();
            }
            self.forget_scroll(name);
            if let Err(error) = self.title_cache.clear_latest(name) {
                tracing::warn!(
                    target: "slopd::titles",
                    session = %name,
                    error = %error,
                    outcome = "cache_write_failed",
                    "could not clear session title"
                );
            }
            removed.push(name.clone());
            false
        });
        drop(live);
        for (task_id, name) in worker_tasks {
            self.fail_worker_task(&task_id, format!("worker session {name} was removed"));
        }
        for name in &removed {
            self.clear_activity(name).await;
        }
        removed
    }

    pub(super) async fn upsert_sessions(&self, cfg: &Config) {
        let mut live = self.live.write().await;
        if !cfg.daemon.experimental {
            for session in live.values_mut() {
                session.breadcrumbs_pending = false;
                session.breadcrumbs.clear();
            }
        }
        for s in &cfg.sessions {
            let mut title = TitleCapture::default();
            title.override_title = self.title_cache.latest(&s.name);
            title.once_requested = title.override_title.is_some();
            live.entry(s.name.clone())
                .and_modify(|l| {
                    l.cfg = s.clone();
                    if !s.breadcrumb_yolo {
                        l.breadcrumbs_pending = false;
                    }
                })
                .or_insert(Live::new(s.clone(), title));
        }
    }

    pub(super) async fn upsert_host_terminals(&self, cfg: &Config) {
        let mut live = self.live.write().await;
        for tab in &cfg.host_terminals {
            if let Err(error) = crate::session::check_name(&tab.name) {
                tracing::warn!("ignoring invalid host terminal {}: {error:#}", tab.name);
                continue;
            }
            if cfg.session(&tab.name).is_some() {
                tracing::warn!(
                    "ignoring host terminal {} because an agent has the same name",
                    tab.name
                );
                continue;
            }
            let mut session = SessionCfg {
                name: tab.name.clone(),
                label: tab.label.clone(),
                project: tab.project.clone(),
                command: cfg.defaults.shell.clone(),
                ..Default::default()
            };
            let path = if tab.path.trim().is_empty() {
                cfg.project(&tab.project)
                    .map(|p| crate::config::expand(&p.dir))
                    .unwrap_or_default()
            } else {
                crate::config::expand(&tab.path)
            };
            session.autostart = tab.autostart;
            let title = TitleCapture::default();
            live.entry(tab.name.clone())
                .and_modify(|l| {
                    if l.host {
                        l.cfg = session.clone();
                        l.host_path = path.clone();
                    }
                })
                .or_insert_with(|| {
                    let mut l = Live::new(session, title);
                    l.ephemeral = true;
                    l.host = true;
                    l.host_path = path;
                    l
                });
        }
    }

    pub(super) async fn autostart(self: &Arc<Self>, cfg: &Config) {
        // Task workers are explicit, one-shot work; old autostart flags must not relaunch them.
        for s in cfg.sessions.iter().filter(|s| s.autostart && !s.worker) {
            if !self.tmux.exists(&s.name).await {
                if let Err(e) = self.start(&s.name).await {
                    tracing::error!("autostart {}: {e:#}", s.name);
                }
            }
        }
    }

    pub(super) async fn autostart_host_terminals(self: &Arc<Self>, cfg: &Config) {
        for tab in cfg.host_terminals.iter().filter(|tab| tab.autostart) {
            if !self.tmux.exists(&tab.name).await {
                if let Err(error) = self.start(&tab.name).await {
                    tracing::error!("autostart host terminal {}: {error:#}", tab.name);
                }
            }
        }
    }
}
