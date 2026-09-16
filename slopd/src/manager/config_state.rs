//! State used to serialize configuration persistence and observe external changes.

use std::sync::atomic::AtomicU64;
use std::sync::{Arc, Mutex};
use std::time::SystemTime;

/// Configuration I/O bookkeeping has a different lifetime from the live session table.
/// Keeping its stamps and serialization gate together makes it harder for a new config path to
/// publish a disk stamp before the corresponding contents have been accepted. The maintenance
/// stamps are separate because config, presets, and jukebox have independent reload deadlines.
pub(crate) struct ConfigState {
    pub(crate) cfg_mtime: Mutex<Option<SystemTime>>,
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
        presets_mtime: Option<SystemTime>,
        jukebox_mtime: Option<SystemTime>,
    ) -> Self {
        Self {
            cfg_mtime: Mutex::new(cfg_mtime),
            persist: Arc::new(tokio::sync::Mutex::new(())),
            presets_mtime: Mutex::new(presets_mtime),
            jukebox_mtime: Mutex::new(jukebox_mtime),
            config_checked: AtomicU64::new(0),
            presets_checked: AtomicU64::new(0),
            jukebox_checked: AtomicU64::new(0),
        }
    }
}
