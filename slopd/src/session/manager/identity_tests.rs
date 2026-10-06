use super::*;

#[tokio::test]
async fn new_agent_id_is_opaque_and_retained_state_errors_are_not_vacancy() {
    let Some(root) = crate::test_support::isolated() else {
        return;
    };
    let manager = crate::session::test_manager(Config::default());
    let id = manager
        .session_operation(manager.allocate_agent_identity())
        .await
        .unwrap();
    assert!(crate::storage_id::valid(&id));
    let trash = root.join("state/.trash/broken");
    std::fs::create_dir_all(&trash).unwrap();
    let error = manager
        .session_operation(manager.allocate_agent_identity())
        .await
        .unwrap_err();
    assert!(format!("{error:#}").contains("metadata"));
    assert!(manager.config().await.sessions.is_empty());
}

#[tokio::test]
async fn configured_creation_ignores_client_identity_and_preserves_id_on_label_edit() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    let manager = crate::session::test_manager(Config {
        projects: vec![ProjectCfg {
            name: "repo".into(),
            dir: "/tmp".into(),
            ..Default::default()
        }],
        ..Default::default()
    });
    let supplied = uuid::Uuid::new_v4().to_string();
    manager
        .add(SessionCfg {
            name: "created".into(),
            project: "repo".into(),
            state_id: supplied.clone(),
            cmd: Some("true".into()),
            ..Default::default()
        })
        .await
        .unwrap();
    let saved = manager
        .config()
        .await
        .session("created")
        .unwrap()
        .state_id
        .clone();
    assert!(crate::storage_id::valid(&saved));
    assert_ne!(saved, supplied);
    manager
        .update_cfg(super::super::config::ConfigMutation::Agents, |cfg| {
            cfg.sessions[0].label = Some("label".into());
            Ok(())
        })
        .await
        .unwrap();
    assert_eq!(manager.config().await.sessions[0].state_id, saved);
}
