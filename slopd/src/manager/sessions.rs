//! Session targets, lifecycle, stored state and views.

use super::super::*;
use crate::sandbox::build_argv;
use anyhow::anyhow;

impl Manager {
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
        if let Some(p) = self.temp.read().await.get(&s.project).cloned() {
            return Some(p);
        }
        // A host tab may outlive the project entry that created it. Its last tmux cwd is
        // still a safe host starting point, so keep the tab usable and ungrouped instead of
        // making it impossible to restart.
        let path = self
            .live
            .read()
            .await
            .get(&s.name)
            .filter(|l| l.host)
            .map(|l| l.host_path.clone())
            .filter(|path| !path.trim().is_empty())?;
        Some(ProjectCfg {
            name: s.project.clone(),
            dir: path,
            ..Default::default()
        })
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
        let mut crumbs = crumbs;
        if !host
            && s.slopworld_md
            && s.instructions_breadcrumb
            && cfg.daemon.instructions.breadcrumb_enabled
        {
            let discovery = crate::manifest::render_breadcrumb(
                &cfg.daemon.instructions.breadcrumb,
                &p.name,
                &cfg.daemon.instructions.mount_path,
            );
            if !discovery.trim().is_empty() {
                crumbs.push(discovery);
            }
        }
        let mut live = self.live.write().await;
        if let Some(l) = live.get_mut(name) {
            l.breadcrumbs.clear();
            l.breadcrumbs_pending = false;
            if !host && s.breadcrumb_yolo && !crumbs.is_empty() {
                l.breadcrumbs = breadcrumb_block(&crumbs).into_bytes();
                l.breadcrumbs_pending = true;
            }
        }
        drop(live);
    }

    pub async fn start(self: &Arc<Self>, name: &str) -> Result<()> {
        let cfg = self.config().await;
        let (s, p) = self.resolve_target(&cfg, name).await?;
        if self.tmux.exists(name).await {
            bail!("session {name} is already running");
        }
        if cfg.command_of(&s).trim().is_empty() {
            bail!(
                "session {name} names command preset {:?}, which has no file",
                s.command
            );
        }
        let (cols, rows, host, host_path) = match self.live.read().await.get(name) {
            Some(l) => (l.cols, l.rows, l.host, l.host_path.clone()),
            None => (BOOT_COLS, BOOT_ROWS, false, String::new()),
        };
        let dir = if host && !host_path.trim().is_empty() {
            let remembered = expand(&host_path);
            if std::path::Path::new(&remembered).is_dir() {
                remembered
            } else {
                Self::validate_dir(&p)?
            }
        } else {
            Self::validate_dir(&p)?
        };
        if !std::path::Path::new(&dir).is_dir() {
            bail!("{dir} is not a directory");
        }
        let argv = if host {
            crate::sandbox::host_argv(&cfg, &s, &p)
        } else {
            crate::sandbox::prepare_network(&cfg, &s, &p)?;
            if s.slopworld_md {
                let sessions = self.views().await;
                crate::manifest::prepare(&std::path::PathBuf::from(&dir), &cfg, &p, &sessions)?;
            }
            build_argv(&cfg, &s, &p)?
        };
        tracing::info!("starting {name}: {}", argv.join(" "));
        self.tmux.spawn(name, &dir, cols, rows, &argv, host).await?;
        if host {
            if let Err(error) = self.tmux.set_host_metadata(name, &s.project, &dir).await {
                tracing::warn!("could not persist host metadata for {name}: {error:#}");
            }
            self.remember_host_path(name, &dir).await;
        }
        self.clear_activity(name).await;
        let auto_resume_pending = !host && s.auto_resume;
        let (title_was_cleared, run_id) = {
            let mut live = self.live.write().await;
            if let Some(l) = live.get_mut(name) {
                let had_title = l.title.override_title.is_some();
                l.state = State::Down;
                l.last_change = 0;
                l.state_since = 0;
                l.title = TitleCapture::default();
                l.auto_resume_pending = auto_resume_pending;
                l.run_id = l.run_id.wrapping_add(1);
                (had_title, l.run_id)
            } else {
                (false, 0)
            }
        };
        if let Err(error) = self.title_cache.clear_latest(name) {
            tracing::warn!(
                target: "slopd::titles",
                session = %name,
                error = %error,
                outcome = "cache_write_failed",
                "could not clear session title before start"
            );
        }
        if title_was_cleared || auto_resume_pending {
            let _ = self.events.send(Event::Sessions {
                sessions: self.views().await,
            });
        }
        self.spawn_reader(name).await;
        self.wire_live_state(name, &cfg, &s, &p, host).await;
        if !host && s.auto_resume {
            self.queue_auto_resume(name, run_id);
        }
        Ok(())
    }

    pub async fn stop(self: &Arc<Self>, name: &str) -> Result<()> {
        if self.tmux.exists(name).await {
            self.tmux.kill(name).await?;
        }
        if self.is_ephemeral(name).await && !self.is_host(name).await {
            self.forget(name).await;
            return Ok(());
        }
        if let Some(l) = self.live.write().await.get_mut(name) {
            l.set_state(State::Down);
            l.auto_resume_pending = false;
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
        // Removal is the visible transition. Publish it before filesystem, sandbox and tmux
        // cleanup so a dead ephemeral pane cannot hold the client on its old session list.
        let _ = self.events.send(Event::Sessions {
            sessions: self.views().await,
        });
        self.clear_activity(name).await;
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

    pub(super) async fn is_host(&self, name: &str) -> bool {
        self.live
            .read()
            .await
            .get(name)
            .map(|l| l.host)
            .unwrap_or(false)
    }

    pub(super) async fn remember_host_terminal(
        &self,
        name: &str,
        project: &str,
        path: &str,
    ) -> Result<()> {
        let mut cfg = self.cfg.write().await;
        if cfg.session(name).is_some() {
            bail!("host terminal {name} conflicts with an agent session");
        }
        if let Some(tab) = cfg.host_terminals.iter_mut().find(|tab| tab.name == name) {
            tab.project = project.to_string();
            tab.path = path.to_string();
        } else {
            cfg.host_terminals.push(crate::config::HostTerminalCfg {
                name: name.to_string(),
                project: project.to_string(),
                path: path.to_string(),
                autostart: true,
            });
        }
        self.save_cfg(&cfg)?;
        Ok(())
    }

    pub(super) async fn remember_host_path(&self, name: &str, path: &str) -> bool {
        if path.trim().is_empty() {
            return false;
        }
        let live_changed = {
            let mut live = self.live.write().await;
            live.get_mut(name)
                .filter(|l| l.host && l.host_path != path)
                .map(|l| {
                    l.host_path = path.to_string();
                    true
                })
                .unwrap_or(false)
        };

        let (project, cfg_changed) = {
            let mut changed = false;
            let mut project = None;
            let mut cfg = self.cfg.write().await;
            if let Some(tab) = cfg.host_terminals.iter_mut().find(|tab| tab.name == name) {
                project = Some(tab.project.clone());
                if tab.path != path {
                    tab.path = path.to_string();
                    changed = true;
                }
            }
            if changed {
                if let Err(error) = self.save_cfg(&cfg) {
                    tracing::warn!("could not persist host path for {name}: {error:#}");
                }
            }
            (project, changed)
        };

        if live_changed || cfg_changed {
            if let Some(project) = project {
                if let Err(error) = self.tmux.set_host_metadata(name, &project, path).await {
                    tracing::debug!("could not refresh host metadata for {name}: {error:#}");
                }
            }
        }
        live_changed || cfg_changed
    }

    async fn refresh_host_paths(&self) -> bool {
        let names: Vec<String> = self
            .live
            .read()
            .await
            .values()
            .filter(|l| l.host && l.state != State::Down)
            .map(|l| l.cfg.name.clone())
            .collect();
        let mut changed = false;
        for name in names {
            let Some(path) = self.tmux.current_path(&name).await else {
                continue;
            };
            changed |= self.remember_host_path(&name, &path).await;
        }
        changed
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
        self.clear_activity(name).await;
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
        if let Some(h) = handle {
            h.abort();
        }
    }

    pub async fn restart(self: &Arc<Self>, name: &str) -> Result<()> {
        self.stop(name).await?;
        self.start(name).await
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
        crate::runtime::validate_limits(&s.limits)?;
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
        crate::runtime::validate_limits(&s.limits)?;
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
        if let Err(error) = self.activity_cache.rename(old, new) {
            tracing::warn!(
                target: "slopd::activity",
                old = %old,
                new = %new,
                %error,
                "could not rename session activity cache"
            );
        }
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
        if self.is_host(name).await {
            self.stop(name).await.ok();

            let mut cfg = self.cfg.write().await;
            let old_hosts = cfg.host_terminals.clone();
            let saved = cfg.host_terminals.iter().any(|tab| tab.name == name);
            cfg.host_terminals.retain(|tab| tab.name != name);
            if saved {
                if let Err(error) = self.save_cfg(&cfg) {
                    cfg.host_terminals = old_hosts;
                    return Err(error);
                }
            }
            drop(cfg);

            // Host rows have no private agent state to trash. Forget both durable tabs and
            // unnamed runtime-only host errands after the pane has been stopped.
            self.forget(name).await;
            let _ = self.events.send(Event::Sessions {
                sessions: self.views().await,
            });
            return Ok(());
        }
        if self.is_ephemeral(name).await {
            return self.stop(name).await;
        }
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

    pub async fn views(&self) -> Vec<SessionView> {
        let cfg = self.config().await;
        let live = self.live.read().await;
        let temp = self.temp.read().await;
        let mut out: Vec<SessionView> = live
            .values()
            .map(|l| {
                let p = cfg.project_of(&l.cfg).or_else(|| temp.get(&l.cfg.project));
                // An unassigned host shell uses a disposable project only to own its
                // working directory. Keep that implementation detail out of sidebar
                // grouping so it remains in the top host-terminal rows.
                let display_project = if l.host && temp.contains_key(&l.cfg.project) {
                    String::new()
                } else {
                    l.cfg.project.clone()
                };
                SessionView {
                    name: l.cfg.name.clone(),
                    label: l.cfg.label.clone().unwrap_or_default(),
                    project: display_project,
                    dir: if l.host && !l.host_path.trim().is_empty() {
                        l.host_path.clone()
                    } else {
                        p.map(|p| p.dir.clone()).unwrap_or_default()
                    },
                    command: l.cfg.command.clone(),
                    command_preset: cfg.command_name(&l.cfg),
                    cmd: l.cfg.cmd.clone(),
                    sandbox: l.cfg.sandbox.clone(),
                    breadcrumbs: l.cfg.breadcrumbs.clone(),
                    slopworld_md: l.cfg.slopworld_md,
                    instructions_breadcrumb: l.cfg.instructions_breadcrumb,
                    persistent_tmp: l.cfg.persistent_tmp,
                    breadcrumb_yolo: l.cfg.breadcrumb_yolo,
                    breadcrumbs_pending: l.breadcrumbs_pending,
                    auto_resume_pending: l.auto_resume_pending,
                    agent: cfg.command_of(&l.cfg),
                    state: l.state,
                    alive: l.state != State::Down,
                    cols: l.cols,
                    rows: l.rows,
                    network: p.map(|p| cfg.network_of(&l.cfg, p)).unwrap_or_default(),
                    network_override: l.cfg.network,
                    dns: p.map(|p| cfg.dns_of(&l.cfg, p)).unwrap_or_default(),
                    dns_override: l.cfg.dns.clone(),
                    limits: p.map(|p| cfg.limits_of(&l.cfg, p)).unwrap_or(l.cfg.limits),
                    limits_override: l.cfg.limits,
                    mounts: l.cfg.mounts.clone(),
                    autostart: l.cfg.autostart,
                    auto_resume: l.cfg.auto_resume,
                    ephemeral: l.ephemeral,
                    host: l.host,
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

    pub(super) async fn classify_initial(&self, previous: State, text: &str) -> State {
        if let Some(state) = match_rules(&self.rules.read().await, text) {
            return state;
        }
        if previous != State::Down {
            previous
        } else {
            State::Working
        }
    }

    pub async fn retick(self: &Arc<Self>) {
        self.reload_if_due().await;

        let host_paths_changed = self.refresh_host_paths().await;

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
            let mut activity = None;
            let mut live = self.live.write().await;
            if let Some(l) = live.get_mut(&name) {
                l.retick_seq = l.seq;
                if l.set_state(state) {
                    dirty_list = true;
                    if !l.ephemeral {
                        activity = Some((l.state, l.state_since));
                    }
                }
            }
            drop(live);
            if let Some((state, state_since)) = activity {
                self.persist_activity(&name, state, state_since).await;
            }
        }

        if dirty_list || host_paths_changed {
            let _ = self.events.send(Event::Sessions {
                sessions: self.views().await,
            });
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn project(name: &str, dir: &std::path::Path, temp: bool) -> ProjectCfg {
        ProjectCfg {
            name: name.into(),
            dir: dir.to_string_lossy().into_owned(),
            temp,
            ..Default::default()
        }
    }

    #[test]
    fn project_directory_must_exist_unless_it_is_temporary() {
        let root = std::env::temp_dir().join(format!(
            "slopd-project-validation-{}-{}",
            std::process::id(),
            uuid::Uuid::new_v4()
        ));
        let missing = root.join("missing");

        let error = Manager::validate_dir(&project("ordinary", &missing, false))
            .unwrap_err()
            .to_string();
        assert!(error.contains("is not a directory"), "{error}");

        assert_eq!(
            Manager::validate_dir(&project("temporary", &missing, true)).unwrap(),
            missing.to_string_lossy()
        );
        assert!(missing.is_dir());
        std::fs::remove_dir_all(root).unwrap();
    }

    #[test]
    fn project_directory_rejects_a_protected_ancestor() {
        let error = Manager::validate_dir(&project("world", std::path::Path::new("/"), false))
            .unwrap_err()
            .to_string();
        assert!(error.contains("the whole filesystem"), "{error}");
    }

    #[tokio::test]
    async fn session_and_project_targets_resolve_from_config_temp_and_host_state() {
        let manager = crate::session::test_manager(Config::default());
        let configured_project = ProjectCfg {
            name: "repo".into(),
            dir: "/tmp/repo".into(),
            ..Default::default()
        };
        let configured = SessionCfg {
            name: "agent".into(),
            project: "repo".into(),
            ..Default::default()
        };
        let cfg = Config {
            projects: vec![configured_project.clone()],
            sessions: vec![configured.clone()],
            ..Default::default()
        };
        *manager.cfg.write().await = cfg.clone();

        assert_eq!(manager.session_cfg("agent").await.unwrap().name, "agent");
        let resolved = manager.project_for(&cfg, &configured).await.unwrap();
        assert_eq!(resolved.name, "repo");
        assert_eq!(resolved.dir, "/tmp/repo");

        let temporary = ProjectCfg {
            name: "scratch".into(),
            dir: "/tmp/scratch".into(),
            temp: true,
            ..Default::default()
        };
        manager
            .temp
            .write()
            .await
            .insert("scratch".into(), temporary.clone());
        let scratch = SessionCfg {
            name: "scratch-agent".into(),
            project: "scratch".into(),
            ..Default::default()
        };
        let resolved = manager.project_for(&cfg, &scratch).await.unwrap();
        assert_eq!(resolved.name, temporary.name);
        assert_eq!(resolved.dir, temporary.dir);

        let mut host = Live::new(
            SessionCfg {
                name: "host-shell".into(),
                project: "gone".into(),
                ..Default::default()
            },
            TitleCapture::default(),
        );
        host.host = true;
        host.host_path = "/tmp/remembered".into();
        manager.live.write().await.insert("host-shell".into(), host);
        let orphan_host = SessionCfg {
            name: "host-shell".into(),
            project: "gone".into(),
            ..Default::default()
        };
        assert_eq!(
            manager.project_for(&cfg, &orphan_host).await.unwrap().dir,
            "/tmp/remembered"
        );
    }

    #[tokio::test]
    async fn instructions_breadcrumb_follows_agent_and_global_settings() {
        let mut cfg = Config::default();
        let session = SessionCfg {
            name: "agent".into(),
            project: "repo".into(),
            slopworld_md: true,
            ..Default::default()
        };
        let project = ProjectCfg {
            name: "repo".into(),
            dir: "/tmp/repo".into(),
            ..Default::default()
        };
        let manager = crate::session::test_manager(cfg.clone());
        manager.live.write().await.insert(
            session.name.clone(),
            Live::new(session.clone(), TitleCapture::default()),
        );

        manager
            .wire_live_state("agent", &cfg, &session, &project, false)
            .await;
        let live = manager.live.read().await;
        assert!(live["agent"].breadcrumbs_pending);
        assert!(String::from_utf8_lossy(&live["agent"].breadcrumbs)
            .contains("Read `SLOPWORLD.md` for SlopWorld runtime context."));
        drop(live);

        let disabled = SessionCfg {
            instructions_breadcrumb: false,
            ..session.clone()
        };
        manager
            .wire_live_state("agent", &cfg, &disabled, &project, false)
            .await;
        let live = manager.live.read().await;
        assert!(!live["agent"].breadcrumbs_pending);
        assert!(live["agent"].breadcrumbs.is_empty());
        drop(live);

        cfg.daemon.instructions.breadcrumb_enabled = false;
        manager
            .wire_live_state("agent", &cfg, &session, &project, false)
            .await;
        let live = manager.live.read().await;
        assert!(!live["agent"].breadcrumbs_pending);
        assert!(live["agent"].breadcrumbs.is_empty());
    }

    #[tokio::test]
    async fn state_classification_prefers_rules_then_activity_age() {
        let manager = crate::session::test_manager(Config::default());
        *manager.rules.write().await = vec![(
            State::Waiting,
            regex::Regex::new("choose an option").unwrap(),
        )];
        assert_eq!(
            manager.classify(false, 0, "choose an option").await,
            State::Waiting
        );

        manager.rules.write().await.clear();
        assert_eq!(
            manager.classify(true, u64::MAX, "changed").await,
            State::Working
        );
        assert_eq!(manager.classify(false, 0, "stale").await, State::Idle);
        assert_eq!(
            manager.classify_initial(State::Idle, "unchanged").await,
            State::Idle
        );
        assert_eq!(
            manager.classify_initial(State::Down, "first frame").await,
            State::Working
        );
    }

    #[tokio::test]
    async fn views_keep_host_paths_and_sort_by_session_name() {
        let manager = crate::session::test_manager(Config::default());
        *manager.cfg.write().await = Config {
            sessions: vec![SessionCfg {
                name: "agent".into(),
                label: Some("Agent label".into()),
                ..Default::default()
            }],
            ..Default::default()
        };
        let mut agent = Live::new(
            SessionCfg {
                name: "agent".into(),
                label: Some("Agent label".into()),
                ..Default::default()
            },
            TitleCapture::default(),
        );
        agent.state = State::Working;
        agent.seq = 4;
        let mut host = Live::new(
            SessionCfg {
                name: "z-shell".into(),
                ..Default::default()
            },
            TitleCapture::default(),
        );
        host.host = true;
        host.ephemeral = true;
        host.host_path = "/tmp/host-cwd".into();
        manager.live.write().await.insert("z-shell".into(), host);
        manager.live.write().await.insert("agent".into(), agent);

        let views = manager.views().await;
        assert_eq!(
            views.iter().map(|v| v.name.as_str()).collect::<Vec<_>>(),
            ["agent", "z-shell"]
        );
        assert_eq!(views[0].label, "Agent label");
        assert_eq!(views[0].state, State::Working);
        assert_eq!(views[0].seq, 4);
        assert_eq!(views[1].dir, "/tmp/host-cwd");
        assert!(views[1].host && views[1].ephemeral);
    }
}
