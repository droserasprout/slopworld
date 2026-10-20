//! Root-only CRUD for user jukebox station definitions.

use crate::api::protobuf::Proto;
use crate::shared::wire;

use axum::extract::{Path, State};
use axum::http::StatusCode;

use super::super::{ApiResult, App, err};

pub(crate) async fn jukebox_presets() -> ApiResult<wire::JukeboxCatalog> {
    // The regular catalog omits URLs before sending data to the mod.
    // This editor endpoint requires root and returns the complete user definition for the form.
    let stations = crate::jukebox::Catalog::user_presets()
        .into_iter()
        .map(|station| wire::Station {
            id: station.id,
            default_rate: station.default_rate,
            metadata: Some(wire::StationMetadata {
                name: station.metadata.name,
                donate: station.metadata.donate,
                title_regex: station.metadata.title_regex,
            }),
            streams: station
                .streams
                .into_iter()
                .map(|stream| wire::StationStream {
                    rate: stream.rate,
                    key: stream.key,
                    url: stream.url,
                })
                .collect(),
        })
        .collect::<Vec<_>>();
    Ok(Proto(wire::JukeboxCatalog { stations }))
}

pub(crate) async fn create_jukebox_preset(
    State(m): State<App>,
    Proto(body): Proto<wire::Station>,
) -> ApiResult<wire::Ack> {
    save(m, body, None).await
}

pub(crate) async fn update_jukebox_preset(
    State(m): State<App>,
    Path(id): Path<String>,
    Proto(body): Proto<wire::Station>,
) -> ApiResult<wire::Ack> {
    save(m, body, Some(id)).await
}

pub(crate) async fn delete_jukebox_preset(
    State(m): State<App>,
    Path(id): Path<String>,
) -> ApiResult<wire::Ack> {
    let deleted = tokio::task::spawn_blocking(move || crate::jukebox::delete_user(&id))
        .await
        .map_err(|error| err(StatusCode::INTERNAL_SERVER_ERROR, error))?;
    deleted.map_err(jukebox_error)?;
    reload_after_mutation(&m).await?;
    Ok(Proto(wire::Ack { ok: true }))
}

async fn save(m: App, body: wire::Station, original_id: Option<String>) -> ApiResult<wire::Ack> {
    let station = station_from_wire(body);
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
    reload_after_mutation(&m).await?;
    Ok(Proto(wire::Ack { ok: true }))
}

async fn reload_after_mutation(m: &App) -> Result<(), super::super::ApiError> {
    m.services.reload_jukebox_if_changed(&m.sessions).await.map_err(|error| {
        err(StatusCode::INTERNAL_SERVER_ERROR, format!(
            "The change was saved to disk, but the live jukebox catalog could not reload. Fix the station definitions to apply it: {error:#}"
        ))
    })?;
    Ok(())
}

fn station_from_wire(body: wire::Station) -> crate::jukebox::Station {
    let metadata = body.metadata.unwrap_or_default();
    crate::jukebox::Station {
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
    }
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
