use super::*;
use std::collections::HashMap;
use std::path::PathBuf;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex};
use tokio::sync::{broadcast, RwLock};

pub struct Manager {
    pub tmux: Tmux,
    pub cfg_path: PathBuf,
    pub(super) cfg: RwLock<Config>,
    pub(super) live: RwLock<HashMap<String, Live>>,
    pub(super) temp: RwLock<HashMap<String, ProjectCfg>>,
    pub(super) rules: RwLock<Vec<(State, Regex)>>,
    pub(super) config_state: super::manager::ConfigState,
    /// Host panes need combined cwd/process refreshes, but not at the one-second
    /// state-classification cadence. The timestamp is also a cheap guard if another maintenance
    /// caller is added.
    pub(super) host_metadata_checked: AtomicU64,
    pub(super) signals: super::manager::Signals,
    pub(super) scroll_cache: Mutex<HashMap<String, CachedScroll>>,
    pub(super) activity_cache: crate::activity::ActivityCache,
    pub audio: crate::audio::Audio,
    pub events: broadcast::Sender<Arc<EventMessage>>,
    pub(super) auth_generation: AtomicU64,
    pub(super) auth_changes: broadcast::Sender<AuthChange>,
    pub(super) grants: RwLock<crate::grant::Grants>,
    pub(crate) tasks: super::manager::TaskStore,
    /// Serializes daemon-owned worker creation so two root requests cannot reserve one child name
    /// or split task/session persistence between each other.
    pub(super) worker_spawn: tokio::sync::Mutex<()>,
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
        self.0.signals.clients.fetch_sub(1, Ordering::Relaxed);
    }
}

pub struct WatchGuard(pub(super) Arc<Manager>, pub(super) String);

impl Drop for WatchGuard {
    fn drop(&mut self) {
        let Ok(mut w) = self.0.signals.watchers.lock() else {
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

impl Manager {
    pub(crate) fn emit(&self, event: Event) {
        let _ = self.events.send(EventMessage::new(event));
    }

    pub(crate) fn auth_generation(&self) -> u64 {
        self.auth_generation.load(Ordering::Acquire)
    }

    pub(crate) fn auth_changes(&self) -> broadcast::Receiver<AuthChange> {
        self.auth_changes.subscribe()
    }

    pub(crate) fn invalidate_auth(&self, change: AuthChange) {
        self.auth_generation.fetch_add(1, Ordering::AcqRel);
        let _ = self.auth_changes.send(change);
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
    let (auth_changes, _) = broadcast::channel(16);
    Arc::new(Manager {
        tmux: Tmux::new("slopworld-unit-test"),
        cfg_path: cfg_path.clone(),
        cfg: RwLock::new(config),
        live: RwLock::new(HashMap::new()),
        temp: RwLock::new(HashMap::new()),
        rules: RwLock::new(Vec::new()),
        config_state: super::manager::ConfigState::new(None, None, None),
        host_metadata_checked: AtomicU64::new(0),
        signals: super::manager::Signals::new(),
        scroll_cache: Mutex::new(HashMap::new()),
        activity_cache: crate::activity::ActivityCache::load(crate::activity::cache_path(
            &cfg_path,
        )),
        audio: crate::audio::Audio::new(),
        events,
        auth_generation: AtomicU64::new(0),
        auth_changes,
        grants: RwLock::new(crate::grant::Grants::default()),
        tasks: super::manager::TaskStore::new(
            crate::tasks::Tasks::load(&cfg_path).expect("test task store"),
        ),
        worker_spawn: tokio::sync::Mutex::new(()),
        title_cache: crate::title::SummaryCache::load(crate::title::cache_path(&cfg_path)),
    })
}
