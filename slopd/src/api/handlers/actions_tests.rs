use super::*;
use crate::config::Config;
use axum::http::StatusCode;

fn proto<T: serde::de::DeserializeOwned>(value: serde_json::Value) -> Proto<T> {
    Proto(serde_json::from_value(value).unwrap())
}

#[tokio::test]
async fn invalid_explicit_runs_and_file_actions_return_bad_request() {
    let manager = crate::session::test_manager(Config::default());
    for (request, message) in [
        (
            json!({"kind":"prompt","command":" ","temp":true}),
            "Provide a command",
        ),
        (
            json!({"kind":"shell","command":"echo test"}),
            "Choose a project",
        ),
        (
            json!({"kind":"shell","project":"missing","path":"file","command":"cat"}),
            "does not exist",
        ),
    ] {
        let (status, Proto(error)) = run(State(manager.clone()), proto(request))
            .await
            .unwrap_err();
        assert_eq!(status, StatusCode::BAD_REQUEST);
        assert!(error.error.contains(message), "{}", error.error);
    }
    assert_eq!(
        file_action(
            State(manager),
            proto(json!({"project":"missing","path":"file","command":"cat"}))
        )
        .await
        .unwrap_err()
        .0,
        StatusCode::BAD_REQUEST
    );
}

#[tokio::test]
async fn reader_launches_reject_unknown_intents_and_missing_paths() {
    let manager = crate::session::test_manager(Config::default());
    for (intent, message) in [
        ("unknown", "Unknown terminal intent"),
        ("view", "needs a path"),
        ("edit", "needs a path"),
        ("search", "needs a path"),
    ] {
        let request = serde_json::from_value(json!({"intent": intent, "temp": true})).unwrap();
        let Err((status, Proto(error))) = plan_run(&manager, request).await else {
            panic!("invalid reader launch accepted");
        };
        assert_eq!(status, StatusCode::BAD_REQUEST);
        assert!(error.error.contains(message), "{}", error.error);
    }
}

#[tokio::test]
async fn host_shell_persistence_requires_a_project_and_no_explicit_command() {
    let manager = crate::session::test_manager(Config::default());
    for (project, command, temp, persistent, temporary) in [
        (" repo ", " ", false, true, false),
        ("repo", "echo hello", false, false, false),
        ("repo", "", true, false, true),
        ("", "", false, false, true),
    ] {
        let request = serde_json::from_value(json!({
            "kind": "shell", "host": true, "project": project,
            "command": command, "temp": temp, "like": " source "
        }))
        .unwrap();
        let plan = plan_run(&manager, request).await.unwrap();
        assert_eq!(plan.persistent_host, persistent);
        assert_eq!(plan.destination.temp, temporary);
        assert!(plan.host_action);
        assert_eq!(plan.item.project, project.trim());
        assert_eq!(plan.like, "source");
        assert_eq!(
            plan.item.command.as_deref(),
            (!command.trim().is_empty()).then_some(command.trim())
        );
        assert!(!plan.item.name.is_empty());
    }
}

#[tokio::test]
async fn reader_plan_preserves_source_identity_geometry_and_template() {
    let manager = crate::session::test_manager(Config::default());
    let request = serde_json::from_value(json!({
        "kind": "shell", "project": " repo ", "command": " less file ",
        "label": " reader ", "intent": "view", "worktree": "checkout",
        "reader": {"path": "file", "key": "key", "scope": "scope", "line": 17, "pinned": true},
        "cols": 90, "rows": 30, "agent_template": "review", "random_tips": ["tip"]
    }))
    .unwrap();
    let plan = plan_run(&manager, request).await.unwrap();
    assert_eq!(plan.item.name, "reader");
    assert_eq!(plan.item.command.as_deref(), Some("less file"));
    assert_eq!(plan.item.agent_template, "review");
    assert!(!plan.persistent_host);
    assert!(!plan.host_action);
    let destination = plan.destination;
    assert_eq!(destination.worktree, "checkout");
    assert_eq!(destination.context.intent, "view");
    assert_eq!(destination.context.reader.path, "file");
    assert_eq!(destination.context.reader.key, "key");
    assert_eq!(destination.context.reader.scope, "scope");
    assert_eq!(destination.context.reader.line, 17);
    assert!(destination.context.reader.pinned);
    assert_eq!((destination.cols, destination.rows), (Some(90), Some(30)));
    assert_eq!(destination.random_tips, ["tip"]);
}
