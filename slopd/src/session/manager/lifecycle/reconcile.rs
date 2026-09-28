//! Reconcile one config snapshot: prune, refresh rows, autostart, then adopt surviving panes.

use super::stop::{finish_reader, DetachCause, ReaderDisposition};
use crate::session::*;

/// Own reconciliation order; callers enter through `Manager::sync_from_config`.
pub(in crate::session::manager) struct ConfigReconciler;

impl ConfigReconciler {
    /// Apply the snapshot before recovering panes that are absent from configuration.
    pub(in crate::session::manager) async fn sync(manager: &Arc<Manager>) {
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
    /// Detach removed configured rows, preserving temporary sessions and saved host tabs.
    pub(in crate::session::manager) async fn prune_removed(self: &Arc<Self>, cfg: &Config) {
        let mut plans = {
            let mut live = self.live.write().await;
            let names: Vec<String> = live
                .iter()
                .filter(|(name, l)| !keep_live_session(cfg, name, l))
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

        // Abort the whole batch before cleanup yields, so cancellation cannot strand readers.
        for plan in &mut plans {
            finish_reader(std::mem::replace(&mut plan.reader, ReaderDisposition::None));
        }
        for plan in plans {
            self.execute_cleanup(plan).await;
        }
    }

    /// Refresh configured agent rows and seed new rows with cached titles.
    pub(in crate::session::manager) async fn upsert_sessions(&self, cfg: &Config) {
        let mut live = self.live.write().await;
        for s in &cfg.sessions {
            let title = TitleCapture::restored(self.title_cache.latest(&s.name));
            live.entry(s.name.clone())
                .and_modify(|l| {
                    l.cfg = s.clone();
                })
                .or_insert(Live::new(s.clone(), title));
        }
    }

    /// Restore saved host tabs without overwriting an existing non-host identity.
    pub(in crate::session::manager) async fn upsert_host_terminals(&self, cfg: &Config) {
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
            let session = SessionCfg {
                name: tab.name.clone(),
                label: tab.label.clone(),
                project: tab.project.clone(),
                command: cfg.defaults.shell.clone(),
                autostart: tab.autostart,
                ..Default::default()
            };
            let path = if tab.path.trim().is_empty() {
                cfg.project(&tab.project)
                    .map(|p| crate::config::expand(&p.dir))
                    .unwrap_or_default()
            } else {
                crate::config::expand(&tab.path)
            };
            if let Some(l) = live.get_mut(&tab.name) {
                if !l.host {
                    continue;
                }
                l.cfg = session;
                l.persistent_host = true;
                l.host_path = path;
                continue;
            }

            let mut l = Live::new(session, TitleCapture::default());
            l.ephemeral = true;
            l.host = true;
            l.persistent_host = true;
            l.host_path = path;
            live.insert(tab.name.clone(), l);
        }
    }

    /// Start missing agent panes; workers require an explicit task request.
    pub(in crate::session::manager) async fn autostart(self: &Arc<Self>, cfg: &Config) {
        for s in cfg.sessions.iter().filter(|s| s.autostart && !s.worker) {
            if self.tmux.exists(&s.name).await {
                continue;
            }
            if let Err(e) = self.start(&s.name).await {
                tracing::error!("autostart {}: {e:#}", s.name);
            }
        }
    }

    /// Start missing saved host panes whose catalog entries enable autostart.
    pub(in crate::session::manager) async fn autostart_host_terminals(
        self: &Arc<Self>,
        cfg: &Config,
    ) {
        for tab in cfg.host_terminals.iter().filter(|tab| tab.autostart) {
            if self.tmux.exists(&tab.name).await {
                continue;
            }
            if let Err(error) = self.start(&tab.name).await {
                tracing::error!("autostart host terminal {}: {error:#}", tab.name);
            }
        }
    }
}

/// Configured agents and temporary rows survive; saved host tabs must remain in the catalog.
fn keep_live_session(cfg: &Config, name: &str, live: &Live) -> bool {
    if cfg.session(name).is_some() {
        return true;
    }
    if !live.ephemeral {
        return false;
    }
    if !live.persistent_host {
        return true;
    }
    live.host && cfg.host_terminals.iter().any(|tab| tab.name == name)
}

#[cfg(test)]
#[path = "reconcile_tests.rs"]
mod tests;

impl Manager {
    /// Hold the session boundary across the complete reconciliation pass.
    pub async fn sync_from_config(self: &Arc<Self>) {
        self.session_operation(self.sync_from_config_inner()).await
    }

    async fn sync_from_config_inner(self: &Arc<Self>) {
        ConfigReconciler::sync(self).await;
    }
}
