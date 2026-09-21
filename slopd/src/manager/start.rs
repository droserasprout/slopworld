//! Session target preparation and process startup.

use super::super::*;
use super::session_lifecycle::{
    finish_reader, reset_process_state, take_reader_for_abort, ReaderDisposition,
};
use crate::sandbox::{build_plan, LaunchPlan};
use anyhow::{anyhow, Context};

struct StartPlan {
    session: SessionCfg,
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

        let launch = match self.build_start_plan(&cfg, &session, &project, host).await {
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
            session,
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
        host: bool,
    ) -> Result<Option<LaunchPlan>> {
        if host {
            return Ok(None);
        }

        crate::sandbox::prepare_network(cfg, session, project)?;
        build_plan(cfg, session, project).map(Some)
    }

    async fn launch_tmux(&self, name: &str, plan: &StartPlan) -> Result<()> {
        if let Err(error) = self
            .tmux
            // Keep the pane silent until the control reader is attached. Capturing an
            // already running command and then attaching loses bytes between those steps.
            .spawn(
                name,
                &plan.dir,
                plan.cols,
                plan.rows,
                &["sleep".into(), "2147483647".into()],
                plan.host,
            )
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
        if let Err(error) = self.tmux.start_command(name, &plan.dir, &plan.argv).await {
            self.cleanup_failed_start(name, plan.is_worker()).await;
            if plan.is_worker() {
                return Err(anyhow!("could not start worker session {name}"));
            }
            return Err(error.context(format!("starting session {name} command")));
        }
        self.wire_live_state(name).await;
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

    pub(super) async fn wire_live_state(&self, name: &str) {
        let mut live = self.live.write().await;
        if let Some(live) = live.get_mut(name) {
            live.breadcrumbs.clear();
            live.breadcrumbs_pending = false;
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn manager_for_start() -> Arc<Manager> {
        let mut cfg = Config::default();
        cfg.projects.push(ProjectCfg {
            name: "project".into(),
            dir: std::env::temp_dir().to_string_lossy().into_owned(),
            ..Default::default()
        });
        cfg.sessions.push(SessionCfg {
            name: "agent".into(),
            project: "project".into(),
            cmd: Some("printf ready".into()),
            ..Default::default()
        });
        crate::session::test_manager_with_socket(
            cfg,
            format!("slopd-start-{}", uuid::Uuid::new_v4()),
        )
    }

    #[tokio::test]
    async fn target_resolution_reports_missing_sessions_and_projects() {
        let manager = manager_for_start();
        let mut cfg = manager.config().await;
        assert!(manager
            .resolve_target(&cfg, "missing")
            .await
            .unwrap_err()
            .to_string()
            .contains("no such session: missing"));
        cfg.sessions[0].project.clear();
        assert!(manager
            .resolve_target(&cfg, "agent")
            .await
            .unwrap_err()
            .to_string()
            .contains("belongs to no project"));
        cfg.sessions[0].project = "deleted".into();
        assert!(manager
            .resolve_target(&cfg, "agent")
            .await
            .unwrap_err()
            .to_string()
            .contains("project deleted, which does not exist"));
        let session = SessionCfg {
            name: "ephemeral".into(),
            project: "project".into(),
            ..Default::default()
        };
        manager.live.write().await.insert(
            session.name.clone(),
            Live::new(session, TitleCapture::default()),
        );
        let (session, project) = manager.resolve_target(&cfg, "ephemeral").await.unwrap();
        assert_eq!(session.name, "ephemeral");
        assert_eq!(project.name, "project");
    }

    #[test]
    fn directory_validation_creates_only_temporary_projects_and_rejects_files() {
        let root = std::env::temp_dir().join(format!("slopd-start-dir-{}", uuid::Uuid::new_v4()));
        let mut project = ProjectCfg {
            name: "project".into(),
            dir: root.join("nested").to_string_lossy().into_owned(),
            ..Default::default()
        };
        assert!(Manager::validate_dir(&project)
            .unwrap_err()
            .to_string()
            .contains("not a directory"));
        assert!(!root.exists());
        project.temp = true;
        assert_eq!(Manager::validate_dir(&project).unwrap(), project.dir);
        project.temp = false;
        assert_eq!(Manager::validate_dir(&project).unwrap(), project.dir);
        let file = root.join("file");
        std::fs::write(&file, "occupied").unwrap();
        project.dir = file.to_string_lossy().into_owned();
        assert!(Manager::validate_dir(&project).is_err());
        project.temp = true;
        assert!(Manager::validate_dir(&project)
            .unwrap_err()
            .to_string()
            .contains("making"));
        project.dir = "/".into();
        project.temp = false;
        assert!(Manager::validate_dir(&project)
            .unwrap_err()
            .to_string()
            .contains("cannot live at"));
        std::fs::remove_dir_all(root).unwrap();
    }

    #[tokio::test]
    async fn preparation_rejects_missing_commands_and_invalid_worker_ownership() {
        let manager = manager_for_start();
        {
            let mut cfg = manager.cfg.write().await;
            cfg.sessions[0].cmd = None;
            cfg.sessions[0].command = format!("missing-{}", uuid::Uuid::new_v4());
        }
        let error = manager.prepare_start("agent").await.err().unwrap();
        assert!(error.to_string().contains("has no file"));
        for (parent, task) in [("", "task"), ("parent", " ")] {
            {
                let mut cfg = manager.cfg.write().await;
                let session = &mut cfg.sessions[0];
                session.cmd = Some("printf ready".into());
                session.worker = true;
                session.parent = parent.into();
                session.task_id = task.into();
            }
            let error = manager.prepare_start("agent").await.err().unwrap();
            assert!(error.to_string().contains("incomplete task ownership"));
        }
        {
            let mut cfg = manager.cfg.write().await;
            cfg.sessions[0].task_id = "task".into();
            cfg.sessions[0].network = NetworkMode::None;
        }
        let error = manager.prepare_start("agent").await.err().unwrap();
        assert!(error.to_string().contains("networking disabled"));
        assert!(!manager.tmux.exists("agent").await);
    }

    #[tokio::test]
    async fn host_preparation_prefers_remembered_directory_and_falls_back_if_removed() {
        let manager = manager_for_start();
        let cfg = manager.config().await;
        let mut live = Live::new(cfg.sessions[0].clone(), TitleCapture::default());
        live.host = true;
        live.cols = 101;
        live.rows = 37;
        live.host_path = manager
            .cfg_path
            .parent()
            .unwrap()
            .to_string_lossy()
            .into_owned();
        let remembered = live.host_path.clone();
        manager.live.write().await.insert("agent".into(), live);
        let plan = manager.prepare_start("agent").await.unwrap();
        assert!(plan.host);
        assert!(!plan.is_worker());
        assert!(plan.launch.is_none());
        assert_eq!((plan.cols, plan.rows), (101, 37));
        assert_eq!(plan.dir, remembered);
        assert!(!plan.argv.is_empty());
        manager
            .live
            .write()
            .await
            .get_mut("agent")
            .unwrap()
            .host_path = manager
            .cfg_path
            .with_extension("missing")
            .to_string_lossy()
            .into_owned();
        let plan = manager.prepare_start("agent").await.unwrap();
        assert_eq!(plan.dir, cfg.projects[0].dir);
    }

    #[tokio::test]
    async fn command_waits_for_reader_and_first_screen_is_complete() {
        let socket = format!("slopd-start-{}", uuid::Uuid::new_v4());
        let root = std::env::temp_dir().join(&socket);
        std::fs::create_dir_all(&root).unwrap();
        let manager = crate::session::test_manager_with_socket(Config::default(), socket.clone());
        let session = SessionCfg {
            name: "preview".into(),
            ..Default::default()
        };
        let plan = StartPlan {
            session: session.clone(),
            cols: 80,
            rows: 24,
            host: true,
            dir: root.to_string_lossy().into_owned(),
            launch: None,
            argv: vec![
                "/bin/sh".into(),
                "-c".into(),
                // One startup burst followed by silence: no input/redraw can repair a loss.
                r"touch started; printf '\033[?1049h\033[Hfirst row\033[23;1Hlast row'; exec sleep 60".into(),
            ],
        };
        manager.live.write().await.insert(
            "preview".into(),
            Live::new(session, TitleCapture::default()),
        );
        let result: Result<()> = async {
            manager.launch_tmux("preview", &plan).await?;
            // Widen the former capture/attach gap deterministically.
            tokio::time::sleep(Duration::from_millis(150)).await;
            anyhow::ensure!(
                !root.join("started").exists(),
                "command ran before reader attach"
            );
            anyhow::ensure!(manager.spawn_reader("preview").await?);
            manager
                .tmux
                .start_command("preview", &plan.dir, &plan.argv)
                .await?;
            tokio::time::timeout(Duration::from_secs(5), async {
                loop {
                    let complete = {
                        let live = manager.live.read().await;
                        let mut emu = live["preview"].emu.as_ref().unwrap().lock().unwrap();
                        let frame = emu.render();
                        frame.alt_screen
                            && frame.lines[0].contains("first row")
                            && frame.lines[22].contains("last row")
                    };
                    if complete {
                        break;
                    }
                    tokio::time::sleep(Duration::from_millis(10)).await;
                }
            })
            .await
            .context("initial screen never reached the mirror")?;
            Ok(())
        }
        .await;
        let _ = manager.tmux.kill("preview").await;
        let _ = tokio::process::Command::new("tmux")
            .args(["-L", &socket, "kill-server"])
            .output()
            .await;
        std::fs::remove_dir_all(root).unwrap();
        result.unwrap();
    }
}
