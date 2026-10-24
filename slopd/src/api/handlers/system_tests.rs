use crate::api::protobuf::CONTENT_TYPE;
use crate::grant::Cap;
use crate::shared::{protocol, wire};
use axum::{
    Extension, Router,
    body::{Body, to_bytes},
    http::{Request, StatusCode},
};
use prost::Message;
use tower::ServiceExt;

async fn open(app: &Router, request: wire::RedrawReq) -> (StatusCode, Vec<u8>) {
    let response = app
        .clone()
        .oneshot(
            Request::builder()
                .method("POST")
                .uri(protocol::routes::NCSPOT)
                .header("content-type", CONTENT_TYPE)
                .body(Body::from(request.encode_to_vec()))
                .unwrap(),
        )
        .await
        .unwrap();
    let status = response.status();
    assert_eq!(response.headers()["content-type"], CONTENT_TYPE);
    (
        status,
        to_bytes(response.into_body(), 1024 * 1024)
            .await
            .unwrap()
            .to_vec(),
    )
}

#[tokio::test]
async fn ncspot_http_clamps_dimensions_reuses_player_and_reports_launch_errors() {
    use std::os::unix::fs::PermissionsExt;
    let Some(root) = crate::test_support::isolated_with_env(|command, root| {
        let path = std::env::var_os("PATH").unwrap_or_default();
        command
            .env(
                "PATH",
                format!("{}:{}", root.join("bin").display(), path.to_string_lossy()),
            )
            .env("XDG_RUNTIME_DIR", root.join("runtime"))
            .env_remove("SLOPD_RUNTIME");
    }) else {
        return;
    };
    std::fs::create_dir_all(root.join("bin")).unwrap();
    std::fs::create_dir_all(root.join("runtime")).unwrap();
    let executable = root.join("bin/ncspot");
    std::fs::write(
        &executable,
        "#!/bin/sh\nif [ \"$1\" = --version ]; then exit 0; fi\nexec /bin/sleep 60\n",
    )
    .unwrap();
    std::fs::set_permissions(&executable, std::fs::Permissions::from_mode(0o700)).unwrap();
    let socket = crate::test_support::TmuxSocket::new();
    let manager =
        crate::application::test_application_with_socket(Default::default(), socket.path.clone());
    let app = crate::api::router(manager.clone()).layer(Extension(Cap::Root));
    for (cols, rows, expected) in [
        (90, 30, (90, 30)),
        (
            0,
            u32::MAX,
            (protocol::TERMINAL_MIN_COLS, protocol::TERMINAL_MAX_ROWS),
        ),
        (
            u32::MAX,
            0,
            (protocol::TERMINAL_MAX_COLS, protocol::TERMINAL_MIN_ROWS),
        ),
    ] {
        let request = wire::RedrawReq {
            cols: Some(cols),
            rows: Some(rows),
        };
        let (status, bytes) = open(&app, request).await;
        assert_eq!(
            status,
            StatusCode::OK,
            "{}",
            String::from_utf8_lossy(&bytes)
        );
        let result = wire::SessionResult::decode(bytes.as_slice()).unwrap();
        assert!(result.ok);
        let views = manager.sessions.views().await;
        let view = views
            .iter()
            .find(|view| view.name == result.session)
            .unwrap();
        assert!(view.host && view.ephemeral);
        assert_eq!((view.runtime.cols, view.runtime.rows), expected);
        let (status, bytes) = open(&app, request).await;
        assert_eq!(status, StatusCode::OK);
        assert_eq!(
            wire::SessionResult::decode(bytes.as_slice())
                .unwrap()
                .session,
            result.session
        );
        assert_eq!(manager.sessions.host_sessions().await.len(), 1);
        manager.sessions.stop(&result.session).await.unwrap();
    }
    std::fs::write(&executable, "#!/bin/sh\nexit 9\n").unwrap();
    let (status, bytes) = open(&app, wire::RedrawReq::default()).await;
    assert_eq!(status, StatusCode::INTERNAL_SERVER_ERROR);
    assert!(
        wire::Error::decode(bytes.as_slice())
            .unwrap()
            .error
            .contains("ncspot --version failed")
    );
    assert!(manager.sessions.host_sessions().await.is_empty());
}
