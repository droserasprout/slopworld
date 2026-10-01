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
    let socket = socket.into();
    let socket = if std::path::Path::new(&socket).is_absolute() {
        socket
    } else {
        directory.join("tmux").to_str().unwrap().to_owned()
    };
    let cfg_path = directory.join("config.toml");
    let (events, _) = broadcast::channel(16);
    Arc::new(Manager {
        frame_commit_pause: Mutex::new(None),
        reader_attach_pause: Mutex::new(None),
        input_sink: Mutex::new(None),
        _test_directory: Some(TestDirectory(directory.clone())),
        tmux: Tmux::new(socket),
        cfg_path: cfg_path.clone(),
        endpoint_path: cfg_path.with_extension("endpoint.toml"),
        library_snapshot: Mutex::new(config.library_items_all()),
        cfg: RwLock::new(config),
        templates: TemplateStore::new(AgentTemplateStore::default()),
        live: RwLock::new(HashMap::new()),
        temp: RwLock::new(HashMap::new()),
        config_state: super::ConfigState::new(None, None, None, None),
        host_metadata: HostMetadataPoll::default(),
        signals: super::Signals::new(),
        scroll_cache: Mutex::new(HashMap::new()),
        activity_mutation: tokio::sync::Mutex::new(()),
        activity_cache: crate::activity::ActivityCache::load(crate::activity::cache_path(
            &cfg_path,
        )),
        music: MusicState::new(),
        events,
        auth: Authorization::new(crate::grant::Grants::default()),
        session_boundary: Arc::new(tokio::sync::RwLock::new(())),
        terminal_boundaries: Mutex::new(HashMap::new()),
        terminal_resizes: Mutex::new(HashMap::new()),
        tasks: super::TaskStore::new(
            crate::tasks::Tasks::load(&cfg_path).expect("test task store"),
        ),
        worker_spawn: tokio::sync::Mutex::new(()),
        worktrees: WorktreeState::default(),
        title_cache: crate::title::SummaryCache::load(directory.join("summary-cache.toml")),
    })
}

// Declared last in the fixture's lifetime: Manager resources no longer use it on drop.
pub(super) struct TestDirectory(PathBuf);
impl Drop for TestDirectory {
    fn drop(&mut self) {
        let socket = self.0.join("tmux");
        drop(
            std::process::Command::new("tmux")
                .args(["-S", socket.to_str().unwrap(), "kill-server"])
                .output(),
        );
        drop(std::fs::remove_dir_all(&self.0));
    }
}
