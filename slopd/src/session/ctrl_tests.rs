use super::*;

pub(crate) fn test_manager(config: Config) -> Arc<Manager> {
    test_manager_with_socket(config, "slopworld-unit-test")
}

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
        config_state: super::super::manager::ConfigState::new(None, None, None, None),
        host_metadata_checked: AtomicU64::new(0),
        host_metadata_poll: tokio::sync::Mutex::new(None),
        signals: super::super::manager::Signals::new(),
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
        tasks: super::super::manager::TaskStore::new(
            crate::tasks::Tasks::load(&cfg_path).expect("test task store"),
        ),
        worker_spawn: tokio::sync::Mutex::new(()),
        worktree_mutation: tokio::sync::Mutex::new(()),
        title_cache: crate::title::SummaryCache::load(crate::title::cache_path(&cfg_path)),
    })
}
