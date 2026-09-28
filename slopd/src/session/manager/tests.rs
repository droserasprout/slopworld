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
    // The task store is a sibling of config.toml. Caches use the XDG cache root and are
    // independently redirected by the test environment.
    std::fs::create_dir_all(&directory).expect("test manager directory");
    let cfg_path = directory.join("config.toml");
    let (events, _) = broadcast::channel(16);
    Arc::new(Manager {
        tmux: Tmux::new(socket),
        cfg_path: cfg_path.clone(),
        endpoint_path: cfg_path.with_extension("endpoint.toml"),
        cfg: RwLock::new(config),
        templates: TemplateStore::new(AgentTemplateStore::default()),
        live: RwLock::new(HashMap::new()),
        temp: RwLock::new(HashMap::new()),
        config_state: super::ConfigState::new(None, None, None, None),
        host_metadata: HostMetadataPoll::default(),
        signals: super::Signals::new(),
        scroll_cache: Mutex::new(HashMap::new()),
        activity_cache: crate::activity::ActivityCache::load(crate::activity::cache_path(
            &cfg_path,
        )),
        music: MusicState::new(),
        events,
        auth: Authorization::new(crate::grant::Grants::default()),
        session_boundary: tokio::sync::RwLock::new(()),
        resize_mutation: tokio::sync::Mutex::new(()),
        tasks: super::TaskStore::new(
            crate::tasks::Tasks::load(&cfg_path).expect("test task store"),
        ),
        worker_spawn: tokio::sync::Mutex::new(()),
        worktrees: WorktreeState::default(),
        title_cache: crate::title::SummaryCache::load(crate::title::cache_path(&cfg_path)),
    })
}
