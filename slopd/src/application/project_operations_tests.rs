use crate::application::projects::Projects;
use crate::config::{Config, ProjectCfg};
use std::time::Duration;

#[tokio::test]
async fn project_removal_rejects_library_references_until_removed() {
    let manager = crate::session::test_manager(Config {
        projects: vec![ProjectCfg {
            name: "repo".into(),
            dir: "/tmp".into(),
            ..Default::default()
        }],
        library: vec![crate::config::LibraryItemCfg {
            name: "review".into(),
            project: "repo".into(),
            host: true,
            text: "Review changes".into(),
            ..Default::default()
        }],
        ..Default::default()
    });
    let application = crate::application::Application::from_runtime(&manager);
    let projects = Projects::new(&application);
    let error = projects.remove_project("repo").await.unwrap_err();
    assert!(error.to_string().contains("review"), "{error}");
    let cfg = manager.config().await;
    assert!(cfg.project("repo").is_some());
    crate::application::launch::plan_library_launch(&cfg, "review", Default::default()).unwrap();
    crate::application::library::Library::new(&application)
        .remove_library_item("review")
        .await
        .unwrap();
    projects.remove_project("repo").await.unwrap();
    assert!(manager.config().await.project("repo").is_none());
}

#[tokio::test]
async fn project_temporary_mode_is_immutable_after_creation() {
    let manager = crate::session::test_manager(Config {
        projects: vec![ProjectCfg {
            name: "scratch".into(),
            dir: "/tmp/slopworld-scratch".into(),
            temp: true,
            ..Default::default()
        }],
        ..Default::default()
    });
    let error = Projects::new(&crate::application::Application::from_runtime(&manager))
        .update_project(
            "scratch",
            ProjectCfg {
                name: "scratch".into(),
                dir: "/tmp/slopworld-scratch".into(),
                temp: false,
                ..Default::default()
            },
        )
        .await
        .expect_err("temporary mode changed");
    assert!(
        error
            .to_string()
            .contains("cannot change project temporary mode"),
        "{error}"
    );
}

#[tokio::test]
async fn canceled_project_update_retains_boundary_until_commit() {
    let project = ProjectCfg {
        name: "original".into(),
        dir: "/tmp/slopworld-project-cancellation".into(),
        ..Default::default()
    };
    let manager = crate::session::test_manager(Config {
        projects: vec![project.clone()],
        ..Default::default()
    });
    let reached = std::sync::Arc::new(tokio::sync::Notify::new());
    let release = std::sync::Arc::new(tokio::sync::Notify::new());
    *manager.workspace.worktrees.relocation_pause.lock().unwrap() =
        Some((reached.clone(), release.clone()));
    let owner = manager.clone();
    let request = tokio::spawn(async move {
        Projects::new(&crate::application::Application::from_runtime(&owner))
            .update_project(
                "original",
                ProjectCfg {
                    name: "renamed".into(),
                    ..project
                },
            )
            .await
    });
    tokio::time::timeout(Duration::from_secs(5), reached.notified())
        .await
        .unwrap();
    request.abort();
    assert!(request.await.unwrap_err().is_cancelled());
    manager.session_boundary_fixture().try_write().unwrap_err();
    release.notify_one();
    tokio::time::timeout(
        Duration::from_secs(5),
        manager.session_operation(async {
            assert!(manager.config().await.project("renamed").is_some());
        }),
    )
    .await
    .unwrap();
}

#[tokio::test]
async fn project_rename_persists_library_references_and_keeps_items_runnable() {
    let project = ProjectCfg {
        name: "old".into(),
        dir: "/tmp/slopworld-library-rename".into(),
        ..Default::default()
    };
    let manager = crate::session::test_manager(Config {
        projects: vec![project.clone()],
        library: vec![crate::config::LibraryItemCfg {
            name: "review".into(),
            project: "old".into(),
            host: true,
            text: "Review changes".into(),
            ..Default::default()
        }],
        ..Default::default()
    });
    Projects::new(&crate::application::Application::from_runtime(&manager))
        .update_project(
            "old",
            ProjectCfg {
                name: "new".into(),
                ..project
            },
        )
        .await
        .unwrap();
    let cfg = manager.config().await;
    let request =
        crate::application::launch::plan_library_launch(&cfg, "review", Default::default())
            .unwrap();
    assert!(
        matches!(request.destination, crate::session::LaunchDestination::Project(name) if name == "new")
    );
    let disk = Config::load_library_for(manager.cfg_path()).await.unwrap();
    assert_eq!(disk[0].project, "new");
    assert!(!manager.configuration().reload_if_changed().await);
}

#[tokio::test]
async fn enabled_board_is_archived_and_recreated_project_starts_without_a_board() {
    let app = crate::application::test_application(Config::default());
    let projects = Projects::new(&app);
    let draft = ProjectCfg {
        name: "repo".into(),
        dir: "/tmp".into(),
        ..Default::default()
    };
    projects.add_project(draft.clone()).await.unwrap();
    let old = projects.projects().await[0].config.id.clone();
    let ids = crate::application::boards::identities(&app.sessions.config().await);
    app.services
        .boards
        .run(
            ids,
            Some((
                crate::shared::wire::BoardEdit {
                    action: "enable-board".into(),
                    board: old.clone(),
                    text: "repo".into(),
                    ..Default::default()
                },
                "host".into(),
            )),
        )
        .await
        .unwrap();
    projects.remove_project("repo").await.unwrap();
    projects.add_project(draft).await.unwrap();
    let ids = crate::application::boards::identities(&app.sessions.config().await);
    let catalog = app.services.boards.run(ids, None).await.unwrap();
    let archived = catalog.boards.iter().find(|b| b.id == old).unwrap();
    assert!(archived.archived);
    assert_eq!(archived.name, "repo");
    assert_eq!(archived.columns.len(), 4);
    assert_eq!(catalog.boards.len(), 1);
}
