//! Poll configuration catalogs and choose the next maintenance wake.
//! Activity deadline policy belongs to session_state.rs and is shared with retick selection.

use super::super::*;
use super::session_state::{HOST_METADATA_POLL_MS, classification_deadline};
use crate::clock::unix_ms;

// Check external configuration edits on the maintenance clock.
const CFG_CHECK_MS: u64 = 2_000;
// Check preset files independently of configuration changes.
const PRESETS_CHECK_MS: u64 = 2_000;
// Check station catalog edits without restarting playback.
const JUKEBOX_CHECK_MS: u64 = 2_000;

/// Claim one polling interval, even when maintenance callers overlap.
fn claim_due(clock: &std::sync::atomic::AtomicU64, now: u64, period: u64) -> bool {
    let last = clock.load(Ordering::Acquire);
    if now.saturating_sub(last) < period {
        return false;
    }
    clock
        .compare_exchange(last, now, Ordering::AcqRel, Ordering::Acquire)
        .is_ok()
}

/// Return an epoch-millisecond deadline, or zero when the polling interval has elapsed.
fn next_periodic_deadline(last: u64, period: u64, now: u64) -> u64 {
    if now.saturating_sub(last) >= period {
        0
    } else {
        last.saturating_add(period)
    }
}

impl Manager {
    pub(super) async fn reload_if_due(self: &Arc<Self>) {
        let now = unix_ms();
        if claim_due(&self.config_state.config_checked, now, CFG_CHECK_MS) {
            self.reload_if_changed().await;
        }
        if claim_due(&self.config_state.presets_checked, now, PRESETS_CHECK_MS) {
            self.reload_presets_if_changed().await;
        }
        if claim_due(&self.config_state.jukebox_checked, now, JUKEBOX_CHECK_MS) {
            self.reload_jukebox_if_changed().await;
        }
    }

    /// Delay until the next poll or activity transition; zero when work is already due.
    /// Convert catalog epochs and monotonic runtime deadlines separately.
    pub(crate) async fn maintenance_delay(&self) -> Duration {
        let now = unix_ms();
        let mut deadline = next_periodic_deadline(
            self.config_state.config_checked.load(Ordering::Acquire),
            CFG_CHECK_MS,
            now,
        );
        deadline = deadline.min(next_periodic_deadline(
            self.config_state.presets_checked.load(Ordering::Acquire),
            PRESETS_CHECK_MS,
            now,
        ));
        deadline = deadline.min(next_periodic_deadline(
            self.config_state.jukebox_checked.load(Ordering::Acquire),
            JUKEBOX_CHECK_MS,
            now,
        ));

        let live = self.live.read().await;
        if live.values().any(|l| l.host && l.state != State::Down) {
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
        let reloaded = {
            let mut seen = self
                .config_state
                .presets_mtime
                .lock()
                .unwrap_or_else(|error| error.into_inner());
            let disk = crate::presets::Table::stamp();
            if *seen == disk || !crate::presets::reload() {
                false
            } else {
                *seen = disk;
                true
            }
        };
        if !reloaded {
            return false;
        }
        tracing::info!("presets changed on disk, reloading");
        self.announce_sessions().await;
        self.signals.maintenance_wake.notify_waiters();
        true
    }

    pub async fn reload_jukebox_if_changed(self: &Arc<Self>) -> bool {
        let reloaded = {
            let mut seen = self
                .config_state
                .jukebox_mtime
                .lock()
                .unwrap_or_else(|error| error.into_inner());
            let disk = crate::paths::dir_stamp(&crate::jukebox::Catalog::dir());
            if *seen == disk || !crate::jukebox::reload() {
                false
            } else {
                *seen = disk;
                true
            }
        };
        if !reloaded {
            return false;
        }
        tracing::info!("jukebox definitions changed on disk, reloading");
        self.emit(Event::Jukebox {
            jukebox: crate::jukebox::catalog(),
        });
        self.signals.maintenance_wake.notify_waiters();
        true
    }
}
