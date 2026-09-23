//! Manager construction and configuration synchronization.

use super::super::*;
use super::{reconcile::ConfigReconciler, ConfigState, Signals};

enum ConfigOrigin {
    StructuredMutation,
    FileUpdate,
    DiskReload { mtime: Option<SystemTime> },
}

struct ConfigChange {
    new: Config,
    rules: Vec<(State, Regex)>,
    root_token_changed: bool,
}

pub(super) struct PreparedConfigChange {
    change: ConfigChange,
    // Keep the serialization gate until the caller either commits or abandons this prepared
    // change. Session renames hold it across the tmux rename so the candidate cannot go stale
    // before persistence.
    _persist: tokio::sync::OwnedMutexGuard<()>,
}

struct ConfigEffects {
    endpoint_token: String,
    reconcile: bool,
    announce_sessions: bool,
    announce_projects_and_library: bool,
}

fn prepare_candidate(old: &Config, mut new: Config) -> Result<ConfigChange> {
    if new.daemon.token == crate::config::TOKEN_REDACTED {
        new.daemon.token = old.daemon.token.clone();
    }
    let root_token_changed = old.daemon.token != new.daemon.token;
    validate_config(&new)?;
    let rules = compile_rules(&new);
    Ok(ConfigChange {
        new,
        rules,
        root_token_changed,
    })
}

fn restore_redacted_document_token(old: &Config, document: &mut toml::Value) {
    if let Some(daemon) = document
        .get_mut("daemon")
        .and_then(toml::Value::as_table_mut)
    {
        daemon.insert(
            "token".into(),
            toml::Value::String(old.daemon.token.clone()),
        );
    }
}

fn claim_due(clock: &std::sync::atomic::AtomicU64, now: u64, period: u64) -> bool {
    let last = clock.load(Ordering::Acquire);
    if now.saturating_sub(last) < period {
        return false;
    }
    clock
        .compare_exchange(last, now, Ordering::AcqRel, Ordering::Acquire)
        .is_ok()
}

fn next_periodic_deadline(last: u64, period: u64, now: u64) -> u64 {
    if now.saturating_sub(last) >= period {
        0
    } else {
        last.saturating_add(period)
    }
}

impl ConfigOrigin {
    fn effects(&self, endpoint_token: String) -> ConfigEffects {
        ConfigEffects {
            endpoint_token,
            reconcile: !matches!(self, Self::StructuredMutation),
            announce_sessions: matches!(self, Self::DiskReload { .. }),
            announce_projects_and_library: !matches!(self, Self::StructuredMutation),
        }
    }
}

impl Live {
    pub(super) fn new(cfg: SessionCfg, title: TitleCapture) -> Self {
        Self {
            cfg,
            ephemeral: false,
            host: false,
            persistent_host: false,
            host_path: String::new(),
            state: State::Down,
            process_running: false,
            seq: 0,
            retick_seq: 0,
            hash: 0,
            activity_hash: 0,
            last_change: 0,
            rule_cache: None,
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
        let library_mtime = Config::library_stamp_for(&cfg_path);
        let presets_mtime = crate::presets::Table::stamp();
        let presets_loaded = crate::presets::reload();
        let jukebox_mtime = crate::paths::dir_stamp(&crate::jukebox::Catalog::dir());
        let grants = crate::grant::Grants::load(&cfg_path)
            .unwrap_or_else(|error| panic!("grant store for {}: {error:#}", cfg_path.display()));
        let tasks = crate::tasks::Tasks::load(&cfg_path)
            .unwrap_or_else(|e| panic!("task store {}: {e:#}", cfg_path.display()));
        let title_cache = crate::title::SummaryCache::load(crate::title::cache_path(&cfg_path));
        let template_path = crate::session::AgentTemplateStore::path_for(&cfg_path);
        let templates = crate::session::AgentTemplateStore::load(&template_path)
            .await
            .unwrap_or_else(|e| panic!("agent template store {}: {e:#}", template_path.display()));
        let activity_cache =
            crate::activity::ActivityCache::load(crate::activity::cache_path(&cfg_path));
        // main logs catalogs before constructing the manager. Refresh them again here.
        // A definition can change between those reads.
        // Without this refresh, the watcher could record the current timestamp while the catalog still contains old data.
        // Leave the timestamp unset after a failed reload so the watcher retries.
        let jukebox_loaded = crate::jukebox::reload();
        let m = Arc::new(Self {
            tmux: Tmux::new(crate::config::tmux_socket()),
            cfg_path,
            endpoint_path: crate::endpoint::path(),
            rules: RwLock::new(compile_rules(&cfg)),
            rules_revision: AtomicU64::new(0),
            live: RwLock::new(HashMap::new()),
            temp: RwLock::new(HashMap::new()),
            cfg: RwLock::new(cfg),
            templates: RwLock::new(templates),
            config_state: ConfigState::new(
                mtime,
                library_mtime,
                presets_loaded.then_some(presets_mtime).flatten(),
                jukebox_loaded.then_some(jukebox_mtime).flatten(),
            ),
            host_metadata_checked: AtomicU64::new(0),
            host_metadata_poll: tokio::sync::Mutex::new(None),
            signals: Signals::new(),
            scroll_cache: Mutex::new(HashMap::new()),
            activity_cache,
            audio: crate::audio::Audio::new(),
            music_transition: tokio::sync::Mutex::new(()),
            ncspot: tokio::sync::Mutex::new(Default::default()),
            events,
            auth_generation: AtomicU64::new(0),
            auth_changes,
            grants: RwLock::new(grants),
            session_boundary: tokio::sync::Mutex::new(()),
            template_mutation: tokio::sync::Mutex::new(()),
            tasks: crate::session::manager::TaskStore::new(tasks),
            worker_spawn: tokio::sync::Mutex::new(()),
            worktree_mutation: tokio::sync::Mutex::new(()),
            title_cache,
        });
        if let Ok(n) = crate::sandbox::purge_trash() {
            if n > 0 {
                tracing::info!("purged {n} expired private-state trash entries");
            }
        }
        if let Err(error) = m.recover_worktrees().await {
            tracing::error!("Worktree recovery failed. Records remain: {error:#}");
        }
        m.tmux.ensure_server().await;
        m.sync_from_config().await;
        let (state_ids, host_sessions) = {
            let live = m.live.read().await;
            let mut state_ids = HashMap::new();
            let mut host_sessions = std::collections::HashSet::new();
            for (name, session) in live.iter() {
                if session.host {
                    host_sessions.insert(name.clone());
                } else {
                    state_ids.insert(name.clone(), session.cfg.state_id.clone());
                }
            }
            (state_ids, host_sessions)
        };
        if let Err(error) = m
            .grants
            .write()
            .await
            .prune_stale(&state_ids, &host_sessions)
        {
            tracing::error!("could not prune stale persisted grants: {error:#}");
        }
        if let Ok(root) = super::ncspot::runtime() {
            m.recover_ncspot(&root).await;
        }
        m
    }

    async fn persist_cfg(&self, cfg: &Config) -> Result<()> {
        cfg.save(&self.cfg_path).await?;
        self.mark_cfg_mtime().await;
        Ok(())
    }

    async fn persist_cfg_text(&self, text: &str) -> Result<()> {
        Config::save_text(&self.cfg_path, text).await?;
        self.mark_cfg_mtime().await;
        Ok(())
    }

    async fn mark_cfg_mtime(&self) {
        *self.config_state.cfg_mtime.lock().unwrap() = disk_mtime(&self.cfg_path).await;
        *self.config_state.library_mtime.lock().unwrap() =
            Config::library_stamp_for(&self.cfg_path);
    }

    async fn publish_config(&self, change: ConfigChange, origin: ConfigOrigin) -> ConfigEffects {
        let endpoint_token = change.new.daemon.token.clone();

        let old = self.config().await;
        for session in &old.sessions {
            if !change
                .new
                .session(&session.name)
                .is_some_and(|next| next.state_id == session.state_id)
            {
                self.invalidate_session(&session.name).await;
            }
        }

        let mut rules = self.rules.write().await;
        *rules = change.rules;
        self.rules_revision
            .fetch_add(1, std::sync::atomic::Ordering::AcqRel);
        drop(rules);
        *self.cfg.write().await = change.new;
        if let ConfigOrigin::DiskReload { mtime } = origin {
            // Do not consume a disk stamp until the accepted contents are visible in memory.
            *self.config_state.cfg_mtime.lock().unwrap() = mtime;
        }
        if change.root_token_changed {
            self.invalidate_auth(crate::session::AuthChange::RootTokenChanged);
        }
        self.signals.maintenance_wake.notify_waiters();

        origin.effects(endpoint_token)
    }

    async fn update_endpoint(&self, token: &str) {
        if let Err(e) = crate::endpoint::update_token(&self.endpoint_path, token).await {
            tracing::warn!("The daemon accepted the config but could not update the endpoint descriptor: {e:#}");
        }
    }

    async fn finish_config_change(self: &Arc<Self>, effects: ConfigEffects) {
        if effects.reconcile {
            self.sync_from_config().await;
        }
        if effects.announce_sessions {
            self.announce_sessions().await;
        }
        if effects.announce_projects_and_library {
            self.announce_projects().await;
            self.announce_library().await;
        }
    }

    /// Apply a configuration change to a private snapshot. Save it without holding cfg, then publish it.
    /// The second lock serializes snapshots to prevent concurrent requests from overwriting each other during filesystem writes.
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
        self.session_operation(self.update_cfg_if_changed_within_boundary(update))
            .await
    }

    async fn update_cfg_if_changed_within_boundary<T>(
        &self,
        update: impl FnOnce(&mut Config) -> Result<(T, bool)>,
    ) -> Result<T> {
        let (result, change) = self.prepare_cfg_change(update).await?;
        let Some(change) = change else {
            return Ok(result);
        };
        self.commit_prepared_cfg(change).await?;
        Ok(result)
    }

    pub(super) async fn prepare_cfg_change<T>(
        &self,
        update: impl FnOnce(&mut Config) -> Result<(T, bool)>,
    ) -> Result<(T, Option<PreparedConfigChange>)> {
        let persist = self.config_state.persist.clone().lock_owned().await;
        let old = self.cfg.read().await.clone();
        let mut candidate = old.clone();
        let (result, changed) = update(&mut candidate)?;
        if !changed {
            return Ok((result, None));
        }

        self.validate_worktree_config(&old, &candidate).await?;
        let change = prepare_candidate(&old, candidate)?;
        Ok((
            result,
            Some(PreparedConfigChange {
                change,
                _persist: persist,
            }),
        ))
    }

    pub(super) async fn commit_prepared_cfg(&self, prepared: PreparedConfigChange) -> Result<()> {
        let PreparedConfigChange { change, _persist } = prepared;
        self.persist_cfg(&change.new).await?;
        let effects = self
            .publish_config(change, ConfigOrigin::StructuredMutation)
            .await;
        // Update the endpoint descriptor while holding the same lock as the configuration write.
        // Otherwise, token changes could finish out of order and give the mod a token that differs from the published configuration.
        self.update_endpoint(&effects.endpoint_token).await;
        drop(_persist);
        debug_assert!(!effects.reconcile);
        Ok(())
    }

    pub async fn reload_if_changed(self: &Arc<Self>) -> bool {
        if self.session_request_active() {
            return false;
        }
        self.session_operation(self.reload_if_changed_within_boundary())
            .await
    }

    async fn reload_if_changed_within_boundary(self: &Arc<Self>) -> bool {
        // Keep the disk read and in-memory replacement together with config saves. The lock is
        // deliberately not held while the rest of synchronization runs below.
        let persist = self.config_state.persist.lock().await;
        let disk = disk_mtime(&self.cfg_path).await;
        let library_disk = Config::library_stamp_for(&self.cfg_path);
        {
            let cfg_seen = self.config_state.cfg_mtime.lock().unwrap();
            let library_seen = self.config_state.library_mtime.lock().unwrap();
            if *cfg_seen == disk && *library_seen == library_disk {
                return false;
            }
        }

        let parsed = match Config::load(&self.cfg_path).await {
            Ok(cfg) => cfg,
            Err(e) => {
                tracing::warn!("config changed on disk but is invalid, keeping the old one: {e:#}");
                return false;
            }
        };
        let old = self.cfg.read().await.clone();
        if let Err(error) = self.validate_worktree_config(&old, &parsed).await {
            tracing::warn!("config worktree validation failed: {error:#}");
            return false;
        }
        let change = match prepare_candidate(&old, parsed) {
            Ok(change) => change,
            Err(e) => {
                tracing::warn!("config changed on disk but is invalid, keeping the old one: {e:#}");
                return false;
            }
        };
        tracing::info!("config changed on disk, reloading");
        let effects = self
            .publish_config(change, ConfigOrigin::DiskReload { mtime: disk })
            .await;
        *self.config_state.library_mtime.lock().unwrap() = library_disk;
        self.update_endpoint(&effects.endpoint_token).await;
        drop(persist);
        self.finish_config_change(effects).await;
        true
    }

    pub(super) async fn reload_if_due(self: &Arc<Self>) {
        let now = now_ms();
        if claim_due(&self.config_state.config_checked, now, CFG_CHECK_MS) {
            self.reload_if_changed().await;
        }
        if claim_due(&self.config_state.presets_checked, now, PRESETS_CHECK_MS) {
            self.reload_presets_if_changed().await;
        }
        if claim_due(&self.config_state.jukebox_checked, now, JUKEBOX_CHECK_MS) {
            self.reload_jukebox_if_changed().await;
        }
    }

    /// Return the next maintenance deadline as a monotonic duration.
    /// Stored activity uses epoch milliseconds to support restarts. Only this conversion uses wall time.
    pub(crate) async fn maintenance_delay(&self) -> Duration {
        let now = now_ms();
        let mut deadline = next_periodic_deadline(
            self.config_state.config_checked.load(Ordering::Acquire),
            CFG_CHECK_MS,
            now,
        );
        deadline = deadline.min(next_periodic_deadline(
            self.config_state.presets_checked.load(Ordering::Acquire),
            PRESETS_CHECK_MS,
            now,
        ));
        deadline = deadline.min(next_periodic_deadline(
            self.config_state.jukebox_checked.load(Ordering::Acquire),
            JUKEBOX_CHECK_MS,
            now,
        ));

        let rules_revision = self.rules_revision.load(Ordering::Acquire);
        let live = self.live.read().await;
        if live.values().any(|l| l.host && l.state != State::Down) {
            deadline = deadline.min(next_periodic_deadline(
                self.host_metadata_checked.load(Ordering::Acquire),
                HOST_METADATA_POLL_MS,
                now,
            ));
        }
        for l in live.values().filter(|l| l.state != State::Down) {
            if l.screen.is_none() {
                continue;
            }
            let cache_current = l.rule_cache.as_ref().is_some_and(|cache| {
                cache.revision == rules_revision && cache.text.as_ref() == l.plain.as_ref()
            });
            if l.seq != l.retick_seq || !cache_current {
                deadline = 0;
                break;
            }
            if matches!(l.state, State::Working | State::Waiting)
                && !l.rule_cache.as_ref().is_some_and(|cache| {
                    matches!(cache.matched, Some(State::Waiting | State::Idle))
                })
            {
                deadline = deadline.min(l.last_change.saturating_add(IDLE_MS));
            }
        }
        Duration::from_millis(deadline.saturating_sub(now))
    }

    pub(crate) fn maintenance_wake(&self) -> Arc<tokio::sync::Notify> {
        self.signals.maintenance_wake.clone()
    }

    pub async fn reload_presets_if_changed(self: &Arc<Self>) -> bool {
        let disk = crate::presets::Table::stamp();
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
        self.announce_sessions().await;
        self.signals.maintenance_wake.notify_waiters();
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
        self.signals.maintenance_wake.notify_waiters();
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
        self.signals.watchers_changed.send_replace(());
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
        self.session_operation(self.sync_from_config_within_boundary())
            .await
    }

    async fn sync_from_config_within_boundary(self: &Arc<Self>) {
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

    /// Request a repaint from every live tmux pane after a sidebar layout change.
    /// Run separately from the WebSocket handler because each repaint waits between temporarily shrinking and restoring the pane.
    /// The session table includes viewers and editors as well as agents.
    pub fn request_redraw(self: &Arc<Self>, shape: Option<(u16, u16)>) {
        let shape = shape.map(|(cols, rows)| {
            (
                cols.clamp(
                    crate::shared::protocol::TERMINAL_MIN_COLS,
                    crate::shared::protocol::TERMINAL_MAX_COLS,
                ),
                rows.clamp(
                    crate::shared::protocol::TERMINAL_MIN_ROWS,
                    crate::shared::protocol::TERMINAL_MAX_ROWS,
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
        // A capture cannot restore terminal modes. SIGWINCH makes terminal applications set them again.
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
        self.session_operation(self.patch_config_within_boundary(patch))
            .await
    }

    async fn patch_config_within_boundary(self: &Arc<Self>, patch: Value) -> Result<()> {
        self.reload_if_changed().await;

        let persist = self.config_state.persist.lock().await;
        let text = tokio::fs::read_to_string(&self.cfg_path)
            .await
            .with_context(|| format!("reading {}", self.cfg_path.display()))?;
        let mut document: toml::Value = toml::from_str(&text).context("parsing config.toml")?;
        let patch = json_to_toml(patch)?;
        if !patch.is_table() {
            bail!("config patch must be a JSON object");
        }
        merge_toml(&mut document, patch);

        let old = self.cfg.read().await.clone();
        let (mut parsed, mut document) =
            Config::parse_document(&toml::to_string_pretty(&document)?)?;
        parsed.library = old.library.clone();
        if let Some(table) = document.as_table_mut() {
            table.remove("library");
        }
        if parsed.daemon.token == crate::config::TOKEN_REDACTED {
            restore_redacted_document_token(&old, &mut document);
        }
        self.validate_worktree_config(&old, &parsed).await?;
        let change = prepare_candidate(&old, parsed)?;

        let text = toml::to_string_pretty(&document)?;
        self.persist_cfg_text(&text).await?;
        let effects = self.publish_config(change, ConfigOrigin::FileUpdate).await;
        self.update_endpoint(&effects.endpoint_token).await;
        drop(persist);
        self.finish_config_change(effects).await;
        Ok(())
    }

    pub async fn replace_config(self: &Arc<Self>, text: &str) -> Result<()> {
        self.session_operation(self.replace_config_within_boundary(text))
            .await
    }

    async fn replace_config_within_boundary(self: &Arc<Self>, text: &str) -> Result<()> {
        let persist = self.config_state.persist.lock().await;
        let old = self.cfg.read().await.clone();
        let (mut parsed, mut document) = Config::parse_document(text)?;
        parsed.library = old.library.clone();
        if let Some(table) = document.as_table_mut() {
            table.remove("library");
        }
        if parsed.daemon.token == crate::config::TOKEN_REDACTED {
            restore_redacted_document_token(&old, &mut document);
        }
        self.validate_worktree_config(&old, &parsed).await?;
        let change = prepare_candidate(&old, parsed)?;
        self.persist_cfg_text(&toml::to_string_pretty(&document)?)
            .await?;
        let effects = self.publish_config(change, ConfigOrigin::FileUpdate).await;
        self.update_endpoint(&effects.endpoint_token).await;
        drop(persist);
        self.finish_config_change(effects).await;
        Ok(())
    }
}

#[cfg(test)]
#[path = "config_tests.rs"]
mod tests;
