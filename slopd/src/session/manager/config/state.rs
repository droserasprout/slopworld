//! State used to serialize configuration persistence and observe external changes.

use std::sync::atomic::AtomicU64;
use std::sync::{Arc, Mutex};
use std::time::SystemTime;

/// Configuration I/O state has a different lifetime from the live session table.
/// Publish catalog revisions only after accepting the corresponding contents.
/// Configuration catalogs, presets, and jukebox definitions have separate maintenance timestamps because their reload deadlines differ.
pub(crate) struct ConfigState {
    pub(in crate::session::manager) library_revision:
        Mutex<Option<crate::config::catalog::Revision>>,
    pub(in crate::session::manager) records: Mutex<Option<Arc<super::backend::records::Records>>>,
    #[cfg(test)]
    pub(in crate::session::manager) commit_pause:
        Mutex<Option<(Arc<tokio::sync::Barrier>, Arc<tokio::sync::Notify>)>>,
    pub(crate) persist: Arc<tokio::sync::Mutex<()>>,
    pub(crate) presets_mtime: Mutex<Option<SystemTime>>,
    pub(crate) jukebox_mtime: Mutex<Option<SystemTime>>,
    pub(crate) config_checked: AtomicU64,
    pub(crate) presets_checked: AtomicU64,
    pub(crate) jukebox_checked: AtomicU64,
}

impl ConfigState {
    pub(crate) fn new(
        presets_mtime: Option<SystemTime>,
        jukebox_mtime: Option<SystemTime>,
    ) -> Self {
        Self {
            library_revision: Mutex::new(None),
            records: Mutex::new(None),
            #[cfg(test)]
            commit_pause: Mutex::new(None),
            persist: Arc::new(tokio::sync::Mutex::new(())),
            presets_mtime: Mutex::new(presets_mtime),
            jukebox_mtime: Mutex::new(jukebox_mtime),
            config_checked: AtomicU64::new(0),
            presets_checked: AtomicU64::new(0),
            jukebox_checked: AtomicU64::new(0),
        }
    }
}
