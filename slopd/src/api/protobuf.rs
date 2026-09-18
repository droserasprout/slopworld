//! Binary HTTP boundary. Serde projections reuse domain validation without JSON text on the wire.
use crate::shared::wire;
use axum::{
    async_trait,
    body::Bytes,
    extract::{FromRequest, Request},
    http::{header, StatusCode},
    response::{IntoResponse, Response},
};
use prost::Message;
use serde::{de::DeserializeOwned, Serialize};

pub(crate) const CONTENT_TYPE: &str = "application/x-protobuf";
#[derive(Debug)]
pub(crate) struct Proto<T>(pub T);
pub(crate) type ApiError = (StatusCode, Proto<wire::Error>);
impl<T: Message> IntoResponse for Proto<T> {
    fn into_response(self) -> Response {
        (
            [(header::CONTENT_TYPE, CONTENT_TYPE)],
            self.0.encode_to_vec(),
        )
            .into_response()
    }
}
#[async_trait]
impl<S: Send + Sync, T: Message + Default> FromRequest<S> for Proto<T> {
    type Rejection = ApiError;
    async fn from_request(req: Request, state: &S) -> Result<Self, Self::Rejection> {
        if req
            .headers()
            .get(header::CONTENT_TYPE)
            .and_then(|v| v.to_str().ok())
            != Some(CONTENT_TYPE)
        {
            return Err(super::err(
                StatusCode::UNSUPPORTED_MEDIA_TYPE,
                "expected application/x-protobuf (protocol 2)",
            ));
        }
        let bytes = Bytes::from_request(req, state)
            .await
            .map_err(|e| super::err(e.status(), e))?;
        T::decode(bytes)
            .map(Self)
            .map_err(|e| super::err(StatusCode::BAD_REQUEST, e))
    }
}
pub(crate) fn reply<T: Message + DeserializeOwned>(value: impl Serialize) -> super::ApiResult<T> {
    serde_json::to_value(value)
        .and_then(serde_json::from_value)
        .map(Proto)
        .map_err(|e| super::err(StatusCode::INTERNAL_SERVER_ERROR, e))
}
pub(crate) fn domain<T: DeserializeOwned>(value: impl Serialize) -> Result<T, ApiError> {
    serde_json::to_value(value)
        .and_then(serde_json::from_value)
        .map_err(|e| super::err(StatusCode::BAD_REQUEST, e))
}

pub(crate) fn config_patch(patch: wire::ConfigPatch) -> Result<serde_json::Value, ApiError> {
    let values = serde_json::to_value(patch.values.unwrap_or_default())
        .map_err(|e| super::err(StatusCode::BAD_REQUEST, e))?;
    let mut result = serde_json::json!({});
    for path in patch.paths {
        let parts: Vec<_> = path
            .split('.')
            .map(|p| p.replace("~1", ".").replace("~0", "~"))
            .collect();
        if parts.len() < 2
            || !matches!(parts[0].as_str(), "daemon" | "defaults" | "commands")
            || (parts[0] == "daemon" && matches!(parts[1].as_str(), "bind" | "token"))
        {
            return Err(super::err(
                StatusCode::BAD_REQUEST,
                "invalid editable config path",
            ));
        }
        let mut source = &values;
        for part in &parts {
            source = source.get(part).ok_or_else(|| {
                super::err(
                    StatusCode::BAD_REQUEST,
                    format!("unknown config path: {path}"),
                )
            })?;
        }
        if source.is_object() {
            return Err(super::err(
                StatusCode::BAD_REQUEST,
                "config path must name a leaf",
            ));
        }
        let mut target = &mut result;
        for part in &parts[..parts.len() - 1] {
            target = target
                .as_object_mut()
                .ok_or_else(|| super::err(StatusCode::BAD_REQUEST, "overlapping config paths"))?
                .entry(part.clone())
                .or_insert_with(|| serde_json::json!({}));
        }
        target
            .as_object_mut()
            .ok_or_else(|| super::err(StatusCode::BAD_REQUEST, "overlapping config paths"))?
            .insert(parts.last().unwrap().clone(), source.clone());
    }
    Ok(result)
}

/// Extractor and router rejections use the same error envelope as handler failures.
pub(crate) async fn normalize_errors(req: Request, next: axum::middleware::Next) -> Response {
    let response = next.run(req).await;
    let status = response.status();
    if (status.is_client_error() || status.is_server_error())
        && response
            .headers()
            .get(header::CONTENT_TYPE)
            .and_then(|v| v.to_str().ok())
            != Some(CONTENT_TYPE)
    {
        let bytes = axum::body::to_bytes(response.into_body(), 64 * 1024)
            .await
            .unwrap_or_default();
        let message = if bytes.is_empty() {
            status.to_string()
        } else {
            String::from_utf8_lossy(&bytes).into_owned()
        };
        return super::err(status, message).into_response();
    }
    response
}

#[cfg(test)]
mod tests {
    use super::*;
    use axum::{body::Body, Extension};
    use serde_json::json;
    use tower::ServiceExt;

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
        for path in [
            "daemon.token",
            "daemon.bind",
            "daemon.typo",
            "daemon.usage_items",
            "session",
            "daemon",
        ] {
            assert!(config_patch(wire::ConfigPatch {
                values: Some(wire::EditableConfig {
                    daemon: Some(wire::Daemon::default()),
                    ..Default::default()
                }),
                paths: vec![path.into()]
            })
            .is_err());
        }
    }
}
