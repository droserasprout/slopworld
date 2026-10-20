use super::*;

pub(crate) fn test_manager(config: Config) -> Arc<Manager> {
    test_manager_with_socket(config, "slopworld-unit-test")
}

pub(crate) fn test_manager_with_socket(
    mut config: Config,
    socket: impl Into<String>,
) -> Arc<Manager> {
    if config.daemon.token.trim().is_empty() {
        config.daemon.token = "test-root-token".into();
    }
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
    let records = crate::config::transactions::records::Records::fixture(&mut config, &cfg_path)
        .expect("record fixture");
    let configuration = crate::config::transactions::Transactions::new(
        records,
        crate::config::catalog::revision(&cfg_path).ok(),
    );
    let tmux = Tmux::new(socket);
    let library_snapshot = config.library_items_all();
    let manager = Arc::new(Manager {
        frame_commit_pause: Mutex::new(None),
        scroll_capture_pause: Mutex::new(None),
        reader_attach_pause: Mutex::new(None),
        input_sink: Mutex::new(None),
        _test_directory: Some(TestDirectory(directory.clone())),
        tmux: tmux.clone(),
        cfg_path: cfg_path.clone(),
        endpoint_path: cfg_path.with_extension("endpoint.toml"),
        config_authority: Arc::new(crate::application::ConfigAuthority::new(
            config,
            crate::grant::Grants::default(),
        )),
        live: RwLock::new(HashMap::new()),
        temp: RwLock::new(HashMap::new()),
        configuration,
        host_metadata: HostMetadataPoll::default(),
        signals: super::Signals::new(),
        scroll_cache: Mutex::new(HashMap::new()),
        activity_mutation: tokio::sync::Mutex::new(()),
        activity_cache: crate::activity::ActivityCache::load(crate::activity::cache_path(
            &cfg_path,
        )),
        application: Arc::new(crate::application::Services::new(
            crate::tasks::io::TaskStore::new(
                crate::tasks::Tasks::load(&cfg_path).expect("test task store"),
            ),
            Arc::new(crate::title::SummaryCache::load(
                directory.join("summary-cache.toml"),
            )),
            tmux,
            16,
            crate::application::AgentTemplates::new(
                AgentTemplateStore::default(),
                directory.join("agent_templates"),
            ),
        )),
        session_boundary: Arc::new(tokio::sync::RwLock::new(())),
        terminal_boundaries: Mutex::new(HashMap::new()),
        terminal_resizes: Mutex::new(HashMap::new()),
        worker_spawn: tokio::sync::Mutex::new(()),
        worktrees: WorktreeState::default(),
    });
    manager.application.library.seed(library_snapshot);
    manager
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

impl Manager {
    /// Exercise production ownership and commit while replacing fixture membership.
    pub(crate) async fn replace_workspace_fixture(self: &Arc<Self>, cfg: &Config) -> Result<()> {
        self.update_cfg(super::config::ConfigMutation::ProjectReferences, |next| {
            next.projects = cfg.projects.clone();
            next.sessions = cfg.sessions.clone();
            next.host_terminals = cfg.host_terminals.clone();
            Ok(())
        })
        .await
    }
}

impl Manager {
    /// Hold queued input at the transport boundary for command authorization tests.
    pub(crate) async fn terminal_input_fixture(
        &self,
        name: &str,
        run_id: u64,
    ) -> mpsc::UnboundedReceiver<Input> {
        let (tx, rx) = mpsc::unbounded_channel();
        let mut live = Live::new(
            SessionCfg {
                name: name.into(),
                ..Default::default()
            },
            TitleCapture::default(),
        );
        live.run_id = run_id;
        live.state = State::Working;
        live.input.sender = Some(tx);
        let mut emu = SessionEmu::new(80, 24);
        emu.feed(b"\x1b[?1000h\x1b[?1006h");
        live.capture.emu = Some(Arc::new(Mutex::new(emu)));
        self.live.write().await.insert(name.into(), live);
        rx
    }
}

impl Manager {
    pub(crate) async fn use_persisted_grants_for_test(&self) {
        *self.config_authority.grants_for_test().write().await =
            crate::grant::Grants::load(&self.cfg_path).unwrap();
    }
}
