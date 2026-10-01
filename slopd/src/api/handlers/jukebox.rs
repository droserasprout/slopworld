//! Root-only CRUD for user jukebox station definitions.

use crate::api::protobuf::{Proto, reply};
use crate::shared::wire;

use axum::extract::{Path, State};
use axum::http::StatusCode;
use serde_json::json;

use super::super::{ApiResult, Mgr, err};

pub(crate) async fn jukebox_presets() -> ApiResult<wire::JukeboxCatalog> {
    // The regular catalog omits URLs before sending data to the mod.
    // This editor endpoint requires root and returns the complete user definition for the form.
    let stations = crate::jukebox::Catalog::user_presets()
        .into_iter()
        .map(|station| {
            json!({
                "id": station.id,
                "default_rate": station.default_rate,
                "metadata": station.metadata,
                "streams": station.streams.into_iter().map(|stream| json!({
                    "rate": stream.rate,
                    "key": stream.key,
                    "url": stream.url,
                })).collect::<Vec<_>>(),
            })
        })
        .collect::<Vec<_>>();
    reply(json!({ "stations": stations }))
}

pub(crate) async fn create_jukebox_preset(
    State(m): State<Mgr>,
    Proto(body): Proto<wire::Station>,
) -> ApiResult<wire::Ack> {
    save(m, body, None).await
}

pub(crate) async fn update_jukebox_preset(
    State(m): State<Mgr>,
    Path(id): Path<String>,
    Proto(body): Proto<wire::Station>,
) -> ApiResult<wire::Ack> {
    save(m, body, Some(id)).await
}

pub(crate) async fn delete_jukebox_preset(
    State(m): State<Mgr>,
    Path(id): Path<String>,
) -> ApiResult<wire::Ack> {
    let deleted = tokio::task::spawn_blocking(move || crate::jukebox::delete_user(&id))
        .await
        .map_err(|error| err(StatusCode::INTERNAL_SERVER_ERROR, error))?;
    deleted.map_err(jukebox_error)?;
    m.reload_jukebox_if_changed().await;
    Ok(Proto(wire::Ack { ok: true }))
}

async fn save(m: Mgr, body: wire::Station, original_id: Option<String>) -> ApiResult<wire::Ack> {
    let station = station_from_wire(body)?;
    if let Some(original_id) = original_id
        && station.id != original_id
    {
        return Err(err(
            StatusCode::BAD_REQUEST,
            "Keep the station ID unchanged when you edit it.",
        ));
    }
    tokio::task::spawn_blocking(move || crate::jukebox::save_user(station))
        .await
        .map_err(|error| err(StatusCode::INTERNAL_SERVER_ERROR, error))?
        .map_err(jukebox_error)?;
    m.reload_jukebox_if_changed().await;
    Ok(Proto(wire::Ack { ok: true }))
}

fn station_from_wire(
    body: wire::Station,
) -> Result<crate::jukebox::Station, super::super::ApiError> {
    let metadata = body.metadata.unwrap_or_default();
    let station = crate::jukebox::Station {
        id: body.id,
        default_rate: body.default_rate,
        metadata: crate::jukebox::Metadata {
            name: metadata.name,
            donate: metadata.donate,
            title_regex: metadata.title_regex,
        },
        streams: body
            .streams
            .into_iter()
            .map(|stream| crate::jukebox::Stream {
                rate: stream.rate,
                key: stream.key,
                url: stream.url,
            })
            .collect(),
    };
    Ok(station)
}

fn jukebox_error(error: crate::jukebox::JukeboxError) -> super::super::protobuf::ApiError {
    let status = match &error {
        crate::jukebox::JukeboxError::Missing(_) => StatusCode::NOT_FOUND,
        crate::jukebox::JukeboxError::Invalid(_) => StatusCode::BAD_REQUEST,
        crate::jukebox::JukeboxError::Storage(_) => StatusCode::INTERNAL_SERVER_ERROR,
    };
    err(status, error)
}

#[cfg(test)]
#[path = "jukebox_tests.rs"]
mod tests;
