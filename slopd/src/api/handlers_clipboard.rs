//! Host clipboard HTTP boundaries.

use axum::http::StatusCode;
use axum::Json;
use serde_json::json;

use super::super::types::ClipReq;
use super::{err, ApiResult};

/// A tool that is missing or wedged is a 502 rather than a 400, because nothing
/// about the request was wrong.
pub(crate) async fn clip_read() -> ApiResult {
    let text = crate::clipboard::read()
        .await
        .map_err(|e| err(StatusCode::BAD_GATEWAY, e))?;
    Ok(Json(json!({ "text": text })))
}

pub(crate) async fn clip_read_text() -> ApiResult {
    let text = crate::clipboard::read_text()
        .await
        .map_err(|e| err(StatusCode::BAD_GATEWAY, e))?;
    Ok(Json(json!({ "text": text })))
}

pub(crate) async fn clip_read_primary() -> ApiResult {
    let text = crate::clipboard::read_primary()
        .await
        .map_err(|e| err(StatusCode::BAD_GATEWAY, e))?;
    Ok(Json(json!({ "text": text })))
}

pub(crate) async fn clip_read_primary_text() -> ApiResult {
    let text = crate::clipboard::read_primary_text()
        .await
        .map_err(|e| err(StatusCode::BAD_GATEWAY, e))?;
    Ok(Json(json!({ "text": text })))
}

pub(crate) async fn clip_write(Json(q): Json<ClipReq>) -> ApiResult {
    crate::clipboard::write(&q.text)
        .await
        .map_err(|e| err(StatusCode::BAD_GATEWAY, e))?;
    Ok(Json(json!({ "ok": true })))
}

pub(crate) async fn clip_write_primary(Json(q): Json<ClipReq>) -> ApiResult {
    crate::clipboard::write_primary(&q.text)
        .await
        .map_err(|e| err(StatusCode::BAD_GATEWAY, e))?;
    Ok(Json(json!({ "ok": true })))
}
