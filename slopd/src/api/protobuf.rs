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
                "Set the Content-Type header to application/x-protobuf. Use protocol version 2.",
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
        let root = parts.first().map(String::as_str);
        let first_key = parts.get(1).map(String::as_str);
        if parts.len() < 2
            || !matches!(root, Some("daemon" | "defaults" | "commands"))
            || (root == Some("daemon") && matches!(first_key, Some("bind" | "token")))
        {
            return Err(super::err(
                StatusCode::BAD_REQUEST,
                "This config path is not valid for editing.",
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
                "Choose a config path that identifies one value.",
            ));
        }
        let Some((leaf, parents)) = parts.split_last() else {
            return Err(super::err(
                StatusCode::BAD_REQUEST,
                "This config path is not valid for editing.",
            ));
        };
        let mut target = &mut result;
        for part in parents {
            target = target
                .as_object_mut()
                .ok_or_else(|| {
                    super::err(StatusCode::BAD_REQUEST, "Config paths must not overlap.")
                })?
                .entry(part.clone())
                .or_insert_with(|| serde_json::json!({}));
        }
        target
            .as_object_mut()
            .ok_or_else(|| super::err(StatusCode::BAD_REQUEST, "Config paths must not overlap."))?
            .insert(leaf.clone(), source.clone());
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
#[path = "protobuf_tests.rs"]
mod tests;
