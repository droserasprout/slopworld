//! File mutation HTTP decoding and error projection.
use super::super::{ApiResult, err};
use crate::api::{
    protobuf::{Proto, domain},
    types::FileReq,
};
use crate::{files::mutations as service, shared::wire};
use axum::http::StatusCode;
pub(crate) async fn create_file(Proto(q): Proto<wire::FileReq>) -> ApiResult<wire::Ack> {
    let q: FileReq = domain(q)?;
    service::create_file(&q.path, &q.name, &q.kind)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Proto(wire::Ack { ok: true }))
}
pub(crate) async fn rename_file(Proto(q): Proto<wire::FileReq>) -> ApiResult<wire::Ack> {
    let q: FileReq = domain(q)?;
    service::rename_file(&q.path, &q.name)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Proto(wire::Ack { ok: true }))
}
pub(crate) async fn remove_file(Proto(q): Proto<wire::FileReq>) -> ApiResult<wire::Ack> {
    let q: FileReq = domain(q)?;
    service::remove_file(&q.path)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Proto(wire::Ack { ok: true }))
}
#[cfg(test)]
use service::file_path;
#[cfg(test)]
#[path = "mutations_tests.rs"]
mod tests;
