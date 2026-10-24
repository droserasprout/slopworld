use super::*;

#[test]
fn warning_projection_and_wire_round_trip_preserve_capability_floor() {
    let table = crate::presets::Table::default();
    for (tmux, daemon_config) in [(true, false), (false, true)] {
        let preset = crate::presets::SandboxPreset {
            name: "custom".into(),
            tmux,
            daemon_config,
            ..Default::default()
        };
        let json = sandbox_json(&preset, &table, &table);
        assert_eq!(json["warning_level"], "host_control");
        let mut wire: wire::SandboxPreset = serde_json::from_value(json).unwrap();
        wire.source = None;
        let round_trip: crate::presets::SandboxPreset = domain(wire).unwrap();
        assert_eq!(
            round_trip.warning_level(),
            crate::presets::WarningLevel::HostControl
        );
    }
}

#[tokio::test]
async fn update_distinguishes_validation_from_storage_failure() {
    let Some(root) = crate::test_support::isolated_with_env(|command, root| {
        command.env("SLOPD_CONFIG_ROOT", root.join("blocked-presets"));
    }) else {
        return;
    };
    let blocked = root.join("blocked-presets");
    std::fs::write(&blocked, "not a directory").unwrap();
    let manager = crate::application::test_application(crate::config::Config::default());
    let request = |command: &str| {
        Proto(wire::PresetRequest {
            definition: Some(wire::preset_request::Definition::Command(
                wire::CommandPreset {
                    name: Some("example".into()),
                    kind: Some("agent".into()),
                    cmd: Some(command.into()),
                    ..Default::default()
                },
            )),
        })
    };
    let path = || Path(("app_presets".into(), "example".into()));
    assert_eq!(
        update_preset(State(manager.clone()), path(), request(" "))
            .await
            .unwrap_err()
            .0,
        StatusCode::BAD_REQUEST
    );
    let (status, Proto(error)) = update_preset(State(manager), path(), request("echo ok"))
        .await
        .unwrap_err();
    assert_eq!(status, StatusCode::INTERNAL_SERVER_ERROR, "{}", error.error);
}

use crate::grant::{Cap, Grant, Level};
use axum::{
    Extension, Router,
    body::{Body, to_bytes},
    http::Request,
};
use prost::Message;
use tower::ServiceExt;

async fn request(app: &Router, method: &str, path: &str, bytes: Vec<u8>) -> StatusCode {
    let response = app
        .clone()
        .oneshot(
            Request::builder()
                .method(method)
                .uri(path)
                .header("content-type", crate::api::protobuf::CONTENT_TYPE)
                .body(Body::from(bytes))
                .unwrap(),
        )
        .await
        .unwrap();
    let status = response.status();
    let bytes = to_bytes(response.into_body(), 1024 * 1024).await.unwrap();
    if status.is_success() {
        assert!(wire::Ack::decode(bytes).unwrap().ok);
    }
    status
}

fn definition(command: &str) -> Vec<u8> {
    wire::PresetRequest {
        definition: Some(wire::preset_request::Definition::Command(
            wire::CommandPreset {
                name: Some("ignored-body-name".into()),
                kind: Some("agent".into()),
                cmd: Some(command.into()),
                source: Some("builtin".into()),
                ..Default::default()
            },
        )),
    }
    .encode_to_vec()
}

#[tokio::test]
async fn preset_routes_persist_reload_notify_and_reject_failed_mutations() {
    let Some(_) = crate::test_support::isolated_with_env(|command, root| {
        command.env("SLOPD_CONFIG_ROOT", root.join("presets"));
    }) else {
        return;
    };
    let manager = crate::application::test_application(crate::config::Config::default());
    let app = crate::api::router(manager.clone()).layer(Extension(Cap::Root));
    let mut events = manager.services.subscribe();
    let path = "/api/presets/app_presets/coverage-agent";
    assert_eq!(
        request(&app, "PUT", path, definition("echo first")).await,
        StatusCode::OK
    );
    assert!(
        crate::presets::table()
            .commands
            .iter()
            .any(|p| p.name == "coverage-agent" && p.cmd == "echo first")
    );
    assert!(
        !crate::presets::table()
            .commands
            .iter()
            .any(|p| p.name == "ignored-body-name")
    );
    assert!(matches!(
        events.try_recv().unwrap().event(),
        crate::application::Event::Sessions { .. }
    ));
    let disk = crate::presets::Table::users();
    assert!(
        disk.commands
            .iter()
            .any(|p| p.name == "coverage-agent" && p.cmd == "echo first")
    );
    assert_eq!(
        request(&app, "PUT", path, definition(" ")).await,
        StatusCode::BAD_REQUEST
    );
    assert!(events.try_recv().is_err());
    assert!(
        crate::presets::table()
            .commands
            .iter()
            .any(|p| p.name == "coverage-agent" && p.cmd == "echo first")
    );
    let scoped = crate::api::router(manager.clone()).layer(Extension(Cap::Scoped(Grant {
        grantor: "agent".into(),
        sessions: ["agent".into()].into(),
        level: Level::Rw,
        revoked: Default::default(),
    })));
    for (method, endpoint, body) in [
        ("PUT", path.to_string(), definition("echo forbidden")),
        ("DELETE", path.to_string(), vec![]),
        (
            "POST",
            "/api/presets/sandbox_presets/dotnet/copy".into(),
            vec![],
        ),
    ] {
        assert_eq!(
            request(&scoped, method, &endpoint, body).await,
            StatusCode::FORBIDDEN
        );
    }
    assert!(events.try_recv().is_err());
    assert_eq!(request(&app, "DELETE", path, vec![]).await, StatusCode::OK);
    assert!(!crate::presets::table().contains(PresetKind::AppPresets, "coverage-agent"));
    assert!(!crate::presets::Table::users().contains(PresetKind::AppPresets, "coverage-agent"));
    assert!(matches!(
        events.try_recv().unwrap().event(),
        crate::application::Event::Sessions { .. }
    ));
    assert_eq!(
        request(&app, "DELETE", path, vec![]).await,
        StatusCode::NOT_FOUND
    );
    assert!(events.try_recv().is_err());
}

#[tokio::test]
async fn copied_sandbox_can_be_updated_but_rejects_wrong_definition_kind() {
    let Some(_) = crate::test_support::isolated_with_env(|command, root| {
        command.env("SLOPD_CONFIG_ROOT", root.join("presets"));
    }) else {
        return;
    };
    let manager = crate::application::test_application(crate::config::Config::default());
    let app = crate::api::router(manager.clone()).layer(Extension(Cap::Root));
    let mut events = manager.services.subscribe();
    let copy = wire::CopyPresetReq {
        name: Some("coverage-dotnet".into()),
    }
    .encode_to_vec();
    assert_eq!(
        request(
            &app,
            "POST",
            "/api/presets/sandbox_presets/dotnet/copy",
            copy
        )
        .await,
        StatusCode::OK
    );
    assert!(crate::presets::table().contains(PresetKind::SandboxPresets, "coverage-dotnet"));
    assert!(matches!(
        events.try_recv().unwrap().event(),
        crate::application::Event::Sessions { .. }
    ));
    let sandbox_path = "/api/presets/sandbox_presets/coverage-dotnet";
    assert_eq!(
        request(&app, "PUT", sandbox_path, definition("true")).await,
        StatusCode::BAD_REQUEST
    );
    assert!(events.try_recv().is_err());
    let sandbox = wire::PresetRequest {
        definition: Some(wire::preset_request::Definition::Sandbox(Box::new(
            wire::SandboxPreset {
                name: Some("ignored-body-name".into()),
                description: Some("updated sandbox".into()),
                ..Default::default()
            },
        ))),
    }
    .encode_to_vec();
    assert_eq!(
        request(&app, "PUT", sandbox_path, sandbox).await,
        StatusCode::OK
    );
    assert!(
        crate::presets::table()
            .sandbox
            .iter()
            .any(|p| p.name == "coverage-dotnet" && p.description == "updated sandbox")
    );
    assert!(matches!(
        events.try_recv().unwrap().event(),
        crate::application::Event::Sessions { .. }
    ));
    assert_eq!(
        request(
            &app,
            "POST",
            "/api/presets/sandbox_presets/nonexistent/copy",
            vec![]
        )
        .await,
        StatusCode::NOT_FOUND
    );
    assert!(events.try_recv().is_err());
}
