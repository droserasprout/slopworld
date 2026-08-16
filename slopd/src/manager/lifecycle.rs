//! Manager construction, configuration, state transitions and session lifecycle.

use super::super::*;
use std::process::Stdio;

use crate::sandbox::build_argv;
use anyhow::anyhow;
use tokio::process::Command;

impl Live {
    fn new(cfg: SessionCfg, title: TitleCapture) -> Self {
        Self {
            cfg,
            ephemeral: false,
            host: false,
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
            input: None,
            breadcrumbs: Vec::new(),
            breadcrumbs_pending: false,
            title,
        }
    }
}

impl Manager {
    pub async fn new(cfg: Config, cfg_path: PathBuf) -> Arc<Self> {
        let (events, _) = broadcast::channel(256);
        let mtime = disk_mtime(&cfg_path);
        let tasks = crate::tasks::Tasks::load(&cfg_path)
            .unwrap_or_else(|e| panic!("task store {}: {e:#}", cfg_path.display()));
        let title_cache = crate::title::SummaryCache::load(crate::title::cache_path(&cfg_path));
        let m = Arc::new(Self {
            tmux: Tmux::new(crate::config::TMUX_SOCKET),
            cfg_path,
            rules: RwLock::new(compile_rules(&cfg)),
            live: RwLock::new(HashMap::new()),
            temp: RwLock::new(HashMap::new()),
            cfg: RwLock::new(cfg),
            cfg_mtime: Mutex::new(mtime),
            presets_mtime: Mutex::new(crate::presets::dir_stamp()),
            jukebox_mtime: Mutex::new(crate::jukebox::dir_stamp()),
            cfg_checked: AtomicU64::new(0),
            usage: RwLock::new(crate::usage::Snapshot::default()),
            clients: AtomicUsize::new(0),
            clients_since: AtomicU64::new(0),
            watchers: Mutex::new(HashMap::new()),
            scroll_cache: Mutex::new(HashMap::new()),
            audio: crate::audio::Audio::new(),
            events,
            grants: RwLock::new(crate::grant::Grants::default()),
            tasks: Mutex::new(tasks),
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

    pub(super) fn save_cfg(&self, cfg: &Config) -> Result<()> {
        cfg.save(&self.cfg_path)?;
        *self.cfg_mtime.lock().unwrap() = disk_mtime(&self.cfg_path);
        if let Err(e) = crate::endpoint::update_token(&cfg.daemon.token) {
            tracing::warn!("config saved but endpoint descriptor was not updated: {e:#}");
        }
        Ok(())
    }

    pub(super) fn save_cfg_text(&self, cfg: &Config, text: &str) -> Result<()> {
        Config::save_text(&self.cfg_path, text)?;
        *self.cfg_mtime.lock().unwrap() = disk_mtime(&self.cfg_path);
        if let Err(e) = crate::endpoint::update_token(&cfg.daemon.token) {
            tracing::warn!("config saved but endpoint descriptor was not updated: {e:#}");
        }
        Ok(())
    }

    pub async fn reload_if_changed(self: &Arc<Self>) -> bool {
        let disk = disk_mtime(&self.cfg_path);
        {
            let mut seen = self.cfg_mtime.lock().unwrap();
            if *seen == disk {
                return false;
            }
            *seen = disk;
        }

        let text = match std::fs::read_to_string(&self.cfg_path) {
            Ok(t) => t,
            Err(e) => {
                tracing::warn!("config changed on disk but is unreadable: {e:#}");
                return false;
            }
        };
        let new = match Config::parse(&text) {
            Ok(c) => c,
            Err(e) => {
                tracing::warn!(
                    "config changed on disk but does not parse, keeping the old one: {e:#}"
                );
                return false;
            }
        };

        tracing::info!("config changed on disk, reloading");
        *self.rules.write().await = compile_rules(&new);
        *self.cfg.write().await = new;
        if let Err(e) = crate::endpoint::update_token(&self.cfg.read().await.daemon.token) {
            tracing::warn!("config reloaded but endpoint descriptor was not updated: {e:#}");
        }
        self.sync_from_config().await;
        let _ = self.events.send(Event::Sessions {
            sessions: self.views().await,
        });
        self.announce_projects().await;
        self.announce_shortcuts().await;
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

    pub fn prune_tasks(&self, who: &str, all: bool) -> Result<usize> {
        self.tasks.lock().unwrap().prune(who, all)
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
        let titles_changed = self.reconcile_title_settings(&cfg).await;
        self.autostart(&cfg).await;

        let adopted = self.adopt_orphans(&cfg).await;
        if titles_changed || adopted {
            let _ = self.events.send(Event::Sessions {
                sessions: self.views().await,
            });
        }
    }

    async fn prune_removed(&self, cfg: &Config) -> Vec<String> {
        // Dropping a `Live` only *detaches* its reader: `JoinHandle`'s own `Drop` lets the
        // task run on, and it would keep a control-mode attach open against a session
        // nothing points at any more. Every other removal path aborts, so this one does too.
        let mut live = self.live.write().await;
        let mut removed = Vec::new();
        live.retain(|name, l| {
            if l.ephemeral || cfg.session(name).is_some() {
                return true;
            }
            tracing::info!("agent {name} is gone from the config, ending its reader");
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

    async fn autostart(self: &Arc<Self>, cfg: &Config) {
        for s in cfg.sessions.iter().filter(|s| s.autostart) {
            if !self.tmux.exists(&s.name).await {
                if let Err(e) = self.start(&s.name).await {
                    tracing::error!("autostart {}: {e:#}", s.name);
                }
            }
        }
    }

    async fn adopt_orphans(self: &Arc<Self>, cfg: &Config) -> bool {
        let mut adopted = false;
        for name in self.tmux.list().await {
            let host = self.tmux.is_host(&name).await || Self::legacy_host_session(&name, cfg);
            let needs_size = {
                let mut live = self.live.write().await;
                if !live.contains_key(&name) {
                    tracing::info!(
                        "adopting tmux session {name} as a temporary {}",
                        if host { "host terminal" } else { "agent" }
                    );
                    let now = now_ms();
                    let mut l = Live::new(
                        SessionCfg {
                            name: name.clone(),
                            ..Default::default()
                        },
                        TitleCapture::default(),
                    );
                    l.ephemeral = true;
                    l.host = host;
                    l.state = State::Working;
                    l.last_change = now;
                    l.state_since = now;
                    live.insert(name.clone(), l);
                    adopted = true;
                }
                live.get(&name).is_some_and(|l| l.emu.is_none())
            };

            // An established reader already knows its dimensions. Query tmux only for a
            // newly adopted or reader-less session, and do not hold the live-table lock
            // across the subprocess await.
            if needs_size {
                self.refresh_readerless_size(&name).await;
            }
            if self.spawn_reader(&name).await {
                let m = self.clone();
                let name = name.clone();
                tokio::spawn(async move { m.nudge_redraw(&name).await });
            }
        }
        adopted
    }

    /// Host markers were added after host terminals could outlive the daemon. Recognize the
    /// deterministic old names once so a pre-marker host does not get one agent-policy prompt
    /// sent to the summarizer before it is closed and recreated.
    fn legacy_host_session(name: &str, cfg: &Config) -> bool {
        crate::sandbox::host_session_name("") == name
            || cfg
                .projects
                .iter()
                .any(|p| crate::sandbox::host_session_name(&p.name) == name)
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

    pub(super) async fn session_cfg(&self, name: &str) -> Option<SessionCfg> {
        if let Some(s) = self.cfg.read().await.session(name) {
            return Some(s.clone());
        }
        self.live.read().await.get(name).map(|l| l.cfg.clone())
    }

    pub(super) async fn project_for(&self, cfg: &Config, s: &SessionCfg) -> Option<ProjectCfg> {
        if let Some(p) = cfg.project_of(s) {
            return Some(p.clone());
        }
        self.temp.read().await.get(&s.project).cloned()
    }

    async fn resolve_target(&self, cfg: &Config, name: &str) -> Result<(SessionCfg, ProjectCfg)> {
        let s = match cfg.session(name) {
            Some(s) => s.clone(),
            None => self
                .session_cfg(name)
                .await
                .ok_or_else(|| anyhow!("no such session: {name}"))?,
        };
        let p = self.project_for(cfg, &s).await.ok_or_else(|| {
            if s.project.is_empty() {
                anyhow!("session {name} belongs to no project")
            } else {
                anyhow!(
                    "session {name} belongs to project {}, which does not exist",
                    s.project
                )
            }
        })?;
        Ok((s, p))
    }

    fn validate_dir(p: &ProjectCfg) -> Result<String> {
        let dir = expand(&p.dir);
        if p.temp {
            std::fs::create_dir_all(&dir).with_context(|| format!("making {dir}"))?;
        }
        if !std::path::Path::new(&dir).is_dir() {
            bail!("{dir} is not a directory");
        }
        if let Some(what) = crate::sandbox::refused(&dir) {
            bail!("project {} cannot live at {dir}: it reaches {what}", p.name);
        }
        Ok(dir)
    }

    async fn wire_live_state(
        &self,
        name: &str,
        cfg: &Config,
        s: &SessionCfg,
        p: &ProjectCfg,
        host: bool,
    ) {
        let command = cfg.command_of(s);
        let directory = expand(&p.dir);
        let vars = TemplateVars {
            agent: &s.name,
            project: &p.name,
            directory: &directory,
            command: &command,
        };
        let crumbs: Vec<String> = cfg
            .breadcrumbs_of(s, p)
            .into_iter()
            .map(|text| render_template_with(&text, &[], Some(&vars)))
            .collect();
        let restored_title = title_settings(cfg, s, host)
            .filter(|(policy, _)| *policy != TitlePolicy::Never)
            .and(self.title_cache.latest(name));
        let announced_title = restored_title.is_some();
        let mut live = self.live.write().await;
        if let Some(l) = live.get_mut(name) {
            l.title = TitleCapture::default();
            l.title.override_title = restored_title;
            l.title.once_requested = l.title.override_title.is_some();
            l.breadcrumbs.clear();
            l.breadcrumbs_pending = false;
            if !host && s.breadcrumb_yolo && !crumbs.is_empty() {
                l.breadcrumbs = breadcrumb_block(&crumbs).into_bytes();
                l.breadcrumbs_pending = true;
            }
        }
        drop(live);
        if announced_title {
            let _ = self.events.send(Event::Sessions {
                sessions: self.views().await,
            });
        }
    }

    pub async fn start(self: &Arc<Self>, name: &str) -> Result<()> {
        self.ensure_state_id(name).await?;
        let cfg = self.config().await;
        let (s, p) = self.resolve_target(&cfg, name).await?;
        cfg.network_of(&s, &p)?;

        if self.tmux.exists(name).await {
            bail!("session {name} is already running");
        }
        if cfg.command_of(&s).trim().is_empty() {
            bail!(
                "session {name} names command preset {:?}, which has no file",
                s.command
            );
        }
        let dir = Self::validate_dir(&p)?;

        let (cols, rows, host) = match self.live.read().await.get(name) {
            Some(l) => (l.cols, l.rows, l.host),
            None => (BOOT_COLS, BOOT_ROWS, false),
        };
        let argv = if host {
            crate::sandbox::host_argv(&cfg, &s, &p)
        } else {
            crate::sandbox::prepare_network(&cfg, &s, &p)?;
            build_argv(&cfg, &s, &p)?
        };
        tracing::info!("starting {name}: {}", argv.join(" "));
        self.tmux.spawn(name, &dir, cols, rows, &argv, host).await?;
        self.spawn_reader(name).await;
        self.wire_live_state(name, &cfg, &s, &p, host).await;
        Ok(())
    }

    pub async fn stop(self: &Arc<Self>, name: &str) -> Result<()> {
        if self.tmux.exists(name).await {
            self.tmux.kill(name).await?;
        }
        if self.is_ephemeral(name).await {
            self.forget(name).await;
            return Ok(());
        }
        if let Some(l) = self.live.write().await.get_mut(name) {
            l.set_state(State::Down);
            l.screen = None;
            l.emu = None;
            // A stopped run must not leave its generated task title on the downed
            // session. Resetting the capture also makes late title responses from
            // this run stale before the next start.
            l.title = TitleCapture::default();
            if let Some(h) = l.reader.take() {
                h.abort();
            }
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
        Ok(())
    }

    pub(super) async fn is_ephemeral(&self, name: &str) -> bool {
        self.live
            .read()
            .await
            .get(name)
            .map(|l| l.ephemeral)
            .unwrap_or(false)
    }

    pub(super) async fn forget(self: &Arc<Self>, name: &str) {
        let (handle, project, session) = {
            let mut live = self.live.write().await;
            match live.remove(name) {
                Some(mut l) => (l.reader.take(), l.cfg.project.clone(), l.cfg),
                None => return,
            }
        };
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
        if let Err(e) = crate::sandbox::remove_ephemeral_state(&session) {
            tracing::warn!("removing temporary private state for {name}: {e:#}");
        }
        self.temp.write().await.remove(&project);
        self.grants.write().await.revoke_grantor(name);
        let _ = self.events.send(Event::Sessions {
            sessions: self.views().await,
        });
        if let Some(h) = handle {
            h.abort();
        }
    }

    pub async fn restart(self: &Arc<Self>, name: &str) -> Result<()> {
        self.stop(name).await?;
        self.start(name).await
    }

    pub async fn projects(&self) -> Vec<ProjectCfg> {
        self.cfg.read().await.projects.clone()
    }

    pub(super) async fn announce_projects(&self) {
        let _ = self.events.send(Event::Projects {
            projects: self.projects().await,
        });
    }

    pub async fn add_project(self: &Arc<Self>, mut p: ProjectCfg) -> Result<()> {
        self.reload_if_changed().await;
        settle(&mut p);
        check_project(&p)?;
        let mut cfg = self.cfg.write().await;
        check_breadcrumbs(&cfg, &p.breadcrumbs)?;
        if cfg.project(&p.name).is_some() {
            bail!("project {} already exists", p.name);
        }
        cfg.projects.push(p);
        self.save_cfg(&cfg)?;
        drop(cfg);
        self.announce_projects().await;
        Ok(())
    }

    pub async fn update_project(self: &Arc<Self>, name: &str, mut p: ProjectCfg) -> Result<()> {
        self.reload_if_changed().await;
        settle(&mut p);
        check_project(&p)?;

        let mut cfg = self.cfg.write().await;
        check_breadcrumbs(&cfg, &p.breadcrumbs)?;
        let idx = cfg
            .projects
            .iter()
            .position(|x| x.name == name)
            .ok_or_else(|| anyhow!("no such project: {name}"))?;
        if p.name != name && cfg.project(&p.name).is_some() {
            bail!("project {} already exists", p.name);
        }
        for s in cfg.sessions.iter().filter(|s| s.project == name) {
            cfg.network_of(s, &p).with_context(|| {
                format!(
                    "project {} cannot lower its network ceiling below agent {}",
                    p.name, s.name
                )
            })?;
        }

        let renamed = p.name.clone();
        cfg.projects[idx] = p;
        if renamed != name {
            for s in cfg.sessions.iter_mut().filter(|s| s.project == name) {
                s.project = renamed.clone();
            }
        }
        self.save_cfg(&cfg)?;
        drop(cfg);

        self.announce_projects().await;
        let _ = self.events.send(Event::Sessions {
            sessions: self.views().await,
        });
        Ok(())
    }

    pub async fn remove_project(self: &Arc<Self>, name: &str) -> Result<()> {
        self.reload_if_changed().await;
        let mut cfg = self.cfg.write().await;
        if cfg.project(name).is_none() {
            bail!("no such project: {name}");
        }
        let users: Vec<&str> = cfg
            .sessions
            .iter()
            .filter(|s| s.project == name)
            .map(|s| s.name.as_str())
            .collect();
        if !users.is_empty() {
            bail!(
                "project {name} still has agents in it: {}. Remove or move them first.",
                users.join(", ")
            );
        }
        cfg.projects.retain(|p| p.name != name);
        self.save_cfg(&cfg)?;
        drop(cfg);
        self.announce_projects().await;
        Ok(())
    }

    pub async fn shortcuts(&self) -> Vec<ShortcutCfg> {
        self.cfg.read().await.shortcuts_all()
    }

    pub(super) async fn announce_shortcuts(&self) {
        let _ = self.events.send(Event::Shortcuts {
            shortcuts: self.shortcuts().await,
        });
    }

    pub async fn add_shortcut(self: &Arc<Self>, mut sc: ShortcutCfg) -> Result<()> {
        self.reload_if_changed().await;
        let mut cfg = self.cfg.write().await;
        sc.builtin = false;
        check_shortcut(&cfg, &sc)?;
        if cfg
            .shortcuts
            .iter()
            .any(|existing| existing.name == sc.name)
        {
            bail!("shortcut {} already exists", sc.name);
        }
        let attach_project = (sc.kind == ShortcutKind::Breadcrumb && !sc.project.trim().is_empty())
            .then(|| sc.project.clone());
        cfg.shortcuts.push(sc.clone());
        if let Some(project) = attach_project {
            if let Some(p) = cfg.projects.iter_mut().find(|p| p.name == project) {
                if !p.breadcrumbs.contains(&sc.name) {
                    p.breadcrumbs.push(sc.name.clone());
                }
            }
        }
        self.save_cfg(&cfg)?;
        drop(cfg);
        self.announce_shortcuts().await;
        Ok(())
    }

    pub async fn update_shortcut(self: &Arc<Self>, name: &str, mut sc: ShortcutCfg) -> Result<()> {
        self.reload_if_changed().await;
        let mut cfg = self.cfg.write().await;
        sc.builtin = false;
        if cfg.is_builtin_shortcut(name) {
            bail!("shortcut {name} is built in and cannot be edited");
        }
        check_shortcut(&cfg, &sc)?;
        let idx = cfg
            .shortcuts
            .iter()
            .position(|x| x.name == name)
            .ok_or_else(|| anyhow!("no such shortcut: {name}"))?;
        if sc.name != name
            && cfg
                .shortcuts
                .iter()
                .any(|existing| existing.name == sc.name)
        {
            bail!("shortcut {} already exists", sc.name);
        }
        let old = cfg.shortcuts[idx].clone();
        if sc.name != name {
            for p in &mut cfg.projects {
                for attached in &mut p.breadcrumbs {
                    if attached == name {
                        *attached = sc.name.clone();
                    }
                }
            }
            for s in &mut cfg.sessions {
                for attached in &mut s.breadcrumbs {
                    if attached == name {
                        *attached = sc.name.clone();
                    }
                }
            }
        }
        cfg.shortcuts[idx] = sc.clone();
        if sc.kind == ShortcutKind::Breadcrumb {
            if !old.project.trim().is_empty() && old.project != sc.project {
                if let Some(p) = cfg.projects.iter_mut().find(|p| p.name == old.project) {
                    p.breadcrumbs.retain(|b| b != &sc.name);
                }
            }
            if !sc.project.trim().is_empty() {
                if let Some(p) = cfg.projects.iter_mut().find(|p| p.name == sc.project) {
                    if !p.breadcrumbs.contains(&sc.name) {
                        p.breadcrumbs.push(sc.name.clone());
                    }
                }
            }
        } else {
            for p in &mut cfg.projects {
                p.breadcrumbs.retain(|b| b != name && b != &sc.name);
            }
            for s in &mut cfg.sessions {
                s.breadcrumbs.retain(|b| b != name && b != &sc.name);
            }
        }
        self.save_cfg(&cfg)?;
        drop(cfg);
        self.announce_projects().await;
        self.announce_shortcuts().await;
        Ok(())
    }

    pub async fn remove_shortcut(self: &Arc<Self>, name: &str) -> Result<()> {
        self.reload_if_changed().await;
        let mut cfg = self.cfg.write().await;
        if cfg.is_builtin_shortcut(name) {
            bail!("shortcut {name} is built in and cannot be deleted");
        }
        let Some(sc) = cfg.shortcut(name) else {
            bail!("no such shortcut: {name}");
        };
        if sc.kind == ShortcutKind::Breadcrumb
            && (cfg
                .projects
                .iter()
                .any(|p| p.breadcrumbs.iter().any(|b| b == name))
                || cfg
                    .sessions
                    .iter()
                    .any(|s| s.breadcrumbs.iter().any(|b| b == name)))
        {
            bail!("breadcrumb {name} is still attached to a project or agent");
        }
        cfg.shortcuts.retain(|s| s.name != name);
        self.save_cfg(&cfg)?;
        drop(cfg);
        self.announce_shortcuts().await;
        Ok(())
    }

    pub async fn run_shortcut(self: &Arc<Self>, name: &str, want: RunWhere) -> Result<String> {
        self.reload_if_changed().await;
        let cfg = self.config().await;
        let sc = cfg
            .shortcut(name)
            .ok_or_else(|| anyhow!("no such shortcut: {name}"))?
            .clone();
        check_shortcut(&cfg, &sc)?;
        if matches!(sc.kind, ShortcutKind::Breadcrumb | ShortcutKind::FileAction) {
            bail!("shortcut {} is not runnable as an agent errand", sc.name);
        }
        drop(cfg);
        self.run_errand(sc, want, false).await
    }

    fn resolve_file_action(
        cfg: &Config,
        project: &str,
        raw_path: &str,
        command: &str,
    ) -> Result<(ProjectCfg, SessionCfg)> {
        let p = cfg
            .project(project.trim())
            .cloned()
            .ok_or_else(|| anyhow!("no such project: {project}"))?;
        let path = project_action_path(&p, raw_path)?;
        if command.trim().is_empty() {
            bail!("file action has no command");
        }
        let command = normalize_action_command(raw_path, &path, command);
        let s = SessionCfg {
            name: "file-action".into(),
            project: p.name.clone(),
            command: cfg.defaults.shell.clone(),
            cmd: Some(command),
            ..Default::default()
        };
        Ok((p, s))
    }

    async fn run_file_action_command(
        argv: &[String],
    ) -> Result<((Vec<u8>, bool), (Vec<u8>, bool), std::process::ExitStatus)> {
        let mut child = Command::new(&argv[0])
            .args(&argv[1..])
            .kill_on_drop(true)
            .stdin(Stdio::null())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .spawn()
            .context("starting file action")?;
        let stdout = child
            .stdout
            .take()
            .context("capturing file action stdout")?;
        let stderr = child
            .stderr
            .take()
            .context("capturing file action stderr")?;

        let collected = tokio::time::timeout(FILE_ACTION_TIMEOUT, async {
            let stdout = read_action_output(stdout);
            let stderr = read_action_output(stderr);
            let status = child.wait();
            let (stdout, stderr, status) = tokio::join!(stdout, stderr, status);
            Ok::<_, anyhow::Error>((stdout?, stderr?, status?))
        })
        .await;

        match collected {
            Ok(result) => result,
            Err(_) => {
                let _ = child.kill().await;
                let _ = child.wait().await;
                bail!("file action timed out")
            }
        }
    }

    fn format_file_action_result(
        stdout: Vec<u8>,
        stderr: Vec<u8>,
        stdout_truncated: bool,
        stderr_truncated: bool,
        status: std::process::ExitStatus,
    ) -> Result<String> {
        let mut text = String::from_utf8_lossy(&stdout).into_owned();
        if !stderr.is_empty() {
            if !text.is_empty() && !text.ends_with('\n') {
                text.push('\n');
            }
            text.push_str(&String::from_utf8_lossy(&stderr));
        }
        if stdout_truncated || stderr_truncated {
            text.push_str("\n[output truncated]");
        }
        if !status.success() {
            bail!("file action failed: {}", text.trim());
        }
        Ok(if text.trim().is_empty() {
            "(no output)".into()
        } else {
            text.trim_end().into()
        })
    }

    async fn execute_file_action(cfg: &Config, s: &SessionCfg, p: &ProjectCfg) -> Result<String> {
        crate::sandbox::prepare_network(cfg, s, p)?;
        let argv = build_argv(cfg, s, p)?;
        let ((stdout, stdout_truncated), (stderr, stderr_truncated), status) =
            Self::run_file_action_command(&argv).await?;
        Self::format_file_action_result(stdout, stderr, stdout_truncated, stderr_truncated, status)
    }

    pub async fn file_action(
        self: &Arc<Self>,
        project: &str,
        raw_path: &str,
        command: &str,
    ) -> Result<String> {
        self.reload_if_changed().await;
        let cfg = self.config().await;
        let (p, s) = Self::resolve_file_action(&cfg, project, raw_path, command)?;
        let result = Self::execute_file_action(&cfg, &s, &p).await;

        if let Err(e) = crate::sandbox::remove_ephemeral_state(&s) {
            tracing::warn!("removing file action private state: {e:#}");
        }
        result
    }

    pub async fn file_action_command(
        self: &Arc<Self>,
        project: &str,
        raw_path: &str,
        command: &str,
    ) -> Result<String> {
        self.reload_if_changed().await;
        let cfg = self.config().await;
        let p = cfg
            .project(project.trim())
            .cloned()
            .ok_or_else(|| anyhow!("no such project: {project}"))?;
        let path = project_action_path(&p, raw_path)?;
        Ok(normalize_action_command(raw_path, &path, command))
    }

    fn validate_errand(sc: &ShortcutCfg) -> Result<()> {
        if matches!(sc.kind, ShortcutKind::Breadcrumb | ShortcutKind::FileAction) {
            bail!("shortcut {} is not runnable as an agent errand", sc.name);
        }
        Ok(())
    }

    async fn create_errand_session(
        &self,
        cfg: &Config,
        sc: &ShortcutCfg,
        want: &RunWhere,
        host: bool,
    ) -> Result<String> {
        let name = sc.name.as_str();
        let asked = match want.project.as_deref().map(str::trim) {
            Some(p) if !p.is_empty() => Some(p.to_string()),
            _ => None,
        };
        let fresh = want.temp || (asked.is_none() && sc.link == ShortcutLink::Temp);
        let named = match (&asked, sc.link) {
            _ if fresh => String::new(),
            (Some(p), _) => p.clone(),
            (None, ShortcutLink::Ask) => {
                bail!(
                    "shortcut {name} asks where to run; name a project or ask for a temporary one"
                )
            }
            (None, _) => sc.project.clone(),
        };
        if !fresh && cfg.project(&named).is_none() {
            bail!("no such project: {named}");
        }
        let template = cfg.project(&sc.project).cloned().unwrap_or_default();

        let mut live = self.live.write().await;
        let name = free_name(&live, cfg, &slug(&sc.name));
        check_name(&name)?;

        let project = if fresh {
            let mut temp = self.temp.write().await;
            let pname = free_project_name(cfg, &temp, &name);
            temp.insert(
                pname.clone(),
                ProjectCfg {
                    name: pname.clone(),
                    dir: crate::config::temp_dir(&pname),
                    temp: true,
                    ..template
                },
            );
            pname
        } else {
            named
        };

        let mut l = Live::new(
            cfg.session_for(sc, name.clone(), project),
            TitleCapture::default(),
        );
        l.ephemeral = true;
        l.host = host;
        live.insert(name.clone(), l);
        Ok(name)
    }

    fn queue_errand_delivery(self: &Arc<Self>, session: &str, sc: &ShortcutCfg, want: &RunWhere) {
        let text = render_template(&sc.text, &want.random_tips);
        if text.trim().is_empty() {
            return;
        }
        let m = self.clone();
        let target = session.to_string();
        let tips = want.random_tips.clone();
        tokio::spawn(async move { m.deliver(&target, &text, tips).await });
    }

    pub async fn run_errand(
        self: &Arc<Self>,
        sc: ShortcutCfg,
        want: RunWhere,
        host: bool,
    ) -> Result<String> {
        self.reload_if_changed().await;
        let cfg = self.config().await;
        Self::validate_errand(&sc)?;
        let session = self.create_errand_session(&cfg, &sc, &want, host).await?;

        if let Err(e) = self.start(&session).await {
            self.forget(&session).await;
            return Err(e);
        }

        let _ = self.events.send(Event::Sessions {
            sessions: self.views().await,
        });
        self.queue_errand_delivery(&session, &sc, &want);

        Ok(session)
    }

    pub(super) async fn deliver(
        self: &Arc<Self>,
        name: &str,
        text: &str,
        random_tips: Vec<String>,
    ) {
        match self.wait_ready(name).await {
            Ready::Gone => {
                tracing::warn!("{name} was gone before its shortcut text could be sent");
                return;
            }
            Ready::Timeout => tracing::warn!(
                "{name} never went quiet after {}ms; sending its shortcut text anyway",
                READY_MS
            ),
            Ready::Settled => {}
        }

        if let Err(e) = self.paste(name, text).await {
            tracing::error!("sending shortcut text to {name}: {e:#}");
            return;
        }
        tokio::time::sleep(Duration::from_millis(ENTER_GAP_MS)).await;
        self.send_keys(name, vec!["Enter".into()], false, random_tips)
            .await;
    }

    pub(super) async fn wait_ready(&self, name: &str) -> Ready {
        let deadline = now_ms() + READY_MS;
        let mut last_seq = u64::MAX;
        let mut still_since = 0u64;

        loop {
            let snap = {
                let live = self.live.read().await;
                live.get(name).map(|l| {
                    let printed = l
                        .screen
                        .as_ref()
                        .map(|s| s.lines.iter().any(|x| !strip_sgr(x).trim().is_empty()))
                        .unwrap_or(false);
                    (l.seq, printed, l.state)
                })
            };
            let Some((seq, printed, state)) = snap else {
                return Ready::Gone;
            };
            if state == State::Down {
                return Ready::Gone;
            }

            let now = now_ms();
            if !printed {
                still_since = 0;
            } else if seq != last_seq {
                last_seq = seq;
                still_since = now;
            } else if still_since > 0 && now.saturating_sub(still_since) >= SETTLE_MS {
                return Ready::Settled;
            }

            if now >= deadline {
                return Ready::Timeout;
            }
            tokio::time::sleep(Duration::from_millis(100)).await;
        }
    }

    pub async fn add(self: &Arc<Self>, mut s: SessionCfg) -> Result<()> {
        self.reload_if_changed().await;
        let mut cfg = self.cfg.write().await;
        if cfg.session(&s.name).is_some() {
            bail!("session {} already exists", s.name);
        }
        check_name(&s.name)?;
        check_belongs(&cfg, &s)?;
        s.limits.validate()?;
        // A client has no authority over which durable state an agent receives.  Always mint a
        // fresh key, including if a hand-written request carried a stale one.
        s.state_id = uuid::Uuid::new_v4().to_string();
        let autostart = s.autostart;
        let name = s.name.clone();
        cfg.sessions.push(s);
        self.save_cfg(&cfg)?;
        drop(cfg);

        self.sync_from_config().await;
        if autostart {
            self.start(&name).await.ok();
        }
        Ok(())
    }

    pub async fn update(self: &Arc<Self>, name: &str, mut s: SessionCfg) -> Result<()> {
        self.reload_if_changed().await;
        let renamed = s.name != name;
        if renamed {
            check_name(&s.name)?;
            let cfg = self.cfg.read().await;
            if cfg.session(name).is_none() {
                bail!("no such session: {name}");
            }
            if cfg.session(&s.name).is_some() {
                bail!("session {} already exists", s.name);
            }
            drop(cfg);
            if self.tmux.exists(name).await {
                self.tmux.rename(name, &s.name).await?;
            }
        }

        let mut cfg = self.cfg.write().await;
        check_belongs(&cfg, &s)?;
        s.limits.validate()?;
        let idx = cfg
            .sessions
            .iter()
            .position(|x| x.name == name)
            .ok_or_else(|| anyhow!("no such session: {name}"))?;
        // Keep private state with the agent across every edit, particularly a rename.  The wire
        // deliberately does not expose this field, but also must not be able to change it.
        s.state_id = cfg.sessions[idx].state_id.clone();
        cfg.sessions[idx] = s.clone();
        self.save_cfg(&cfg)?;
        drop(cfg);

        if renamed {
            self.readopt(name, &s.name).await;
        }
        self.sync_from_config().await;
        Ok(())
    }

    /// Set the optional manual sidebar label without making the edit dialog round-trip
    /// read-only session fields. A non-empty label also invalidates a title request already
    /// in flight; clearing it leaves any existing generated title in place and lets the next
    /// prompt use the configured title policy again.

    pub async fn set_label(self: &Arc<Self>, name: &str, label: String) -> Result<()> {
        self.reload_if_changed().await;
        let label = label.trim().to_string();
        if label.chars().count() > MAX_MANUAL_LABEL_CHARS {
            bail!("label must be at most {MAX_MANUAL_LABEL_CHARS} characters");
        }

        let saved = (!label.is_empty()).then_some(label.clone());
        if !self.is_ephemeral(name).await {
            let mut cfg = self.cfg.write().await;
            let session = cfg
                .sessions
                .iter_mut()
                .find(|session| session.name == name)
                .ok_or_else(|| anyhow!("no such session: {name}"))?;
            session.label = saved.clone();
            self.save_cfg(&cfg)?;
            drop(cfg);
        }

        {
            let mut live = self.live.write().await;
            let Some(live) = live.get_mut(name) else {
                bail!("no such session: {name}");
            };
            live.cfg.label = saved;
            live.title.generation = live.title.generation.wrapping_add(1);
            live.title.pending = false;
        }
        let _ = self.events.send(Event::Sessions {
            sessions: self.views().await,
        });
        Ok(())
    }

    pub(super) async fn readopt(self: &Arc<Self>, old: &str, new: &str) {
        // The control reader is attached by tmux name; renaming requires a fresh capture/emulator.
        let (running, title) = {
            let mut live = self.live.write().await;
            let mut l = match live.remove(old) {
                Some(l) => l,
                None => return,
            };
            if let Some(h) = l.reader.take() {
                h.abort();
            }
            // The input consumer captures the tmux target when it is spawned. Drop its
            // sender so the next key creates a consumer addressed to the new name; keeping
            // it would silently route every later key to the vanished old session.
            l.input = None;
            let running = l.emu.take().is_some();
            let title = l.title.override_title.clone();
            l.cfg.name = new.to_string();
            live.insert(new.to_string(), l);
            (running, title)
        };
        // The cache is keyed by name, and the frames under the old one describe a pane that
        // is about to be re-captured against a fresh emulator.
        self.forget_scroll(old);
        self.forget_scroll(new);
        let _ = self.title_cache.clear_latest(old);
        if let Some(t) = title {
            let _ = self.title_cache.remember(new, &t);
        }
        if running && self.spawn_reader(new).await {
            let m = self.clone();
            let name = new.to_string();
            tokio::spawn(async move { m.nudge_redraw(&name).await });
        }
    }

    pub async fn remove(self: &Arc<Self>, name: &str) -> Result<()> {
        self.reload_if_changed().await;
        if self.is_ephemeral(name).await {
            return self.stop(name).await;
        }
        self.ensure_state_id(name).await?;
        self.stop(name).await.ok();
        let mut cfg = self.cfg.write().await;
        let session = cfg
            .session(name)
            .cloned()
            .ok_or_else(|| anyhow!("no such session: {name}"))?;
        let trashed = crate::sandbox::trash_state(&session, name)?;
        cfg.sessions.retain(|s| s.name != name);
        if let Err(e) = self.save_cfg(&cfg) {
            if let Some(path) = trashed.as_deref() {
                if let Err(restore) = crate::sandbox::restore_trashed_state(&session, path) {
                    tracing::error!("config delete failed: {e:#}; private-state restore also failed: {restore:#}");
                }
            }
            return Err(e);
        }
        drop(cfg);
        if let Err(e) = crate::sandbox::purge_trash() {
            tracing::warn!("purging private-state trash: {e:#}");
        }
        self.sync_from_config().await;
        Ok(())
    }

    /// Stop an agent and discard only its private tool state.  The old tree is recoverable in
    /// the daemon-owned trash for two weeks; the configured agent remains and reseeds on start.

    pub async fn reset_state(self: &Arc<Self>, name: &str) -> Result<()> {
        self.reload_if_changed().await;
        if self.is_ephemeral(name).await {
            bail!("temporary session {name} has no resettable private state");
        }
        self.ensure_state_id(name).await?;
        self.stop(name).await.ok();
        let cfg = self.config().await;
        let session = cfg
            .session(name)
            .cloned()
            .ok_or_else(|| anyhow!("no such session: {name}"))?;
        crate::sandbox::trash_state(&session, &format!("{name}-reset"))?;
        if let Err(e) = crate::sandbox::purge_trash() {
            tracing::warn!("purging private-state trash: {e:#}");
        }
        self.sync_from_config().await;
        Ok(())
    }

    pub async fn stored_states(&self) -> Result<Vec<crate::sandbox::StoredState>> {
        let sessions = self.config().await.sessions;
        tokio::task::spawn_blocking(move || crate::sandbox::stored_states(&sessions))
            .await
            .map_err(|e| anyhow!("scanning private state: {e}"))
    }

    pub async fn delete_stored_state(&self, kind: &str, key: &str) -> Result<()> {
        let sessions = self.config().await.sessions;
        let kind = kind.to_string();
        let key = key.to_string();
        tokio::task::spawn_blocking(move || {
            crate::sandbox::delete_stored_state(&kind, &key, &sessions)
        })
        .await
        .map_err(|e| anyhow!("deleting private state: {e}"))?
    }

    pub async fn restore_stored_state(self: &Arc<Self>, key: &str) -> Result<String> {
        self.reload_if_changed().await;
        let archived = crate::sandbox::trashed_session(key)?;
        let mut cfg = self.cfg.write().await;
        let existing = cfg
            .sessions
            .iter()
            .find(|s| !archived.state_id.is_empty() && s.state_id == archived.state_id)
            .cloned();
        let add = existing.is_none();
        let session = existing.unwrap_or_else(|| archived.clone());
        if add {
            if cfg.session(&session.name).is_some() {
                bail!(
                    "session {:?} already exists with different private state",
                    session.name
                );
            }
            check_name(&session.name)?;
            check_belongs(&cfg, &session)?;
        }

        crate::sandbox::restore_stored_state(key, &cfg.sessions)?;
        if add {
            cfg.sessions.push(session.clone());
            if let Err(e) = self.save_cfg(&cfg) {
                if let Err(rollback) = crate::sandbox::rollback_restored_state(key, &session) {
                    tracing::error!("restored-agent config save failed: {e:#}; state rollback also failed: {rollback:#}");
                }
                return Err(e);
            }
        }
        crate::sandbox::finish_restored_state(&session);
        drop(cfg);
        self.sync_from_config().await;
        Ok(session.name)
    }

    /// Fill in a config entry written before state identities existed. It deliberately receives
    /// a fresh tree; any unclaimed name-keyed directory is an orphan in the storage inventory.

    pub(super) async fn ensure_state_id(&self, name: &str) -> Result<()> {
        let mut cfg = self.cfg.write().await;
        let Some(idx) = cfg.sessions.iter().position(|s| s.name == name) else {
            return Ok(()); // temporary/session-adopted agent: its in-memory Default owns a key.
        };
        if cfg.sessions[idx].state_id.is_empty() {
            cfg.sessions[idx].state_id = uuid::Uuid::new_v4().to_string();
            self.save_cfg(&cfg)?;
        }
        Ok(())
    }

    pub async fn patch_config(self: &Arc<Self>, patch: Value) -> Result<()> {
        self.reload_if_changed().await;

        let text = std::fs::read_to_string(&self.cfg_path)
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

        self.save_cfg_text(&new, &toml::to_string_pretty(&document)?)?;

        *self.rules.write().await = compile_rules(&new);
        *self.cfg.write().await = new;
        self.sync_from_config().await;
        self.announce_projects().await;
        self.announce_shortcuts().await;
        Ok(())
    }

    pub async fn replace_config(self: &Arc<Self>, text: &str) -> Result<()> {
        let mut new = Config::parse(text)?;
        if new.daemon.token == crate::config::TOKEN_REDACTED {
            new.daemon.token = self.cfg.read().await.daemon.token.clone();
        }
        validate_config(&new)?;
        self.save_cfg(&new)?;
        *self.rules.write().await = compile_rules(&new);
        *self.cfg.write().await = new;
        self.sync_from_config().await;
        self.announce_projects().await;
        self.announce_shortcuts().await;
        Ok(())
    }

    pub async fn views(&self) -> Vec<SessionView> {
        let cfg = self.config().await;
        let live = self.live.read().await;
        let temp = self.temp.read().await;
        let mut out: Vec<SessionView> = live
            .values()
            .map(|l| {
                let p = cfg.project_of(&l.cfg).or_else(|| temp.get(&l.cfg.project));
                SessionView {
                    name: l.cfg.name.clone(),
                    label: l.cfg.label.clone().unwrap_or_default(),
                    project: l.cfg.project.clone(),
                    dir: p.map(|p| p.dir.clone()).unwrap_or_default(),
                    command: l.cfg.command.clone(),
                    command_preset: cfg.command_name(&l.cfg),
                    cmd: l.cfg.cmd.clone(),
                    sandbox: l.cfg.sandbox.clone(),
                    breadcrumbs: l.cfg.breadcrumbs.clone(),
                    breadcrumb_yolo: l.cfg.breadcrumb_yolo,
                    breadcrumbs_pending: l.breadcrumbs_pending,
                    agent: cfg.command_of(&l.cfg),
                    state: l.state,
                    alive: l.state != State::Down,
                    cols: l.cols,
                    rows: l.rows,
                    network: p
                        .map(|p| cfg.network_of(&l.cfg, p).unwrap_or(p.network))
                        .unwrap_or_default(),
                    network_override: l.cfg.network,
                    dns: p.map(|p| cfg.dns_of(&l.cfg, p)).unwrap_or_default(),
                    dns_override: l.cfg.dns.clone(),
                    limits: p.map(|p| cfg.limits_of(&l.cfg, p)).unwrap_or(l.cfg.limits),
                    limits_override: l.cfg.limits,
                    autostart: l.cfg.autostart,
                    ephemeral: l.ephemeral,
                    last_change: l.last_change,
                    state_since: l.state_since,
                    title: l
                        .cfg
                        .label
                        .clone()
                        .filter(|label| !label.trim().is_empty())
                        .or_else(|| l.title.override_title.clone())
                        .or_else(|| l.screen.as_ref().map(|s| s.title.clone()))
                        .unwrap_or_default(),
                    bell: l.bell,
                    seq: l.seq,
                }
            })
            .collect();
        out.sort_by(|a, b| a.name.cmp(&b.name));
        out
    }

    pub(super) async fn classify(&self, changed: bool, last_change: u64, text: &str) -> State {
        if let Some(state) = match_rules(&self.rules.read().await, text) {
            return state;
        }
        if changed || now_ms().saturating_sub(last_change) < IDLE_MS {
            State::Working
        } else {
            State::Idle
        }
    }

    pub async fn retick(self: &Arc<Self>) {
        self.reload_if_due().await;

        let now = now_ms();

        // Classification awaits the rules lock, so snapshot before releasing the live lock.
        let snapshot: Vec<(String, u64, String)> = {
            let live = self.live.read().await;
            live.iter()
                .filter(|(_, l)| l.state != State::Down)
                .filter_map(|(n, l)| {
                    let _ = l.screen.as_ref()?;
                    let idle_due = match l.state {
                        State::Working | State::Waiting => {
                            now.saturating_sub(l.state_since) >= IDLE_MS
                        }
                        State::Idle | State::Down => false,
                    };
                    if l.seq == l.retick_seq && !idle_due {
                        return None;
                    }
                    Some((n.clone(), l.last_change, l.plain.clone()))
                })
                .collect()
        };

        let mut dirty_list = false;
        for (name, last_change, plain) in snapshot {
            let state = self.classify(false, last_change, &plain).await;
            let mut live = self.live.write().await;
            if let Some(l) = live.get_mut(&name) {
                l.retick_seq = l.seq;
                if l.set_state(state) {
                    dirty_list = true;
                }
            }
        }

        if dirty_list {
            let _ = self.events.send(Event::Sessions {
                sessions: self.views().await,
            });
        }
    }
}

#[cfg(test)]
mod tests {
    use super::Manager;
    use crate::config::{Config, ProjectCfg};

    #[test]
    fn legacy_host_names_are_recognized_during_adoption() {
        let cfg = Config {
            projects: vec![ProjectCfg {
                name: "repo".into(),
                ..Default::default()
            }],
            ..Default::default()
        };
        let host = crate::sandbox::host_session_name("repo");

        assert!(Manager::legacy_host_session(&host, &cfg));
        assert!(!Manager::legacy_host_session("ordinary-agent", &cfg));
    }
}
