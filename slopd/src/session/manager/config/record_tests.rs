//! Manager integration fixtures for the unselected record layout.

use super::*;
use crate::config::HostTerminalCfg;
use crate::storage::{
    sessions::{Definition, Kind, Store},
    target::StorageBinding,
    transaction,
};

pub(in crate::session::manager) async fn select_records(manager: &Arc<Manager>) -> StorageBinding {
    let root = manager.cfg_path.parent().unwrap();
    let binding = StorageBinding::new(root, &root.join("data"), &manager.cfg_path).unwrap();
    let mut cfg = manager.config().await;
    for project in &mut cfg.projects {
        if project.id.is_empty() {
            project.id = crate::storage_id::allocate(|_| Ok(false)).unwrap();
        }
    }
    let gate = manager.config_state.persist.clone().lock_owned().await;
    let mut changes = Vec::new();
    for (kind, values) in [
        (
            Kind::Agent,
            cfg.sessions
                .into_iter()
                .map(|row| Definition::Agent(Box::new(row)))
                .collect::<Vec<_>>(),
        ),
        (
            Kind::HostShell,
            cfg.host_terminals
                .into_iter()
                .map(Definition::HostShell)
                .collect(),
        ),
    ] {
        let mut store = Store::empty(kind);
        for value in values {
            let prepared = store.create(value).unwrap();
            changes.push(prepared.change.clone());
            store.publish(prepared);
        }
    }
    let projects = crate::storage::workspace::Store::<ProjectCfg>::empty()
        .prepare(cfg.projects)
        .unwrap();
    changes.extend(projects.changes);
    let worktrees = crate::storage::workspace::Store::<crate::worktrees::Worktree>::empty()
        .prepare(
            crate::worktrees::Store::load(&manager.cfg_path)
                .await
                .unwrap()
                .worktrees,
        )
        .unwrap();
    changes.extend(worktrees.changes);
    transaction::commit_in_operation(&binding, changes, &gate)
        .await
        .unwrap();
    drop(gate);
    manager
        .select_record_fixture(binding.clone())
        .await
        .unwrap();
    binding
}

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
        let binding = select_records(&manager).await;
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
    let stored = Store::load(&f.binding, Kind::Agent).await.unwrap();
    let Definition::Agent(row) = &stored.ordered()[0] else {
        panic!()
    };
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
    let loaded = Store::load(&f.binding, Kind::Agent).await.unwrap();
    let Definition::Agent(row) = &loaded.ordered()[0] else {
        panic!()
    };
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
    f.manager.replace_config("").await.unwrap_err();
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
    let stamp = *f.manager.config_state.library_mtime.lock().unwrap();
    tokio::fs::write(&path, "invalid library [").await.unwrap();
    std::fs::File::open(&path)
        .unwrap()
        .set_modified(std::time::SystemTime::now() + Duration::from_secs(60))
        .unwrap();
    assert!(!f.manager.reload_if_changed().await);
    assert_eq!(*f.manager.config_state.library_mtime.lock().unwrap(), stamp);
    assert_eq!(f.manager.config().await.library[0].text, "prompt");
    assert_eq!(f.manager.config().await.sessions[0].name, "agent");
    assert_eq!(
        tokio::fs::read_to_string(&f.manager.cfg_path)
            .await
            .unwrap(),
        "unaccepted root ["
    );
}
