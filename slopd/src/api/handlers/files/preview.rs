//! File preview HTTP decoding and projection.
use super::super::{ApiResult, App, err};
use crate::api::{
    protobuf::{Proto, path_text},
    types::ReadReq,
};
use crate::files::preview::{self as service, reader_stat};
use crate::shared::wire;
use axum::{
    extract::{Query, State},
    http::StatusCode,
};

pub(crate) async fn file_stat(
    State(_m): State<App>,
    Query(q): Query<ReadReq>,
) -> ApiResult<wire::FileStatResult> {
    let path = &q.path;
    if path.trim().is_empty() {
        return Err(err(StatusCode::BAD_REQUEST, "A path is required."));
    }
    let path = std::path::PathBuf::from(path);
    let stat = reader_stat(&path)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Proto(wire::FileStatResult {
        is_file: stat.is_file,
        stamp: stat.stamp,
    }))
}

pub(crate) async fn read_file(
    State(_m): State<App>,
    Query(q): Query<ReadReq>,
) -> ApiResult<wire::TextResult> {
    let path = &q.path;
    if path.trim().is_empty() {
        return Err(err(StatusCode::BAD_REQUEST, "A path is required."));
    }

    let path = std::path::PathBuf::from(path);
    let text = service::read_text(&path, q.root.as_deref())
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Proto(wire::TextResult {
        path: path_text(&path)?,
        bytes: text.len() as u64,
        text,
    }))
}

pub(crate) async fn read_image(
    State(_m): State<App>,
    Query(q): Query<ReadReq>,
) -> ApiResult<wire::ImageResult> {
    let path = &q.path;
    if path.trim().is_empty() {
        return Err(err(StatusCode::BAD_REQUEST, "A path is required."));
    }

    let path = std::path::PathBuf::from(path);
    let bytes = service::read_image(&path, q.root.as_deref())
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Proto(wire::ImageResult {
        path: path_text(&path)?,
        bytes: bytes.len() as u64,
        data: bytes,
    }))
}

#[cfg(test)]
#[path = "preview_read_tests.rs"]
mod read_tests;
