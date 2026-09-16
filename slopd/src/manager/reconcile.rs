//! Configuration-to-live reconciliation.

use super::super::*;
use super::session_lifecycle::{finish_reader, DetachCause, ReaderDisposition};

/// Keeps the ordering of durable configuration reconciliation in one named owner. The manager
/// remains the public façade because callers must not be able to skip pruning, manifest updates,
/// or autostart ordering.
pub(super) struct ConfigReconciler;

impl ConfigReconciler {
    pub(super) async fn sync(manager: &Arc<Manager>) {
        let cfg = manager.config().await;
        manager.prune_removed(&cfg).await;

        manager.upsert_sessions(&cfg).await;
        manager.upsert_host_terminals(&cfg).await;
        manager.sync_manifests(&cfg).await;
        let titles_changed = manager.reconcile_title_settings(&cfg).await;
        manager.autostart(&cfg).await;
        manager.autostart_host_terminals(&cfg).await;

        let adopted = manager.adopt_orphans(&cfg).await;
        if titles_changed || adopted {
            manager.announce_sessions().await;
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
            let enabled = cfg.daemon.experimental_instructions
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

    pub(super) async fn prune_removed(self: &Arc<Self>, cfg: &Config) {
        let mut plans = {
            let mut live = self.live.write().await;
            let names: Vec<String> = live
                .iter()
                .filter(|(name, l)| {
                    let saved_host = l.host
                        && cfg
                            .host_terminals
                            .iter()
                            .any(|tab| tab.name == (*name).as_str());
                    !((l.ephemeral && (!l.persistent_host || saved_host))
                        || cfg.session((*name).as_str()).is_some())
                })
                .map(|(name, _)| name.clone())
                .collect();
            names
                .iter()
                .filter_map(|name| {
                    tracing::info!("agent {name} is gone from the config, ending its reader");
                    self.detach_live_locked(&mut live, name, DetachCause::ConfigRemoval)
                })
                .collect::<Vec<_>>()
        };

        // Abort the entire batch before cleanup can yield. Cancellation during one cleanup
        // must not drop the remaining JoinHandles and detach readers with no live rows.
        for plan in &mut plans {
            finish_reader(std::mem::replace(&mut plan.reader, ReaderDisposition::None));
        }
        for plan in plans {
            self.execute_cleanup(plan).await;
        }
    }

    pub(super) async fn upsert_sessions(&self, cfg: &Config) {
        let mut live = self.live.write().await;
        for s in &cfg.sessions {
            let mut title = TitleCapture::default();
            title.override_title = self.title_cache.latest(&s.name);
            title.once_requested = title.override_title.is_some();
            live.entry(s.name.clone())
                .and_modify(|l| {
                    l.cfg = s.clone();
                    // Discovery is armed only when the next process starts. Named
                    // breadcrumbs are never copied into live state automatically.
                    l.breadcrumbs_pending = false;
                    l.breadcrumbs.clear();
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
                        l.persistent_host = true;
                        l.host_path = path.clone();
                    }
                })
                .or_insert_with(|| {
                    let mut l = Live::new(session, title);
                    l.ephemeral = true;
                    l.host = true;
                    l.persistent_host = true;
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

#[cfg(test)]
mod tests {
    use super::*;

    #[tokio::test]
    async fn cancelling_batch_cleanup_aborts_all_removed_readers() {
        let manager = crate::session::test_manager(Config::default());
        let mut completions = Vec::new();
        for name in ["first", "second"] {
            let (done, completion) = tokio::sync::oneshot::channel::<()>();
            let mut live = Live::new(
                SessionCfg {
                    name: name.into(),
                    ..Default::default()
                },
                TitleCapture::default(),
            );
            live.reader = Some(tokio::spawn(async move {
                let _done = done;
                std::future::pending::<()>().await;
            }));
            manager.live.write().await.insert(name.into(), live);
            completions.push(completion);
        }

        // Keep cleanup from completing even if tmux activity clearing returns immediately.
        let _temp = manager.temp.write().await;
        let cfg = Config::default();
        let mut pruning = Box::pin(manager.prune_removed(&cfg));
        assert!(futures::poll!(pruning.as_mut()).is_pending());
        assert!(manager.live.read().await.is_empty());
        drop(pruning);

        for completion in completions {
            assert!(tokio::time::timeout(Duration::from_secs(1), completion)
                .await
                .expect("removed reader survived cancellation of batch cleanup")
                .is_err());
        }
    }
}
