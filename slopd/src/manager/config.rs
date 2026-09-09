//! Manager construction and configuration synchronization.

use super::super::*;
use super::{reconcile::ConfigReconciler, ConfigState, Signals};

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
            config_state: ConfigState::new(
                mtime,
                presets_loaded.then_some(presets_mtime).flatten(),
                jukebox_loaded.then_some(jukebox_mtime).flatten(),
            ),
            host_metadata_checked: AtomicU64::new(0),
            signals: Signals::new(),
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
        self.config_saved(cfg).await;
        Ok(())
    }

    async fn persist_cfg_text(&self, cfg: &Config, text: &str) -> Result<()> {
        Config::save_text(&self.cfg_path, text).await?;
        self.config_saved(cfg).await;
        Ok(())
    }

    async fn config_saved(&self, cfg: &Config) {
        *self.config_state.cfg_mtime.lock().unwrap() = disk_mtime(&self.cfg_path).await;
        if let Err(e) = crate::endpoint::update_token(&cfg.daemon.token).await {
            tracing::warn!("config saved but endpoint descriptor was not updated: {e:#}");
        }
    }

    /// Apply a config mutation to a private snapshot, persist it without holding `cfg`, then
    /// publish it. The second lock serializes snapshots so concurrent requests cannot overwrite
    /// one another while a slow filesystem is servicing an earlier write.
    pub(super) async fn update_cfg<T>(
        &self,
        update: impl FnOnce(&mut Config) -> Result<T>,
    ) -> Result<T> {
        self.update_cfg_if_changed(|cfg| update(cfg).map(|result| (result, true)))
            .await
    }

    pub(super) async fn update_cfg_if_changed<T>(
        &self,
        update: impl FnOnce(&mut Config) -> Result<(T, bool)>,
    ) -> Result<T> {
        let _persist = self.config_state.persist.lock().await;
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
        let persist = self.config_state.persist.lock().await;
        let disk = disk_mtime(&self.cfg_path).await;
        {
            let seen = self.config_state.cfg_mtime.lock().unwrap();
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
        *self.config_state.cfg_mtime.lock().unwrap() = disk;
        drop(persist);
        if let Err(e) = crate::endpoint::update_token(&token).await {
            tracing::warn!("config reloaded but endpoint descriptor was not updated: {e:#}");
        }
        self.sync_from_config().await;
        self.emit(Event::Sessions {
            sessions: self.views().await,
        });
        self.announce_projects().await;
        self.announce_library().await;
        true
    }

    pub(super) async fn reload_if_due(self: &Arc<Self>) {
        let now = now_ms();
        let last = self.config_state.checked.load(Ordering::Relaxed);
        if now.saturating_sub(last) < CFG_CHECK_MS {
            return;
        }
        if self
            .config_state
            .checked
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
            let mut seen = self.config_state.presets_mtime.lock().unwrap();
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
        self.emit(Event::Sessions {
            sessions: self.views().await,
        });
        true
    }

    pub async fn reload_jukebox_if_changed(self: &Arc<Self>) -> bool {
        let disk = crate::paths::dir_stamp(&crate::jukebox::Catalog::dir());
        let reloaded = {
            let mut seen = self.config_state.jukebox_mtime.lock().unwrap();
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
        self.emit(Event::Jukebox {
            jukebox: crate::jukebox::catalog(),
        });
        true
    }

    pub fn client_joined(self: &Arc<Self>) -> ClientGuard {
        if self.signals.clients.fetch_add(1, Ordering::Relaxed) == 0 {
            self.signals
                .clients_since
                .store(now_ms(), Ordering::Relaxed);
        }
        ClientGuard(self.clone())
    }

    pub fn watching(self: &Arc<Self>, name: &str) -> WatchGuard {
        if let Ok(mut w) = self.signals.watchers.lock() {
            *w.entry(name.to_string()).or_insert(0) += 1;
        }
        WatchGuard(self.clone(), name.to_string())
    }

    pub(super) fn watched(&self, name: &str) -> bool {
        self.signals
            .watchers
            .lock()
            .map(|w| w.contains_key(name))
            .unwrap_or(true)
    }

    pub async fn config(&self) -> Config {
        self.cfg.read().await.clone()
    }

    pub async fn usage(&self) -> crate::usage::Snapshot {
        self.signals.usage.read().await.clone()
    }

    pub async fn set_usage(&self, snap: crate::usage::Snapshot) {
        {
            let mut cur = self.signals.usage.write().await;
            if cur.same_readout(&snap) {
                *cur = snap;
                return;
            }
            *cur = snap.clone();
        }
        self.emit(Event::Usage { usage: snap });
    }

    pub async fn sync_from_config(self: &Arc<Self>) {
        ConfigReconciler::sync(self).await;
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
            let Ok(permit) = m.signals.redraw_nudge.clone().acquire_owned().await else {
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

        let _persist = self.config_state.persist.lock().await;
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
        let _persist = self.config_state.persist.lock().await;
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
        assert_eq!(manager.signals.clients.load(Ordering::Relaxed), 2);
        assert!(manager.signals.clients_since.load(Ordering::Relaxed) > 0);
        drop(second_client);
        assert_eq!(manager.signals.clients.load(Ordering::Relaxed), 1);
        drop(first_client);
        assert_eq!(manager.signals.clients.load(Ordering::Relaxed), 0);

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
        assert!(matches!(event.event(), Event::Usage { usage } if usage == &changed));
        assert_eq!(manager.usage().await, changed);
    }

    #[tokio::test]
    async fn persisted_root_token_changes_invalidate_existing_auth() {
        let manager = test_manager(Config::default());
        let mut changes = manager.auth_changes();

        manager
            .update_cfg(|cfg| {
                cfg.daemon.token = "rotated-root".into();
                Ok(())
            })
            .await
            .expect("persist token rotation");

        assert_eq!(manager.auth_generation(), 1);
        assert!(matches!(
            changes.try_recv().expect("auth invalidation"),
            AuthChange::RootTokenChanged
        ));
        assert_eq!(manager.config().await.daemon.token, "rotated-root");
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
        *manager.config_state.cfg_mtime.lock().unwrap() = Some(old_stamp);

        std::fs::write(&path, "[daemon\n").unwrap();
        std::fs::File::open(&path)
            .unwrap()
            .set_modified(failed_stamp)
            .unwrap();
        assert!(!manager.reload_if_changed().await);
        assert_eq!(
            *manager.config_state.cfg_mtime.lock().unwrap(),
            Some(old_stamp)
        );

        std::fs::write(&path, toml::to_string_pretty(&Config::default()).unwrap()).unwrap();
        std::fs::File::open(&path)
            .unwrap()
            .set_modified(failed_stamp)
            .unwrap();
        assert!(manager.reload_if_changed().await);
        assert_eq!(
            *manager.config_state.cfg_mtime.lock().unwrap(),
            Some(failed_stamp)
        );

        let _ = std::fs::remove_file(path);
    }
}
