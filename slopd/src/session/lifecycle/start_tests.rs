use super::*;
use crate::session::{AgentLifetime, SessionRole};

fn manager_for_start() -> Arc<SessionRuntime> {
    let mut cfg = Config::default();
    cfg.daemon.token = "test-root-token".into();
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
    assert!(
        manager
            .resolve_target(&cfg, "missing")
            .await
            .unwrap_err()
            .to_string()
            .contains("no such session: missing")
    );
    cfg.sessions[0].project.clear();
    assert!(
        manager
            .resolve_target(&cfg, "agent")
            .await
            .unwrap_err()
            .to_string()
            .contains("belongs to no project")
    );
    cfg.sessions[0].project = "deleted".into();
    assert!(
        manager
            .resolve_target(&cfg, "agent")
            .await
            .unwrap_err()
            .to_string()
            .contains("project deleted, which does not exist")
    );
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
fn directory_preparation_creates_only_temporary_projects_and_rejects_files() {
    let root = std::env::temp_dir().join(format!("slopd-start-dir-{}", uuid::Uuid::new_v4()));
    let mut project = ProjectCfg {
        name: "project".into(),
        dir: root.join("nested").to_string_lossy().into_owned(),
        ..Default::default()
    };
    assert!(
        prepare_project_dir(&project)
            .unwrap_err()
            .to_string()
            .contains("not a directory")
    );
    assert!(!root.exists());
    project.temp = true;
    assert_eq!(prepare_project_dir(&project).unwrap(), project.dir);
    project.temp = false;
    assert_eq!(prepare_project_dir(&project).unwrap(), project.dir);
    let file = root.join("file");
    std::fs::write(&file, "occupied").unwrap();
    project.dir = file.to_string_lossy().into_owned();
    prepare_project_dir(&project).unwrap_err();
    project.temp = true;
    assert!(
        prepare_project_dir(&project)
            .unwrap_err()
            .to_string()
            .contains("could not create project directory")
    );
    project.dir = "/".into();
    project.temp = false;
    assert!(
        prepare_project_dir(&project)
            .unwrap_err()
            .to_string()
            .contains("overlaps a protected location")
    );
    std::fs::remove_dir_all(root).unwrap();
}

#[tokio::test]
async fn preparation_rejects_missing_commands_and_invalid_worker_ownership() {
    let manager = manager_for_start();
    {
        let mut cfg = manager.config_authority.write().await;
        cfg.sessions[0].cmd = None;
        cfg.sessions[0].command = format!("missing-{}", uuid::Uuid::new_v4());
    }
    let error = manager.prepare_start("agent").await.err().unwrap();
    assert!(error.to_string().contains("has no command"));
    for (parent, task) in [("", "task"), ("parent", " ")] {
        {
            let mut cfg = manager.config_authority.write().await;
            let session = &mut cfg.sessions[0];
            session.cmd = Some("printf ready".into());
            session.worker = true;
            session.parent = parent.into();
            session.task_id = task.into();
        }
        let error = manager.prepare_start("agent").await.err().unwrap();
        assert!(
            error
                .to_string()
                .contains("must have a task ID and a parent session")
        );
    }
    {
        let mut cfg = manager.config_authority.write().await;
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
    live.role = SessionRole::HostAction;
    live.cols = 101;
    live.rows = 37;
    live.host_path = manager
        .cfg_path()
        .parent()
        .unwrap()
        .to_string_lossy()
        .into_owned();
    let remembered = live.host_path.clone();
    manager.config_authority.write().await.sessions.clear();
    manager.live.write().await.insert("agent".into(), live);
    let plan = manager.prepare_start("agent").await.unwrap();
    assert!(plan.role.is_host());
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
        .cfg_path()
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
    let socket = root.join("tmux").to_str().unwrap().to_owned();
    let manager = crate::session::test_manager_with_socket(Config::default(), socket.clone());
    let session = SessionCfg {
        name: "preview".into(),
        ..Default::default()
    };
    let plan = StartPlan {
        context: LaunchContext::default(),
        session: session.clone(),
        cols: 80,
        rows: 24,
        role: SessionRole::HostAction,
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
    drop(manager.tmux.kill("preview").await);
    drop(
        tokio::process::Command::new("tmux")
            .args(["-S", &socket, "kill-server"])
            .output()
            .await,
    );
    std::fs::remove_dir_all(root).unwrap();
    result.unwrap();
}

#[tokio::test]
async fn host_start_rejects_duplicate_without_replacing_the_running_pane() {
    let manager = manager_for_start();
    let cfg = manager.config().await;
    let mut row = Live::new(cfg.sessions[0].clone(), TitleCapture::default());
    row.role = SessionRole::HostAction;
    row.run_id = 7;
    row.cfg.cmd = Some("/bin/sleep 60".into());
    manager.live.write().await.insert("agent".into(), row);
    manager.config_authority.write().await.sessions.clear();

    let result: Result<()> = async {
        manager.start("agent").await?;
        let run_id = {
            let live = manager.live.read().await;
            let row = &live["agent"];
            assert!(row.run_id > 7);
            assert!(row.capture.reader.is_some());
            assert!(!row.input.auto_resume_pending);
            row.run_id
        };
        let error = manager.start("agent").await.unwrap_err();
        assert!(error.to_string().contains("already running"));
        assert!(manager.tmux.exists("agent").await);
        assert_eq!(manager.live.read().await["agent"].run_id, run_id);
        Ok(())
    }
    .await;

    manager.stop("agent").await.unwrap();
    std::fs::remove_dir_all(manager.cfg_path().parent().unwrap()).unwrap();
    result.unwrap();
}

#[tokio::test]
async fn failed_worker_start_removes_owned_state_and_revokes_credentials() {
    let Some(root) = crate::test_support::isolated() else {
        return;
    };
    let manager = manager_for_start();
    let mut session = manager.config().await.sessions[0].clone();
    session.state_id = crate::storage_id::draft_identity();
    let private = root.join("data/sessions").join(&session.state_id);
    std::fs::create_dir_all(&private).unwrap();
    let unrelated = root.join("data/sessions/unrelated");
    std::fs::create_dir_all(&unrelated).unwrap();
    let mut row = Live::new(session, TitleCapture::default());
    row.role = SessionRole::Agent(AgentLifetime::Temporary);
    manager.live.write().await.insert("agent".into(), row);
    let token = manager
        .mint_grant(
            "agent".into(),
            vec!["agent".into()],
            crate::grant::Level::Rw,
        )
        .await
        .unwrap();
    let cap = manager
        .config_authority
        .resolve_cap(Some(&token))
        .await
        .unwrap();

    manager.cleanup_failed_start("agent", true).await;

    assert!(!cap.is_valid());
    assert!(
        manager
            .config_authority
            .resolve_cap(Some(&token))
            .await
            .is_none()
    );
    assert!(!private.exists());
    assert!(unrelated.exists());
    assert!(!manager.tmux.exists("agent").await);
    // Cleanup can repeat after a partially completed failure path.
    manager.cleanup_failed_start("agent", true).await;
    std::fs::remove_dir_all(manager.cfg_path().parent().unwrap()).unwrap();
}

#[tokio::test]
async fn start_rejects_retained_incompatible_identities_before_launch() {
    for host in [true, false] {
        let manager = manager_for_start();
        let cfg = manager.config().await;
        let mut live = Live::new(cfg.sessions[0].clone(), TitleCapture::default());
        live.role = if host {
            SessionRole::HostAction
        } else {
            SessionRole::Agent(AgentLifetime::Durable)
        };
        if !host {
            live.cfg.state_id = crate::storage_id::draft_identity();
        }
        manager.live.write().await.insert("agent".into(), live);
        let error = manager.prepare_start("agent").await.err().unwrap();
        assert!(
            error.to_string().contains("incompatible live identity"),
            "{error:#}"
        );
        assert!(!manager.tmux.exists("agent").await);
    }
}

#[tokio::test]
async fn malformed_commands_reject_before_tmux_creation_and_revoke_worker_credentials() {
    for host in [false, true] {
        let manager = manager_for_start();
        let mut session = manager.config().await.sessions[0].clone();
        session.cmd = Some("printf 'unfinished".into());
        session.worker = !host;
        session.parent = "parent".into();
        session.task_id = "task".into();
        if host {
            let mut row = Live::new(session, TitleCapture::default());
            row.role = SessionRole::HostAction;
            manager.config_authority.write().await.sessions.clear();
            manager.live.write().await.insert("agent".into(), row);
        } else {
            manager.config_authority.write().await.sessions[0] = session;
        }
        assert_eq!(
            manager
                .config_authority
                .grants_for_test()
                .read()
                .await
                .count(),
            0
        );
        let error = manager.prepare_start("agent").await.err().unwrap();
        assert!(error.to_string().contains("invalid command"), "{error:#}");
        assert_eq!(
            manager
                .config_authority
                .grants_for_test()
                .read()
                .await
                .count(),
            0
        );
        assert!(!manager.tmux.exists("agent").await);
        std::fs::remove_dir_all(manager.cfg_path().parent().unwrap()).unwrap();
    }
}

#[tokio::test]
async fn start_plan_snapshots_reader_context_and_publishes_recoverable_metadata() {
    let socket = crate::test_support::TmuxSocket::new();
    let cfg = Config {
        projects: vec![ProjectCfg {
            name: "repo".into(),
            dir: "/tmp".into(),
            ..Default::default()
        }],
        ..Default::default()
    };
    let manager = crate::session::test_manager_with_socket(cfg.clone(), socket.path.clone());
    let item = LibraryItemCfg {
        name: "view-file.rs".into(),
        project: "repo".into(),
        host: true,
        kind: LibraryItemKind::Shell,
        command: Some("cat /tmp/file.rs".into()),
        ..Default::default()
    };
    let want = RunWhere {
        context: LaunchContext {
            intent: "view".into(),
            reader: ReaderContext {
                path: "/tmp/file.rs".into(),
                key: "key".into(),
                scope: "scope".into(),
                pinned: false,
                line: 7,
            },
            ..Default::default()
        },
        ..Default::default()
    };
    let name = manager
        .create_test_errand_session(&cfg, &item, &want, true, false, "")
        .await
        .unwrap();
    let plan = manager.prepare_start(&name).await.unwrap();
    assert_eq!(plan.context.reader, want.context.reader);
    assert_eq!(plan.context.original_label, "file.rs");
    manager.launch_tmux(&name, &plan).await.unwrap();
    let metadata = manager.tmux.reader_metadata(&name).await.unwrap();
    assert_eq!(LaunchContext::from(&metadata), plan.context);
    manager.set_reader_pinned(&name, true).await.unwrap();
    manager
        .edits()
        .set_label(&name, "custom".into())
        .await
        .unwrap();
    let metadata = manager.tmux.reader_metadata(&name).await.unwrap();
    assert!(metadata.pinned);
    assert_eq!(metadata.label, "custom");
    assert_eq!(metadata.original_label, "file.rs");
    manager
        .edits()
        .set_label(&name, String::new())
        .await
        .unwrap();
    assert_eq!(
        manager.tmux.reader_metadata(&name).await.unwrap().label,
        "file.rs"
    );
    assert!(
        !plan.context.reader.pinned,
        "the owned start plan changed after preparation"
    );
    let saved = Config::load(manager.cfg_path()).await.unwrap();
    assert!(saved.sessions.is_empty() && saved.host_terminals.is_empty());
}

#[tokio::test]
async fn temporary_agent_adoption_preserves_configuration_and_cleans_original_state() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    let socket = crate::test_support::TmuxSocket::new();
    let cfg = Config::default();
    let manager = crate::session::test_manager_with_socket(cfg.clone(), socket.path.clone());
    let session = SessionCfg {
        name: "temporary-agent".into(),
        project: "original-project".into(),
        worktree: "original-checkout".into(),
        label: Some("Original label".into()),
        command: "codex".into(),
        sandbox: vec!["original-preset".into()],
        state_id: crate::storage_id::draft_identity(),
        ..Default::default()
    };
    let private = crate::sandbox::state_dir(&session).unwrap();
    std::fs::create_dir_all(&private).unwrap();
    std::fs::write(private.join("proof"), "original private data").unwrap();
    let unrelated = crate::sandbox::state_dir(&SessionCfg::default()).unwrap();
    std::fs::create_dir_all(&unrelated).unwrap();
    let plan = StartPlan {
        session: session.clone(),
        context: LaunchContext::default(),
        cols: 80,
        rows: 24,
        role: SessionRole::Agent(AgentLifetime::Temporary),
        dir: "/tmp".into(),
        launch: None,
        argv: vec!["sleep".into(), "60".into()],
    };
    // Exercise the metadata written by startup without needing a sandbox process.
    manager.launch_tmux(&session.name, &plan).await.unwrap();
    let recovered = crate::session::test_manager_with_socket(cfg, socket.path.clone());
    recovered.sync_from_config().await;
    recovered.sync_from_config().await;
    {
        let live = recovered.live.read().await;
        let row = &live[&session.name];
        assert!(row.role.is_temporary_agent());
        assert_eq!(row.cfg.state_id, session.state_id);
        assert_eq!(row.cfg.project, session.project);
        assert_eq!(row.cfg.worktree, session.worktree);
        assert_eq!(row.cfg.label, session.label);
        assert_eq!(row.cfg.command, session.command);
        assert_eq!(row.cfg.sandbox, session.sandbox);
    }
    recovered.stop(&session.name).await.unwrap();
    recovered.stop(&session.name).await.unwrap();
    assert!(!private.exists());
    assert!(unrelated.exists());
}
