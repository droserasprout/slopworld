//! Manager integration tests for production record storage.

use super::*;
use crate::config::HostTerminalCfg;
use crate::storage::{target::StorageBinding, workspace::Store};

struct Fixture {
    manager: Arc<Manager>,
    binding: StorageBinding,
}

impl Fixture {
    async fn new() -> Self {
        let manager = crate::session::test_manager(Config {
            projects: vec![ProjectCfg {
                name: "repo".into(),
                dir: "/tmp".into(),
                ..Default::default()
            }],
            sessions: ["agent", "sibling"]
                .into_iter()
                .map(|name| SessionCfg {
                    name: name.into(),
                    project: "repo".into(),
                    cmd: Some("true".into()),
                    ..Default::default()
                })
                .collect(),
            host_terminals: vec![HostTerminalCfg {
                id: "1111111111111111".into(),
                name: "shell".into(),
                project: "repo".into(),
                path: "/old".into(),
                autostart: false,
                ..Default::default()
            }],
            ..Default::default()
        });
        let binding = manager.config_state.records.binding.clone();
        crate::paths::write_atomic_async(&manager.cfg_path, "# untouched root\n", Some(0o600))
            .await
            .unwrap();
        manager.sync_from_config().await;
        Self { manager, binding }
    }
    async fn agent_path(&self, name: &str) -> PathBuf {
        let cfg = self.manager.config().await;
        self.binding
            .data
            .join("agents")
            .join(format!("{}.toml", cfg.session(name).unwrap().state_id))
    }
    fn shell_path(&self) -> PathBuf {
        self.binding.data.join("host_shells/1111111111111111.toml")
    }
}

impl Drop for Fixture {
    fn drop(&mut self) {
        drop(std::fs::remove_dir_all(
            self.manager.cfg_path.parent().unwrap(),
        ));
    }
}

#[tokio::test]
async fn manager_routes_rename_label_and_directory_updates_without_touching_other_stores() {
    let f = Fixture::new().await;
    let agent = f.agent_path("agent").await;
    let sibling = f.agent_path("sibling").await;
    let before = tokio::fs::read(&sibling).await.unwrap();
    let stamp = tokio::fs::metadata(&sibling)
        .await
        .unwrap()
        .modified()
        .unwrap();
    let root = tokio::fs::read(&f.manager.cfg_path).await.unwrap();
    let mut edited = f.manager.config().await.session("agent").unwrap().clone();
    edited.name = "renamed".into();
    f.manager.update("agent", edited).await.unwrap();
    f.manager
        .set_label("renamed", "new label".into())
        .await
        .unwrap();
    assert_eq!(f.agent_path("renamed").await, agent);
    let stored = Store::<SessionCfg>::load(&f.binding).await.unwrap();
    let rows = stored.ordered();
    let row = &rows[0];
    assert_eq!(row.name, "renamed");
    assert_eq!(row.label.as_deref(), Some("new label"));
    let agent_bytes = tokio::fs::read(&agent).await.unwrap();
    f.manager
        .remember_host_terminal("shell", "repo", "/new")
        .await
        .unwrap();
    assert_eq!(f.manager.config().await.host_terminals[0].path, "/new");
    assert_eq!(tokio::fs::read(&agent).await.unwrap(), agent_bytes);
    assert_eq!(tokio::fs::read(&sibling).await.unwrap(), before);
    assert_eq!(
        tokio::fs::metadata(sibling)
            .await
            .unwrap()
            .modified()
            .unwrap(),
        stamp
    );
    assert_eq!(tokio::fs::read(&f.manager.cfg_path).await.unwrap(), root);
}

#[tokio::test]
async fn configured_creation_and_retirement_use_records_and_keep_reused_names_distinct() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    let f = Fixture::new().await;
    f.manager
        .add(SessionCfg {
            name: "new-agent".into(),
            project: "repo".into(),
            cmd: Some("true".into()),
            ..Default::default()
        })
        .await
        .unwrap();
    let path = f.agent_path("new-agent").await;
    f.manager
        .remove_configured_session("new-agent")
        .await
        .unwrap();
    assert!(!path.exists());
    f.manager
        .remove_configured_session("new-agent")
        .await
        .unwrap();
    f.manager
        .add(SessionCfg {
            name: "new-agent".into(),
            project: "repo".into(),
            cmd: Some("true".into()),
            ..Default::default()
        })
        .await
        .unwrap();
    assert_ne!(f.agent_path("new-agent").await, path);
    assert_eq!(
        tokio::fs::read_to_string(&f.manager.cfg_path)
            .await
            .unwrap(),
        "# untouched root\n"
    );
}

#[tokio::test]
async fn failed_record_commit_does_not_publish_indexes_or_config_and_retry_succeeds() {
    let f = Fixture::new().await;
    let path = f.agent_path("agent").await;
    let before = tokio::fs::read(&path).await.unwrap();
    let fault = crate::paths::fail_writes(&path);
    f.manager
        .set_label("agent", "failed".into())
        .await
        .unwrap_err();
    assert_eq!(
        f.manager.config().await.session("agent").unwrap().label,
        None
    );
    assert_eq!(f.manager.live.read().await["agent"].cfg.label, None);
    assert_eq!(tokio::fs::read(&path).await.unwrap(), before);
    drop(fault);
    f.manager
        .set_label("agent", "accepted".into())
        .await
        .unwrap();
    assert_eq!(
        f.manager
            .config()
            .await
            .session("agent")
            .unwrap()
            .label
            .as_deref(),
        Some("accepted")
    );
}

#[tokio::test]
async fn coordinated_agent_edits_rollback_all_selected_files_before_publication() {
    let f = Fixture::new().await;
    let first = f.agent_path("agent").await;
    let second = f.agent_path("sibling").await;
    let before = (
        tokio::fs::read(&first).await.unwrap(),
        tokio::fs::read(&second).await.unwrap(),
    );
    let fault = crate::paths::fail_writes(&second);
    f.manager
        .update_cfg(ConfigMutation::Agents, |cfg| {
            for row in &mut cfg.sessions {
                row.label = Some("candidate".into());
            }
            Ok(())
        })
        .await
        .unwrap_err();
    assert!(
        f.manager
            .config()
            .await
            .sessions
            .iter()
            .all(|row| row.label.is_none())
    );
    assert_eq!(tokio::fs::read(&first).await.unwrap(), before.0);
    assert_eq!(tokio::fs::read(&second).await.unwrap(), before.1);
    assert!(!f.binding.journal().unwrap().exists());
    drop(fault);
    f.manager.set_label("agent", "retry".into()).await.unwrap();
}

#[tokio::test]
async fn cancellation_after_disk_commit_still_publishes_config_and_record_indexes() {
    let f = Fixture::new().await;
    let committed = Arc::new(tokio::sync::Barrier::new(2));
    let release = Arc::new(tokio::sync::Notify::new());
    *f.manager.config_state.commit_pause.lock().unwrap() =
        Some((committed.clone(), release.clone()));
    let manager = f.manager.clone();
    let request = tokio::spawn(async move { manager.set_label("agent", "committed".into()).await });
    tokio::time::timeout(Duration::from_secs(5), committed.wait())
        .await
        .unwrap();
    assert_eq!(f.manager.config().await.sessions[0].label, None);
    request.abort();
    drop(request.await);
    f.manager.config_state.persist.try_lock().unwrap_err();
    release.notify_one();
    let gate = tokio::time::timeout(
        Duration::from_secs(5),
        f.manager.config_state.persist.lock(),
    )
    .await
    .unwrap();
    assert_eq!(
        f.manager.config().await.sessions[0].label.as_deref(),
        Some("committed")
    );
    drop(gate);
    let boundary = tokio::time::timeout(Duration::from_secs(5), f.manager.session_boundary.write())
        .await
        .unwrap();
    assert_eq!(
        f.manager.live.read().await["agent"].cfg.label.as_deref(),
        Some("committed")
    );
    drop(boundary);
    // A second plan must see the accepted index and retain the first field.
    f.manager
        .update_cfg(ConfigMutation::Agents, |cfg| {
            cfg.sessions[0].args = Some("--next".into());
            Ok(())
        })
        .await
        .unwrap();
    let loaded = Store::<SessionCfg>::load(&f.binding).await.unwrap();
    let rows = loaded.ordered();
    let row = &rows[0];
    assert_eq!(row.label.as_deref(), Some("committed"));
    assert_eq!(row.args.as_deref(), Some("--next"));
}

#[tokio::test]
async fn record_mode_does_not_poll_or_accept_external_workspace_changes() {
    let f = Fixture::new().await;
    let path = f.agent_path("agent").await;
    tokio::fs::write(path, "invalid offline edit [")
        .await
        .unwrap();
    tokio::fs::write(&f.manager.cfg_path, "invalid root edit [")
        .await
        .unwrap();
    assert!(!f.manager.reload_if_changed().await);
    assert_eq!(f.manager.config().await.sessions[0].name, "agent");
    f.manager
        .remember_host_terminal("shell", "repo", "/accepted")
        .await
        .unwrap();
    assert_eq!(
        tokio::fs::read_to_string(&f.manager.cfg_path)
            .await
            .unwrap(),
        "invalid root edit ["
    );
    let raw: toml::Value =
        toml::from_str(&tokio::fs::read_to_string(f.shell_path()).await.unwrap()).unwrap();
    assert_eq!(raw["path"].as_str(), Some("/accepted"));
    f.manager.replace_config("").await.unwrap();
    assert_eq!(f.manager.config().await.sessions[0].name, "agent");
}

#[tokio::test]
async fn independent_library_reload_preserves_workspace_and_rejects_invalid_revisions() {
    let f = Fixture::new().await;
    let directory = f.binding.config.join("prompts");
    tokio::fs::create_dir_all(&directory).await.unwrap();
    let path = directory.join("external.toml");
    let item = LibraryItemCfg {
        name: "external".into(),
        text: "prompt".into(),
        ..Default::default()
    };
    tokio::fs::write(&path, toml::to_string(&item).unwrap())
        .await
        .unwrap();
    tokio::fs::write(&f.manager.cfg_path, "unaccepted root [")
        .await
        .unwrap();
    assert!(f.manager.reload_if_changed().await);
    assert_eq!(f.manager.config().await.library[0].text, "prompt");
    let stamp = f
        .manager
        .config_state
        .library_revision
        .lock()
        .unwrap()
        .clone();
    tokio::fs::write(&path, "invalid library [").await.unwrap();
    std::fs::File::open(&path)
        .unwrap()
        .set_modified(std::time::SystemTime::now() + Duration::from_secs(60))
        .unwrap();
    assert!(!f.manager.reload_if_changed().await);
    assert_eq!(
        *f.manager.config_state.library_revision.lock().unwrap(),
        stamp
    );
    assert_eq!(f.manager.config().await.library[0].text, "prompt");
    assert_eq!(f.manager.config().await.sessions[0].name, "agent");
    assert_eq!(
        tokio::fs::read_to_string(&f.manager.cfg_path)
            .await
            .unwrap(),
        "unaccepted root ["
    );
}

#[tokio::test]
async fn root_settings_edits_preserve_records_and_reject_inline_workspace() {
    let f = Fixture::new().await;
    let agent = tokio::fs::read(f.agent_path("agent").await).await.unwrap();
    let shell = tokio::fs::read(f.shell_path()).await.unwrap();
    f.manager
        .patch_config(serde_json::json!({"daemon":{"token":"secret"}}))
        .await
        .unwrap();
    f.manager
        .replace_config("[daemon]\nbind = '127.0.0.1:7717'\ntoken = '<redacted>'\n")
        .await
        .unwrap();
    assert_eq!(f.manager.config().await.daemon.token, "secret");
    assert_eq!(f.manager.config().await.sessions.len(), 2);
    for key in ["session", "project", "host_terminal"] {
        assert!(
            f.manager
                .replace_config(&format!("{key} = []"))
                .await
                .is_err()
        );
        assert!(
            f.manager
                .patch_config(serde_json::json!({key:[]}))
                .await
                .is_err()
        );
    }
    assert_eq!(
        tokio::fs::read(f.agent_path("agent").await).await.unwrap(),
        agent
    );
    assert_eq!(tokio::fs::read(f.shell_path()).await.unwrap(), shell);
}

#[tokio::test]
async fn library_deletion_is_not_hidden_by_a_newer_sibling_timestamp() {
    let f = Fixture::new().await;
    let directory = f.binding.config.join("prompts");
    tokio::fs::create_dir_all(&directory).await.unwrap();
    for name in ["older", "newer"] {
        let item = LibraryItemCfg {
            name: name.into(),
            text: name.into(),
            ..Default::default()
        };
        tokio::fs::write(
            directory.join(format!("{name}.toml")),
            toml::to_string(&item).unwrap(),
        )
        .await
        .unwrap();
    }
    std::fs::File::open(directory.join("newer.toml"))
        .unwrap()
        .set_modified(std::time::SystemTime::now() + Duration::from_secs(3600))
        .unwrap();
    assert!(f.manager.reload_if_changed().await);
    assert_eq!(f.manager.config().await.library.len(), 2);
    tokio::fs::remove_file(directory.join("older.toml"))
        .await
        .unwrap();
    assert!(f.manager.reload_if_changed().await);
    assert_eq!(f.manager.config().await.library.len(), 1);
    f.manager
        .update_cfg(ConfigMutation::Library, |cfg| {
            cfg.library[0].text = "api update".into();
            Ok(())
        })
        .await
        .unwrap();
    assert!(!f.manager.reload_if_changed().await);
}

#[tokio::test]
async fn delayed_library_commit_rejects_external_changes_without_overwriting_them() {
    let f = Fixture::new().await;
    f.manager
        .update_cfg(ConfigMutation::Library, |cfg| {
            cfg.library.push(LibraryItemCfg {
                name: "prompt".into(),
                text: "accepted".into(),
                ..Default::default()
            });
            Ok(())
        })
        .await
        .unwrap();
    let ((), prepared) = f
        .manager
        .prepare_cfg_change(ConfigMutation::Library, |cfg| {
            cfg.library[0].text = "api".into();
            Ok(((), true))
        })
        .await
        .unwrap();
    let path = f.binding.config.join("prompts/prompt.toml");
    let external = toml::to_string(&LibraryItemCfg {
        name: "prompt".into(),
        text: "external edit with distinct length".into(),
        ..Default::default()
    })
    .unwrap();
    tokio::fs::write(&path, &external).await.unwrap();
    f.manager
        .commit_prepared_cfg(prepared.unwrap())
        .await
        .unwrap_err();
    assert_eq!(tokio::fs::read_to_string(&path).await.unwrap(), external);
    assert_eq!(f.manager.config().await.library[0].text, "accepted");
    assert!(f.manager.reload_if_changed().await);
    assert_eq!(
        f.manager.config().await.library[0].text,
        "external edit with distinct length"
    );
}

#[tokio::test]
async fn library_reload_retries_recovery_without_accepting_pending_files() {
    let f = Fixture::new().await;
    let path = f.binding.config.join("prompts/prompt.toml");
    let mut item = LibraryItemCfg {
        name: "prompt".into(),
        text: "accepted".into(),
        project: "repo".into(),
        ..Default::default()
    };
    f.manager.add_library_item(item.clone()).await.unwrap();
    let original = std::fs::read_to_string(&path).unwrap();
    let revision = f
        .manager
        .config_state
        .library_revision
        .lock()
        .unwrap()
        .clone();
    // A failed rollback leaves a valid candidate file and its undo journal.
    item.text = "uncommitted candidate".into();
    let pending = toml::to_string(&item).unwrap();
    std::fs::write(&path, &pending).unwrap();
    let undo = serde_json::json!({
        "version": 1,
        "binding": f.binding,
        "files": [{
            "target": crate::storage::target::Target::Config("prompts/prompt.toml".into()),
            "text": original
        }]
    });
    let journal = f.binding.journal().unwrap();
    std::fs::write(&journal, undo.to_string()).unwrap();
    let fault = crate::paths::fail_writes(&path);
    for _ in 0..2 {
        assert!(!f.manager.reload_if_changed().await);
        assert_eq!(f.manager.config().await.library[0].text, "accepted");
        assert_eq!(
            *f.manager.config_state.library_revision.lock().unwrap(),
            revision
        );
        assert_eq!(std::fs::read_to_string(&path).unwrap(), pending);
        assert!(journal.exists());
    }
    drop(fault);
    f.manager.reload_if_changed().await;
    assert_eq!(f.manager.config().await.library[0].text, "accepted");
    assert_eq!(std::fs::read_to_string(&path).unwrap(), original);
    assert!(!journal.exists());
    assert!(!f.manager.reload_if_changed().await);
}

#[tokio::test]
async fn library_reload_recovers_before_the_unchanged_revision_shortcut() {
    let f = Fixture::new().await;
    let path = f.shell_path();
    let original = std::fs::read_to_string(&path).unwrap();
    std::fs::write(&path, "interrupted host record").unwrap();
    let undo = serde_json::json!({
        "version": 1,
        "binding": f.binding,
        "files": [{
            "target": crate::storage::target::Target::Data("host_shells/1111111111111111.toml".into()),
            "text": original
        }]
    });
    let journal = f.binding.journal().unwrap();
    std::fs::write(&journal, undo.to_string()).unwrap();
    f.manager.reload_if_stale().await;
    assert_eq!(std::fs::read_to_string(&path).unwrap(), original);
    assert!(!journal.exists());
}

#[tokio::test]
async fn canceled_library_reload_retains_its_boundary_through_recovery() {
    let f = Fixture::new().await;
    let path = f.shell_path();
    let original = std::fs::read_to_string(&path).unwrap();
    std::fs::write(&path, "interrupted host record").unwrap();
    let undo = serde_json::json!({
        "version": 1,
        "binding": f.binding,
        "files": [{
            "target": crate::storage::target::Target::Data("host_shells/1111111111111111.toml".into()),
            "text": original
        }]
    });
    let journal = f.binding.journal().unwrap();
    std::fs::write(&journal, undo.to_string()).unwrap();
    let gate = f.manager.config_state.persist.lock().await;
    let reached = Arc::new(tokio::sync::Notify::new());
    let request = tokio::spawn({
        let manager = f.manager.clone();
        let reached = reached.clone();
        async move {
            manager
                .session_operation(async {
                    reached.notify_one();
                    manager.reload_if_changed().await
                })
                .await
        }
    });
    reached.notified().await;
    request.abort();
    assert!(request.await.unwrap_err().is_cancelled());
    f.manager.session_boundary.try_write().unwrap_err();
    drop(gate);
    let _boundary =
        tokio::time::timeout(Duration::from_secs(5), f.manager.session_boundary.write())
            .await
            .unwrap();
    assert_eq!(std::fs::read_to_string(&path).unwrap(), original);
    assert!(!journal.exists());
}
