//! Runtime catalog and usage HTTP boundaries.
use crate::api::protobuf::reply;
use crate::shared::wire;

use axum::extract::State;

use serde_json::json;

use super::super::{ApiResult, Mgr};

pub(crate) async fn usage(State(m): State<Mgr>) -> ApiResult<wire::UsageSnapshot> {
    reply(json!(m.usage().await))
}

/// What the jukebox is doing, for anything that would rather ask than listen - which in
/// practice means a person with `curl` and a suspicion. The mod hears the same thing as an
/// event; nothing needs this route to work.
pub(crate) async fn audio(State(m): State<Mgr>) -> ApiResult<wire::AudioState> {
    reply(json!(m.music_state().await))
}

/// The station catalog for clients that draw the jukebox. URLs stay daemon-side; this carries
/// only ids, stream keys, rates and the metadata a UI may render.
pub(crate) async fn jukebox() -> ApiResult<wire::JukeboxCatalog> {
    reply(json!(crate::jukebox::catalog()))
}

/// Open the daemon-owned Spotify terminal; playback uses the ordinary audio selection.
pub(crate) async fn ncspot(
    State(m): State<Mgr>,
    super::super::protobuf::Proto(q): super::super::protobuf::Proto<wire::RedrawReq>,
) -> ApiResult<wire::SessionResult> {
    let _transition = m.music_transition.lock().await;
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
