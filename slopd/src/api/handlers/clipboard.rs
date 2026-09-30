//! Host clipboard HTTP boundaries.
use crate::api::protobuf::{Proto, domain, reply};
use crate::shared::wire;

use axum::http::StatusCode;

use serde_json::json;

use super::super::types::ClipReq;
use super::{ApiResult, err};

/// Return 502 for a missing or unresponsive tool. The request itself is valid.
pub(crate) async fn clip_read() -> ApiResult<wire::TextResult> {
    let text = crate::clipboard::read()
        .await
        .map_err(|e| err(StatusCode::BAD_GATEWAY, e))?;
    reply(json!({ "text": text }))
}

pub(crate) async fn clip_read_text() -> ApiResult<wire::TextResult> {
    let text = crate::clipboard::read_text()
        .await
        .map_err(|e| err(StatusCode::BAD_GATEWAY, e))?;
    reply(json!({ "text": text }))
}

pub(crate) async fn clip_read_primary() -> ApiResult<wire::TextResult> {
    let text = crate::clipboard::read_primary()
        .await
        .map_err(|e| err(StatusCode::BAD_GATEWAY, e))?;
    reply(json!({ "text": text }))
}

pub(crate) async fn clip_read_primary_text() -> ApiResult<wire::TextResult> {
    let text = crate::clipboard::read_primary_text()
        .await
        .map_err(|e| err(StatusCode::BAD_GATEWAY, e))?;
    reply(json!({ "text": text }))
}

pub(crate) async fn clip_write(Proto(q): Proto<wire::ClipReq>) -> ApiResult<wire::Ack> {
    let q: ClipReq = domain(q)?;
    crate::clipboard::write(&q.text)
        .await
        .map_err(|e| err(StatusCode::BAD_GATEWAY, e))?;
    reply(json!({ "ok": true }))
}

pub(crate) async fn clip_write_primary(Proto(q): Proto<wire::ClipReq>) -> ApiResult<wire::Ack> {
    let q: ClipReq = domain(q)?;
    crate::clipboard::write_primary(&q.text)
        .await
        .map_err(|e| err(StatusCode::BAD_GATEWAY, e))?;
    reply(json!({ "ok": true }))
}
