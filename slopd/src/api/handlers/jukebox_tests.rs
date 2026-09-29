use super::*;
use crate::{
    grant::{Cap, Grant, Level},
    shared::protocol::routes,
};
use axum::{
    body::{to_bytes, Body},
    http::Request,
    Extension, Router,
};
use prost::Message;
use tower::ServiceExt;

async fn request(
    app: &Router,
    method: &str,
    path: &str,
    station: Option<&wire::Station>,
) -> (StatusCode, Vec<u8>) {
    let response = app
        .clone()
        .oneshot(
            Request::builder()
                .method(method)
                .uri(path)
                .header("content-type", crate::api::protobuf::CONTENT_TYPE)
                .body(Body::from(
                    station.map(Message::encode_to_vec).unwrap_or_default(),
                ))
                .unwrap(),
        )
        .await
        .unwrap();
    let status = response.status();
    assert_eq!(
        response.headers()["content-type"],
        crate::api::protobuf::CONTENT_TYPE
    );
    (
        status,
        to_bytes(response.into_body(), 1024 * 1024)
            .await
            .unwrap()
            .to_vec(),
    )
}

#[tokio::test]
async fn preset_routes_persist_reload_validate_and_protect_stream_urls() {
    let Some(root) = crate::test_support::isolated() else {
        return;
    };
    std::env::set_var("SLOPD_JUKEBOX", root.join("jukebox"));
    let manager = crate::session::test_manager_with_socket(
        crate::config::Config::default(),
        format!("jukebox-{}", uuid::Uuid::new_v4()),
    );
    let app = crate::api::router(manager.clone()).layer(Extension(Cap::Root));
    let mut events = manager.events.subscribe();
    let path = "/api/jukebox/presets/radio";
    let mut station: wire::Station = serde_json::from_value(json!({
            "id":"radio", "default_rate":96,
            "metadata":{"name":"Radio", "donate":"https://example.org/donate", "title_regex":"(?P<title>.+)"},
            "streams":[{"rate":96,"key":"main","url":"https://example.org/stream"}]
        })).unwrap();
    let (status, bytes) = request(&app, "GET", routes::JUKEBOX_PRESETS, None).await;
    assert_eq!(status, StatusCode::OK);
    assert!(wire::JukeboxCatalog::decode(bytes.as_slice())
        .unwrap()
        .stations
        .is_empty());

    // Every editor route is root-only, including the URL-bearing read.
    for level in [Level::Ro, Level::Rw] {
        let scoped = crate::api::router(manager.clone()).layer(Extension(Cap::Scoped(Grant {
            grantor: "agent".into(),
            sessions: ["agent".into()].into(),
            level,
            revoked: Default::default(),
        })));
        for (method, uri) in [
            ("GET", routes::JUKEBOX_PRESETS),
            ("POST", routes::JUKEBOX_PRESETS),
            ("PUT", path),
            ("DELETE", path),
        ] {
            let (status, bytes) = request(&scoped, method, uri, Some(&station)).await;
            assert_eq!(status, StatusCode::FORBIDDEN);
            assert!(!wire::Error::decode(bytes.as_slice())
                .unwrap()
                .error
                .is_empty());
        }
    }
    assert!(!root.join("jukebox/radio.toml").exists());
    let (status, bytes) = request(&app, "POST", routes::JUKEBOX_PRESETS, Some(&station)).await;
    assert_eq!(status, StatusCode::OK);
    assert!(wire::Ack::decode(bytes.as_slice()).unwrap().ok);
    assert!(root.join("jukebox/radio.toml").exists());
    assert_eq!(
        crate::jukebox::catalog().resolve("radio", "main").unwrap(),
        "https://example.org/stream"
    );
    assert!(matches!(
        events.try_recv().unwrap().event(),
        crate::session::Event::Jukebox { .. }
    ));
    let (_, bytes) = request(&app, "GET", routes::JUKEBOX_PRESETS, None).await;
    assert_eq!(
        wire::JukeboxCatalog::decode(bytes.as_slice())
            .unwrap()
            .stations,
        vec![station.clone()]
    );
    let saved_user_catalog = bytes.clone();
    let saved = std::fs::read(root.join("jukebox/radio.toml")).unwrap();
    for level in [Level::Ro, Level::Rw] {
        let scoped = crate::api::router(manager.clone()).layer(Extension(Cap::Scoped(Grant {
            grantor: "agent".into(),
            sessions: ["agent".into()].into(),
            level,
            revoked: Default::default(),
        })));
        for method in ["PUT", "DELETE"] {
            let (status, bytes) = request(&scoped, method, path, Some(&station)).await;
            assert_eq!(status, StatusCode::FORBIDDEN);
            assert!(!wire::Error::decode(bytes.as_slice())
                .unwrap()
                .error
                .is_empty());
        }
    }
    assert_eq!(
        std::fs::read(root.join("jukebox/radio.toml")).unwrap(),
        saved
    );
    let (_, bytes) = request(&app, "GET", routes::JUKEBOX_PRESETS, None).await;
    assert_eq!(bytes, saved_user_catalog);
    assert_eq!(
        crate::jukebox::catalog().resolve("radio", "main").unwrap(),
        "https://example.org/stream"
    );
    assert!(matches!(
        events.try_recv(),
        Err(tokio::sync::broadcast::error::TryRecvError::Empty)
    ));
    let (_, bytes) = request(&app, "GET", routes::JUKEBOX, None).await;
    let public = wire::JukeboxCatalog::decode(bytes.as_slice()).unwrap();
    assert_eq!(public.stations[0].streams[0].url, "");

    for invalid in [
        wire::Station {
            id: "renamed".into(),
            ..station.clone()
        },
        wire::Station {
            streams: vec![],
            ..station.clone()
        },
        wire::Station {
            default_rate: 123,
            ..station.clone()
        },
    ] {
        let (status, bytes) = request(&app, "PUT", path, Some(&invalid)).await;
        assert_eq!(status, StatusCode::BAD_REQUEST);
        assert!(!wire::Error::decode(bytes.as_slice())
            .unwrap()
            .error
            .is_empty());
        assert_eq!(
            std::fs::read(root.join("jukebox/radio.toml")).unwrap(),
            saved
        );
        assert!(matches!(
            events.try_recv(),
            Err(tokio::sync::broadcast::error::TryRecvError::Empty)
        ));
    }
    {
        let _fault = crate::paths::fail_writes(&root.join("jukebox/radio.toml"));
        let (status, bytes) = request(&app, "PUT", path, Some(&station)).await;
        assert_eq!(status, StatusCode::INTERNAL_SERVER_ERROR);
        assert!(!wire::Error::decode(bytes.as_slice())
            .unwrap()
            .error
            .is_empty());
    }
    assert_eq!(
        std::fs::read(root.join("jukebox/radio.toml")).unwrap(),
        saved
    );
    assert!(matches!(
        events.try_recv(),
        Err(tokio::sync::broadcast::error::TryRecvError::Empty)
    ));
    station.metadata = None; // Omitted metadata uses the domain's display-name default.
    station.streams[0].url = "https://example.org/updated-stream".into();
    assert_eq!(
        request(&app, "PUT", path, Some(&station)).await.0,
        StatusCode::OK
    );
    let (_, bytes) = request(&app, "GET", routes::JUKEBOX_PRESETS, None).await;
    let edited = wire::JukeboxCatalog::decode(bytes.as_slice()).unwrap();
    assert_eq!(edited.stations[0].metadata.as_ref().unwrap().name, "radio");
    assert_eq!(
        crate::jukebox::catalog().resolve("radio", "main").unwrap(),
        station.streams[0].url
    );
    assert!(matches!(
        events.try_recv().unwrap().event(),
        crate::session::Event::Jukebox { .. }
    ));
    assert_eq!(request(&app, "DELETE", path, None).await.0, StatusCode::OK);
    assert!(!root.join("jukebox/radio.toml").exists());
    assert!(crate::jukebox::catalog().stations.is_empty());
    assert!(matches!(
        events.try_recv().unwrap().event(),
        crate::session::Event::Jukebox { .. }
    ));
    let (status, bytes) = request(&app, "DELETE", path, None).await;
    assert_eq!(status, StatusCode::NOT_FOUND);
    assert!(wire::Error::decode(bytes.as_slice())
        .unwrap()
        .error
        .contains("unknown jukebox preset"));
    let blocked_dir = root.join("not-a-directory");
    std::fs::write(&blocked_dir, "not a directory").unwrap();
    std::env::set_var("SLOPD_JUKEBOX", &blocked_dir);
    let (status, bytes) = request(&app, "POST", routes::JUKEBOX_PRESETS, Some(&station)).await;
    assert_eq!(status, StatusCode::INTERNAL_SERVER_ERROR);
    assert!(!wire::Error::decode(bytes.as_slice())
        .unwrap()
        .error
        .is_empty());
    let (status, bytes) = request(&app, "DELETE", path, None).await;
    assert_eq!(status, StatusCode::INTERNAL_SERVER_ERROR);
    assert!(!wire::Error::decode(bytes.as_slice())
        .unwrap()
        .error
        .is_empty());
    std::fs::remove_dir_all(manager.cfg_path.parent().unwrap()).unwrap();
}
