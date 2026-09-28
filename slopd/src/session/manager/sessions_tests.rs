use super::super::lifecycle::start::prepare_project_dir;
use super::*;

async fn rename_fixture(running: bool) -> (Arc<Manager>, std::path::PathBuf, String) {
    let root = std::env::temp_dir().join(format!(
        "slopd-session-rename-{}-{}",
        std::process::id(),
        uuid::Uuid::new_v4()
    ));
    std::fs::create_dir_all(&root).unwrap();
    let socket = format!("slopd-session-rename-{}", uuid::Uuid::new_v4());
    let manager = crate::session::test_manager_with_socket(
        Config {
            projects: vec![ProjectCfg {
                name: "repo".into(),
                dir: root.to_string_lossy().into_owned(),
                ..Default::default()
            }],
            sessions: vec![SessionCfg {
                name: "old".into(),
                project: "repo".into(),
                ..Default::default()
            }],
            ..Default::default()
        },
        socket.clone(),
    );
    manager.update_cfg(|_| Ok(())).await.unwrap();
    if running {
        manager
            .tmux
            .spawn(
                "old",
                &root.to_string_lossy(),
                80,
                24,
                &["cat".into()],
                false,
            )
            .await
            .unwrap();
    }
    let session = manager.config().await.sessions[0].clone();
    manager
        .live
        .write()
        .await
        .insert("old".into(), Live::new(session, TitleCapture::default()));
    if running {
        assert!(manager.spawn_reader("old").await.unwrap());
        assert_rename_input(&manager, "old", "before-rename").await;
    }
    (manager, root, socket)
}

async fn assert_rename_input(manager: &Arc<Manager>, name: &str, marker: &str) {
    manager
        .queue_input(name, Input::Bytes(format!("{marker}\n").into_bytes()))
        .await;
    tokio::time::timeout(Duration::from_secs(5), async {
        loop {
            if manager.live.read().await.get(name).is_some_and(|live| {
                live.screen
                    .as_ref()
                    .is_some_and(|screen| screen.lines.iter().any(|line| line.contains(marker)))
            }) {
                break;
            }
            tokio::time::sleep(Duration::from_millis(10)).await;
        }
    })
    .await
    .expect("input did not reach the managed capture reader");
}

async fn cleanup_rename_fixture(
    manager: &Arc<Manager>,
    root: std::path::PathBuf,
    socket: &str,
    names: &[&str],
) {
    for name in names {
        if manager.tmux.exists(name).await {
            let _ = manager.tmux.kill(name).await;
        }
    }
    let _ = std::process::Command::new("tmux")
        .args(["-L", socket, "kill-server"])
        .output();
    let _ = std::fs::remove_dir_all(root);
    let _ = std::fs::remove_dir_all(manager.cfg_path.parent().unwrap());
}

fn replacement(name: &str) -> SessionCfg {
    SessionCfg {
        name: name.into(),
        project: "repo".into(),
        ..Default::default()
    }
}

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

    let error = prepare_project_dir(&project("ordinary", &missing, false))
        .unwrap_err()
        .to_string();
    assert!(error.contains("is not a directory"), "{error}");

    assert_eq!(
        prepare_project_dir(&project("temporary", &missing, true)).unwrap(),
        missing.to_string_lossy()
    );
    assert!(missing.is_dir());
    std::fs::remove_dir_all(root).unwrap();
}

#[test]
fn project_directory_rejects_a_protected_ancestor() {
    let error = prepare_project_dir(&project("world", std::path::Path::new("/"), false))
        .unwrap_err()
        .to_string();
    assert!(error.contains("the whole filesystem"), "{error}");
}

#[tokio::test]
async fn invalid_or_conflicting_renames_do_not_touch_tmux_or_config() {
    let (manager, root, socket) = rename_fixture(true).await;
    manager
        .update_cfg(|cfg| {
            cfg.sessions.push(replacement("occupied"));
            Ok(())
        })
        .await
        .unwrap();
    let persisted = std::fs::read(&manager.cfg_path).unwrap();
    for invalid in [
        replacement("occupied"),
        SessionCfg {
            name: "renamed".into(),
            project: "missing".into(),
            ..Default::default()
        },
        SessionCfg {
            name: "renamed".into(),
            project: "repo".into(),
            sandbox: vec!["missing-preset".into()],
            ..Default::default()
        },
        SessionCfg {
            name: "renamed".into(),
            project: "repo".into(),
            limits: crate::config::Limits {
                memory_mb: Some(0),
                ..Default::default()
            },
            ..Default::default()
        },
    ] {
        assert!(manager.update("old", invalid).await.is_err());
        assert!(manager.tmux.exists("old").await);
        assert!(!manager.tmux.exists("renamed").await);
        assert_eq!(manager.config().await.sessions[0].name, "old");
        assert_eq!(manager.config().await.sessions[1].name, "occupied");
        assert!(manager.live.read().await.contains_key("old"));
        assert_eq!(std::fs::read(&manager.cfg_path).unwrap(), persisted);
    }
    cleanup_rename_fixture(&manager, root, &socket, &["old", "renamed"]).await;
}

#[tokio::test]
async fn running_and_down_renames_preserve_private_state() {
    for running in [false, true] {
        let (manager, root, socket) = rename_fixture(running).await;
        let state_id = manager.config().await.sessions[0].state_id.clone();
        manager.update("old", replacement("renamed")).await.unwrap();

        assert!(!manager.tmux.exists("old").await);
        assert_eq!(manager.tmux.exists("renamed").await, running);
        if running {
            assert_rename_input(&manager, "renamed", "after-rename").await;
        }
        let cfg = manager.config().await;
        assert_eq!(cfg.sessions[0].name, "renamed");
        assert_eq!(cfg.sessions[0].state_id, state_id);
        let saved = Config::load(&manager.cfg_path).await.unwrap();
        assert_eq!(saved.sessions[0].name, "renamed");
        assert_eq!(saved.sessions[0].state_id, state_id);
        assert!(manager.live.read().await.contains_key("renamed"));
        assert!(!manager.live.read().await.contains_key("old"));
        cleanup_rename_fixture(&manager, root, &socket, &["old", "renamed"]).await;
    }
}

#[tokio::test]
async fn persistence_failure_restores_tmux_or_reports_failed_rollback() {
    for rollback_fails in [false, true] {
        let (manager, root, socket) = rename_fixture(true).await;
        let persisted = std::fs::read(&manager.cfg_path).unwrap();
        std::fs::create_dir_all(manager.cfg_path.with_extension("toml.tmp")).unwrap();
        if rollback_fails {
            manager.tmux.fail_rename_call_for_test(2);
        }

        let error = manager
            .update("old", replacement("renamed"))
            .await
            .unwrap_err()
            .to_string();
        assert!(
            error.contains("Could not persist session rename"),
            "{error}"
        );
        if rollback_fails {
            assert!(error.contains("Could not restore tmux to old"), "{error}");
            assert!(
                error.contains("Actual tmux identity: renamed session renamed is present"),
                "{error}"
            );
            assert!(error.contains("Manual recovery is required"), "{error}");
        } else {
            assert!(error.contains("Restored tmux to old"), "{error}");
            assert_rename_input(&manager, "old", "after-rollback").await;
        }
        assert_eq!(manager.tmux.exists("old").await, !rollback_fails);
        assert_eq!(manager.tmux.exists("renamed").await, rollback_fails);
        assert_eq!(manager.config().await.sessions[0].name, "old");
        assert!(manager.live.read().await.contains_key("old"));
        assert_eq!(std::fs::read(&manager.cfg_path).unwrap(), persisted);
        cleanup_rename_fixture(&manager, root, &socket, &["old", "renamed"]).await;
    }
}

#[tokio::test]
async fn abandoning_update_cannot_skip_commit_or_rollback() {
    let (manager, root, socket) = rename_fixture(true).await;
    std::fs::create_dir_all(manager.cfg_path.with_extension("toml.tmp")).unwrap();
    let (renamed, release) = manager.tmux.pause_after_rename_for_test();
    let update = tokio::spawn({
        let manager = manager.clone();
        async move { manager.update("old", replacement("renamed")).await }
    });
    renamed.notified().await;
    update.abort();
    release.notify_one();

    tokio::time::timeout(std::time::Duration::from_secs(2), async {
        loop {
            if manager.tmux.exists("old").await && !manager.tmux.exists("renamed").await {
                break;
            }
            tokio::time::sleep(std::time::Duration::from_millis(10)).await;
        }
    })
    .await
    .expect("detached update did not finish rollback");
    assert_eq!(manager.config().await.sessions[0].name, "old");
    cleanup_rename_fixture(&manager, root, &socket, &["old", "renamed"]).await;
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
async fn retick_moves_a_quiet_working_session_to_idle() {
    let manager = crate::session::test_manager(Config::default());
    let mut live = Live::new(
        SessionCfg {
            name: "agent".into(),
            ..Default::default()
        },
        TitleCapture::default(),
    );
    live.state = State::Working;
    live.state_since = 0;
    live.last_change = 0;
    live.seq = 1;
    live.screen = Some(ScreenView {
        input_timings: Vec::new(),
        name: "agent".into(),
        seq: 1,
        cols: 80,
        rows: 24,
        cx: 0,
        cy: 0,
        off: 0,
        history: 0,
        cursor_shape: 0,
        cursor_blink: false,
        app_mouse: false,
        app_drag: false,
        alt_screen: false,
        title: String::new(),
        request_id: 0,
        lines: Vec::new(),
    });
    manager.live.write().await.insert("agent".into(), live);

    manager.retick().await;

    assert_eq!(manager.live.read().await["agent"].state, State::Idle);
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
    assert_eq!(views[0].runtime.state, State::Working);
    assert_eq!(views[0].runtime.seq, 4);
    assert_eq!(views[1].dir, "/tmp/host-cwd");
    assert!(views[1].host && views[1].ephemeral);
    assert!(views[1].runtime.process_running);
}

#[test]
fn host_commands_update_foreground_process_state() {
    let mut live = Live::new(SessionCfg::default(), TitleCapture::default());
    assert!(update_host_process(&mut live, "python"));
    assert!(live.process_running);
    assert!(update_host_process(&mut live, "/bin/bash"));
    assert!(!live.process_running);
    assert!(!update_host_process(&mut live, "/bin/bash"));
}

async fn metadata_fixture() -> Arc<Manager> {
    let manager = crate::session::test_manager(Config::default());
    let dir = manager
        .cfg_path
        .parent()
        .unwrap()
        .to_string_lossy()
        .into_owned();
    manager
        .update_cfg(|cfg| {
            cfg.projects.push(ProjectCfg {
                name: "repo".into(),
                dir,
                ..Default::default()
            });
            cfg.sessions.push(SessionCfg {
                name: "agent".into(),
                project: "repo".into(),
                ..Default::default()
            });
            Ok(())
        })
        .await
        .unwrap();
    manager
        .remember_host_terminal("shell", "repo", "/old")
        .await
        .unwrap();
    for (name, host) in [("shell", true), ("agent", false)] {
        let mut live = Live::new(
            SessionCfg {
                name: name.into(),
                project: "repo".into(),
                ..Default::default()
            },
            TitleCapture::default(),
        );
        live.host = host;
        live.host_path = "/old".into();
        live.state = State::Idle;
        live.run_id = 17;
        manager.live.write().await.insert(name.into(), live);
    }
    manager
}

#[tokio::test]
async fn host_metadata_ignores_stale_runs_down_sessions_and_agents() {
    let manager = metadata_fixture().await;
    assert_eq!(
        manager.host_metadata_targets().await,
        vec![("shell".into(), 17)]
    );
    for (name, run, path) in [
        ("shell", 16, "/stale"),
        ("agent", 17, "/agent"),
        ("missing", 17, "/missing"),
        ("shell", 17, "  "),
    ] {
        assert!(!manager.remember_host_path_for_run(name, run, path).await);
    }
    manager.live.write().await.get_mut("shell").unwrap().state = State::Down;
    assert!(manager.host_metadata_targets().await.is_empty());
    assert!(
        !manager
            .remember_host_path_for_run("shell", 17, "/down")
            .await
    );
    assert_eq!(manager.live.read().await["shell"].host_path, "/old");
    assert_eq!(manager.config().await.host_terminals[0].path, "/old");
    std::fs::remove_dir_all(manager.cfg_path.parent().unwrap()).unwrap();
}

#[tokio::test]
async fn current_host_metadata_persists_path_and_updates_foreground_process() {
    let manager = metadata_fixture().await;
    let metadata = |path: &str, command: &str| {
        [(
            "shell".into(),
            crate::tmux::HostMetadata {
                path: Some(path.into()),
                command: Some(command.into()),
            },
        )]
        .into_iter()
        .collect()
    };
    manager
        .apply_host_metadata(vec![("shell".into(), 16)], metadata("/stale", "python"))
        .await;
    assert!(!manager.live.read().await["shell"].process_running);
    manager
        .apply_host_metadata(
            vec![("shell".into(), 17), ("missing".into(), 17)],
            metadata("/current", "python"),
        )
        .await;
    assert_eq!(manager.live.read().await["shell"].host_path, "/current");
    assert!(manager.live.read().await["shell"].process_running);
    let saved: Config =
        toml::from_str(&std::fs::read_to_string(&manager.cfg_path).unwrap()).unwrap();
    assert_eq!(saved.host_terminals[0].path, "/current");
    assert!(
        !manager
            .remember_host_path_for_run("shell", 17, "/current")
            .await
    );
    manager
        .apply_host_metadata(
            vec![("shell".into(), 17)],
            metadata("/current", "/bin/bash"),
        )
        .await;
    assert!(!manager.live.read().await["shell"].process_running);
    std::fs::remove_dir_all(manager.cfg_path.parent().unwrap()).unwrap();
}

#[tokio::test]
async fn remembered_host_edits_preserve_labels_and_reject_agent_collisions() {
    let manager = metadata_fixture().await;
    manager
        .set_label("shell", "  My shell  ".into())
        .await
        .unwrap();
    manager
        .remember_host_terminal("shell", "repo", "/new")
        .await
        .unwrap();
    let cfg = manager.config().await;
    assert_eq!(cfg.host_terminals.len(), 1);
    assert_eq!(cfg.host_terminals[0].label.as_deref(), Some("My shell"));
    assert_eq!(cfg.host_terminals[0].path, "/new");
    assert!(cfg.host_terminals[0].autostart);
    assert!(manager
        .remember_host_terminal("agent", "repo", "/collision")
        .await
        .unwrap_err()
        .to_string()
        .contains("conflicts"));
    assert_eq!(manager.config().await.host_terminals.len(), 1);
    std::fs::remove_dir_all(manager.cfg_path.parent().unwrap()).unwrap();
}

#[tokio::test]
async fn manual_labels_count_unicode_characters_clear_and_invalidate_pending_titles() {
    let manager = metadata_fixture().await;
    for name in ["agent", "shell"] {
        let request = {
            let mut live = manager.live.write().await;
            let row = live.get_mut(name).unwrap();
            row.title = TitleCapture::restored(Some("old automatic title".into()));
            pending_title(&mut row.title)
        };
        let label = "界".repeat(MAX_MANUAL_LABEL_CHARS);
        manager
            .set_label(name, format!("  {label}  "))
            .await
            .unwrap();
        {
            let live = manager.live.read().await;
            assert_eq!(live[name].cfg.label.as_deref(), Some(label.as_str()));
            assert!(!live[name].title.accepts(&request));
            if name == "shell" {
                assert!(live[name].title.title().is_none());
            }
        }
        assert!(manager
            .set_label(name, "界".repeat(MAX_MANUAL_LABEL_CHARS + 1))
            .await
            .is_err());
        assert_eq!(
            manager.live.read().await[name].cfg.label.as_deref(),
            Some(label.as_str())
        );
        manager.set_label(name, "  ".into()).await.unwrap();
        assert!(manager.live.read().await[name].cfg.label.is_none());
    }
    let saved: Config =
        toml::from_str(&std::fs::read_to_string(&manager.cfg_path).unwrap()).unwrap();
    assert!(saved.sessions[0].label.is_none());
    assert!(saved.host_terminals[0].label.is_none());
    assert!(manager
        .set_label("missing", "label".into())
        .await
        .unwrap_err()
        .to_string()
        .contains("no such session"));
    std::fs::remove_dir_all(manager.cfg_path.parent().unwrap()).unwrap();
}

#[tokio::test]
async fn ephemeral_labels_stay_runtime_only() {
    let manager = metadata_fixture().await;
    let before = std::fs::read(&manager.cfg_path).unwrap();
    let mut live = Live::new(
        SessionCfg {
            name: "worker".into(),
            project: "repo".into(),
            ..Default::default()
        },
        TitleCapture::default(),
    );
    live.ephemeral = true;
    manager.live.write().await.insert("worker".into(), live);
    manager
        .set_label("worker", "Temporary worker".into())
        .await
        .unwrap();
    assert_eq!(
        manager.live.read().await["worker"].cfg.label.as_deref(),
        Some("Temporary worker")
    );
    assert!(manager.config().await.session("worker").is_none());
    assert_eq!(std::fs::read(&manager.cfg_path).unwrap(), before);
    std::fs::remove_dir_all(manager.cfg_path.parent().unwrap()).unwrap();
}

#[tokio::test]
async fn failed_label_persistence_keeps_live_and_config_labels_unchanged() {
    let manager = metadata_fixture().await;
    manager.set_label("agent", "Original".into()).await.unwrap();
    let request = pending_title(&mut manager.live.write().await.get_mut("agent").unwrap().title);
    std::fs::remove_file(&manager.cfg_path).unwrap();
    std::fs::create_dir(&manager.cfg_path).unwrap();
    assert!(manager.set_label("agent", "Unsaved".into()).await.is_err());
    assert_eq!(
        manager.live.read().await["agent"].cfg.label.as_deref(),
        Some("Original")
    );
    assert!(manager.live.read().await["agent"].title.accepts(&request));
    assert_eq!(
        manager
            .config()
            .await
            .session("agent")
            .unwrap()
            .label
            .as_deref(),
        Some("Original")
    );
    std::fs::remove_dir_all(manager.cfg_path.parent().unwrap()).unwrap();
}
#[tokio::test]
async fn remove_and_restore_roll_back_private_state_when_config_cannot_be_saved() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    let (manager, root, socket) = rename_fixture(false).await;
    let session = manager.config().await.sessions[0].clone();
    let private = crate::sandbox::state_dir(&session).unwrap();
    std::fs::create_dir_all(&private).unwrap();
    std::fs::write(private.join("memory"), "remember me").unwrap();
    let saved = std::fs::read(&manager.cfg_path).unwrap();
    let blocker = manager.cfg_path.with_extension("toml.tmp");
    std::fs::create_dir_all(&blocker).unwrap();
    assert!(manager.remove("old").await.is_err());
    assert_eq!(std::fs::read(&manager.cfg_path).unwrap(), saved);
    assert_eq!(
        std::fs::read_to_string(private.join("memory")).unwrap(),
        "remember me"
    );
    assert!(manager.config().await.session("old").is_some());
    assert!(manager.live.read().await.contains_key("old"));
    assert!(!manager
        .stored_states()
        .await
        .unwrap()
        .iter()
        .any(|s| s.kind == "trash"));
    std::fs::remove_dir_all(&blocker).unwrap();

    manager.remove("old").await.unwrap();
    assert!(!private.exists());
    assert!(manager.config().await.session("old").is_none());
    assert!(!manager.live.read().await.contains_key("old"));
    let entries = manager.stored_states().await.unwrap();
    let archived = entries.iter().find(|s| s.kind == "trash").unwrap();
    assert_eq!(archived.session.as_deref(), Some("old"));
    std::fs::create_dir_all(&blocker).unwrap();
    assert!(manager.restore_stored_state(&archived.key).await.is_err());
    assert!(!private.exists());
    assert!(manager.config().await.session("old").is_none());
    assert_eq!(
        std::fs::read_to_string(std::path::Path::new(&archived.path).join("memory")).unwrap(),
        "remember me"
    );
    std::fs::remove_dir_all(&blocker).unwrap();
    assert_eq!(
        manager.restore_stored_state(&archived.key).await.unwrap(),
        "old"
    );
    assert_eq!(
        manager.config().await.session("old").unwrap().state_id,
        session.state_id
    );
    assert_eq!(
        Config::load(&manager.cfg_path)
            .await
            .unwrap()
            .session("old")
            .unwrap()
            .state_id,
        session.state_id
    );
    assert_eq!(
        std::fs::read_to_string(private.join("memory")).unwrap(),
        "remember me"
    );
    assert!(manager.live.read().await.contains_key("old"));

    // Reset archives private state while retaining the configured agent identity.
    manager.reset_state("old").await.unwrap();
    assert!(!private.exists());
    assert_eq!(
        manager.config().await.session("old").unwrap().state_id,
        session.state_id
    );
    let entries = manager.stored_states().await.unwrap();
    let archived = entries.iter().find(|s| s.kind == "trash").unwrap();
    assert_eq!(
        manager.restore_stored_state(&archived.key).await.unwrap(),
        "old"
    );
    assert_eq!(manager.config().await.sessions.len(), 1);
    assert_eq!(
        std::fs::read_to_string(private.join("memory")).unwrap(),
        "remember me"
    );
    cleanup_rename_fixture(&manager, root, &socket, &["old"]).await;
}

fn pending_title(title: &mut TitleCapture) -> TitleRequest {
    let mut cfg = Config::default();
    cfg.daemon.agent_titles = TitlePolicy::Always;
    cfg.daemon.title_min_chars = 0;
    let session = SessionCfg {
        command: "codex".into(),
        ..Default::default()
    };
    let settings = TitleSettings::for_session(&cfg, &session, false).unwrap();
    title.paste("Describe the current work");
    let Some(TitleAction::Request(request)) =
        title.capture_keys(&settings, &["Enter".into()], false)
    else {
        panic!("expected request")
    };
    request
}
