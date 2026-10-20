//! Saved host records preserve identities and publish only committed changes.
use super::*;
use crate::config::{Config, ProjectCfg, SessionCfg};

fn fixture() -> (Arc<SessionRuntime>, HostRecords) {
    let manager = crate::session::test_manager(Config {
        projects: vec![ProjectCfg {
            name: "repo".into(),
            dir: "/tmp".into(),
            ..Default::default()
        }],
        sessions: vec![SessionCfg {
            name: "agent".into(),
            project: "repo".into(),
            ..Default::default()
        }],
        ..Default::default()
    });
    let records = HostRecords::new(manager.workspace.clone(), manager.config_authority.clone());
    (manager, records)
}

#[tokio::test]
async fn host_creation_serializes_identity_and_failed_save_does_not_publish() {
    let (manager, records) = fixture();
    records
        .remember(&manager, "shell", "repo", "/old")
        .await
        .unwrap();
    let (one, two) = tokio::join!(
        records.remember(&manager, "one", "repo", "/one"),
        records.remember(&manager, "two", "repo", "/two"),
    );
    one.unwrap();
    two.unwrap();
    let cfg = manager.config().await;
    let ids: std::collections::HashSet<_> = cfg.host_terminals.iter().map(|tab| &tab.id).collect();
    assert_eq!(ids.len(), 3);
    assert!(ids.iter().all(|id| crate::storage_id::valid(id)));
    let before = std::fs::read(manager.cfg_path()).unwrap();
    let directory = manager
        .cfg_path()
        .parent()
        .unwrap()
        .join("data/host_shells");
    let backup = directory.with_extension("saved");
    std::fs::rename(&directory, &backup).unwrap();
    std::fs::write(&directory, "blocked").unwrap();
    records
        .remember(&manager, "failed", "repo", "/failed")
        .await
        .unwrap_err();
    assert_eq!(manager.config().await.host_terminals.len(), 3);
    assert_eq!(std::fs::read(manager.cfg_path()).unwrap(), before);
    std::fs::remove_file(&directory).unwrap();
    std::fs::rename(&backup, &directory).unwrap();
    std::fs::remove_dir_all(manager.cfg_path().parent().unwrap()).unwrap();
}

#[tokio::test]
async fn remembered_host_edits_preserve_labels_and_reject_agent_collisions() {
    let (manager, records) = fixture();
    records
        .remember(&manager, "shell", "repo", "/old")
        .await
        .unwrap();
    let id = manager.config().await.host_terminals[0].id.clone();
    assert!(crate::storage_id::valid(&id));
    manager.sync_from_config().await;
    manager
        .edits()
        .set_label("shell", "  My shell  ".into())
        .await
        .unwrap();
    records
        .remember(&manager, "shell", "repo", "/new")
        .await
        .unwrap();
    let cfg = manager.config().await;
    assert_eq!(cfg.host_terminals.len(), 1);
    assert_eq!(cfg.host_terminals[0].id, id);
    assert_eq!(cfg.host_terminals[0].label.as_deref(), Some("My shell"));
    assert_eq!(cfg.host_terminals[0].path, "/new");
    assert!(cfg.host_terminals[0].autostart);
    assert!(
        records
            .remember(&manager, "agent", "repo", "/collision")
            .await
            .unwrap_err()
            .to_string()
            .contains("conflicts")
    );
    assert_eq!(manager.config().await.host_terminals.len(), 1);
    std::fs::remove_dir_all(manager.cfg_path().parent().unwrap()).unwrap();
}

#[tokio::test]
async fn absent_path_updates_do_not_create_shell_records() {
    let (manager, records) = fixture();
    let saved = records
        .remember_path(&manager, "missing", "/new")
        .await
        .unwrap();
    assert!(!saved.changed);
    assert!(saved.project.is_none());
    assert!(manager.config().await.host_terminals.is_empty());
}

#[tokio::test]
async fn cancelled_registration_finishes_publication_and_releases_guard() {
    let (manager, records) = fixture();
    let committed = Arc::new(tokio::sync::Barrier::new(2));
    let release = Arc::new(tokio::sync::Notify::new());
    *manager.workspace.configuration.commit_pause.lock().unwrap() =
        Some((committed.clone(), release.clone()));
    let sessions = manager.clone();
    let caller =
        tokio::spawn(async move { records.remember(&sessions, "shell", "repo", "/saved").await });
    committed.wait().await;
    caller.abort();
    assert!(caller.await.unwrap_err().is_cancelled());
    assert!(manager.config().await.host_terminals.is_empty());
    release.notify_one();
    manager
        .session_operation(async {
            let cfg = manager.config().await;
            assert_eq!(cfg.host_terminals.len(), 1);
            assert_eq!(cfg.host_terminals[0].path, "/saved");
            assert_eq!(
                Config::load(manager.cfg_path())
                    .await
                    .unwrap()
                    .host_terminals[0]
                    .id,
                cfg.host_terminals[0].id
            );
        })
        .await;
}
