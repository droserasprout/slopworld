//! Root-authenticated, non-Protobuf media responses. The station catalog owns upstream URLs.

use super::App;
use crate::audio::relay::OpenError;
use axum::{
    Extension, Json,
    body::Body,
    extract::{Query, State},
    http::{HeaderValue, StatusCode, header},
    response::{IntoResponse, Response},
};
use serde::{Deserialize, Serialize};
use uuid::Uuid;

#[derive(Deserialize)]
#[serde(deny_unknown_fields)]
pub(super) struct StreamQuery {
    station: String,
    stream: String,
    listener: String,
}

#[derive(Deserialize)]
#[serde(deny_unknown_fields)]
pub(super) struct TitleQuery {
    listener: String,
}

#[derive(Serialize)]
pub(super) struct Metadata {
    title: Option<String>,
}

pub(super) async fn radio(
    State(app): State<App>,
    Extension(super::RootGeneration(epoch)): Extension<super::RootGeneration>,
    Query(query): Query<StreamQuery>,
) -> Result<Response, StatusCode> {
    let listener =
        Uuid::parse_str(&query.listener).map_err(|_invalid_uuid| StatusCode::BAD_REQUEST)?;
    let url = crate::jukebox::resolve(&query.station, &query.stream)
        .map_err(|_missing_stream| StatusCode::NOT_FOUND)?;
    let authority = app.config_authority.clone();
    let mut changes = authority.auth_changes();
    let revoked = async move {
        while authority.root_generation() == epoch {
            if let Err(tokio::sync::broadcast::error::RecvError::Closed) = changes.recv().await {
                break;
            }
        }
    };
    let (content_type, chunks) = app
        .services
        .radio_listeners
        .open(listener, url, revoked)
        .await
        .map_err(|error| match error {
            OpenError::Revoked => StatusCode::UNAUTHORIZED,
            OpenError::Duplicate => StatusCode::CONFLICT,
            OpenError::Unavailable => StatusCode::SERVICE_UNAVAILABLE,
            OpenError::Station(error) => {
                tracing::debug!("radio relay open failed: {error}");
                StatusCode::BAD_GATEWAY
            }
        })?;
    // Only serve inert media at this origin, including stations that use a generic binary type.
    let media = content_type
        .split(';')
        .next()
        .unwrap_or_default()
        .trim()
        .to_ascii_lowercase();
    if !(media.starts_with("audio/")
        || matches!(
            media.as_str(),
            "application/ogg" | "application/octet-stream" | "video/mp4" | "video/webm"
        ))
    {
        return Err(StatusCode::UNSUPPORTED_MEDIA_TYPE);
    }
    let mime =
        HeaderValue::from_str(&content_type).map_err(|_invalid_header| StatusCode::BAD_GATEWAY)?;
    Ok((
        [
            (header::CONTENT_TYPE, mime),
            (header::CACHE_CONTROL, HeaderValue::from_static("no-store")),
            (
                header::X_CONTENT_TYPE_OPTIONS,
                HeaderValue::from_static("nosniff"),
            ),
        ],
        Body::from_stream(chunks),
    )
        .into_response())
}

pub(super) async fn radio_title(
    State(app): State<App>,
    Query(query): Query<TitleQuery>,
) -> Result<impl IntoResponse, StatusCode> {
    let listener =
        Uuid::parse_str(&query.listener).map_err(|_invalid_uuid| StatusCode::BAD_REQUEST)?;
    let title = app
        .services
        .radio_listeners
        .title(listener)
        .ok_or(StatusCode::NOT_FOUND)?;
    Ok((
        [(header::CACHE_CONTROL, "no-store")],
        Json(Metadata { title }),
    ))
}
