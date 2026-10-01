use super::*;
use crate::api::handlers::config_patch;
use axum::{Extension, body::Body};
use serde_json::json;
use tower::ServiceExt;

#[tokio::test]
async fn shared_health_and_task_routes_continue_during_worktree_guard() {
    let manager = crate::session::test_manager(crate::config::Config::default());
    let app = crate::api::router(manager.clone()).layer(Extension(crate::grant::Cap::Root));
    let (release, wait) = tokio::sync::oneshot::channel::<()>();
    let (started, started_wait) = tokio::sync::oneshot::channel();
    let checkout = manager.session_read_operation(async {
        started.send(()).unwrap();
        wait.await.unwrap();
    });
    tokio::pin!(checkout);
    tokio::select! {
        () = checkout.as_mut() => panic!("checkout guard ended early"),
        _ = started_wait => {},
    }
    for path in ["/api/health", "/api/tasks"] {
        let response = tokio::time::timeout(
            std::time::Duration::from_secs(2),
            app.clone().oneshot(
                Request::builder()
                    .uri(path)
                    .header(crate::shared::protocol::SESSION_HEADER, "host")
                    .body(Body::empty())
                    .unwrap(),
            ),
        )
        .await
        .expect("route stalled behind the worktree guard")
        .unwrap();
        assert_eq!(response.status(), StatusCode::OK, "{path}");
    }
    release.send(()).unwrap();
    checkout.await;
}

#[tokio::test]
async fn read_routes_emit_the_declared_binary_payloads() {
    let manager = crate::session::test_manager(crate::config::Config::default());
    std::fs::write(
        &manager.cfg_path,
        toml::to_string(&crate::config::Config::default()).unwrap(),
    )
    .unwrap();
    let app = crate::api::router(manager).layer(Extension(crate::grant::Cap::Root));
    for path in [
        "/api/health",
        "/api/sessions",
        "/api/projects",
        "/api/library",
        "/api/templates",
        "/api/presets",
        "/api/config",
        "/api/capabilities",
        "/api/usage",
        "/api/audio",
        "/api/jukebox",
        "/api/grants",
        "/api/tasks",
        "/api/browse?path=/tmp",
    ] {
        let response = app
            .clone()
            .oneshot(
                Request::builder()
                    .uri(path)
                    .header(crate::shared::protocol::SESSION_HEADER, "host")
                    .body(Body::empty())
                    .unwrap(),
            )
            .await
            .unwrap();
        let status = response.status();
        assert_eq!(
            response.headers()[header::CONTENT_TYPE],
            CONTENT_TYPE,
            "{path}"
        );
        let bytes = axum::body::to_bytes(response.into_body(), 32 * 1024 * 1024)
            .await
            .unwrap();
        assert_eq!(
            status,
            StatusCode::OK,
            "{path}: {:?}",
            wire::Error::decode(bytes.clone())
        );
        let value = crate::shared::http_wire::decode_response("GET", path, &bytes).unwrap();
        if path == "/api/health" {
            assert_eq!(value["protocol_version"], 2);
        }
        if path == "/api/config" {
            assert!(value["metadata"]["defaults"]["daemon"].is_object());
        }
        if path == "/api/presets" {
            assert!(value["presets"].is_array());
        }
    }
}

#[tokio::test]
async fn binary_requests_preserve_defaults_and_reject_wrong_media_and_malformed_bytes() {
    let manager = crate::session::test_manager(crate::config::Config::default());
    let app = crate::api::router(manager).layer(Extension(crate::grant::Cap::Root));
    for (mime, body, expected) in [
        (
            "application/json",
            br#"{"name":"test","temp":true}"#.to_vec(),
            StatusCode::UNSUPPORTED_MEDIA_TYPE,
        ),
        (CONTENT_TYPE, vec![0x80], StatusCode::BAD_REQUEST),
        (
            CONTENT_TYPE,
            wire::ProjectPreviewReq {
                name: Some("test".into()),
                temp: Some(true),
            }
            .encode_to_vec(),
            StatusCode::OK,
        ),
    ] {
        let response = app
            .clone()
            .oneshot(
                Request::builder()
                    .method("POST")
                    .uri("/api/projects/preview")
                    .header(header::CONTENT_TYPE, mime)
                    .body(Body::from(body))
                    .unwrap(),
            )
            .await
            .unwrap();
        assert_eq!(response.status(), expected);
    }
    let session: crate::config::SessionCfg = domain(wire::SessionConfig {
        name: Some("agent".into()),
        ..Default::default()
    })
    .unwrap();
    assert_eq!(
        session.network,
        crate::config::SessionCfg::default().network
    );
    let request: super::super::types::RunReq = domain(wire::RunReq::default()).unwrap();
    assert_eq!(request.kind, crate::config::LibraryItemKind::default());
}

#[test]
fn patches_are_explicit_and_preserve_false_zero_empty_and_escaped_map_keys() {
    let mut values = wire::EditableConfig::default();
    let mut daemon = wire::Daemon {
        worker_templates: vec![],
        ..Default::default()
    };
    daemon.usage_items.insert(
        "a.b~c".into(),
        wire::UsageItem {
            poll: false,
            interval_secs: Some(0),
        },
    );
    values.daemon = Some(daemon);
    let patch = config_patch(wire::ConfigPatch {
        values: Some(values),
        paths: vec![
            "daemon.usage_items.a~1b~0c.poll".into(),
            "daemon.usage_items.a~1b~0c.interval_secs".into(),
            "daemon.worker_templates".into(),
        ],
    })
    .unwrap();
    assert_eq!(
        patch,
        json!({"daemon":{"usage_items":{"a.b~c":{"poll":false,"interval_secs":0}},"worker_templates":[]}})
    );
    for (path, expected) in [
        ("daemon.token", "This config path is not valid for editing."),
        ("daemon.bind", "This config path is not valid for editing."),
        ("daemon.typo", "unknown config path: daemon.typo"),
        (
            "daemon.usage_items",
            "Choose a config path that identifies one value.",
        ),
        ("session", "This config path is not valid for editing."),
        ("daemon", "This config path is not valid for editing."),
    ] {
        let (_, Proto(error)) = config_patch(wire::ConfigPatch {
            values: Some(wire::EditableConfig {
                daemon: Some(wire::Daemon::default()),
                ..Default::default()
            }),
            paths: vec![path.into()],
        })
        .unwrap_err();
        assert_eq!(error.error, expected, "{path}");
    }
}

#[tokio::test]
async fn normalized_errors_keep_response_metadata() {
    use axum::{Router, routing::get};
    let app = Router::new()
        .route(
            "/bad",
            get(|| async {
                (
                    StatusCode::BAD_REQUEST,
                    [("x-error-source", "fixture")],
                    "bad body",
                )
            }),
        )
        .layer(axum::middleware::from_fn(normalize_errors));
    let response = app
        .oneshot(Request::builder().uri("/bad").body(Body::empty()).unwrap())
        .await
        .unwrap();
    assert_eq!(response.status(), StatusCode::BAD_REQUEST);
    assert_eq!(response.headers()["x-error-source"], "fixture");
    assert_eq!(response.headers()[header::CONTENT_TYPE], CONTENT_TYPE);
    let body = axum::body::to_bytes(response.into_body(), 1024)
        .await
        .unwrap();
    assert_eq!(wire::Error::decode(body).unwrap().error, "bad body");
}

#[tokio::test]
async fn root_session_snapshots_continue_during_an_unrelated_lifecycle_transaction() {
    let manager = crate::session::test_manager(crate::config::Config {
        sessions: vec![crate::config::SessionCfg {
            name: "pager".into(),
            autostart: false,
            ..Default::default()
        }],
        ..Default::default()
    });
    manager.sync_from_config().await;
    let app = crate::api::router(manager.clone()).layer(Extension(crate::grant::Cap::Root));
    let (release, wait) = tokio::sync::oneshot::channel::<()>();
    let transaction = manager.session_operation(async { wait.await.unwrap() });
    tokio::pin!(transaction);
    assert!(futures::poll!(transaction.as_mut()).is_pending());
    for path in ["/api/sessions", "/api/sessions/pager"] {
        let response = tokio::time::timeout(
            std::time::Duration::from_secs(1),
            app.clone()
                .oneshot(Request::builder().uri(path).body(Body::empty()).unwrap()),
        )
        .await
        .expect("root snapshot waited on lifecycle I/O")
        .unwrap();
        assert_eq!(response.status(), StatusCode::OK);
    }
    release.send(()).unwrap();
    transaction.await;
}
