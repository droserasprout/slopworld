//! Configuration-to-live reconciliation.

use super::super::*;
use super::session_lifecycle::{finish_reader, DetachCause, ReaderDisposition};

/// Control the order of persistent configuration reconciliation in one component.
/// Callers use the manager interface so they cannot skip pruning or change autostart order.
pub(super) struct ConfigReconciler;

impl ConfigReconciler {
    pub(super) async fn sync(manager: &Arc<Manager>) {
        let cfg = manager.config().await;
        manager.prune_removed(&cfg).await;

        manager.upsert_sessions(&cfg).await;
        manager.upsert_host_terminals(&cfg).await;
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

        // Abort all readers in the batch before cleanup can yield.
        // Ensure cancellation does not leave readers running after cleanup removes their live rows.
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
                    // Enable discovery only when the next process starts.
                    // Do not automatically copy named breadcrumbs into live state.
                    l.input.breadcrumbs_pending = false;
                    l.input.breadcrumbs.clear();
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
        // Task workers run only when explicitly requested.
        // Old autostart flags must not restart them.
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
#[path = "reconcile_tests.rs"]
mod tests;

impl Manager {
    pub async fn sync_from_config(self: &Arc<Self>) {
        self.session_operation(self.sync_from_config_within_boundary())
            .await
    }

    async fn sync_from_config_within_boundary(self: &Arc<Self>) {
        ConfigReconciler::sync(self).await;
    }
}
