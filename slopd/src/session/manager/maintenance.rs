//! Poll configuration catalogs and choose the next maintenance wake.
//! Activity deadline policy belongs to session_state.rs and is shared with retick selection.

use super::super::*;
use super::session_state::{HOST_METADATA_POLL_MS, classification_deadline};
use crate::application::catalogs::{Catalog, next_periodic_deadline};
use crate::clock::unix_ms;

impl Manager {
    pub(super) async fn reload_if_due(self: &Arc<Self>) {
        let now = unix_ms();
        if self.application.catalogs.claim(Catalog::Config, now) {
            self.reload_if_changed().await;
        }
        if self.application.catalogs.claim(Catalog::Presets, now) {
            self.reload_presets_if_changed().await;
        }
        if self.application.catalogs.claim(Catalog::Jukebox, now)
            && let Err(error) = self.reload_jukebox_if_changed().await
        {
            tracing::warn!("jukebox definitions are not reloadable: {error:#}");
        }
    }

    /// Delay until the next poll or activity transition; zero when work is already due.
    /// Convert catalog epochs and monotonic runtime deadlines separately.
    pub(crate) async fn maintenance_delay(&self) -> Duration {
        let now = unix_ms();
        let mut deadline = self.application.catalogs.deadline(now);

        let live = self.live.read().await;
        if live
            .values()
            .any(|l| l.role.is_host() && l.state != State::Down)
        {
            deadline = deadline.min(next_periodic_deadline(
                self.host_metadata.checked.load(Ordering::Acquire),
                HOST_METADATA_POLL_MS,
                now,
            ));
        }
        let mut delay = Duration::from_millis(deadline.saturating_sub(now));
        let activity_now = tokio::time::Instant::now();
        for l in live.values() {
            for due in [classification_deadline(l, activity_now), l.capture.retry_at]
                .into_iter()
                .flatten()
            {
                delay = delay.min(due.saturating_duration_since(activity_now));
            }
        }
        delay
    }

    pub(crate) fn maintenance_wake(&self) -> Arc<tokio::sync::Notify> {
        self.signals.maintenance_wake.clone()
    }

    pub async fn reload_presets_if_changed(self: &Arc<Self>) -> bool {
        let reloaded = self.application.catalogs.reload_presets_if_changed();
        if !reloaded {
            return false;
        }
        tracing::info!("presets changed on disk, reloading");
        self.announce_sessions().await;
        self.signals.maintenance_wake.notify_waiters();
        true
    }

    pub async fn reload_jukebox_if_changed(self: &Arc<Self>) -> anyhow::Result<bool> {
        let reloaded = self.application.catalogs.reload_jukebox_if_changed()?;
        if !reloaded {
            return Ok(false);
        }
        tracing::info!("jukebox definitions changed on disk, reloading");
        self.emit(Event::Jukebox {
            jukebox: crate::jukebox::catalog(),
        });
        self.signals.maintenance_wake.notify_waiters();
        Ok(true)
    }
}
