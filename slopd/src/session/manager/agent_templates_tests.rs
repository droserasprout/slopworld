use super::*;
use crate::config::{Config, ProjectCfg, SessionCfg};
use crate::presets::SandboxPreset;

#[tokio::test]
async fn template_creation_uses_destination_project_mounts() {
    let root = std::env::temp_dir().join(format!("slopd-template-mounts-{}", uuid::Uuid::new_v4()));
    std::fs::create_dir_all(&root).unwrap();
    let manager = crate::session::test_manager(Config {
        projects: ["repo", "extra"]
            .into_iter()
            .map(|name| ProjectCfg {
                name: name.into(),
                dir: root.to_string_lossy().into_owned(),
                mounts: if name == "repo" {
                    vec![crate::config::Mount {
                        from: "/tmp".into(),
                        to: "/mnt/extra".into(),
                        mode: crate::config::MountMode::Ro,
                    }]
                } else {
                    Vec::new()
                },
                ..Default::default()
            })
            .collect(),
        sessions: vec![SessionCfg {
            name: "source".into(),
            project: "repo".into(),
            ..Default::default()
        }],
        ..Default::default()
    });
    manager
        .save_agent_template("source", "reviewer".into(), "".into())
        .await
        .unwrap();
    manager
        .create_from_agent_template("reviewer", "new-agent".into(), "repo".into(), None, None)
        .await
        .unwrap();
    assert!(manager.config().await.session("new-agent").unwrap().project == "repo");
    assert_eq!(
        manager.config().await.project("repo").unwrap().mounts.len(),
        1
    );
    manager.templates.store.write().await.templates[0]
        .defaults
        .autostart = Some(true);
    manager
        .create_from_agent_template(
            "reviewer",
            "stopped-agent".into(),
            "repo".into(),
            None,
            Some(false),
        )
        .await
        .unwrap();
    assert!(
        !manager
            .config()
            .await
            .session("stopped-agent")
            .unwrap()
            .autostart
    );
    drop(std::fs::remove_dir_all(root));
}

#[tokio::test]
async fn saving_a_template_persists_it_separately_from_config() {
    let root = std::env::temp_dir().join(format!("slopd-template-test-{}", uuid::Uuid::new_v4()));
    std::fs::create_dir_all(&root).unwrap();
    let manager = crate::session::test_manager(Config {
        projects: vec![ProjectCfg {
            name: "repo".into(),
            dir: root.to_string_lossy().into_owned(),
            ..Default::default()
        }],
        sessions: vec![SessionCfg {
            name: "agent".into(),
            project: "repo".into(),
            ..Default::default()
        }],
        ..Default::default()
    });

    manager
        .save_agent_template("agent", "reviewer".into(), "Review changes".into())
        .await
        .unwrap();
    assert_eq!(manager.agent_templates().await.len(), 1);
    assert!(AgentTemplateStore::path_for(&manager.cfg_path).is_dir());
    assert!(!manager.cfg_path.is_file());
    drop(std::fs::remove_dir_all(root));
}

#[tokio::test]
async fn an_instantiated_template_can_still_be_edited_without_live_dependencies() {
    let root = std::env::temp_dir().join(format!("slopd-template-edit-{}", uuid::Uuid::new_v4()));
    std::fs::create_dir_all(&root).unwrap();
    let manager = crate::session::test_manager(Config {
        projects: vec![ProjectCfg {
            name: "repo".into(),
            dir: root.to_string_lossy().into_owned(),
            ..Default::default()
        }],
        sessions: vec![SessionCfg {
            name: "source".into(),
            project: "repo".into(),
            command: "claude".into(),
            sandbox: vec!["captured".into()],
            sandbox_snapshots: vec![SandboxPreset {
                name: "captured".into(),
                ..Default::default()
            }],
            ..Default::default()
        }],
        ..Default::default()
    });

    manager
        .save_agent_template("source", "reviewer".into(), "".into())
        .await
        .unwrap();
    manager
        .create_from_agent_template("reviewer", "new-agent".into(), "repo".into(), None, None)
        .await
        .unwrap();

    // This update matches the mod editor's request format, which omits snapshot fields.
    // The manager must restore the instance's private definitions before validation.
    let update = SessionCfg {
        name: "new-agent".into(),
        project: "repo".into(),
        command: "claude".into(),
        sandbox: manager
            .config()
            .await
            .session("new-agent")
            .unwrap()
            .sandbox
            .clone(),
        ..Default::default()
    };
    manager.update("new-agent", update).await.unwrap();
    let session = manager.config().await.session("new-agent").unwrap().clone();
    assert!(!session.sandbox_snapshots.is_empty());
    drop(std::fs::remove_dir_all(root));
}

#[tokio::test]
async fn template_writes_compare_versions_and_preserve_rejected_drafts() {
    let root =
        std::env::temp_dir().join(format!("slopd-template-version-{}", uuid::Uuid::new_v4()));
    std::fs::create_dir_all(&root).unwrap();
    let manager = crate::session::test_manager(Config {
        projects: vec![ProjectCfg {
            name: "repo".into(),
            dir: root.to_string_lossy().into_owned(),
            ..Default::default()
        }],
        sessions: vec![SessionCfg {
            name: "source".into(),
            project: "repo".into(),
            command: "claude".into(),
            ..Default::default()
        }],
        ..Default::default()
    });

    manager
        .save_agent_template("source", "reviewer".into(), "first".into())
        .await
        .unwrap();
    let original = manager.agent_templates().await.remove(0);
    let mut edited = original.clone();
    edited.description = "winner".into();
    let winner = manager
        .replace_agent_template_definition("reviewer", original.version, edited)
        .await
        .unwrap();
    assert!(winner.version > original.version);

    // Two clients can submit changes to the same revision. Accept exactly one update.
    let mut left = winner.clone();
    left.description = "winner".into();
    let right = left.clone();
    let (left, right) = tokio::join!(
        manager.replace_agent_template_definition("reviewer", winner.version, left),
        manager.replace_agent_template_definition("reviewer", winner.version, right),
    );
    assert_ne!(left.is_ok(), right.is_ok());
    let winner = left.or(right).unwrap();

    let mut stale_draft = original.clone();
    stale_draft.description = "rejected draft".into();
    let error = manager
        .replace_agent_template_definition("reviewer", original.version, stale_draft)
        .await
        .unwrap_err();
    assert!(error
        .downcast_ref::<crate::session::AgentTemplateError>()
        .is_some());
    assert_eq!(manager.agent_templates().await[0].description, "winner");

    let stale_delete = manager
        .remove_agent_template("reviewer", original.version)
        .await;
    assert!(stale_delete.is_err());
    manager
        .remove_agent_template("reviewer", winner.version)
        .await
        .unwrap();
    let missing_edit = manager
        .replace_agent_template_definition("reviewer", winner.version, winner.clone())
        .await;
    assert!(matches!(
        missing_edit
            .unwrap_err()
            .downcast_ref::<crate::session::AgentTemplateError>(),
        Some(crate::session::AgentTemplateError::Missing(_))
    ));
    let mut recreated = winner;
    recreated.name = "reviewer".into();
    recreated.version = 0;
    let recreated = manager
        .create_agent_template_definition(recreated)
        .await
        .unwrap();
    assert!(recreated.version > original.version);
    drop(std::fs::remove_dir_all(root));
}
