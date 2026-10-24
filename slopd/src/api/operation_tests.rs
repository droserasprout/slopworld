//! Exercise application operations through routing, authorization and Protobuf boundaries.
use crate::config::{Config, LibraryItemCfg, LibraryItemKind, ProjectCfg};
use axum::{
    Extension, Router,
    body::Body,
    http::{Request, StatusCode},
};
use serde_json::{Value, json};
use tower::ServiceExt;

async fn request(app: &Router, method: &str, path: &str, value: Value) -> (StatusCode, Value) {
    let body = crate::shared::http_wire::encode_request(method, path, value).unwrap();
    let response = app
        .clone()
        .oneshot(
            Request::builder()
                .method(method)
                .uri(path)
                .header("content-type", crate::api::protobuf::CONTENT_TYPE)
                .header(crate::shared::protocol::SESSION_HEADER, crate::tasks::HOST)
                .body(Body::from(body))
                .unwrap(),
        )
        .await
        .unwrap();
    let status = response.status();
    let body = axum::body::to_bytes(response.into_body(), 1024 * 1024)
        .await
        .unwrap();
    let value = if status.is_success() {
        crate::shared::http_wire::decode_response(method, path, &body).unwrap()
    } else {
        use prost::Message;
        json!({"error": crate::shared::wire::Error::decode(body).unwrap().error})
    };
    (status, value)
}

fn project(path: &std::path::Path) -> ProjectCfg {
    ProjectCfg {
        name: "repo".into(),
        dir: path.to_string_lossy().into(),
        ..Default::default()
    }
}

#[tokio::test]
async fn worktree_http_round_trip_preserves_identity_and_removes_records() {
    let root = crate::test_support::fixture("worktree-http");
    for args in [
        vec!["init", "-q"],
        vec![
            "-c",
            "user.name=Test",
            "-c",
            "user.email=test@example.invalid",
            "commit",
            "-q",
            "--allow-empty",
            "-m",
            "initial",
        ],
    ] {
        assert!(
            std::process::Command::new("git")
                .arg("-C")
                .arg(&*root)
                .args(args)
                .status()
                .unwrap()
                .success()
        );
    }
    let manager = crate::application::test_application(Config {
        projects: vec![project(&root)],
        ..Default::default()
    });
    let app = crate::api::router(manager.clone()).layer(Extension(crate::grant::Cap::Root));
    let (status, preview) = request(
        &app,
        "POST",
        "/api/worktrees/preview",
        json!({"project":"repo"}),
    )
    .await;
    assert_eq!(status, StatusCode::OK, "{preview}");
    assert_eq!(preview["commit"].as_str().unwrap().len(), 40);
    let (status, created) = request(
        &app,
        "POST",
        "/api/worktrees",
        json!({"project":"repo", "name":"feature"}),
    )
    .await;
    assert_eq!(status, StatusCode::OK, "{created}");
    let id = created["id"].as_str().unwrap();
    let path = format!("/api/worktrees/{id}?project=repo");
    let (status, renamed) = request(&app, "PUT", &path, json!({"name":"renamed"})).await;
    assert_eq!(status, StatusCode::OK, "{renamed}");
    assert_eq!(renamed["id"], id);
    assert_eq!(renamed["name"], "renamed");
    assert!(!std::path::Path::new(created["path"].as_str().unwrap()).exists());
    // A checkout removed outside the daemon still needs its durable record removed.
    assert!(
        std::process::Command::new("git")
            .arg("-C")
            .arg(&*root)
            .args(["worktree", "remove", renamed["path"].as_str().unwrap()])
            .status()
            .unwrap()
            .success()
    );
    let (status, removed) = request(&app, "DELETE", &path, json!({})).await;
    assert_eq!(status, StatusCode::OK, "{removed}");
    assert!(manager.sessions.worktree_records().worktrees.is_empty());
    let (status, missing) = request(&app, "PUT", &path, json!({"name":"again"})).await;
    assert_eq!(status, StatusCode::BAD_REQUEST, "{missing}");
}

#[tokio::test]
async fn failed_launch_removes_disposable_rows_but_retains_saved_shells() {
    let Some(root) = crate::test_support::isolated() else {
        return;
    };
    let project_dir = root.join("project");
    std::fs::create_dir_all(&project_dir).unwrap();
    let manager = crate::application::test_application_with_socket(
        Config {
            projects: vec![project(&project_dir)],
            ..Default::default()
        },
        "/dev/null/slopworld-test-socket",
    );
    let app = crate::api::router(manager.clone()).layer(Extension(crate::grant::Cap::Root));
    let (status, error) = request(&app, "POST", "/api/run", json!({
        "kind":"shell", "host":true, "project":"repo", "command":"sleep 60", "label":"disposable"
    })).await;
    assert_eq!(status, StatusCode::BAD_REQUEST, "{error}");
    assert!(manager.sessions.views().await.is_empty());
    let (status, error) = request(
        &app,
        "POST",
        "/api/run",
        json!({
            "kind":"shell", "host":true, "project":"repo", "label":"saved"
        }),
    )
    .await;
    assert_eq!(status, StatusCode::BAD_REQUEST, "{error}");
    assert_eq!(manager.sessions.views().await.len(), 1);
    assert_eq!(manager.sessions.config().await.host_terminals.len(), 1);
}

#[tokio::test]
async fn explicit_saved_and_captured_actions_succeed_through_http() {
    let Some(root) = crate::test_support::isolated() else {
        return;
    };
    let project_dir = root.join("project");
    std::fs::create_dir_all(&project_dir).unwrap();
    let manager = crate::application::test_application(Config {
        projects: vec![project(&project_dir)],
        library: vec![LibraryItemCfg {
            name: "saved".into(),
            text: "# saved command".into(),
            project: "repo".into(),
            host: true,
            kind: LibraryItemKind::Shell,
            command: Some("/bin/sleep 60".into()),
            ..Default::default()
        }],
        ..Default::default()
    });
    let app = crate::api::router(manager.clone()).layer(Extension(crate::grant::Cap::Root));
    for (path, value) in [
        (
            "/api/run",
            json!({"kind":"shell", "host":true, "project":"repo", "command":"/bin/sleep 60", "label":"explicit"}),
        ),
        ("/api/library/saved/run", json!({"project":"repo"})),
    ] {
        let (status, launched) = request(&app, "POST", path, value).await;
        assert_eq!(status, StatusCode::OK, "{launched}");
        let name = launched["session"].as_str().unwrap();
        assert!(manager.sessions.tmux.exists(name).await);
        manager.sessions.stop(name).await.unwrap();
    }
    let file = root.join("sample ' {{ absolute_path }}");
    std::fs::write(&file, "file-action-result").unwrap();
    let (status, output) = request(
        &app,
        "POST",
        "/api/file-action",
        json!({
            "host":true, "path":file,
            "command":format!("cat {}", shlex::try_quote(file.to_str().unwrap()).unwrap())
        }),
    )
    .await;
    assert_eq!(status, StatusCode::OK, "{output}");
    assert!(
        output["output"]
            .as_str()
            .unwrap()
            .contains("file-action-result")
    );
}
