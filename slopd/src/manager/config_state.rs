//! State used to serialize configuration persistence and observe external changes.

use std::sync::atomic::AtomicU64;
use std::sync::{Arc, Mutex};
use std::time::SystemTime;

/// Configuration I/O state has a different lifetime from the live session table.
/// Keep timestamps and the serialization lock together to prevent timestamp publication before acceptance of the corresponding contents.
/// Configuration catalogs, presets, and jukebox definitions have separate maintenance timestamps because their reload deadlines differ.
pub(crate) struct ConfigState {
    pub(crate) cfg_mtime: Mutex<Option<SystemTime>>,
    pub(crate) library_mtime: Mutex<Option<SystemTime>>,
    pub(crate) persist: Arc<tokio::sync::Mutex<()>>,
    pub(crate) presets_mtime: Mutex<Option<SystemTime>>,
    pub(crate) jukebox_mtime: Mutex<Option<SystemTime>>,
    pub(crate) config_checked: AtomicU64,
    pub(crate) presets_checked: AtomicU64,
    pub(crate) jukebox_checked: AtomicU64,
}

impl ConfigState {
    pub(crate) fn new(
        cfg_mtime: Option<SystemTime>,
        library_mtime: Option<SystemTime>,
        presets_mtime: Option<SystemTime>,
        jukebox_mtime: Option<SystemTime>,
    ) -> Self {
        Self {
            cfg_mtime: Mutex::new(cfg_mtime),
            library_mtime: Mutex::new(library_mtime),
            persist: Arc::new(tokio::sync::Mutex::new(())),
            presets_mtime: Mutex::new(presets_mtime),
            jukebox_mtime: Mutex::new(jukebox_mtime),
            config_checked: AtomicU64::new(0),
            presets_checked: AtomicU64::new(0),
            jukebox_checked: AtomicU64::new(0),
        }
    }
}
