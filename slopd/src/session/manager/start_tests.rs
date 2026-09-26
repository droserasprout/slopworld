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
    crate::session::test_manager_with_socket(cfg, format!("slopd-start-{}", uuid::Uuid::new_v4()))
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
        .contains("could not create project directory"));
    project.dir = "/".into();
    project.temp = false;
    assert!(Manager::validate_dir(&project)
        .unwrap_err()
        .to_string()
        .contains("overlaps a protected location"));
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
    assert!(error.to_string().contains("has no command"));
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
        assert!(error
            .to_string()
            .contains("must have a task ID and a parent session"));
    }
    {
        let mut cfg = manager.cfg.write().await;
        cfg.sessions[0].task_id = "task".into();
        cfg.sessions[0].network = NetworkMode::None;
    }
    let error = manager.prepare_start("agent").await.err().unwrap();
    assert!(error.to_string().contains("needs network access"));
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
            r"touch started; printf '\033[?1049h\033[Hfirst row\033[23;1Hlast row'; exec sleep 60"
                .into(),
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
                    let mut emu = live["preview"]
                        .capture
                        .emu
                        .as_ref()
                        .unwrap()
                        .lock()
                        .unwrap();
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
