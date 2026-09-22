//! Root-only CRUD for user jukebox station definitions.

use crate::api::protobuf::{reply, Proto};
use crate::shared::wire;

use axum::extract::{Path, State};
use axum::http::StatusCode;
use serde_json::json;

use super::super::{err, ApiResult, Mgr};

pub(crate) async fn jukebox_presets() -> ApiResult<wire::JukeboxCatalog> {
    // The regular catalog omits URLs before it reaches the mod. This separate editor road is
    // root-only and intentionally returns the complete user definition for the form.
    let stations = crate::jukebox::Catalog::user_presets()
        .into_iter()
        .map(|station| {
            json!({
                "id": station.id,
                "default_rate": station.default_rate,
                "metadata": station.metadata,
                "streams": station.streams.into_iter().map(|stream| json!({
                    "rate": stream.rate,
                    "key": stream.key,
                    "url": stream.url,
                })).collect::<Vec<_>>(),
            })
        })
        .collect::<Vec<_>>();
    reply(json!({ "stations": stations }))
}

pub(crate) async fn create_jukebox_preset(
    State(m): State<Mgr>,
    Proto(body): Proto<wire::Station>,
) -> ApiResult<wire::Ack> {
    save(m, body, None).await
}

pub(crate) async fn update_jukebox_preset(
    State(m): State<Mgr>,
    Path(id): Path<String>,
    Proto(body): Proto<wire::Station>,
) -> ApiResult<wire::Ack> {
    save(m, body, Some(id)).await
}

pub(crate) async fn delete_jukebox_preset(
    State(m): State<Mgr>,
    Path(id): Path<String>,
) -> ApiResult<wire::Ack> {
    let deleted = tokio::task::spawn_blocking(move || crate::jukebox::delete_user(&id))
        .await
        .map_err(|error| err(StatusCode::INTERNAL_SERVER_ERROR, error))?;
    deleted.map_err(|error| err(StatusCode::NOT_FOUND, error))?;
    m.reload_jukebox_if_changed().await;
    reply(json!({ "ok": true }))
}

async fn save(m: Mgr, body: wire::Station, original_id: Option<String>) -> ApiResult<wire::Ack> {
    let station = station_from_wire(body)?;
    if let Some(original_id) = original_id {
        if station.id != original_id {
            return Err(err(
                StatusCode::BAD_REQUEST,
                "jukebox preset id cannot change while editing",
            ));
        }
    }
    tokio::task::spawn_blocking(move || crate::jukebox::save_user(station))
        .await
        .map_err(|error| err(StatusCode::INTERNAL_SERVER_ERROR, error))?
        .map_err(|error| err(StatusCode::BAD_REQUEST, error))?;
    m.reload_jukebox_if_changed().await;
    reply(json!({ "ok": true }))
}

fn station_from_wire(
    body: wire::Station,
) -> Result<crate::jukebox::Station, super::super::ApiError> {
    let metadata = body.metadata.unwrap_or_default();
    let station = crate::jukebox::Station {
        id: body.id,
        default_rate: body.default_rate,
        metadata: crate::jukebox::Metadata {
            name: metadata.name,
            donate: metadata.donate,
            title_regex: metadata.title_regex,
        },
        streams: body
            .streams
            .into_iter()
            .map(|stream| crate::jukebox::Stream {
                rate: stream.rate,
                key: stream.key,
                url: stream.url,
            })
            .collect(),
    };
    Ok(station)
}

#[cfg(test)]
mod tests {
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
        let (_, bytes) = request(&app, "GET", routes::JUKEBOX, None).await;
        let public = wire::JukeboxCatalog::decode(bytes.as_slice()).unwrap();
        assert_eq!(public.stations[0].streams[0].url, "");

        let saved = std::fs::read(root.join("jukebox/radio.toml")).unwrap();
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
            assert!(events.try_recv().is_err());
        }
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
        std::fs::remove_dir_all(manager.cfg_path.parent().unwrap()).unwrap();
    }
}
