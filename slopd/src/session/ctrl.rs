use super::*;
use std::collections::HashMap;
use std::path::PathBuf;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex};
use tokio::sync::{broadcast, RwLock};

pub struct Manager {
    pub tmux: Tmux,
    pub cfg_path: PathBuf,
    pub(super) endpoint_path: PathBuf,
    pub(super) cfg: RwLock<Config>,
    pub(super) templates: RwLock<AgentTemplateStore>,
    pub(super) live: RwLock<HashMap<String, Live>>,
    pub(super) temp: RwLock<HashMap<String, ProjectCfg>>,
    pub(super) rules: RwLock<Vec<(State, Regex)>>,
    pub(super) rules_revision: AtomicU64,
    pub(super) config_state: super::manager::ConfigState,
    /// Host panes need combined cwd/process refreshes, but not at the one-second
    /// state-classification cadence. The timestamp is also a cheap guard if another maintenance
    /// caller is added.
    pub(super) host_metadata_checked: AtomicU64,
    /// A slow tmux listing must not block classification or overlap the next listing.
    pub(super) host_metadata_poll: tokio::sync::Mutex<Option<JoinHandle<()>>>,
    pub(super) signals: super::manager::Signals,
    pub(super) scroll_cache: Mutex<HashMap<String, CachedScroll>>,
    pub(super) activity_cache: crate::activity::ActivityCache,
    pub audio: crate::audio::Audio,
    pub(crate) music_transition: tokio::sync::Mutex<()>,
    pub(crate) ncspot: tokio::sync::Mutex<super::manager::ncspot::Player>,
    pub events: broadcast::Sender<Arc<EventMessage>>,
    pub(super) auth_generation: AtomicU64,
    pub(super) auth_changes: broadcast::Sender<AuthChange>,
    pub(super) grants: RwLock<crate::grant::Grants>,
    pub(super) session_boundary: tokio::sync::Mutex<()>,
    /// Serializes template read/compare/write/publish transactions. A version check made
    /// outside this lock would let two editors both pass and lose one draft.
    pub(super) template_mutation: tokio::sync::Mutex<()>,
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
        self.0.signals.watchers_changed.send_replace(());
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
    test_manager_with_socket(config, "slopworld-unit-test")
}

#[cfg(test)]
pub(crate) fn test_manager_with_socket(config: Config, socket: impl Into<String>) -> Arc<Manager> {
    let directory = std::env::temp_dir().join(format!(
        "slopd-manager-test-{}-{}",
        std::process::id(),
        uuid::Uuid::new_v4()
    ));
    // The task store is a sibling of config.toml; caches use the XDG cache root and are
    // independently redirected by the test environment.
    std::fs::create_dir_all(&directory).expect("test manager directory");
    let cfg_path = directory.join("config.toml");
    let (events, _) = broadcast::channel(16);
    let (auth_changes, _) = broadcast::channel(16);
    Arc::new(Manager {
        tmux: Tmux::new(socket),
        cfg_path: cfg_path.clone(),
        endpoint_path: cfg_path.with_extension("endpoint.toml"),
        cfg: RwLock::new(config),
        templates: RwLock::new(AgentTemplateStore::default()),
        live: RwLock::new(HashMap::new()),
        temp: RwLock::new(HashMap::new()),
        rules: RwLock::new(Vec::new()),
        rules_revision: AtomicU64::new(0),
        config_state: super::manager::ConfigState::new(None, None, None, None),
        host_metadata_checked: AtomicU64::new(0),
        host_metadata_poll: tokio::sync::Mutex::new(None),
        signals: super::manager::Signals::new(),
        scroll_cache: Mutex::new(HashMap::new()),
        activity_cache: crate::activity::ActivityCache::load(crate::activity::cache_path(
            &cfg_path,
        )),
        audio: crate::audio::Audio::new(),
        music_transition: tokio::sync::Mutex::new(()),
        ncspot: tokio::sync::Mutex::new(Default::default()),
        events,
        auth_generation: AtomicU64::new(0),
        auth_changes,
        grants: RwLock::new(crate::grant::Grants::default()),
        session_boundary: tokio::sync::Mutex::new(()),
        template_mutation: tokio::sync::Mutex::new(()),
        tasks: super::manager::TaskStore::new(
            crate::tasks::Tasks::load(&cfg_path).expect("test task store"),
        ),
        worker_spawn: tokio::sync::Mutex::new(()),
        title_cache: crate::title::SummaryCache::load(crate::title::cache_path(&cfg_path)),
    })
}
