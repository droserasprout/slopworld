//! Stored-state routes exercise authentication, extraction and protobuf encoding together.
use crate::config::{Config, SessionCfg};
use crate::grant::Level;
use crate::shared::{http_wire, protocol::TOKEN_HEADER, wire};
use axum::{
    body::Body,
    http::{Request, StatusCode},
    middleware,
};
use prost::Message;
use tower::ServiceExt;

struct Fixture {
    owner: crate::application::Application,
    app: axum::Router,
    session: SessionCfg,
    private: std::path::PathBuf,
}

impl Fixture {
    fn new() -> Self {
        let session = SessionCfg {
            name: "agent".into(),
            state_id: "0123456789abcdef".into(),
            autostart: false,
            ..Default::default()
        };
        let owner = crate::application::test_application(Config {
            sessions: vec![session.clone()],
            ..Default::default()
        });
        let app = super::router(owner.clone())
            .layer(middleware::from_fn_with_state(
                owner.config_authority.clone(),
                crate::auth,
            ))
            .layer(middleware::from_fn(super::protobuf::normalize_errors));
        let private = crate::sandbox::state_dir(&session).unwrap();
        std::fs::create_dir_all(&private).unwrap();
        std::fs::write(private.join("memory"), "retained bytes").unwrap();
        Self {
            owner,
            app,
            session,
            private,
        }
    }

    fn archive(&self) -> String {
        crate::sandbox::trash_state(&self.session, "reset")
            .unwrap()
            .unwrap()
            .file_name()
            .unwrap()
            .to_str()
            .unwrap()
            .to_owned()
    }

    async fn request(
        &self,
        method: &str,
        path: &str,
        token: Option<&str>,
    ) -> (StatusCode, Vec<u8>) {
        let mut request = Request::builder().method(method).uri(path);
        if let Some(token) = token {
            request = request.header(TOKEN_HEADER, token);
        }
        let response = tokio::time::timeout(
            std::time::Duration::from_secs(5),
            self.app
                .clone()
                .oneshot(request.body(Body::empty()).unwrap()),
        )
        .await
        .expect("storage route stalled")
        .unwrap();
        let status = response.status();
        assert_eq!(response.headers()["content-type"], http_wire::CONTENT_TYPE);
        let bytes = axum::body::to_bytes(response.into_body(), 1024 * 1024)
            .await
            .unwrap();
        (status, bytes.to_vec())
    }

    async fn success(&self, method: &str, path: &str) -> serde_json::Value {
        let (status, bytes) = self.request(method, path, Some("test-root-token")).await;
        assert_eq!(
            status,
            StatusCode::OK,
            "{path}: {:?}",
            wire::Error::decode(bytes.as_slice())
        );
        http_wire::decode_response(method, path, &bytes).unwrap()
    }
}

#[tokio::test]
async fn storage_routes_reject_missing_invalid_and_scoped_credentials_without_effects() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    let fixture = Fixture::new();
    let key = fixture.archive();
    let archive = fixture.private.parent().unwrap().join(".trash").join(&key);
    let mut tokens = vec![
        (None, StatusCode::UNAUTHORIZED),
        (Some("wrong".to_owned()), StatusCode::UNAUTHORIZED),
    ];
    for level in [Level::Ro, Level::Rw] {
        let token = fixture
            .owner
            .sessions
            .mint_grant("agent".into(), vec!["agent".into()], level)
            .await
            .unwrap();
        tokens.push((Some(token), StatusCode::FORBIDDEN));
    }
    for (token, expected) in tokens {
        for (method, path) in [
            ("GET", "/api/state".to_owned()),
            ("DELETE", "/api/state/trash".to_owned()),
            ("DELETE", format!("/api/state/trash/{key}")),
            ("POST", format!("/api/state/trash/{key}/restore")),
        ] {
            let (status, bytes) = fixture.request(method, &path, token.as_deref()).await;
            assert_eq!(status, expected, "{method} {path}");
            assert!(
                !wire::Error::decode(bytes.as_slice())
                    .unwrap()
                    .error
                    .is_empty()
            );
            assert!(archive.join("memory").exists());
            assert!(!fixture.private.exists());
        }
    }
}

#[tokio::test]
async fn storage_inventory_defaults_to_measured_sizes_and_rejects_invalid_query() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    let fixture = Fixture::new();
    for (query, measured) in [("", true), ("?sizes=true", true), ("?sizes=false", false)] {
        let value = fixture.success("GET", &format!("/api/state{query}")).await;
        let entries = value["entries"].as_array().unwrap();
        let entry = entries
            .iter()
            .find(|entry| entry["key"] == fixture.session.state_id)
            .unwrap();
        assert_eq!(entry["kind"], "active");
        assert_eq!(entry["session"], "agent");
        assert_eq!(entry["path"], fixture.private.to_str().unwrap());
        assert_eq!(
            entry["bytes"].as_u64().unwrap(),
            if measured { 14 } else { 0 }
        );
    }
    let (status, bytes) = fixture
        .request("GET", "/api/state?sizes=invalid", Some("test-root-token"))
        .await;
    assert_eq!(status, StatusCode::BAD_REQUEST);
    assert!(
        !wire::Error::decode(bytes.as_slice())
            .unwrap()
            .error
            .is_empty()
    );
    assert_eq!(
        std::fs::read(fixture.private.join("memory")).unwrap(),
        b"retained bytes"
    );
}

#[tokio::test]
async fn storage_restore_delete_and_empty_trash_preserve_owned_state_and_encode_errors() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    let fixture = Fixture::new();
    let key = fixture.archive();
    let restored = fixture
        .success("POST", &format!("/api/state/trash/{key}/restore"))
        .await;
    assert_eq!(
        restored,
        serde_json::json!({"ok": true, "session": "agent"})
    );
    assert_eq!(
        std::fs::read(fixture.private.join("memory")).unwrap(),
        b"retained bytes"
    );

    for (method, path, message) in [
        (
            "DELETE",
            format!("/api/state/orphan/{}", fixture.session.state_id),
            "owned",
        ),
        (
            "DELETE",
            format!("/api/state/active/{}", fixture.session.state_id),
            "only orphaned",
        ),
        ("DELETE", "/api/state/trash/%2E%2E%2Fescape".into(), ""),
        ("POST", format!("/api/state/trash/{key}/restore"), ""),
    ] {
        let (status, bytes) = fixture
            .request(method, &path, Some("test-root-token"))
            .await;
        assert_eq!(status, StatusCode::BAD_REQUEST, "{path}");
        let error = wire::Error::decode(bytes.as_slice()).unwrap().error;
        assert!(!error.is_empty() && error.contains(message), "{error}");
        assert!(fixture.private.join("memory").exists());
    }
    let key = fixture.archive();
    assert_eq!(
        fixture
            .success("DELETE", &format!("/api/state/trash/{key}"))
            .await,
        serde_json::json!({"ok": true})
    );
    assert!(
        fixture.success("GET", "/api/state").await["entries"]
            .as_array()
            .unwrap()
            .is_empty()
    );
    std::fs::create_dir_all(&fixture.private).unwrap();
    std::fs::write(fixture.private.join("memory"), "new archive").unwrap();
    fixture.archive();
    std::fs::create_dir_all(&fixture.private).unwrap();
    std::fs::write(fixture.private.join("memory"), "current").unwrap();
    for _ in 0..2 {
        assert_eq!(
            fixture.success("DELETE", "/api/state/trash").await,
            serde_json::json!({"ok": true})
        );
        let inventory = fixture.success("GET", "/api/state").await;
        assert!(
            inventory["entries"]
                .as_array()
                .unwrap()
                .iter()
                .all(|entry| entry["kind"] != "trash")
        );
        assert_eq!(
            std::fs::read(fixture.private.join("memory")).unwrap(),
            b"current"
        );
    }
}
