mod handlers;
mod router;
mod types;
mod ws;

pub(crate) use router::router;

use std::sync::Arc;

use axum::http::{HeaderMap, StatusCode};
use axum::Json;
use serde_json::json;

use crate::session::Manager;

pub(super) type Mgr = Arc<Manager>;
pub(super) type ApiResult = Result<Json<serde_json::Value>, (StatusCode, Json<serde_json::Value>)>;

pub(super) fn err(
    code: StatusCode,
    e: impl std::fmt::Display,
) -> (StatusCode, Json<serde_json::Value>) {
    (code, Json(json!({ "error": e.to_string() })))
}

/// The token a request presents, if any. What `Manager::resolve_cap` weighs against the root
/// and the live grants. Shared with the `/ws` upgrade, which reads it itself because the header
/// rides only on the upgrade request.
pub(crate) fn presented_token(headers: &HeaderMap) -> Option<String> {
    headers
        .get("x-slop-token")
        .and_then(|v| v.to_str().ok())
        .map(str::to_string)
}

#[cfg(test)]
mod tests {
    use super::*;
    use axum::http::HeaderValue;

    #[test]
    fn errors_keep_the_status_and_put_the_message_in_json() {
        let (status, Json(body)) = err(StatusCode::BAD_REQUEST, "bad request body");
        assert_eq!(status, StatusCode::BAD_REQUEST);
        assert_eq!(body, json!({ "error": "bad request body" }));
    }

    #[test]
    fn presented_token_accepts_only_a_valid_header_value() {
        let mut headers = HeaderMap::new();
        assert_eq!(presented_token(&headers), None);

        headers.insert("x-slop-token", HeaderValue::from_static("secret"));
        assert_eq!(presented_token(&headers).as_deref(), Some("secret"));

        headers.insert(
            "x-slop-token",
            HeaderValue::from_bytes(&[0xff]).expect("opaque header value"),
        );
        assert_eq!(presented_token(&headers), None);
    }
}
