use crate::agent_templates::AgentTemplateStore;
use crate::config::{Config, ProjectCfg, SessionCfg};
use crate::presets::SandboxPreset;

#[tokio::test]
async fn template_creation_uses_destination_project_mounts() {
    let root = std::env::temp_dir().join(format!("slopd-template-mounts-{}", uuid::Uuid::new_v4()));
    std::fs::create_dir_all(&root).unwrap();
    let source_mount = root.join("source-mount");
    let destination_mount = root.join("destination-mount");
    std::fs::create_dir_all(&source_mount).unwrap();
    std::fs::create_dir_all(&destination_mount).unwrap();
    let manager = crate::session::test_manager(Config {
        projects: ["repo", "extra"]
            .into_iter()
            .map(|name| ProjectCfg {
                name: name.into(),
                dir: root.to_string_lossy().into_owned(),
                mounts: vec![crate::config::Mount {
                    from: if name == "repo" {
                        &source_mount
                    } else {
                        &destination_mount
                    }
                    .to_string_lossy()
                    .into_owned(),
                    to: format!("/mnt/{name}"),
                    mode: crate::config::MountMode::Ro,
                }],
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
    crate::application::TemplateOperations::new(&crate::application::Application::from_runtime(
        &manager,
    ))
    .capture_agent_template("source", "reviewer".into(), "".into())
    .await
    .unwrap();
    crate::application::TemplateOperations::new(&crate::application::Application::from_runtime(
        &manager,
    ))
    .create_from_agent_template("reviewer", "new-agent".into(), "extra".into(), None, None)
    .await
    .unwrap();
    let cfg = manager.config().await;
    let session = cfg.session("new-agent").unwrap();
    assert_eq!(session.project, "extra");
    let plan = crate::sandbox::build_plan(&cfg, session, cfg.project("extra").unwrap()).unwrap();
    assert!(plan.mounts.windows(3).any(|args| args
        == [
            "--ro-bind",
            destination_mount.to_str().unwrap(),
            "/mnt/extra"
        ]));
    assert!(!plan.mounts.iter().any(|arg| arg == "/mnt/repo"));
    manager
        .application
        .templates
        .store_for_test()
        .write()
        .await
        .templates[0]
        .defaults
        .autostart = Some(true);
    crate::application::TemplateOperations::new(&crate::application::Application::from_runtime(
        &manager,
    ))
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

    crate::application::TemplateOperations::new(&crate::application::Application::from_runtime(
        &manager,
    ))
    .capture_agent_template("agent", "reviewer".into(), "Review changes".into())
    .await
    .unwrap();
    assert_eq!(manager.application.templates.list().await.len(), 1);
    let loaded = AgentTemplateStore::load(&AgentTemplateStore::path_for(manager.cfg_path()))
        .await
        .unwrap();
    let saved = loaded.get("reviewer").unwrap();
    assert_eq!(saved.description, "Review changes");
    assert_eq!(
        toml::to_string(saved).unwrap(),
        toml::to_string(&manager.application.templates.list().await[0]).unwrap()
    );
    assert_eq!(
        std::fs::read_to_string(manager.cfg_path()).unwrap(),
        toml::to_string_pretty(&manager.config().await.settings).unwrap()
    );
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

    crate::application::TemplateOperations::new(&crate::application::Application::from_runtime(
        &manager,
    ))
    .capture_agent_template("source", "reviewer".into(), "".into())
    .await
    .unwrap();
    crate::application::TemplateOperations::new(&crate::application::Application::from_runtime(
        &manager,
    ))
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
