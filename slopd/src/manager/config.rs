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
            process_running: false,
            seq: 0,
            retick_seq: 0,
            hash: 0,
            last_change: 0,
            state_since: 0,
            bell: false,
            cols: BOOT_COLS,
            rows: BOOT_ROWS,
            plain: Arc::new(String::new()),
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
        let (auth_changes, _) = broadcast::channel(16);
        let mtime = disk_mtime(&cfg_path).await;
        let presets_mtime = crate::paths::dir_stamp(&crate::presets::Table::dir());
        let presets_loaded = crate::presets::reload();
        let jukebox_mtime = crate::paths::dir_stamp(&crate::jukebox::Catalog::dir());
        let tasks = crate::tasks::Tasks::load(&cfg_path)
            .unwrap_or_else(|e| panic!("task store {}: {e:#}", cfg_path.display()));
        let title_cache = crate::title::SummaryCache::load(crate::title::cache_path(&cfg_path));
        let activity_cache =
            crate::activity::ActivityCache::load(crate::activity::cache_path(&cfg_path));
        // `main` logs the catalogs before constructing the manager. Refresh them here as well so
        // a definition copied between those two reads cannot leave a static catalog stale while
        // the watcher starts with the directory's already-current timestamp. A failed reload
        // leaves its stamp unset so the watcher retries it.
        let jukebox_loaded = crate::jukebox::reload();
        let m = Arc::new(Self {
            tmux: Tmux::new(crate::config::tmux_socket()),
            cfg_path,
            rules: RwLock::new(compile_rules(&cfg)),
            live: RwLock::new(HashMap::new()),
            temp: RwLock::new(HashMap::new()),
            cfg: RwLock::new(cfg),
            cfg_mtime: Mutex::new(mtime),
            cfg_persist: tokio::sync::Mutex::new(()),
            presets_mtime: Mutex::new(presets_loaded.then_some(presets_mtime).flatten()),
            jukebox_mtime: Mutex::new(jukebox_loaded.then_some(jukebox_mtime).flatten()),
            cfg_checked: AtomicU64::new(0),
            host_metadata_checked: AtomicU64::new(0),
            usage: RwLock::new(crate::usage::Snapshot::default()),
            clients: AtomicUsize::new(0),
            clients_since: AtomicU64::new(0),
            watchers: Mutex::new(HashMap::new()),
            redraw_nudge: Arc::new(tokio::sync::Semaphore::new(1)),
            scroll_cache: Mutex::new(HashMap::new()),
            activity_cache,
            audio: crate::audio::Audio::new(),
            events,
            auth_generation: AtomicU64::new(0),
            auth_changes,
            grants: RwLock::new(crate::grant::Grants::default()),
            tasks: crate::session::manager::TaskStore::new(tasks),
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
        let old_token = self.cfg.read().await.daemon.token.clone();
        let mut candidate = self.cfg.read().await.clone();
        let result = update(&mut candidate)?;
        self.persist_cfg(&candidate).await?;
        *self.cfg.write().await = candidate;
        if old_token != self.cfg.read().await.daemon.token {
            self.invalidate_auth(crate::session::AuthChange::RootTokenChanged);
        }
        Ok(result)
    }

    pub(super) async fn update_cfg_if_changed<T>(
        &self,
        update: impl FnOnce(&mut Config) -> Result<(T, bool)>,
    ) -> Result<T> {
        let _persist = self.cfg_persist.lock().await;
        let old_token = self.cfg.read().await.daemon.token.clone();
        let mut candidate = self.cfg.read().await.clone();
        let (result, changed) = update(&mut candidate)?;
        if changed {
            self.persist_cfg(&candidate).await?;
            *self.cfg.write().await = candidate;
            if old_token != self.cfg.read().await.daemon.token {
                self.invalidate_auth(crate::session::AuthChange::RootTokenChanged);
            }
        }
        Ok(result)
    }

    pub async fn reload_if_changed(self: &Arc<Self>) -> bool {
        // Keep the disk read and in-memory replacement together with config saves. The lock is
        // deliberately not held while the rest of synchronization runs below.
        let persist = self.cfg_persist.lock().await;
        let disk = disk_mtime(&self.cfg_path).await;
        {
            let seen = self.cfg_mtime.lock().unwrap();
            if *seen == disk {
                return false;
            }
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
        let root_token_changed = self.cfg.read().await.daemon.token != new.daemon.token;
        *self.rules.write().await = compile_rules(&new);
        let token = new.daemon.token.clone();
        *self.cfg.write().await = new;
        if root_token_changed {
            self.invalidate_auth(crate::session::AuthChange::RootTokenChanged);
        }
        // Do not mark a disk state as seen until its contents have been accepted and published.
        // This keeps a corrected file retryable even when it retains the failed state's mtime.
        *self.cfg_mtime.lock().unwrap() = disk;
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
        let disk = crate::paths::dir_stamp(&crate::presets::Table::dir());
        let reloaded = {
            let mut seen = self.presets_mtime.lock().unwrap();
            if *seen == disk || !crate::presets::reload() {
                false
            } else {
                *seen = disk;
                true
            }
        };
        if !reloaded {
            return false;
        }
        tracing::info!("presets changed on disk, reloading");
        let _ = self.events.send(Event::Sessions {
            sessions: self.views().await,
        });
        true
    }

    pub async fn reload_jukebox_if_changed(self: &Arc<Self>) -> bool {
        let disk = crate::paths::dir_stamp(&crate::jukebox::Catalog::dir());
        let reloaded = {
            let mut seen = self.jukebox_mtime.lock().unwrap();
            if *seen == disk || !crate::jukebox::reload() {
                false
            } else {
                *seen = disk;
                true
            }
        };
        if !reloaded {
            return false;
        }
        tracing::info!("jukebox definitions changed on disk, reloading");
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

        for name in &removed {
            self.revoke_grants(name).await;
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
                label: tab.label.clone(),
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
            // Host tabs use their fixed label or the native terminal title. Never restore an
            // automatic prompt-summary cache entry for a shell.
            let title = TitleCapture::default();
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

    /// Ask every live tmux-backed pane to repaint after the game's sidebar changes its layout.
    /// This is detached from the WebSocket handler because each nudge waits briefly between the
    /// temporary shrink and restore, and viewer/editor errands live in the same table as agents.
    pub fn request_redraw(self: &Arc<Self>, shape: Option<(u16, u16)>) {
        let shape = shape.map(|(cols, rows)| {
            (
                cols.clamp(
                    crate::wire::TERMINAL_MIN_COLS,
                    crate::wire::TERMINAL_MAX_COLS,
                ),
                rows.clamp(
                    crate::wire::TERMINAL_MIN_ROWS,
                    crate::wire::TERMINAL_MAX_ROWS,
                ),
            )
        });
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
                daemon.insert("token".into(), toml::Value::String(old_token.clone()));
            }
        }
        validate_config(&new)?;

        let text = toml::to_string_pretty(&document)?;
        self.persist_cfg_text(&new, &text).await?;

        let root_token_changed = old_token != new.daemon.token;
        *self.rules.write().await = compile_rules(&new);
        *self.cfg.write().await = new;
        if root_token_changed {
            self.invalidate_auth(crate::session::AuthChange::RootTokenChanged);
        }
        drop(_persist);
        self.sync_from_config().await;
        self.announce_projects().await;
        self.announce_library().await;
        Ok(())
    }

    pub async fn replace_config(self: &Arc<Self>, text: &str) -> Result<()> {
        let mut new = Config::parse(text)?;
        let _persist = self.cfg_persist.lock().await;
        let old_token = self.cfg.read().await.daemon.token.clone();
        if new.daemon.token == crate::config::TOKEN_REDACTED {
            new.daemon.token = old_token.clone();
        }
        validate_config(&new)?;
        self.persist_cfg(&new).await?;
        let root_token_changed = old_token != new.daemon.token;
        *self.rules.write().await = compile_rules(&new);
        *self.cfg.write().await = new;
        if root_token_changed {
            self.invalidate_auth(crate::session::AuthChange::RootTokenChanged);
        }
        drop(_persist);
        self.sync_from_config().await;
        self.announce_projects().await;
        self.announce_library().await;
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::config::HostTerminalCfg;
    use crate::session::test_manager;
    use std::time::{Duration, UNIX_EPOCH};

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
                    label: None,
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

    #[tokio::test]
    async fn invalid_config_does_not_consume_its_disk_stamp() {
        let manager = test_manager(Config::default());
        let path = manager.cfg_path.clone();
        let old_stamp = UNIX_EPOCH;
        let failed_stamp = UNIX_EPOCH + Duration::from_secs(1);
        *manager.cfg_mtime.lock().unwrap() = Some(old_stamp);

        std::fs::write(&path, "[daemon\n").unwrap();
        std::fs::File::open(&path)
            .unwrap()
            .set_modified(failed_stamp)
            .unwrap();
        assert!(!manager.reload_if_changed().await);
        assert_eq!(*manager.cfg_mtime.lock().unwrap(), Some(old_stamp));

        std::fs::write(&path, toml::to_string_pretty(&Config::default()).unwrap()).unwrap();
        std::fs::File::open(&path)
            .unwrap()
            .set_modified(failed_stamp)
            .unwrap();
        assert!(manager.reload_if_changed().await);
        assert_eq!(*manager.cfg_mtime.lock().unwrap(), Some(failed_stamp));

        let _ = std::fs::remove_file(path);
    }
}
