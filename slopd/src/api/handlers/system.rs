//! Runtime catalog and usage HTTP boundaries.
use crate::api::protobuf::reply;
use crate::shared::wire;

use axum::extract::State;

use serde_json::json;

use super::super::{ApiResult, Mgr};

pub(crate) async fn usage(State(m): State<Mgr>) -> ApiResult<wire::UsageSnapshot> {
    reply(json!(m.usage().await))
}

pub(crate) async fn audio(State(m): State<Mgr>) -> ApiResult<wire::AudioState> {
    reply(json!(m.music_state().await))
}

/// Supply the station catalog for jukebox clients. Keep URLs in the daemon.
/// Return only IDs, stream keys, rates, and display metadata.
pub(crate) async fn jukebox() -> ApiResult<wire::JukeboxCatalog> {
    reply(json!(crate::jukebox::catalog()))
}

/// Open the Spotify terminal owned by the daemon. Playback uses the ordinary audio selection.
pub(crate) async fn ncspot(
    State(m): State<Mgr>,
    super::super::protobuf::Proto(q): super::super::protobuf::Proto<wire::RedrawReq>,
) -> ApiResult<wire::SessionResult> {
    let _transition = m.music.transition.lock().await;
    let session = m
        .open_ncspot(
            q.cols.map(|n| n.clamp(1, u16::MAX as u32) as u16),
            q.rows.map(|n| n.clamp(1, u16::MAX as u32) as u16),
            None,
        )
        .await
        .map_err(|e| super::super::err(axum::http::StatusCode::BAD_REQUEST, e))?;
    reply(json!({"ok": true, "session": session}))
}
