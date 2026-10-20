//! Reload scheduling for independent configuration, preset, and jukebox catalogs.

use std::sync::Mutex;
use std::sync::atomic::AtomicU64;
use std::time::SystemTime;

/// Catalogs retain separate timestamps because their reload deadlines differ.
pub(crate) struct CatalogState {
    pub(crate) presets_mtime: Mutex<Option<SystemTime>>,
    pub(crate) jukebox_mtime: Mutex<Option<SystemTime>>,
    pub(crate) config_checked: AtomicU64,
    pub(crate) presets_checked: AtomicU64,
    pub(crate) jukebox_checked: AtomicU64,
}

impl CatalogState {
    pub(in crate::session::manager) fn new(
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
