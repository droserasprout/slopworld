//! Daemon catalog polling and combined application/session maintenance scheduling.
use super::catalogs::Catalog;
use super::{Event, Services};
use crate::{clock::unix_ms, session::Manager};
use std::{sync::Arc, time::Duration};

impl Services {
    pub(crate) fn spawn_maintenance(
        self: &Arc<Self>,
        sessions: Arc<Manager>,
    ) -> tokio::task::JoinHandle<()> {
        let services = self.clone();
        tokio::spawn(async move {
            loop {
                let delay = services.maintenance_delay(&sessions).await;
                let wake = sessions.maintenance_wake();
                tokio::select! {
                    () = tokio::time::sleep(delay) => services.maintain(&sessions).await,
                    () = wake.notified() => {}
                }
            }
        })
    }

    pub(crate) async fn reload_if_due(&self, sessions: &Arc<Manager>) {
        let now = unix_ms();
        if self.catalogs.claim(Catalog::Config, now) {
            sessions.reload_if_changed().await;
        }
        if self.catalogs.claim(Catalog::Presets, now) {
            self.reload_presets_if_changed(sessions).await;
        }
        if self.catalogs.claim(Catalog::Jukebox, now)
            && let Err(error) = self.reload_jukebox_if_changed(sessions).await
        {
            tracing::warn!("jukebox definitions are not reloadable: {error:#}");
        }
    }

    pub(crate) async fn reload_presets_if_changed(&self, sessions: &Arc<Manager>) -> bool {
        let reloaded = self.catalogs.reload_presets_if_changed();
        if !reloaded {
            return false;
        }
        tracing::info!("presets changed on disk, reloading");
        sessions.announce_sessions().await;
        sessions.maintenance_wake().notify_waiters();
        true
    }

    pub(crate) async fn reload_jukebox_if_changed(
        &self,
        sessions: &Arc<Manager>,
    ) -> anyhow::Result<bool> {
        let reloaded = self.catalogs.reload_jukebox_if_changed()?;
        if !reloaded {
            return Ok(false);
        }
        tracing::info!("jukebox definitions changed on disk, reloading");
        self.emit(Event::Jukebox {
            jukebox: crate::jukebox::catalog(),
        });
        sessions.maintenance_wake().notify_waiters();
        Ok(true)
    }

    pub(crate) async fn maintenance_delay(&self, sessions: &Manager) -> Duration {
        let now = unix_ms();
        Duration::from_millis(self.catalogs.deadline(now).saturating_sub(now))
            .min(sessions.maintenance_delay().await)
    }
    pub(crate) async fn maintain(&self, sessions: &Arc<Manager>) {
        self.reload_if_due(sessions).await;
        sessions.retick().await;
    }
}

#[cfg(test)]
#[path = "maintenance_tests.rs"]
mod tests;
