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

type EventReceiver = tokio::sync::broadcast::Receiver<std::sync::Arc<crate::session::EventMessage>>;

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
    let mut station = radio_station();

    assert_initial_catalog_empty(&app).await;
    assert_scoped_routes_forbidden(
        &manager,
        &station,
        &[
            ("GET", routes::JUKEBOX_PRESETS),
            ("POST", routes::JUKEBOX_PRESETS),
            ("PUT", path),
            ("DELETE", path),
        ],
    )
    .await;
    assert!(!root.join("jukebox/radio.toml").exists());

    let (saved_user_catalog, saved) =
        create_and_read_preset(&app, &mut events, &station, &root).await;
    assert_scoped_edits_preserve_preset(PresetRouteContext {
        manager: &manager,
        app: &app,
        events: &mut events,
        station: &station,
        path,
        root: &root,
        saved_user_catalog: &saved_user_catalog,
        saved: &saved,
    })
    .await;
    assert_invalid_and_failed_writes(&app, &mut events, &station, path, &root, &saved).await;
    update_and_delete_preset(&app, &mut events, &mut station, path, &root).await;
    assert_storage_errors(&app, &station, path, &root).await;
    std::fs::remove_dir_all(manager.cfg_path.parent().unwrap()).unwrap();
}

fn radio_station() -> wire::Station {
    serde_json::from_value(json!({
        "id":"radio", "default_rate":96,
        "metadata":{"name":"Radio", "donate":"https://example.org/donate", "title_regex":"(?P<title>.+)"},
        "streams":[{"rate":96,"key":"main","url":"https://example.org/stream"}]
    }))
    .unwrap()
}

async fn assert_initial_catalog_empty(app: &Router) {
    let (status, bytes) = request(app, "GET", routes::JUKEBOX_PRESETS, None).await;
    assert_eq!(status, StatusCode::OK);
    assert!(wire::JukeboxCatalog::decode(bytes.as_slice())
        .unwrap()
        .stations
        .is_empty());
}

fn scoped_app(manager: &std::sync::Arc<crate::session::Manager>, level: Level) -> Router {
    crate::api::router(manager.clone()).layer(Extension(Cap::Scoped(Grant {
        grantor: "agent".into(),
        sessions: ["agent".into()].into(),
        level,
        revoked: Default::default(),
    })))
}

async fn assert_scoped_routes_forbidden(
    manager: &std::sync::Arc<crate::session::Manager>,
    station: &wire::Station,
    routes: &[(&str, &str)],
) {
    for level in [Level::Ro, Level::Rw] {
        let scoped = scoped_app(manager, level);
        for (method, uri) in routes {
            assert_error(&scoped, method, uri, Some(station), StatusCode::FORBIDDEN).await;
        }
    }
}

async fn assert_error(
    app: &Router,
    method: &str,
    path: &str,
    station: Option<&wire::Station>,
    expected: StatusCode,
) {
    let (status, bytes) = request(app, method, path, station).await;
    assert_eq!(status, expected);
    assert!(!wire::Error::decode(bytes.as_slice())
        .unwrap()
        .error
        .is_empty());
}

async fn create_and_read_preset(
    app: &Router,
    events: &mut EventReceiver,
    station: &wire::Station,
    root: &std::path::Path,
) -> (Vec<u8>, Vec<u8>) {
    let (status, bytes) = request(app, "POST", routes::JUKEBOX_PRESETS, Some(station)).await;
    assert_eq!(status, StatusCode::OK);
    assert!(wire::Ack::decode(bytes.as_slice()).unwrap().ok);
    assert!(root.join("jukebox/radio.toml").exists());
    assert_eq!(
        crate::jukebox::catalog().resolve("radio", "main").unwrap(),
        "https://example.org/stream"
    );
    assert_preset_event(events);
    let (_, bytes) = request(app, "GET", routes::JUKEBOX_PRESETS, None).await;
    assert_eq!(
        wire::JukeboxCatalog::decode(bytes.as_slice())
            .unwrap()
            .stations,
        vec![station.clone()]
    );
    let saved = std::fs::read(root.join("jukebox/radio.toml")).unwrap();
    (bytes, saved)
}

struct PresetRouteContext<'a> {
    manager: &'a std::sync::Arc<crate::session::Manager>,
    app: &'a Router,
    events: &'a mut EventReceiver,
    station: &'a wire::Station,
    path: &'a str,
    root: &'a std::path::Path,
    saved_user_catalog: &'a [u8],
    saved: &'a [u8],
}

async fn assert_scoped_edits_preserve_preset(context: PresetRouteContext<'_>) {
    assert_scoped_routes_forbidden(
        context.manager,
        context.station,
        &[("PUT", context.path), ("DELETE", context.path)],
    )
    .await;
    assert_eq!(
        std::fs::read(context.root.join("jukebox/radio.toml")).unwrap(),
        context.saved
    );
    let (_, bytes) = request(context.app, "GET", routes::JUKEBOX_PRESETS, None).await;
    assert_eq!(bytes, context.saved_user_catalog);
    assert_eq!(
        crate::jukebox::catalog().resolve("radio", "main").unwrap(),
        "https://example.org/stream"
    );
    assert_no_preset_event(context.events);
    let (_, bytes) = request(context.app, "GET", routes::JUKEBOX, None).await;
    let public = wire::JukeboxCatalog::decode(bytes.as_slice()).unwrap();
    assert_eq!(public.stations[0].streams[0].url, "");
}

async fn assert_invalid_and_failed_writes(
    app: &Router,
    events: &mut EventReceiver,
    station: &wire::Station,
    path: &str,
    root: &std::path::Path,
    saved: &[u8],
) {
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
        assert_error(app, "PUT", path, Some(&invalid), StatusCode::BAD_REQUEST).await;
        assert_eq!(
            std::fs::read(root.join("jukebox/radio.toml")).unwrap(),
            saved
        );
        assert_no_preset_event(events);
    }
    {
        let _fault = crate::paths::fail_writes(&root.join("jukebox/radio.toml"));
        assert_error(
            app,
            "PUT",
            path,
            Some(station),
            StatusCode::INTERNAL_SERVER_ERROR,
        )
        .await;
    }
    assert_eq!(
        std::fs::read(root.join("jukebox/radio.toml")).unwrap(),
        saved
    );
    assert_no_preset_event(events);
}

async fn update_and_delete_preset(
    app: &Router,
    events: &mut EventReceiver,
    station: &mut wire::Station,
    path: &str,
    root: &std::path::Path,
) {
    station.metadata = None; // Omitted metadata uses the domain's display-name default.
    station.streams[0].url = "https://example.org/updated-stream".into();
    assert_eq!(
        request(app, "PUT", path, Some(station)).await.0,
        StatusCode::OK
    );
    let (_, bytes) = request(app, "GET", routes::JUKEBOX_PRESETS, None).await;
    let edited = wire::JukeboxCatalog::decode(bytes.as_slice()).unwrap();
    assert_eq!(edited.stations[0].metadata.as_ref().unwrap().name, "radio");
    assert_eq!(
        crate::jukebox::catalog().resolve("radio", "main").unwrap(),
        station.streams[0].url
    );
    assert_preset_event(events);
    assert_eq!(request(app, "DELETE", path, None).await.0, StatusCode::OK);
    assert!(!root.join("jukebox/radio.toml").exists());
    assert!(crate::jukebox::catalog().stations.is_empty());
    assert_preset_event(events);
    let (status, bytes) = request(app, "DELETE", path, None).await;
    assert_eq!(status, StatusCode::NOT_FOUND);
    assert!(wire::Error::decode(bytes.as_slice())
        .unwrap()
        .error
        .contains("unknown jukebox preset"));
}

async fn assert_storage_errors(
    app: &Router,
    station: &wire::Station,
    path: &str,
    root: &std::path::Path,
) {
    let blocked_dir = root.join("not-a-directory");
    std::fs::write(&blocked_dir, "not a directory").unwrap();
    std::env::set_var("SLOPD_JUKEBOX", &blocked_dir);
    assert_error(
        app,
        "POST",
        routes::JUKEBOX_PRESETS,
        Some(station),
        StatusCode::INTERNAL_SERVER_ERROR,
    )
    .await;
    assert_error(app, "DELETE", path, None, StatusCode::INTERNAL_SERVER_ERROR).await;
}

fn assert_preset_event(events: &mut EventReceiver) {
    assert!(matches!(
        events.try_recv().unwrap().event(),
        crate::session::Event::Jukebox { .. }
    ));
}

fn assert_no_preset_event(events: &mut EventReceiver) {
    assert!(matches!(
        events.try_recv(),
        Err(tokio::sync::broadcast::error::TryRecvError::Empty)
    ));
}
