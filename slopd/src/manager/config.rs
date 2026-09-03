//! Manager construction and configuration synchronization.

use super::super::*;

impl Live {
    pub(super) fn new(cfg: SessionCfg, title: TitleCapture) -> Self {
        Self {
            cfg,
            ephemeral: false,
            host: false,
            host_path: String::new(),
            state: State::Down,
            seq: 0,
            retick_seq: 0,
            hash: 0,
            last_change: 0,
            state_since: 0,
            bell: false,
            cols: BOOT_COLS,
            rows: BOOT_ROWS,
            plain: String::new(),
            screen: None,
            emu: None,
            reader: None,
            reader_token: None,
            input: None,
            breadcrumbs: Vec::new(),
            breadcrumbs_pending: false,
            auto_resume_pending: false,
            run_id: 0,
            title,
        }
    }
}

impl Manager {
    pub async fn new(cfg: Config, cfg_path: PathBuf) -> Arc<Self> {
        let (events, _) = broadcast::channel(256);
        let mtime = disk_mtime(&cfg_path).await;
        let tasks = crate::tasks::Tasks::load(&cfg_path)
            .unwrap_or_else(|e| panic!("task store {}: {e:#}", cfg_path.display()));
        let title_cache = crate::title::SummaryCache::load(crate::title::cache_path(&cfg_path));
        let activity_cache =
            crate::activity::ActivityCache::load(crate::activity::cache_path(&cfg_path));
        // `main` logs the catalog before constructing the manager. Refresh here as well so a
        // definition copied between those two reads cannot leave the static catalog empty while
        // the watcher starts with the directory's already-current timestamp.
        crate::jukebox::reload();
        let m = Arc::new(Self {
            tmux: Tmux::new(crate::config::tmux_socket()),
            cfg_path,
            rules: RwLock::new(compile_rules(&cfg)),
            live: RwLock::new(HashMap::new()),
            temp: RwLock::new(HashMap::new()),
            cfg: RwLock::new(cfg),
            cfg_mtime: Mutex::new(mtime),
            cfg_persist: tokio::sync::Mutex::new(()),
            presets_mtime: Mutex::new(crate::presets::dir_stamp()),
            jukebox_mtime: Mutex::new(crate::jukebox::dir_stamp()),
            cfg_checked: AtomicU64::new(0),
            usage: RwLock::new(crate::usage::Snapshot::default()),
            clients: AtomicUsize::new(0),
            clients_since: AtomicU64::new(0),
            watchers: Mutex::new(HashMap::new()),
            redraw_nudge: Arc::new(tokio::sync::Semaphore::new(1)),
            scroll_cache: Mutex::new(HashMap::new()),
            activity_cache,
            audio: crate::audio::Audio::new(),
            events,
            grants: RwLock::new(crate::grant::Grants::default()),
            tasks: Mutex::new(tasks),
            worker_spawn: tokio::sync::Mutex::new(()),
            title_cache,
        });
        if let Ok(n) = crate::sandbox::purge_trash() {
            if n > 0 {
                tracing::info!("purged {n} expired private-state trash entries");
            }
        }
        m.tmux.ensure_server().await;
        m.sync_from_config().await;
        m
    }

    async fn persist_cfg(&self, cfg: &Config) -> Result<()> {
        cfg.save(&self.cfg_path).await?;
        *self.cfg_mtime.lock().unwrap() = disk_mtime(&self.cfg_path).await;
        if let Err(e) = crate::endpoint::update_token(&cfg.daemon.token).await {
            tracing::warn!("config saved but endpoint descriptor was not updated: {e:#}");
        }
        Ok(())
    }

    async fn persist_cfg_text(&self, cfg: &Config, text: &str) -> Result<()> {
        Config::save_text(&self.cfg_path, text).await?;
        *self.cfg_mtime.lock().unwrap() = disk_mtime(&self.cfg_path).await;
        if let Err(e) = crate::endpoint::update_token(&cfg.daemon.token).await {
            tracing::warn!("config saved but endpoint descriptor was not updated: {e:#}");
        }
        Ok(())
    }

    /// Apply a config mutation to a private snapshot, persist it without holding `cfg`, then
    /// publish it. The second lock serializes snapshots so concurrent requests cannot overwrite
    /// one another while a slow filesystem is servicing an earlier write.
    pub(super) async fn update_cfg<T>(
        &self,
        update: impl FnOnce(&mut Config) -> Result<T>,
    ) -> Result<T> {
        let _persist = self.cfg_persist.lock().await;
        let mut candidate = self.cfg.read().await.clone();
        let result = update(&mut candidate)?;
        self.persist_cfg(&candidate).await?;
        *self.cfg.write().await = candidate;
        Ok(result)
    }

    pub(super) async fn update_cfg_if_changed<T>(
        &self,
        update: impl FnOnce(&mut Config) -> Result<(T, bool)>,
    ) -> Result<T> {
        let _persist = self.cfg_persist.lock().await;
        let mut candidate = self.cfg.read().await.clone();
        let (result, changed) = update(&mut candidate)?;
        if changed {
            self.persist_cfg(&candidate).await?;
            *self.cfg.write().await = candidate;
        }
        Ok(result)
    }

    pub async fn reload_if_changed(self: &Arc<Self>) -> bool {
        // Keep the disk read and in-memory replacement together with config saves. The lock is
        // deliberately not held while the rest of synchronization runs below.
        let persist = self.cfg_persist.lock().await;
        let disk = disk_mtime(&self.cfg_path).await;
        {
            let mut seen = self.cfg_mtime.lock().unwrap();
            if *seen == disk {
                return false;
            }
            *seen = disk;
        }

        let text = match tokio::fs::read_to_string(&self.cfg_path).await {
            Ok(t) => t,
            Err(e) => {
                tracing::warn!("config changed on disk but is unreadable: {e:#}");
                return false;
            }
        };
        let new = match Config::parse(&text).and_then(|c| {
            validate_config(&c)?;
            Ok(c)
        }) {
            Ok(c) => c,
            Err(e) => {
                tracing::warn!("config changed on disk but is invalid, keeping the old one: {e:#}");
                return false;
            }
        };

        tracing::info!("config changed on disk, reloading");
        *self.rules.write().await = compile_rules(&new);
        let token = new.daemon.token.clone();
        *self.cfg.write().await = new;
        drop(persist);
        if let Err(e) = crate::endpoint::update_token(&token).await {
            tracing::warn!("config reloaded but endpoint descriptor was not updated: {e:#}");
        }
        self.sync_from_config().await;
        let _ = self.events.send(Event::Sessions {
            sessions: self.views().await,
        });
        self.announce_projects().await;
        self.announce_library().await;
        true
    }

    pub(super) async fn reload_if_due(self: &Arc<Self>) {
        let now = now_ms();
        let last = self.cfg_checked.load(Ordering::Relaxed);
        if now.saturating_sub(last) < CFG_CHECK_MS {
            return;
        }
        if self
            .cfg_checked
            .compare_exchange(last, now, Ordering::Relaxed, Ordering::Relaxed)
            .is_err()
        {
            return;
        }
        self.reload_presets_if_changed().await;
        self.reload_jukebox_if_changed().await;
        self.reload_if_changed().await;
    }

    pub async fn reload_presets_if_changed(self: &Arc<Self>) -> bool {
        let disk = crate::presets::dir_stamp();
        {
            let mut seen = self.presets_mtime.lock().unwrap();
            if *seen == disk {
                return false;
            }
            *seen = disk;
        }
        tracing::info!("presets changed on disk, reloading");
        crate::presets::reload();
        let _ = self.events.send(Event::Sessions {
            sessions: self.views().await,
        });
        true
    }

    pub async fn reload_jukebox_if_changed(self: &Arc<Self>) -> bool {
        let disk = crate::jukebox::dir_stamp();
        {
            let mut seen = self.jukebox_mtime.lock().unwrap();
            if *seen == disk {
                return false;
            }
            *seen = disk;
        }
        tracing::info!("jukebox definitions changed on disk, reloading");
        crate::jukebox::reload();
        let _ = self.events.send(Event::Jukebox {
            jukebox: crate::jukebox::catalog(),
        });
        true
    }

    pub fn client_joined(self: &Arc<Self>) -> ClientGuard {
        if self.clients.fetch_add(1, Ordering::Relaxed) == 0 {
            self.clients_since.store(now_ms(), Ordering::Relaxed);
        }
        ClientGuard(self.clone())
    }

    pub fn watching(self: &Arc<Self>, name: &str) -> WatchGuard {
        if let Ok(mut w) = self.watchers.lock() {
            *w.entry(name.to_string()).or_insert(0) += 1;
        }
        WatchGuard(self.clone(), name.to_string())
    }

    pub(super) fn watched(&self, name: &str) -> bool {
        self.watchers
            .lock()
            .map(|w| w.contains_key(name))
            .unwrap_or(true)
    }

    pub async fn config(&self) -> Config {
        self.cfg.read().await.clone()
    }

    pub fn create_task(
        &self,
        from: String,
        to: String,
        body: String,
    ) -> Result<crate::tasks::Task> {
        self.tasks.lock().unwrap().create(from, to, body)
    }

    pub fn tasks_for(&self, who: &str) -> Vec<crate::tasks::Task> {
        self.tasks.lock().unwrap().visible(who)
    }

    pub fn all_tasks(&self) -> Vec<crate::tasks::Task> {
        self.tasks.lock().unwrap().all()
    }

    pub fn task_for(&self, who: &str, id: &str) -> Option<crate::tasks::Task> {
        self.tasks.lock().unwrap().get(who, id)
    }

    pub fn update_task(
        &self,
        who: &str,
        id: &str,
        status: crate::tasks::Status,
        note: Option<String>,
    ) -> Result<crate::tasks::Task> {
        self.tasks.lock().unwrap().update(who, id, status, note)
    }

    pub fn remove_task(&self, who: &str, id: &str, force: bool) -> Result<crate::tasks::Task> {
        self.tasks.lock().unwrap().remove(who, id, force)
    }

    pub fn remove_tasks(&self, who: &str, ids: &[String], force: bool) -> Result<usize> {
        self.tasks.lock().unwrap().remove_many(who, ids, force)
    }

    pub fn prune_tasks(&self, who: &str, all: bool) -> Result<usize> {
        self.tasks.lock().unwrap().prune(who, all)
    }

    pub fn fail_worker_task(&self, task_id: &str, note: impl Into<String>) {
        if task_id.trim().is_empty() {
            return;
        }
        match self.tasks.lock().unwrap().fail_worker(task_id, note.into()) {
            Ok(Some(task)) if task.status == crate::tasks::Status::Failed => {
                tracing::info!(task = %task.id, "task-owned worker task marked failed")
            }
            Ok(_) => {}
            Err(error) => {
                tracing::error!(task = %task_id, "could not persist worker task failure: {error:#}")
            }
        }
    }

    pub async fn usage(&self) -> crate::usage::Snapshot {
        self.usage.read().await.clone()
    }

    pub async fn set_usage(&self, snap: crate::usage::Snapshot) {
        {
            let mut cur = self.usage.write().await;
            if cur.same_readout(&snap) {
                *cur = snap;
                return;
            }
            *cur = snap.clone();
        }
        let _ = self.events.send(Event::Usage { usage: snap });
    }

    pub async fn sync_from_config(self: &Arc<Self>) {
        let cfg = self.config().await;
        let removed = self.prune_removed(&cfg).await;

        if !removed.is_empty() {
            let mut grants = self.grants.write().await;
            for name in &removed {
                grants.revoke_grantor(name);
            }
        }

        self.upsert_sessions(&cfg).await;
        self.upsert_host_terminals(&cfg).await;
        self.sync_manifests(&cfg).await;
        let titles_changed = self.reconcile_title_settings(&cfg).await;
        self.autostart(&cfg).await;
        self.autostart_host_terminals(&cfg).await;

        let adopted = self.adopt_orphans(&cfg).await;
        if titles_changed || adopted {
            let _ = self.events.send(Event::Sessions {
                sessions: self.views().await,
            });
        }
    }

    /// Keep generated project manifests in step with the durable configuration. The file is
    /// project-scoped because agents share a project tree; the session option controls whether
    /// an agent receives its read-only overlay, while daemon instructions control its destination
    /// and optional discovery breadcrumb.
    pub(super) async fn sync_manifests(&self, cfg: &Config) {
        let views = self.views().await;
        for project in &cfg.projects {
            let dir = crate::config::expand(&project.dir);
            let path = std::path::Path::new(&dir);
            if !path.is_dir() {
                continue;
            }
            let enabled = cfg
                .sessions
                .iter()
                .any(|session| session.project == project.name && session.slopworld_md);
            let result = if enabled {
                crate::manifest::prepare(path, cfg, project, &views).map(|_| ())
            } else {
                crate::manifest::remove(path)
            };
            if let Err(error) = result {
                tracing::warn!(
                    project = %project.name,
                    error = %error,
                    "could not synchronize generated SLOPWORLD.md"
                );
            }
        }
    }

    async fn prune_removed(&self, cfg: &Config) -> Vec<String> {
        // Dropping a `Live` only *detaches* its reader: `JoinHandle`'s own `Drop` lets the
        // task run on, and it would keep a control-mode attach open against a session
        // nothing points at any more. Every other removal path aborts, so this one does too.
        let mut live = self.live.write().await;
        let mut removed = Vec::new();
        let mut worker_tasks = Vec::new();
        live.retain(|name, l| {
            let saved_host = l.host && cfg.host_terminals.iter().any(|tab| tab.name == *name);
            if (l.ephemeral && (!l.host || saved_host)) || cfg.session(name).is_some() {
                return true;
            }
            tracing::info!("agent {name} is gone from the config, ending its reader");
            if l.cfg.worker && !l.cfg.task_id.trim().is_empty() {
                worker_tasks.push((l.cfg.task_id.clone(), name.clone()));
            }
            if let Some(h) = l.reader.take() {
                h.abort();
            }
            self.forget_scroll(name);
            if let Err(error) = self.title_cache.clear_latest(name) {
                tracing::warn!(
                    target: "slopd::titles",
                    session = %name,
                    error = %error,
                    outcome = "cache_write_failed",
                    "could not clear session title"
                );
            }
            removed.push(name.clone());
            false
        });
        drop(live);
        for (task_id, name) in worker_tasks {
            self.fail_worker_task(&task_id, format!("worker session {name} was removed"));
        }
        for name in &removed {
            self.clear_activity(name).await;
        }
        removed
    }

    async fn upsert_sessions(&self, cfg: &Config) {
        let mut live = self.live.write().await;
        for s in &cfg.sessions {
            let mut title = TitleCapture::default();
            title.override_title = self.title_cache.latest(&s.name);
            title.once_requested = title.override_title.is_some();
            live.entry(s.name.clone())
                .and_modify(|l| {
                    l.cfg = s.clone();
                    if !s.breadcrumb_yolo {
                        l.breadcrumbs_pending = false;
                    }
                })
                .or_insert(Live::new(s.clone(), title));
        }
    }

    async fn upsert_host_terminals(&self, cfg: &Config) {
        let mut live = self.live.write().await;
        for tab in &cfg.host_terminals {
            if let Err(error) = crate::session::check_name(&tab.name) {
                tracing::warn!("ignoring invalid host terminal {}: {error:#}", tab.name);
                continue;
            }
            if cfg.session(&tab.name).is_some() {
                tracing::warn!(
                    "ignoring host terminal {} because an agent has the same name",
                    tab.name
                );
                continue;
            }
            let mut session = SessionCfg {
                name: tab.name.clone(),
                project: tab.project.clone(),
                command: cfg.defaults.shell.clone(),
                ..Default::default()
            };
            let path = if tab.path.trim().is_empty() {
                cfg.project(&tab.project)
                    .map(|p| crate::config::expand(&p.dir))
                    .unwrap_or_default()
            } else {
                crate::config::expand(&tab.path)
            };
            session.autostart = tab.autostart;
            let mut title = TitleCapture::default();
            title.override_title = self.title_cache.latest(&tab.name);
            title.once_requested = title.override_title.is_some();
            live.entry(tab.name.clone())
                .and_modify(|l| {
                    if l.host {
                        l.cfg = session.clone();
                        l.host_path = path.clone();
                    }
                })
                .or_insert_with(|| {
                    let mut l = Live::new(session, title);
                    // Host tabs use the ghost-row presentation but remain in the durable
                    // host-terminal catalog rather than disappearing when the shell exits.
                    l.ephemeral = true;
                    l.host = true;
                    l.host_path = path;
                    l
                });
        }
    }

    async fn autostart(self: &Arc<Self>, cfg: &Config) {
        // Task workers are explicit, one-shot work. Older configs may still carry autostart=true
        // from before worker lifecycle settings were normalized, so guard the policy here too.
        for s in cfg.sessions.iter().filter(|s| s.autostart && !s.worker) {
            if !self.tmux.exists(&s.name).await {
                if let Err(e) = self.start(&s.name).await {
                    tracing::error!("autostart {}: {e:#}", s.name);
                }
            }
        }
    }

    async fn autostart_host_terminals(self: &Arc<Self>, cfg: &Config) {
        for tab in cfg.host_terminals.iter().filter(|tab| tab.autostart) {
            if !self.tmux.exists(&tab.name).await {
                if let Err(error) = self.start(&tab.name).await {
                    tracing::error!("autostart host terminal {}: {error:#}", tab.name);
                }
            }
        }
    }

    async fn adopt_orphans(self: &Arc<Self>, cfg: &Config) -> bool {
        let mut adopted = false;
        for name in self.tmux.list().await {
            // One-shot workers are deliberately absent from config.toml. Recover their
            // daemon-owned identity from the metadata carried by the surviving tmux session.
            let worker = self.tmux.worker_metadata(&name).await;
            let worker_session = worker
                .as_ref()
                .map(|metadata| recovered_worker_cfg(cfg, &name, metadata));
            let saved_host = cfg.host_terminals.iter().find(|tab| tab.name == name);
            // Worker identity takes precedence over a stale host marker or host-terminal record.
            let host = worker.is_none() && (self.tmux.is_host(&name).await || saved_host.is_some());
            let tmux_host = host.then(|| self.tmux.host_metadata(&name));
            let tmux_host = match tmux_host {
                Some(future) => future.await,
                None => None,
            };
            let host_project = tmux_host
                .as_ref()
                .and_then(|(project, _)| (!project.is_empty()).then_some(project.clone()))
                .or_else(|| saved_host.map(|tab| tab.project.clone()))
                .unwrap_or_default();
            let host_path = tmux_host
                .as_ref()
                .and_then(|(_, path)| (!path.is_empty()).then_some(path.clone()))
                .or_else(|| saved_host.map(|tab| tab.path.clone()))
                .unwrap_or_default();
            let activity = self
                .tmux
                .activity(&name)
                .await
                .map(|(state, state_since)| crate::activity::Activity { state, state_since })
                .or_else(|| self.activity_cache.get(&name));
            let needs_size = {
                let mut live = self.live.write().await;
                if !live.contains_key(&name) {
                    tracing::info!(
                        "adopting tmux session {name} as a temporary {}",
                        if host { "host terminal" } else { "agent" }
                    );
                    let now = now_ms();
                    let mut l = Live::new(
                        worker_session.clone().unwrap_or_else(|| SessionCfg {
                            name: name.clone(),
                            project: host_project.clone(),
                            command: if host {
                                cfg.defaults.shell.clone()
                            } else {
                                String::new()
                            },
                            ..Default::default()
                        }),
                        TitleCapture::default(),
                    );
                    l.ephemeral = worker
                        .as_ref()
                        .map(|metadata| !metadata.durable)
                        .unwrap_or(true);
                    l.host = host;
                    l.host_path = host_path.clone();
                    l.state = State::Working;
                    l.last_change = now;
                    l.state_since = now;
                    live.insert(name.clone(), l);
                    adopted = true;
                } else if let Some(l) = live.get_mut(&name) {
                    if let (Some(metadata), Some(session)) = (worker.as_ref(), worker_session) {
                        // A stale in-memory host row can exist when a daemon reload adopts a
                        // worker name that was also present in an old host-terminal catalog.
                        l.cfg = session;
                        l.ephemeral = !metadata.durable;
                        l.host = false;
                        l.host_path.clear();
                    }
                    if host && l.host {
                        if !host_project.is_empty() {
                            l.cfg.project = host_project.clone();
                        }
                        if !host_path.is_empty() {
                            l.host_path = host_path.clone();
                        }
                    }
                    self.restore_activity(l, activity);
                }
                live.get(&name).is_some_and(|l| l.emu.is_none())
            };

            // An established reader already knows its dimensions. Query tmux only for a
            // newly adopted or reader-less session, and do not hold the live-table lock
            // across the subprocess await.
            if needs_size {
                self.refresh_readerless_size(&name).await;
            }
            match self.spawn_reader(&name).await {
                Ok(true) => {
                    let m = self.clone();
                    let name = name.clone();
                    tokio::spawn(async move { m.nudge_redraw(&name).await });
                }
                Ok(false) => {}
                Err(error) => {
                    tracing::warn!("could not attach reader for adopted session {name}: {error:#}")
                }
            }
            if host {
                let current_path = self.tmux.current_path(&name).await;
                if saved_host.is_none() {
                    let project = self
                        .live
                        .read()
                        .await
                        .get(&name)
                        .filter(|l| l.host)
                        .map(|l| l.cfg.project.clone())
                        .unwrap_or_default();
                    let path = current_path
                        .clone()
                        .filter(|path| !path.trim().is_empty())
                        .or_else(|| (!host_path.trim().is_empty()).then_some(host_path.clone()))
                        .unwrap_or_default();
                    // A nameless host shell is still a runtime-only errand. Project shells
                    // are the durable sidebar tabs; only those can restore grouping on reboot.
                    if !project.trim().is_empty() {
                        if let Err(error) =
                            self.remember_host_terminal(&name, &project, &path).await
                        {
                            tracing::warn!(
                                "could not save adopted host terminal {name}: {error:#}"
                            );
                        }
                    }
                }
                if let Some(path) = current_path {
                    self.remember_host_path(&name, &path).await;
                }
            }
        }
        adopted
    }

    fn restore_activity(&self, l: &mut Live, activity: Option<crate::activity::Activity>) {
        if l.ephemeral || l.state != State::Down {
            return;
        }
        let Some(activity) = activity else { return };
        l.state = activity.state;
        l.state_since = activity.state_since;
        // Working classification uses pane activity as a decay clock. A daemon restart has
        // no frame to compare against, so let the first live frame/ten-second decay establish
        // a fresh last-change sample while preserving the user-visible state age above.
        l.last_change = if activity.state == State::Working {
            now_ms()
        } else {
            0
        };
    }

    pub(super) async fn clear_activity(&self, name: &str) {
        if let Err(error) = self.activity_cache.clear(name) {
            tracing::warn!(
                target: "slopd::activity",
                session = %name,
                %error,
                "could not clear session activity cache"
            );
        }
        if let Err(error) = self.tmux.clear_activity(name).await {
            tracing::debug!(
                target: "slopd::activity",
                session = %name,
                %error,
                "could not clear tmux session activity"
            );
        }
    }

    pub(super) async fn persist_activity(&self, name: &str, state: State, state_since: u64) {
        if let Err(error) = self.activity_cache.remember(name, state, state_since) {
            tracing::warn!(
                target: "slopd::activity",
                session = %name,
                %error,
                "could not persist session activity"
            );
        }
        if let Err(error) = self.tmux.set_activity(name, state, state_since).await {
            tracing::warn!(
                target: "slopd::activity",
                session = %name,
                %error,
                "could not persist tmux session activity"
            );
        }
    }

    async fn refresh_readerless_size(&self, name: &str) {
        let Some((cols, rows)) = self.tmux.size(name).await else {
            return;
        };
        let mut live = self.live.write().await;
        if let Some(l) = live.get_mut(name).filter(|l| l.emu.is_none()) {
            l.cols = cols;
            l.rows = rows;
        }
    }

    /// Ask every live tmux-backed pane to repaint after the game's sidebar changes its layout.
    /// This is detached from the WebSocket handler because each nudge waits briefly between the
    /// temporary shrink and restore, and viewer/editor errands live in the same table as agents.
    pub fn request_redraw(self: &Arc<Self>, shape: Option<(u16, u16)>) {
        let shape = shape.map(|(cols, rows)| (cols.clamp(20, 500), rows.clamp(5, 200)));
        let m = self.clone();
        tokio::spawn(async move {
            // Preserve redraws that arrive while an earlier nudge is sleeping. In particular,
            // the last sidebar drag carries the authoritative panel shape and must not be
            // discarded merely because a preceding layout change is still repainting.
            let Ok(permit) = m.redraw_nudge.clone().acquire_owned().await else {
                return;
            };
            let _permit = permit;
            let names = {
                let live = m.live.read().await;
                live.iter()
                    .filter(|(_, l)| l.emu.is_some() || l.state != State::Down)
                    .map(|(name, _)| name.clone())
                    .collect::<Vec<_>>()
            };
            let jobs = names.into_iter().map(|name| {
                let m = m.clone();
                async move {
                    if let Some((cols, rows)) = shape {
                        if let Err(e) = m.resize(&name, cols, rows).await {
                            tracing::debug!("redraw resize {name}: {e:#}");
                            return;
                        }
                    }
                    m.nudge_redraw(&name).await;
                }
            });
            futures::future::join_all(jobs).await;
        });
    }

    pub(super) async fn nudge_redraw(self: &Arc<Self>, name: &str) {
        // A capture cannot restore terminal modes; SIGWINCH makes TUIs reassert them.
        let Some((cols, rows)) = self.size_of(name).await else {
            return;
        };
        if cols < 2 {
            return;
        }
        if let Err(e) = self.tmux.resize(name, cols - 1, rows).await {
            tracing::debug!("redraw nudge {name}: {e:#}");
            return;
        }
        tokio::time::sleep(Duration::from_millis(60)).await;
        let Some((cols, rows)) = self.size_of(name).await else {
            return;
        };
        if let Err(e) = self.tmux.resize(name, cols, rows).await {
            tracing::debug!("redraw nudge {name}: {e:#}");
        }
    }

    pub(super) async fn size_of(&self, name: &str) -> Option<(u16, u16)> {
        self.live.read().await.get(name).map(|l| (l.cols, l.rows))
    }

    pub async fn patch_config(self: &Arc<Self>, patch: Value) -> Result<()> {
        self.reload_if_changed().await;

        let _persist = self.cfg_persist.lock().await;
        let text = tokio::fs::read_to_string(&self.cfg_path)
            .await
            .with_context(|| format!("reading {}", self.cfg_path.display()))?;
        let mut document: toml::Value = toml::from_str(&text).context("parsing config.toml")?;
        let patch = json_to_toml(patch)?;
        if !patch.is_table() {
            bail!("config patch must be a JSON object");
        }
        merge_toml(&mut document, patch);

        let old_token = self.cfg.read().await.daemon.token.clone();
        let mut new = Config::parse(&toml::to_string_pretty(&document)?)?;
        if new.daemon.token == crate::config::TOKEN_REDACTED {
            new.daemon.token = old_token.clone();
            if let Some(daemon) = document
                .get_mut("daemon")
                .and_then(toml::Value::as_table_mut)
            {
                daemon.insert("token".into(), toml::Value::String(old_token));
            }
        }
        validate_config(&new)?;

        let text = toml::to_string_pretty(&document)?;
        self.persist_cfg_text(&new, &text).await?;

        *self.rules.write().await = compile_rules(&new);
        *self.cfg.write().await = new;
        drop(_persist);
        self.sync_from_config().await;
        self.announce_projects().await;
        self.announce_library().await;
        Ok(())
    }

    pub async fn replace_config(self: &Arc<Self>, text: &str) -> Result<()> {
        let mut new = Config::parse(text)?;
        let _persist = self.cfg_persist.lock().await;
        if new.daemon.token == crate::config::TOKEN_REDACTED {
            new.daemon.token = self.cfg.read().await.daemon.token.clone();
        }
        validate_config(&new)?;
        self.persist_cfg(&new).await?;
        *self.rules.write().await = compile_rules(&new);
        *self.cfg.write().await = new;
        drop(_persist);
        self.sync_from_config().await;
        self.announce_projects().await;
        self.announce_library().await;
        Ok(())
    }
}

fn recovered_worker_cfg(
    cfg: &Config,
    name: &str,
    metadata: &crate::tmux::WorkerMetadata,
) -> SessionCfg {
    let mut session = cfg.session(&metadata.parent).cloned().unwrap_or_default();
    session.name = name.to_string();
    session.state_id = uuid::Uuid::new_v4().to_string();
    session.worker = true;
    session.parent = metadata.parent.clone();
    session.task_id = metadata.task_id.clone();
    session.autostart = false;
    session.auto_resume = false;
    session.worker_token = None;
    if !session
        .sandbox
        .iter()
        .any(|preset| preset == super::workers::WORKER_SANDBOX)
    {
        session.sandbox.push(super::workers::WORKER_SANDBOX.into());
    }
    session
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::config::HostTerminalCfg;
    use crate::session::test_manager;

    #[test]
    fn new_live_starts_as_a_boot_placeholder() {
        let cfg = SessionCfg {
            name: "agent".into(),
            project: "project".into(),
            ..Default::default()
        };
        let live = Live::new(cfg.clone(), TitleCapture::default());

        assert_eq!(live.cfg.name, cfg.name);
        assert_eq!(live.cfg.project, cfg.project);
        assert!(!live.ephemeral);
        assert!(!live.host);
        assert!(live.host_path.is_empty());
        assert_eq!(live.state, State::Down);
        assert_eq!((live.cols, live.rows), (BOOT_COLS, BOOT_ROWS));
        assert_eq!(live.seq, 0);
        assert_eq!(live.state_since, 0);
        assert!(!live.bell);
        assert!(live.screen.is_none());
        assert!(live.emu.is_none());
        assert!(live.reader.is_none());
        assert!(live.input.is_none());
        assert!(live.breadcrumbs.is_empty());
        assert!(!live.breadcrumbs_pending);
    }

    #[tokio::test]
    async fn client_and_watch_guards_release_their_bookkeeping() {
        let manager = test_manager(Config::default());
        assert!(!manager.watched("agent"));

        let first_client = manager.client_joined();
        let second_client = manager.client_joined();
        assert_eq!(manager.clients.load(Ordering::Relaxed), 2);
        assert!(manager.clients_since.load(Ordering::Relaxed) > 0);
        drop(second_client);
        assert_eq!(manager.clients.load(Ordering::Relaxed), 1);
        drop(first_client);
        assert_eq!(manager.clients.load(Ordering::Relaxed), 0);

        let first_watch = manager.watching("agent");
        let second_watch = manager.watching("agent");
        assert!(manager.watched("agent"));
        drop(second_watch);
        assert!(manager.watched("agent"));
        drop(first_watch);
        assert!(!manager.watched("agent"));
    }

    #[tokio::test]
    async fn usage_broadcasts_only_when_the_readout_changes() {
        let manager = test_manager(Config::default());
        let mut events = manager.events.subscribe();
        let unchanged = crate::usage::Snapshot::default();
        manager.set_usage(unchanged).await;
        assert!(events.try_recv().is_err());

        let changed = crate::usage::Snapshot {
            ok: true,
            sources: vec!["test".into()],
            ..Default::default()
        };
        manager.set_usage(changed.clone()).await;
        let event = events.try_recv().expect("usage event");
        assert!(matches!(event, Event::Usage { usage } if usage == changed));
        assert_eq!(manager.usage().await, changed);
    }

    #[tokio::test]
    async fn config_sync_upserts_agents_and_only_valid_host_terminals() {
        let manager = test_manager(Config::default());
        let cfg = Config {
            sessions: vec![SessionCfg {
                name: "agent".into(),
                ..Default::default()
            }],
            host_terminals: vec![
                HostTerminalCfg {
                    name: "bad name".into(),
                    ..Default::default()
                },
                HostTerminalCfg {
                    name: "agent".into(),
                    ..Default::default()
                },
                HostTerminalCfg {
                    name: "shell".into(),
                    project: "repo".into(),
                    path: "~/repo".into(),
                    autostart: true,
                },
            ],
            ..Default::default()
        };
        manager.upsert_sessions(&cfg).await;
        manager.upsert_host_terminals(&cfg).await;

        let live = manager.live.read().await;
        assert!(!live["agent"].host);
        assert!(live["shell"].host);
        assert!(live["shell"].ephemeral);
        assert_eq!(live["shell"].host_path, crate::config::expand("~/repo"));
        assert!(!live.contains_key("bad name"));
        assert_eq!(live.len(), 2);
    }

    #[test]
    fn recovered_worker_uses_parent_settings_and_task_identity() {
        let cfg = Config {
            sessions: vec![SessionCfg {
                name: "parent".into(),
                project: "repo".into(),
                command: "codex".into(),
                sandbox: vec!["gpu".into()],
                ..Default::default()
            }],
            ..Default::default()
        };
        let worker = recovered_worker_cfg(
            &cfg,
            "parent-worker",
            &crate::tmux::WorkerMetadata {
                parent: "parent".into(),
                task_id: "task-7".into(),
                durable: false,
            },
        );

        assert!(worker.worker);
        assert_eq!(worker.parent, "parent");
        assert_eq!(worker.task_id, "task-7");
        assert_eq!(worker.project, "repo");
        assert_eq!(worker.command, "codex");
        assert_eq!(worker.sandbox, ["gpu", "slopworld-worker"]);
        assert!(!worker.autostart);
        assert!(!worker.auto_resume);
    }
}
