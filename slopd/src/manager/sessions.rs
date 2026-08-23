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
        let title_was_cleared = if let Some(l) = self.live.write().await.get_mut(name) {
            let had_title = l.title.override_title.is_some();
            l.state = State::Down;
            l.last_change = 0;
            l.state_since = 0;
            l.title = TitleCapture::default();
            had_title
        } else {
            false
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
        if title_was_cleared {
            let _ = self.events.send(Event::Sessions {
                sessions: self.views().await,
            });
        }
        self.spawn_reader(name).await;
        self.wire_live_state(name, &cfg, &s, &p, host).await;
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
