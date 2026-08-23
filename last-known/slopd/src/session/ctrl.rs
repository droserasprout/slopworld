use super::*;
use std::collections::HashMap;
use std::path::PathBuf;
use std::sync::atomic::{AtomicUsize, Ordering};
use std::sync::{Arc, Mutex};
use std::time::SystemTime;
use tokio::sync::{broadcast, RwLock};

pub struct Manager {
    pub tmux: Tmux,
    pub cfg_path: PathBuf,
    pub(super) cfg: RwLock<Config>,
    pub(super) live: RwLock<HashMap<String, Live>>,
    pub(super) temp: RwLock<HashMap<String, ProjectCfg>>,
    pub(super) rules: RwLock<Vec<(State, Regex)>>,
    pub(super) cfg_mtime: Mutex<Option<SystemTime>>,
    pub(super) presets_mtime: Mutex<Option<SystemTime>>,
    pub(super) jukebox_mtime: Mutex<Option<SystemTime>>,
    pub(super) cfg_checked: AtomicU64,
    pub(super) usage: RwLock<crate::usage::Snapshot>,
    pub(super) clients: AtomicUsize,
    pub(super) clients_since: AtomicU64,
    pub(super) watchers: Mutex<HashMap<String, usize>>,
    pub(super) scroll_cache: Mutex<HashMap<String, CachedScroll>>,
    pub(super) activity_cache: crate::activity::ActivityCache,
    pub audio: crate::audio::Audio,
    pub events: broadcast::Sender<Event>,
    pub(super) grants: RwLock<crate::grant::Grants>,
    pub(super) tasks: Mutex<crate::tasks::Tasks>,
    pub(super) title_cache: crate::title::SummaryCache,
}

pub(crate) struct CachedScroll {
    pub(super) live_seq: u64,
    pub(super) off: u32,
    pub(super) cols: u16,
    pub(super) rows: u16,
    pub(super) view: ScreenView,
}

pub struct ClientGuard(pub(super) Arc<Manager>);

impl Drop for ClientGuard {
    fn drop(&mut self) {
        self.0.clients.fetch_sub(1, Ordering::Relaxed);
    }
}

pub struct WatchGuard(pub(super) Arc<Manager>, pub(super) String);

impl Drop for WatchGuard {
    fn drop(&mut self) {
        let Ok(mut w) = self.0.watchers.lock() else {
            return;
        };
        if let Some(n) = w.get_mut(&self.1) {
            *n -= 1;
            if *n == 0 {
                w.remove(&self.1);
            }
        }
    }
}
