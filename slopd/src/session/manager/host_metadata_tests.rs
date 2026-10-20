use super::*;

async fn finish_poll(manager: &Manager) {
    let task = manager.host_metadata.task.lock().await;
    // Leave the completed handle unconsumed so admission exercises its ordinary
    // reap-and-replace path on the next poll.
    tokio::time::timeout(Duration::from_secs(5), async {
        while !task.as_ref().unwrap().is_finished() {
            tokio::task::yield_now().await;
        }
    })
    .await
    .expect("metadata poll did not finish");
}

fn shell_metadata(path: &str, command: &str) -> HashMap<String, crate::tmux::HostMetadata> {
    [(
        "shell".into(),
        crate::tmux::HostMetadata {
            path: Some(path.into()),
            command: Some(command.into()),
        },
    )]
    .into_iter()
    .collect()
}

#[tokio::test]
async fn slow_poll_excludes_overlap_without_blocking_session_operations() {
    let manager = metadata_fixture().await;
    let inspections = AtomicU64::new(0);
    let (reached_tx, reached_rx) = tokio::sync::oneshot::channel();
    let (release_tx, release_rx) = tokio::sync::oneshot::channel();
    manager
        .start_host_metadata_poll_with(|| {
            inspections.fetch_add(1, Ordering::Relaxed);
            async move {
                reached_tx.send(()).unwrap();
                release_rx.await.unwrap();
                Ok(shell_metadata("/polled", "python"))
            }
        })
        .await;
    tokio::time::timeout(Duration::from_secs(5), reached_rx)
        .await
        .unwrap()
        .unwrap();

    manager
        .start_host_metadata_poll_with(|| {
            inspections.fetch_add(1, Ordering::Relaxed);
            std::future::ready(Ok(HashMap::new()))
        })
        .await;
    assert_eq!(inspections.load(Ordering::Relaxed), 1);
    tokio::time::timeout(Duration::from_secs(5), manager.session_operation(async {}))
        .await
        .expect("slow inspection held the session boundary");
    assert_eq!(manager.live.read().await["shell"].host_path, "/old");

    release_tx.send(()).unwrap();
    finish_poll(&manager).await;
    assert_eq!(manager.config().await.host_terminals[0].path, "/polled");
    assert!(manager.live.read().await["shell"].process_running);
}

#[tokio::test]
async fn failed_poll_preserves_state_and_allows_a_successful_retry() {
    let manager = metadata_fixture().await;
    let cfg = manager.config().await;
    let record = manager
        .configuration
        .records
        .binding
        .data
        .join("host_shells")
        .join(format!("{}.toml", cfg.host_terminals[0].id));
    let before = std::fs::read(&record).unwrap();
    let mut events = manager.events.subscribe();

    manager
        .start_host_metadata_poll_with(|| async { Err(anyhow::anyhow!("inspection failed")) })
        .await;
    finish_poll(&manager).await;
    assert_eq!(std::fs::read(&record).unwrap(), before);
    assert_eq!(manager.live.read().await["shell"].host_path, "/old");
    assert!(!manager.live.read().await["shell"].process_running);
    assert!(matches!(
        events.try_recv(),
        Err(tokio::sync::broadcast::error::TryRecvError::Empty)
    ));

    manager
        .start_host_metadata_poll_with(|| async { Ok(shell_metadata("/retry", "python")) })
        .await;
    finish_poll(&manager).await;
    assert_eq!(manager.live.read().await["shell"].host_path, "/retry");
    assert!(manager.live.read().await["shell"].process_running);
    assert_eq!(
        Config::load(&manager.cfg_path)
            .await
            .unwrap()
            .host_terminals[0]
            .path,
        "/retry"
    );
    assert!(matches!(
        events.try_recv().unwrap().event(),
        Event::Sessions { .. }
    ));
}

#[tokio::test]
async fn delayed_poll_cannot_update_a_replaced_run() {
    let manager = metadata_fixture().await;
    let (release_tx, release_rx) = tokio::sync::oneshot::channel();
    manager
        .start_host_metadata_poll_with(|| async move {
            release_rx.await.unwrap();
            Ok(shell_metadata("/stale", "python"))
        })
        .await;
    // Admission captured run 17. Replace it before inspection returns.
    manager.live.write().await.get_mut("shell").unwrap().run_id = 18;
    release_tx.send(()).unwrap();
    finish_poll(&manager).await;
    assert_eq!(manager.live.read().await["shell"].host_path, "/old");
    assert!(!manager.live.read().await["shell"].process_running);
    assert_eq!(manager.config().await.host_terminals[0].path, "/old");
}

#[tokio::test]
async fn poll_without_live_hosts_does_not_start_inspection() {
    let manager = metadata_fixture().await;
    manager.live.write().await.get_mut("shell").unwrap().state = State::Down;
    let inspections = AtomicU64::new(0);
    manager
        .start_host_metadata_poll_with(|| {
            inspections.fetch_add(1, Ordering::Relaxed);
            std::future::ready(Ok(HashMap::new()))
        })
        .await;
    assert_eq!(inspections.load(Ordering::Relaxed), 0);
    assert!(manager.host_metadata.task.lock().await.is_none());
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

pub(in crate::session::manager) async fn metadata_fixture() -> Arc<Manager> {
    let manager = crate::session::test_manager(Config::default());
    let dir = manager
        .cfg_path
        .parent()
        .unwrap()
        .to_string_lossy()
        .into_owned();
    manager
        .update_cfg(ConfigMutation::ProjectReferences, |cfg| {
            cfg.projects.push(ProjectCfg {
                id: crate::storage_id::draft_identity(),
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
    assert_eq!(manager.live.read().await["shell"].host_path, "/old");
    assert_eq!(
        Config::load(&manager.cfg_path)
            .await
            .unwrap()
            .host_terminals[0]
            .path,
        "/old"
    );
    manager
        .apply_host_metadata(
            vec![("shell".into(), 17), ("missing".into(), 17)],
            metadata("/current", "python"),
        )
        .await;
    assert_eq!(manager.live.read().await["shell"].host_path, "/current");
    assert!(manager.live.read().await["shell"].process_running);
    let saved = Config::load(&manager.cfg_path).await.unwrap();
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
async fn host_creation_serializes_identity_and_failed_save_does_not_publish() {
    let manager = metadata_fixture().await;
    let (one, two) = tokio::join!(
        manager.remember_host_terminal("one", "repo", "/one"),
        manager.remember_host_terminal("two", "repo", "/two"),
    );
    one.unwrap();
    two.unwrap();
    let cfg = manager.config().await;
    let ids: std::collections::HashSet<_> = cfg.host_terminals.iter().map(|tab| &tab.id).collect();
    assert_eq!(ids.len(), 3);
    assert!(ids.iter().all(|id| crate::storage_id::valid(id)));
    let before = std::fs::read(&manager.cfg_path).unwrap();
    let directory = manager.cfg_path.parent().unwrap().join("data/host_shells");
    let backup = directory.with_extension("saved");
    std::fs::rename(&directory, &backup).unwrap();
    std::fs::write(&directory, "blocked").unwrap();
    manager
        .remember_host_terminal("failed", "repo", "/failed")
        .await
        .unwrap_err();
    assert_eq!(manager.config().await.host_terminals.len(), 3);
    assert_eq!(std::fs::read(&manager.cfg_path).unwrap(), before);
    std::fs::remove_file(&directory).unwrap();
    std::fs::rename(&backup, &directory).unwrap();
    std::fs::remove_dir_all(manager.cfg_path.parent().unwrap()).unwrap();
}

#[tokio::test]
async fn remembered_host_edits_preserve_labels_and_reject_agent_collisions() {
    let manager = metadata_fixture().await;
    let id = manager.config().await.host_terminals[0].id.clone();
    assert!(crate::storage_id::valid(&id));
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
    assert_eq!(cfg.host_terminals[0].id, id);
    assert_eq!(cfg.host_terminals[0].label.as_deref(), Some("My shell"));
    assert_eq!(cfg.host_terminals[0].path, "/new");
    assert!(cfg.host_terminals[0].autostart);
    assert!(
        manager
            .remember_host_terminal("agent", "repo", "/collision")
            .await
            .unwrap_err()
            .to_string()
            .contains("conflicts")
    );
    assert_eq!(manager.config().await.host_terminals.len(), 1);
    std::fs::remove_dir_all(manager.cfg_path.parent().unwrap()).unwrap();
}

#[tokio::test]
async fn record_backend_host_metadata_keeps_run_checks_and_targets_one_shell() {
    let manager = metadata_fixture().await;
    let binding = manager.configuration.records.binding.clone();
    let cfg = manager.config().await;
    let agent = binding
        .data
        .join("agents")
        .join(format!("{}.toml", cfg.sessions[0].state_id));
    let shell = binding
        .data
        .join("host_shells")
        .join(format!("{}.toml", cfg.host_terminals[0].id));
    let before = std::fs::read(&agent).unwrap();
    let shell_before = std::fs::read(&shell).unwrap();
    assert!(
        !manager
            .remember_host_path_for_run("shell", 16, "/stale")
            .await
    );
    assert_eq!(std::fs::read(&shell).unwrap(), shell_before);
    assert!(
        manager
            .remember_host_path_for_run("shell", 17, "/new")
            .await
    );
    assert_eq!(manager.config().await.host_terminals[0].path, "/new");
    assert_eq!(std::fs::read(&agent).unwrap(), before);
    manager.live.write().await.get_mut("shell").unwrap().run_id = 18;
    assert!(
        !manager
            .remember_host_path_for_run("shell", 17, "/late")
            .await
    );
    assert_eq!(manager.config().await.host_terminals[0].path, "/new");
    std::fs::remove_dir_all(manager.cfg_path.parent().unwrap()).unwrap();
}
