//! Reload scheduling for independent configuration, preset, and jukebox catalogs.

use std::sync::Mutex;
use std::sync::atomic::{AtomicU64, Ordering};
use std::time::SystemTime;

/// Catalogs retain separate timestamps because their reload deadlines differ.
pub(crate) struct CatalogState {
    presets_mtime: Mutex<Option<SystemTime>>,
    jukebox_mtime: Mutex<Option<SystemTime>>,
    pub(crate) config_checked: AtomicU64,
    pub(crate) presets_checked: AtomicU64,
    pub(crate) jukebox_checked: AtomicU64,
}

impl CatalogState {
    pub(crate) fn new(
        presets_mtime: Option<SystemTime>,
        jukebox_mtime: Option<SystemTime>,
    ) -> Self {
        Self {
            presets_mtime: Mutex::new(presets_mtime),
            jukebox_mtime: Mutex::new(jukebox_mtime),
            config_checked: AtomicU64::new(0),
            presets_checked: AtomicU64::new(0),
            jukebox_checked: AtomicU64::new(0),
        }
    }
}

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
pub(crate) fn next_periodic_deadline(last: u64, period: u64, now: u64) -> u64 {
    if now.saturating_sub(last) >= period {
        0
    } else {
        last.saturating_add(period)
    }
}

#[derive(Clone, Copy)]
pub(crate) enum Catalog {
    Config,
    Presets,
    Jukebox,
}
impl CatalogState {
    pub(crate) fn load() -> Self {
        let presets = crate::presets::Table::stamp();
        let presets_loaded = crate::presets::reload();
        let jukebox = crate::paths::dir_stamp(&crate::jukebox::Catalog::dir());
        let jukebox_loaded = crate::jukebox::reload()
            .inspect_err(|error| {
                tracing::warn!("jukebox definitions are not reloadable: {error:#}")
            })
            .is_ok();
        Self::new(
            presets_loaded.then_some(presets).flatten(),
            jukebox_loaded.then_some(jukebox).flatten(),
        )
    }
    pub(crate) fn claim(&self, catalog: Catalog, now: u64) -> bool {
        let (clock, period) = match catalog {
            Catalog::Config => (&self.config_checked, CFG_CHECK_MS),
            Catalog::Presets => (&self.presets_checked, PRESETS_CHECK_MS),
            Catalog::Jukebox => (&self.jukebox_checked, JUKEBOX_CHECK_MS),
        };
        claim_due(clock, now, period)
    }
    pub(crate) fn deadline(&self, now: u64) -> u64 {
        [
            (&self.config_checked, CFG_CHECK_MS),
            (&self.presets_checked, PRESETS_CHECK_MS),
            (&self.jukebox_checked, JUKEBOX_CHECK_MS),
        ]
        .into_iter()
        .map(|(clock, period)| next_periodic_deadline(clock.load(Ordering::Acquire), period, now))
        .min()
        .unwrap_or(0)
    }
    pub(crate) fn reload_presets_if_changed(&self) -> bool {
        {
            let mut seen = self
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
        }
    }
    pub(crate) fn reload_jukebox_if_changed(&self) -> anyhow::Result<bool> {
        let reloaded = {
            let mut seen = self
                .jukebox_mtime
                .lock()
                .unwrap_or_else(|error| error.into_inner());
            let disk = crate::paths::dir_stamp(&crate::jukebox::Catalog::dir());
            if *seen == disk {
                false
            } else {
                crate::jukebox::reload()?;
                *seen = disk;
                true
            }
        };
        Ok(reloaded)
    }
}

#[cfg(test)]
#[path = "catalogs_tests.rs"]
mod tests;
