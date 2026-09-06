//! Session target resolution and process lifecycle.

use super::super::*;
use crate::sandbox::build_argv;
use anyhow::anyhow;

impl Manager {
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

    pub(super) fn validate_dir(p: &ProjectCfg) -> Result<String> {
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

    pub(super) async fn wire_live_state(
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
        if !host && s.worker {
            crumbs.push(super::workers::WORKER_DISCOVERY_BREADCRUMB.to_string());
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
        let (mut s, p) = self.resolve_target(&cfg, name).await?;
        if self.tmux.exists(name).await {
            bail!("session {name} is already running");
        }
        if cfg.command_of(&s).trim().is_empty() {
            bail!(
                "session {name} names command preset {:?}, which has no file",
                s.command
            );
        }
        if s.worker {
            if s.task_id.trim().is_empty() || s.parent.trim().is_empty() {
                bail!("worker session {name} has incomplete task ownership metadata");
            }
            let table = crate::presets::table();
            crate::sandbox::validate_preset_name(super::workers::WORKER_SANDBOX, &table)
                .context("worker task API preset is invalid")?;
            if cfg.network_of(&s, &p) == NetworkMode::None {
                bail!("worker session {name} cannot reach the task API with networking disabled");
            }
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
        if s.worker {
            let token = self
                .mint_grant(
                    s.name.clone(),
                    vec![s.name.clone()],
                    crate::grant::Level::Rw,
                )
                .await
                .context("minting worker task credential")?;
            s.worker_token = Some(token);
        }
        let argv = if host {
            crate::sandbox::host_argv(&cfg, &s, &p)
        } else {
            if let Err(error) = crate::sandbox::prepare_network(&cfg, &s, &p) {
                if s.worker {
                    self.revoke_grants(&s.name).await;
                }
                return Err(error);
            }
            if s.slopworld_md {
                let sessions = self.views().await;
                if let Err(error) =
                    crate::manifest::prepare(&std::path::PathBuf::from(&dir), &cfg, &p, &sessions)
                {
                    if s.worker {
                        self.revoke_grants(&s.name).await;
                    }
                    return Err(error);
                }
            }
            match build_argv(&cfg, &s, &p) {
                Ok(argv) => argv,
                Err(error) => {
                    if s.worker {
                        self.revoke_grants(&s.name).await;
                    }
                    return Err(error);
                }
            }
        };
        if s.worker {
            tracing::info!("starting {name} (task worker)");
        } else {
            tracing::info!("starting {name}: {}", argv.join(" "));
        }
        if let Err(error) = self.tmux.spawn(name, &dir, cols, rows, &argv, host).await {
            if s.worker {
                self.revoke_grants(&s.name).await;
                // tmux includes its complete argv in command errors. Do not let the worker's
                // bearer credential escape into a persisted task failure note or daemon log.
                tracing::warn!("could not create worker session {name}: tmux spawn failed");
                return Err(anyhow!("could not create worker session {name}"));
            }
            return Err(error);
        }
        if s.worker {
            let durable = !self.is_ephemeral(name).await;
            if let Err(error) = self
                .tmux
                .set_worker_metadata(name, &s.parent, &s.task_id, durable)
                .await
            {
                tracing::warn!("could not persist worker metadata for {name}: {error:#}");
            }
        }
        if host {
            if let Err(error) = self.tmux.set_host_metadata(name, &s.project, &dir).await {
                tracing::warn!("could not persist host metadata for {name}: {error:#}");
            }
            self.remember_host_path(name, &dir).await;
        }
        self.clear_activity(name).await;
        let auto_resume_pending = !host && s.auto_resume && !s.worker;
        let (title_was_cleared, run_id) = {
            let mut live = self.live.write().await;
            if let Some(l) = live.get_mut(name) {
                let had_title = l.title.override_title.is_some();
                l.state = State::Down;
                l.process_running = false;
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
        match self.spawn_reader(name).await {
            Ok(true) => {}
            Ok(false) => {
                // A session created above is not useful without a newly attached control
                // reader. Treat a refused attach as a failed start; otherwise a stale reader or
                // emulator can make the fresh tmux pane look successfully started.
                if self.tmux.exists(name).await {
                    if let Err(cleanup) = self.tmux.kill(name).await {
                        tracing::warn!("could not clean up failed start {name}: {cleanup:#}");
                    }
                }
                if s.worker {
                    self.revoke_grants(&s.name).await;
                }
                return Err(anyhow!(
                    "control reader for {name} was not attached during startup"
                ));
            }
            Err(error) => {
                // The reader has already reset the live state after an attach failure; remove
                // the tmux pane as well so a failed start cannot be mistaken for a running
                // session on the next tick.
                if self.tmux.exists(name).await {
                    if let Err(cleanup) = self.tmux.kill(name).await {
                        tracing::warn!("could not clean up failed start {name}: {cleanup:#}");
                    }
                }
                if s.worker {
                    self.revoke_grants(&s.name).await;
                }
                return Err(error.context(format!("starting session {name} reader")));
            }
        }
        self.wire_live_state(name, &cfg, &s, &p, host).await;
        if auto_resume_pending {
            self.queue_auto_resume(name, run_id);
        }
        Ok(())
    }

    pub async fn stop(self: &Arc<Self>, name: &str) -> Result<()> {
        let worker_task = self
            .live
            .read()
            .await
            .get(name)
            .filter(|l| l.cfg.worker)
            .map(|l| l.cfg.task_id.clone());
        if self.tmux.exists(name).await {
            self.tmux.kill(name).await?;
        }
        if self.is_ephemeral(name).await && !self.is_host(name).await {
            self.forget(name).await;
            return Ok(());
        }
        if let Some(l) = self.live.write().await.get_mut(name) {
            l.set_state(State::Down);
            l.process_running = false;
            l.auto_resume_pending = false;
            l.screen = None;
            l.emu = None;
            // A stopped run must not leave its generated task title on the downed
            // session. Resetting the capture also makes late title responses from
            // this run stale before the next start.
            l.title = TitleCapture::default();
            l.reader_token = None;
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
        if let Some(task_id) = worker_task {
            self.fail_worker_task(&task_id, format!("worker session {name} was stopped"));
            self.revoke_grants(name).await;
        }
        Ok(())
    }

    async fn forget_inner(self: &Arc<Self>, name: &str, reader_token: Option<&Arc<()>>) {
        let (handle, project, session) = {
            let mut live = self.live.write().await;
            let Some(l) = live.get(name) else { return };
            if let Some(reader_token) = reader_token {
                if !l
                    .reader_token
                    .as_ref()
                    .is_some_and(|current| Arc::ptr_eq(current, reader_token))
                {
                    return;
                }
            }
            let mut l = live.remove(name).expect("live entry checked above");
            (l.reader.take(), l.cfg.project.clone(), l.cfg)
        };
        if session.worker {
            self.fail_worker_task(
                &session.task_id,
                format!("worker session {name} exited or was stopped"),
            );
        }
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
        if reader_token.is_none() {
            if let Some(h) = handle {
                h.abort();
            }
        }
    }

    pub(super) async fn forget(self: &Arc<Self>, name: &str) {
        self.forget_inner(name, None).await;
    }

    pub(super) async fn forget_from_reader(self: &Arc<Self>, name: &str, reader_token: &Arc<()>) {
        self.forget_inner(name, Some(reader_token)).await;
    }

    pub async fn restart(self: &Arc<Self>, name: &str) -> Result<()> {
        self.stop(name).await?;
        self.start(name).await
    }
}
