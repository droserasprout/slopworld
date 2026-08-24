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

#[cfg(test)]
pub(crate) fn test_manager(config: Config) -> Arc<Manager> {
    let cfg_path = std::env::temp_dir().join(format!(
        "slopd-manager-test-{}-{}.toml",
        std::process::id(),
        uuid::Uuid::new_v4()
    ));
    let (events, _) = broadcast::channel(16);
    Arc::new(Manager {
        tmux: Tmux::new("slopworld-unit-test"),
        cfg_path: cfg_path.clone(),
        cfg: RwLock::new(config),
        live: RwLock::new(HashMap::new()),
        temp: RwLock::new(HashMap::new()),
        rules: RwLock::new(Vec::new()),
        cfg_mtime: Mutex::new(None),
        presets_mtime: Mutex::new(None),
        jukebox_mtime: Mutex::new(None),
        cfg_checked: AtomicU64::new(0),
        usage: RwLock::new(crate::usage::Snapshot::default()),
        clients: AtomicUsize::new(0),
        clients_since: AtomicU64::new(0),
        watchers: Mutex::new(HashMap::new()),
        scroll_cache: Mutex::new(HashMap::new()),
        activity_cache: crate::activity::ActivityCache::load(crate::activity::cache_path(
            &cfg_path,
        )),
        audio: crate::audio::Audio::new(),
        events,
        grants: RwLock::new(crate::grant::Grants::default()),
        tasks: Mutex::new(crate::tasks::Tasks::load(&cfg_path).expect("test task store")),
        title_cache: crate::title::SummaryCache::load(crate::title::cache_path(&cfg_path)),
    })
}
