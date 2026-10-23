use super::*;
use crate::config::{Config, ProjectCfg};

fn fixture() -> Application {
    super::super::test_application(Config {
        projects: vec![ProjectCfg {
            name: "repo".into(),
            dir: "/tmp".into(),
            ..Default::default()
        }],
        ..Default::default()
    })
}

fn draft() -> SessionCfg {
    SessionCfg {
        name: "created".into(),
        project: "repo".into(),
        cmd: Some("true".into()),
        ..Default::default()
    }
}

#[tokio::test]
async fn ordinary_creation_discards_client_worker_identity_and_private_snapshots() {
    let app = fixture();
    let mut session = draft();
    session.state_id = crate::storage_id::draft_identity();
    let supplied = session.state_id.clone();
    session.worker = true;
    session.parent = "untrusted-parent".into();
    session.task_id = "untrusted-task".into();
    session.command_snapshot = Some(Default::default());
    session.sandbox_snapshots = vec![Default::default()];
    SessionEdits::new(&app).add(session).await.unwrap();
    let cfg = app.config_authority.snapshot().await;
    let saved = cfg.session("created").unwrap();
    assert!(!saved.worker);
    assert!(saved.parent.is_empty() && saved.task_id.is_empty());
    assert!(saved.command_snapshot.is_none() && saved.sandbox_snapshots.is_empty());
    assert!(crate::storage_id::valid(&saved.state_id));
    assert_ne!(saved.state_id, supplied);
}

#[tokio::test]
async fn failed_creation_publishes_nothing_and_can_be_retried() {
    let app = fixture();
    let operations = SessionEdits::new(&app);
    let agents = app
        .workspace
        .configuration
        .records
        .binding
        .data
        .join("agents");
    let backup = agents.with_extension("saved");
    if agents.exists() {
        std::fs::rename(&agents, &backup).unwrap();
    }
    std::fs::create_dir_all(agents.parent().unwrap()).unwrap();
    std::fs::write(&agents, "unavailable record directory").unwrap();
    operations.add(draft()).await.unwrap_err();
    assert!(app.config_authority.snapshot().await.sessions.is_empty());
    assert!(app.sessions.views().await.is_empty());
    std::fs::remove_file(&agents).unwrap();
    if backup.exists() {
        std::fs::rename(&backup, &agents).unwrap();
    }
    operations.add(draft()).await.unwrap();
    assert!(
        app.config_authority
            .snapshot()
            .await
            .session("created")
            .is_some()
    );
    assert_eq!(app.sessions.views().await.len(), 1);
    operations.add(draft()).await.unwrap_err();
    assert_eq!(app.config_authority.snapshot().await.sessions.len(), 1);
}

#[tokio::test]
async fn candidate_writer_rejects_cross_kind_names_without_publishing_or_persisting() {
    let app = fixture();
    let records =
        super::super::HostRecords::new(app.workspace.clone(), app.config_authority.clone());
    records
        .remember(&app.sessions, "created", "repo", "/tmp")
        .await
        .unwrap();
    let accepted = toml::to_string(&app.config_authority.snapshot().await).unwrap();
    let configuration = Configuration::new(&app);
    let error = configuration
        .update_cfg(ConfigMutation::Agents, |cfg| {
            let mut session = draft();
            session.state_id = crate::storage_id::draft_identity();
            cfg.sessions.push(session);
            Ok(())
        })
        .await
        .unwrap_err();
    assert!(
        error
            .to_string()
            .contains("duplicate agent/host-shell name created")
    );
    assert_eq!(
        toml::to_string(&app.config_authority.snapshot().await).unwrap(),
        accepted
    );
    let loaded = Config::load(&app.workspace.configuration.records.binding.settings)
        .await
        .unwrap();
    assert!(loaded.sessions.is_empty());
    assert_eq!(loaded.host_terminals.len(), 1);
    assert_eq!(loaded.host_terminals[0].name, "created");
}
