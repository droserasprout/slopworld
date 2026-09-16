//! Session target preparation and process startup.

use super::super::*;
use super::session_lifecycle::{
    finish_reader, reset_process_state, take_reader_for_abort, ReaderDisposition,
};
use crate::sandbox::{build_plan, LaunchPlan};
use anyhow::{anyhow, Context};

struct StartPlan {
    cfg: Config,
    session: SessionCfg,
    project: ProjectCfg,
    cols: u16,
    rows: u16,
    host: bool,
    dir: String,
    launch: Option<LaunchPlan>,
    argv: Vec<String>,
}

impl StartPlan {
    fn is_worker(&self) -> bool {
        self.session.worker
    }
}

impl Manager {
    async fn resolve_target(&self, cfg: &Config, name: &str) -> Result<(SessionCfg, ProjectCfg)> {
        let session = match cfg.session(name) {
            Some(session) => session.clone(),
            None => self
                .session_cfg(name)
                .await
                .ok_or_else(|| anyhow!("no such session: {name}"))?,
        };
        let project = self.project_for(cfg, &session).await.ok_or_else(|| {
            if session.project.is_empty() {
                anyhow!("session {name} belongs to no project")
            } else {
                anyhow!(
                    "session {name} belongs to project {}, which does not exist",
                    session.project
                )
            }
        })?;
        Ok((session, project))
    }

    pub(super) fn validate_dir(project: &ProjectCfg) -> Result<String> {
        let dir = expand(&project.dir);
        if project.temp {
            std::fs::create_dir_all(&dir).with_context(|| format!("making {dir}"))?;
        }
        if !std::path::Path::new(&dir).is_dir() {
            bail!("{dir} is not a directory");
        }
        if let Some(what) = crate::sandbox::refused(&dir) {
            bail!(
                "project {} cannot live at {dir}: it reaches {what}",
                project.name
            );
        }
        Ok(dir)
    }

    async fn prepare_start(&self, name: &str) -> Result<StartPlan> {
        let cfg = self.config().await;
        let (mut session, project) = self.resolve_target(&cfg, name).await?;
        if self.tmux.exists(name).await {
            bail!("session {name} is already running");
        }
        if cfg.command_of(&session).trim().is_empty() {
            bail!(
                "session {name} names command preset {:?}, which has no file",
                session.command
            );
        }
        if session.worker {
            if session.task_id.trim().is_empty() || session.parent.trim().is_empty() {
                bail!("worker session {name} has incomplete task ownership metadata");
            }
            let table = crate::presets::table();
            crate::sandbox::validate_preset_name(super::workers::WORKER_SANDBOX, &table)
                .context("worker task API preset is invalid")?;
            if cfg.network_of(&session, &project) == NetworkMode::None {
                bail!("worker session {name} cannot reach the task API with networking disabled");
            }
        }
        let (cols, rows, host, host_path) = match self.live.read().await.get(name) {
            Some(live) => (live.cols, live.rows, live.host, live.host_path.clone()),
            None => (BOOT_COLS, BOOT_ROWS, false, String::new()),
        };
        let dir = if host && !host_path.trim().is_empty() {
            let remembered = expand(&host_path);
            if std::path::Path::new(&remembered).is_dir() {
                remembered
            } else {
                Self::validate_dir(&project)?
            }
        } else {
            Self::validate_dir(&project)?
        };
        if !std::path::Path::new(&dir).is_dir() {
            bail!("{dir} is not a directory");
        }

        if session.worker {
            let token = self
                .mint_grant(
                    session.name.clone(),
                    vec![session.name.clone()],
                    crate::grant::Level::Rw,
                )
                .await
                .context("minting worker task credential")?;
            session.worker_token = Some(token);
        }

        let launch = match self
            .build_start_plan(&cfg, &session, &project, &dir, host)
            .await
        {
            Ok(launch) => launch,
            Err(error) => {
                if session.worker {
                    self.invalidate_session(&session.name).await;
                }
                return Err(error);
            }
        };

        let argv = launch
            .as_ref()
            .map(LaunchPlan::lower)
            .unwrap_or_else(|| crate::sandbox::host_argv(&cfg, &session, &project));

        Ok(StartPlan {
            cfg,
            session,
            project,
            cols,
            rows,
            host,
            dir,
            launch,
            argv,
        })
    }

    async fn build_start_plan(
        &self,
        cfg: &Config,
        session: &SessionCfg,
        project: &ProjectCfg,
        dir: &str,
        host: bool,
    ) -> Result<Option<LaunchPlan>> {
        if host {
            return Ok(None);
        }

        crate::sandbox::prepare_network(cfg, session, project)?;
        if cfg.daemon.experimental_instructions && session.slopworld_md {
            let sessions = self.views().await;
            crate::manifest::prepare(&std::path::PathBuf::from(dir), cfg, project, &sessions)?;
        }
        build_plan(cfg, session, project).map(Some)
    }

    async fn launch_tmux(&self, name: &str, plan: &StartPlan) -> Result<()> {
        if let Err(error) = self
            .tmux
            .spawn(name, &plan.dir, plan.cols, plan.rows, &plan.argv, plan.host)
            .await
        {
            if plan.is_worker() {
                // tmux includes its complete argv in command errors. Do not let the worker's
                // bearer credential escape into a persisted task failure note or daemon log.
                tracing::warn!("could not create worker session {name}: tmux spawn failed");
                return Err(anyhow!("could not create worker session {name}"));
            }
            return Err(error);
        }

        if plan.is_worker() {
            let durable = !self.is_ephemeral(name).await;
            if let Err(error) = self
                .tmux
                .set_worker_metadata(
                    name,
                    &plan.session.parent,
                    &plan.session.task_id,
                    durable,
                    &plan.session.state_id,
                )
                .await
            {
                tracing::warn!("could not persist worker metadata for {name}: {error:#}");
            }
        }
        if plan.host {
            if let Err(error) = self
                .tmux
                .set_host_metadata(name, &plan.session.project, &plan.dir)
                .await
            {
                tracing::warn!("could not persist host metadata for {name}: {error:#}");
            }
            self.remember_host_path(name, &plan.dir).await;
        }
        Ok(())
    }

    pub async fn start(self: &Arc<Self>, name: &str) -> Result<()> {
        self.session_operation(self.start_within_boundary(name))
            .await
    }

    async fn start_within_boundary(self: &Arc<Self>, name: &str) -> Result<()> {
        let plan = self.prepare_start(name).await?;
        if let Some(launch) = &plan.launch {
            // Save before tmux receives the command so a rejected launch remains inspectable.
            if let Err(error) = launch.save(&plan.session) {
                self.cleanup_failed_start(name, plan.is_worker()).await;
                return Err(error.context("saving sandbox launch plan"));
            }
            tracing::info!("starting {name}:\n{}", launch.render_human());
        } else {
            tracing::info!("starting {name} (host terminal)");
        }
        if let Err(error) = self.launch_tmux(name, &plan).await {
            self.cleanup_failed_start(name, plan.is_worker()).await;
            return Err(error);
        }

        self.clear_activity(name).await;
        let auto_resume_pending = !plan.host && plan.session.auto_resume && !plan.is_worker();
        let (title_was_cleared, run_id, replaced_reader) = {
            let mut live = self.live.write().await;
            if let Some(live) = live.get_mut(name) {
                let had_title = live.title.override_title.is_some();
                let replaced_reader = take_reader_for_abort(live);
                reset_process_state(live);
                live.last_change = 0;
                live.state_since = 0;
                live.auto_resume_pending = auto_resume_pending;
                (had_title, live.run_id, replaced_reader)
            } else {
                (false, 0, ReaderDisposition::None)
            }
        };
        finish_reader(replaced_reader);
        self.clear_latest_title(name);
        if title_was_cleared || auto_resume_pending {
            self.announce_sessions().await;
        }

        match self.spawn_reader(name).await {
            Ok(true) => {}
            Ok(false) => {
                self.cleanup_failed_start(name, plan.is_worker()).await;
                return Err(anyhow!(
                    "control reader for {name} was not attached during startup"
                ));
            }
            Err(error) => {
                self.cleanup_failed_start(name, plan.is_worker()).await;
                return Err(error.context(format!("starting session {name} reader")));
            }
        }
        self.wire_live_state(name, &plan.cfg, &plan.session, &plan.project, plan.host)
            .await;
        if auto_resume_pending {
            self.queue_auto_resume(name, run_id);
        }
        Ok(())
    }

    async fn cleanup_failed_start(&self, name: &str, worker: bool) {
        // A session created above is not useful without a newly attached control reader. Treat
        // a refused attach as a failed start so a stale reader cannot make it look healthy.
        let ephemeral = self
            .live
            .read()
            .await
            .get(name)
            .filter(|live| live.ephemeral && !live.host)
            .map(|live| live.cfg.clone());
        if self.tmux.exists(name).await {
            if let Err(cleanup) = self.tmux.kill(name).await {
                tracing::warn!("could not clean up failed start {name}: {cleanup:#}");
            }
        }
        if worker {
            self.invalidate_session(name).await;
        }
        if let Some(session) = ephemeral {
            if let Err(error) = crate::sandbox::remove_ephemeral_state(&session) {
                tracing::warn!("removing temporary private state for {name}: {error:#}");
            }
        }
    }

    pub(super) async fn wire_live_state(
        &self,
        name: &str,
        cfg: &Config,
        session: &SessionCfg,
        project: &ProjectCfg,
        host: bool,
    ) {
        let discovery = if !host
            && cfg.daemon.experimental_instructions
            && session.slopworld_md
            && cfg.daemon.instructions.breadcrumb_enabled
        {
            let discovery = crate::manifest::render_breadcrumb(
                &cfg.daemon.instructions.breadcrumb,
                &project.name,
                &cfg.daemon.instructions.mount_path,
            );
            (!discovery.trim().is_empty()).then_some(discovery)
        } else {
            None
        };
        let mut live = self.live.write().await;
        if let Some(live) = live.get_mut(name) {
            live.breadcrumbs.clear();
            live.breadcrumbs_pending = false;
            if let Some(discovery) = discovery {
                live.breadcrumbs = breadcrumb_block(&[discovery]).into_bytes();
                live.breadcrumbs_pending = true;
            }
        }
    }
}
