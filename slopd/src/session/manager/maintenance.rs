//! Poll configuration catalogs and schedule session maintenance.

use super::super::*;
use super::session_state::{HOST_METADATA_POLL_MS, IDLE_MS};

// Check external configuration edits on the maintenance clock.
const CFG_CHECK_MS: u64 = 2_000;
// Check preset files independently of configuration changes.
const PRESETS_CHECK_MS: u64 = 2_000;
// Check station catalog edits without restarting playback.
const JUKEBOX_CHECK_MS: u64 = 2_000;

// Maintenance clock helpers.

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

/// Return an epoch deadline, or zero when already due.
fn next_periodic_deadline(last: u64, period: u64, now: u64) -> u64 {
    if now.saturating_sub(last) >= period {
        0
    } else {
        last.saturating_add(period)
    }
}

impl Manager {
    pub(super) async fn reload_if_due(self: &Arc<Self>) {
        let now = now_ms();
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
    /// Convert epoch timestamps to a duration for the maintenance timer.
    pub(crate) async fn maintenance_delay(&self) -> Duration {
        let now = now_ms();
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

        let rules_revision = self.rules_revision.load(Ordering::Acquire);
        let live = self.live.read().await;
        if live.values().any(|l| l.host && l.state != State::Down) {
            deadline = deadline.min(next_periodic_deadline(
                self.host_metadata.checked.load(Ordering::Acquire),
                HOST_METADATA_POLL_MS,
                now,
            ));
        }
        for l in live.values().filter(|l| l.state != State::Down) {
            if l.screen.is_none() {
                continue;
            }
            let cache_current = l.rule_cache.as_ref().is_some_and(|cache| {
                cache.revision == rules_revision && cache.text.as_ref() == l.plain.as_ref()
            });
            if l.seq != l.retick_seq || !cache_current {
                deadline = 0;
                break;
            }
            if matches!(l.state, State::Working | State::Waiting)
                && !l.rule_cache.as_ref().is_some_and(|cache| {
                    matches!(cache.matched, Some(State::Waiting | State::Idle))
                })
            {
                deadline = deadline.min(l.last_change.saturating_add(IDLE_MS));
            }
        }
        Duration::from_millis(deadline.saturating_sub(now))
    }

    pub(crate) fn maintenance_wake(&self) -> Arc<tokio::sync::Notify> {
        self.signals.maintenance_wake.clone()
    }

    pub async fn reload_presets_if_changed(self: &Arc<Self>) -> bool {
        let disk = crate::presets::Table::stamp();
        let reloaded = {
            let mut seen = self.config_state.presets_mtime.lock().unwrap();
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
        let disk = crate::paths::dir_stamp(&crate::jukebox::Catalog::dir());
        let reloaded = {
            let mut seen = self.config_state.jukebox_mtime.lock().unwrap();
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
