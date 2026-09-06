//! Session targets, lifecycle, stored state and views.

use super::super::*;
use anyhow::anyhow;
use futures::{stream, StreamExt};

const HOST_QUERY_CONCURRENCY: usize = 4;

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
        self.update_cfg(|cfg| {
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
            Ok(())
        })
        .await?;
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

        let (project, cfg_changed) = match self
            .update_cfg_if_changed(|cfg| {
                let mut changed = false;
                let mut project = None;
                if let Some(tab) = cfg.host_terminals.iter_mut().find(|tab| tab.name == name) {
                    project = Some(tab.project.clone());
                    if tab.path != path {
                        tab.path = path.to_string();
                        changed = true;
                    }
                }
                Ok(((project, changed), changed))
            })
            .await
        {
            Ok(result) => result,
            Err(error) => {
                tracing::warn!("could not persist host path for {name}: {error:#}");
                (None, false)
            }
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

    pub(super) async fn refresh_host_paths(&self) -> bool {
        let names: Vec<String> = self
            .live
            .read()
            .await
            .values()
            .filter(|l| l.host && l.state != State::Down)
            .map(|l| l.cfg.name.clone())
            .collect();
        let tmux = self.tmux.clone();
        let results = stream::iter(names.into_iter().map(|name| {
            let tmux = tmux.clone();
            async move { (name.clone(), tmux.current_path(&name).await) }
        }))
        .buffer_unordered(HOST_QUERY_CONCURRENCY)
        .collect::<Vec<_>>()
        .await;

        let mut changed = false;
        for (name, path) in results {
            let Some(path) = path else { continue };
            changed |= self.remember_host_path(&name, &path).await;
        }
        changed
    }

    pub(super) async fn refresh_host_processes(&self) -> bool {
        let names: Vec<String> = self
            .live
            .read()
            .await
            .values()
            .filter(|l| l.host && l.state != State::Down)
            .map(|l| l.cfg.name.clone())
            .collect();
        let tmux = self.tmux.clone();
        let results = stream::iter(names.into_iter().map(|name| {
            let tmux = tmux.clone();
            async move { (name.clone(), tmux.current_command(&name).await) }
        }))
        .buffer_unordered(HOST_QUERY_CONCURRENCY)
        .collect::<Vec<_>>()
        .await;

        let mut changed = false;
        for (name, command) in results {
            let Some(command) = command else { continue };
            let process_running = !crate::sandbox::is_shell_command(&command);
            let mut live = self.live.write().await;
            if let Some(l) = live
                .get_mut(&name)
                .filter(|l| l.host && l.state != State::Down)
            {
                if l.process_running != process_running {
                    l.process_running = process_running;
                    changed = true;
                }
            }
        }
        changed
    }

    pub async fn add(self: &Arc<Self>, mut s: SessionCfg) -> Result<()> {
        self.reload_if_changed().await;
        let (autostart, name) = self
            .update_cfg(|cfg| {
                if cfg.session(&s.name).is_some() {
                    bail!("session {} already exists", s.name);
                }
                check_name(&s.name)?;
                check_belongs(cfg, &s)?;
                s.limits.validate()?;
                crate::runtime::validate_limits(&s.limits)?;
                // Worker identity is daemon-owned. The ordinary session editor cannot create a child by
                // smuggling hierarchy fields through this route; `spawn_worker` is the only constructor.
                s.worker = false;
                s.parent.clear();
                s.task_id.clear();
                // A client has no authority over which durable state an agent receives.  Always mint a
                // fresh key, including if a hand-written request carried a stale one.
                s.state_id = uuid::Uuid::new_v4().to_string();
                let autostart = s.autostart;
                let name = s.name.clone();
                cfg.sessions.push(s.clone());
                Ok((autostart, name))
            })
            .await?;

        self.sync_from_config().await;
        // sync_from_config already attempts autostart for the newly persisted session. Retry
        // only when that attempt left no tmux session; otherwise this second start would turn a
        // successful add into an "already running" error.
        if autostart && !self.tmux.exists(&name).await {
            self.start(&name).await?;
        }
        Ok(())
    }

    pub async fn update(self: &Arc<Self>, name: &str, mut s: SessionCfg) -> Result<()> {
        self.reload_if_changed().await;
        if self
            .cfg
            .read()
            .await
            .session(name)
            .is_some_and(|session| session.worker)
        {
            bail!("task-owned worker {name} cannot be edited; retry its task instead");
        }
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

        self.update_cfg(|cfg| {
            check_belongs(cfg, &s)?;
            s.limits.validate()?;
            crate::runtime::validate_limits(&s.limits)?;
            let idx = cfg
                .sessions
                .iter()
                .position(|x| x.name == name)
                .ok_or_else(|| anyhow!("no such session: {name}"))?;
            // Keep daemon-owned worker identity across an ordinary settings edit. The mod's write
            // model intentionally does not expose these fields.
            s.worker = cfg.sessions[idx].worker;
            s.parent = cfg.sessions[idx].parent.clone();
            s.task_id = cfg.sessions[idx].task_id.clone();
            // Keep private state with the agent across every edit, particularly a rename.  The wire
            // deliberately does not expose this field, but also must not be able to change it.
            s.state_id = cfg.sessions[idx].state_id.clone();
            cfg.sessions[idx] = s.clone();
            Ok(())
        })
        .await?;

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
            self.update_cfg(|cfg| {
                let session = cfg
                    .sessions
                    .iter_mut()
                    .find(|session| session.name == name)
                    .ok_or_else(|| anyhow!("no such session: {name}"))?;
                session.label = saved.clone();
                Ok(())
            })
            .await?;
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
        if running {
            match self.spawn_reader(new).await {
                Ok(true) => {
                    let m = self.clone();
                    let name = new.to_string();
                    tokio::spawn(async move { m.nudge_redraw(&name).await });
                }
                Ok(false) => {}
                Err(error) => tracing::warn!("could not reattach reader for {new}: {error:#}"),
            }
        }
    }

    pub async fn remove(self: &Arc<Self>, name: &str) -> Result<()> {
        self.reload_if_changed().await;
        if self.is_host(name).await {
            self.stop(name).await?;

            let saved = self
                .cfg
                .read()
                .await
                .host_terminals
                .iter()
                .any(|tab| tab.name == name);
            if saved {
                self.update_cfg(|cfg| {
                    cfg.host_terminals.retain(|tab| tab.name != name);
                    Ok(())
                })
                .await?;
            }

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
        self.stop(name).await?;
        let session = self
            .config()
            .await
            .session(name)
            .cloned()
            .ok_or_else(|| anyhow!("no such session: {name}"))?;
        let trashed = crate::sandbox::trash_state(&session, name)?;
        if let Err(e) = self
            .update_cfg(|cfg| {
                cfg.sessions.retain(|s| s.name != name);
                Ok(())
            })
            .await
        {
            if let Some(path) = trashed.as_deref() {
                if let Err(restore) = crate::sandbox::restore_trashed_state(&session, path) {
                    tracing::error!("config delete failed: {e:#}; private-state restore also failed: {restore:#}");
                }
            }
            return Err(e);
        }
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
        self.stop(name).await?;
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
        let cfg = self.config().await;
        let existing = cfg
            .sessions
            .iter()
            .find(|s| !archived.state_id.is_empty() && s.state_id == archived.state_id)
            .cloned();
        let add = existing.is_none();
        let session = existing.unwrap_or_else(|| archived.clone());
        if add {
            check_name(&session.name)?;
        }

        crate::sandbox::restore_stored_state(key, &cfg.sessions)?;
        if add {
            if let Err(e) = self
                .update_cfg(|cfg| {
                    if cfg.session(&session.name).is_some() {
                        bail!(
                            "session {:?} already exists with different private state",
                            session.name
                        );
                    }
                    check_belongs(cfg, &session)?;
                    cfg.sessions.push(session.clone());
                    Ok(())
                })
                .await
            {
                if let Err(rollback) = crate::sandbox::rollback_restored_state(key, &session) {
                    tracing::error!("restored-agent config save failed: {e:#}; state rollback also failed: {rollback:#}");
                }
                return Err(e);
            }
        }
        crate::sandbox::finish_restored_state(&session);
        self.sync_from_config().await;
        Ok(session.name)
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

        let worker = SessionCfg {
            worker: true,
            slopworld_md: false,
            ..session.clone()
        };
        manager
            .wire_live_state("agent", &cfg, &worker, &project, false)
            .await;
        let live = manager.live.read().await;
        assert!(String::from_utf8_lossy(&live["agent"].breadcrumbs)
            .contains("Other SlopWorld agents are available"));
        assert!(live["agent"].breadcrumbs_pending);
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
        host.process_running = true;
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
        assert!(views[1].process_running);
    }
}
