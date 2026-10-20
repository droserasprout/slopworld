use crate::config::{Config, ProjectCfg};
use std::time::Duration;

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
    let error = manager
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
    *manager.worktrees.relocation_pause.lock().unwrap() = Some((reached.clone(), release.clone()));
    let owner = manager.clone();
    let request = tokio::spawn(async move {
        owner
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
    manager.session_boundary.try_write().unwrap_err();
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
