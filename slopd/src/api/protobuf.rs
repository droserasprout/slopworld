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
        let (mut parts, body) = response.into_parts();
        let bytes = axum::body::to_bytes(body, 64 * 1024)
            .await
            .unwrap_or_default();
        let message = if bytes.is_empty() {
            status.to_string()
        } else {
            String::from_utf8_lossy(&bytes).into_owned()
        };
        let normalized = super::err(status, message).into_response();
        parts.headers.remove(header::CONTENT_TYPE);
        parts.headers.remove(header::CONTENT_LENGTH);
        parts.headers.remove(header::TRANSFER_ENCODING);
        parts.headers.remove(header::CONTENT_ENCODING);
        parts.headers.remove(header::CONTENT_RANGE);
        parts.headers.remove(header::ETAG);
        parts.headers.extend(normalized.headers().clone());
        return Response::from_parts(parts, normalized.into_body());
    }
    response
}

#[cfg(test)]
#[path = "protobuf_tests.rs"]
mod tests;
