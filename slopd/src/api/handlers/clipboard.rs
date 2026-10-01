//! Host clipboard HTTP boundaries.
use crate::api::protobuf::Proto;
use crate::shared::wire;

use axum::http::StatusCode;

use super::{ApiResult, err};

/// Return 502 for a missing or unresponsive tool. The request itself is valid.
fn text_result(result: anyhow::Result<String>) -> ApiResult<wire::TextResult> {
    result
        .map(|text| {
            Proto(wire::TextResult {
                text,
                ..Default::default()
            })
        })
        .map_err(|error| err(StatusCode::BAD_GATEWAY, error))
}

pub(crate) async fn clip_read() -> ApiResult<wire::TextResult> {
    text_result(crate::clipboard::read().await)
}

pub(crate) async fn clip_read_text() -> ApiResult<wire::TextResult> {
    text_result(crate::clipboard::read_text().await)
}

pub(crate) async fn clip_read_primary() -> ApiResult<wire::TextResult> {
    text_result(crate::clipboard::read_primary().await)
}

pub(crate) async fn clip_read_primary_text() -> ApiResult<wire::TextResult> {
    text_result(crate::clipboard::read_primary_text().await)
}

pub(crate) async fn clip_write(Proto(q): Proto<wire::ClipReq>) -> ApiResult<wire::Ack> {
    crate::clipboard::write(q.text.as_deref().unwrap_or_default())
        .await
        .map_err(|e| err(StatusCode::BAD_GATEWAY, e))?;
    Ok(Proto(wire::Ack { ok: true }))
}

pub(crate) async fn clip_write_primary(Proto(q): Proto<wire::ClipReq>) -> ApiResult<wire::Ack> {
    crate::clipboard::write_primary(q.text.as_deref().unwrap_or_default())
        .await
        .map_err(|e| err(StatusCode::BAD_GATEWAY, e))?;
    Ok(Proto(wire::Ack { ok: true }))
}
