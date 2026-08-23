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
