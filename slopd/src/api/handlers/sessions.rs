use crate::api::protobuf::{Proto, domain, reply};
use crate::application::SessionEdits;
use crate::shared::wire;
use axum::Extension;
use axum::extract::{Path, Query, State};
use axum::http::StatusCode;
use serde_json::json;

use crate::grant::{Cap, Level};

use super::{ApiResult, App, err};

pub(crate) async fn health(State(_m): State<App>) -> ApiResult<wire::Health> {
    reply(json!({
        "ok": true,
        "protocol_version": 2,
        "version": env!("SLOPWORLD_VERSION"),
        "hostname": crate::runtime::hostname(),
        "tmux_socket": crate::tmux::tmux_socket(),
    }))
}

pub(crate) async fn list(
    State(m): State<App>,
    Extension(cap): Extension<Cap>,
) -> ApiResult<wire::SessionsReply> {
    let mut sessions = Vec::new();
    for session in m.sessions.views().await {
        if m.sessions.cap_ok(&cap, &session.name, Level::Ro).await {
            sessions.push(session);
        }
    }
    reply(json!({ "sessions": sessions }))
}

pub(crate) async fn one(
    State(m): State<App>,
    Extension(cap): Extension<Cap>,
    Path(name): Path<String>,
) -> ApiResult<wire::SessionView> {
    super::guard(&m, &cap, &name, Level::Ro).await?;
    m.sessions
        .views()
        .await
        .into_iter()
        .find(|s| s.name == name)
        .map(|s| reply(json!(s)))
        .ok_or_else(|| err(StatusCode::NOT_FOUND, format!("no such session: {name}")))?
}

pub(crate) async fn cwd(
    State(m): State<App>,
    Extension(cap): Extension<Cap>,
    Path(name): Path<String>,
) -> ApiResult<wire::PathResult> {
    super::guard(&m, &cap, &name, Level::Ro).await?;
    let path = m
        .sessions
        .tmux
        .current_path(&name)
        .await
        .ok_or_else(|| err(StatusCode::NOT_FOUND, format!("no such session: {name}")))?;
    reply(json!({ "path": path }))
}

pub(crate) async fn sandbox(
    State(m): State<App>,
    Extension(cap): Extension<Cap>,
    Path(name): Path<String>,
) -> ApiResult<wire::SandboxReport> {
    super::guard(&m, &cap, &name, Level::Ro).await?;
    if !m.sessions.session_known(&name).await {
        return Err(err(
            StatusCode::NOT_FOUND,
            format!("no such session: {name}"),
        ));
    }
    m.sessions
        .sandbox_inspect(&name)
        .await
        .map(reply)
        .map_err(|error| err(StatusCode::INTERNAL_SERVER_ERROR, error))?
}

pub(crate) async fn create(
    State(m): State<App>,
    Extension(cap): Extension<Cap>,
    Proto(value): Proto<wire::SessionConfig>,
) -> ApiResult<wire::Ack> {
    super::guard_create(&cap)?;
    let s = crate::api::parse_session(domain(value)?)?;
    super::ack_result(SessionEdits::new(&m).add(s).await)
}

pub(crate) async fn update(
    State(m): State<App>,
    Extension(cap): Extension<Cap>,
    Path(name): Path<String>,
    Proto(value): Proto<wire::SessionConfig>,
) -> ApiResult<wire::Ack> {
    super::guard_root(&cap)?;
    let s = crate::api::parse_session(domain(value)?)?;
    super::ack_result(SessionEdits::new(&m).update(&name, s).await)
}

pub(crate) async fn destroy(
    State(m): State<App>,
    Extension(cap): Extension<Cap>,
    Path(name): Path<String>,
) -> ApiResult<wire::Ack> {
    super::guard(&m, &cap, &name, Level::Rw).await?;
    super::ack_result(SessionEdits::new(&m).remove(&name).await)
}

macro_rules! session_action {
    ($($action:ident),+ $(,)?) => {
        $(pub(crate) async fn $action(
            State(m): State<App>,
            Extension(cap): Extension<Cap>,
            Path(name): Path<String>,
        ) -> ApiResult {
            super::guard(&m, &cap, &name, Level::Rw).await?;
            super::ack_result(m.sessions.$action(&name).await)
        })+
    };
}

session_action!(start, stop, restart);

pub(crate) async fn reset_state(
    State(m): State<App>,
    Extension(cap): Extension<Cap>,
    Path(name): Path<String>,
) -> ApiResult {
    super::guard(&m, &cap, &name, Level::Rw).await?;
    super::ack_result(SessionEdits::new(&m).reset_state(&name).await)
}

#[derive(serde::Deserialize)]
pub(crate) struct StorageQuery {
    #[serde(default = "storage_sizes_default")]
    sizes: bool,
}

fn storage_sizes_default() -> bool {
    true
}

pub(crate) async fn stored_states(
    State(m): State<App>,
    Query(query): Query<StorageQuery>,
) -> ApiResult<wire::StoredStates> {
    crate::application::stored_state::stored_states(
        m.config_authority.snapshot().await,
        query.sizes,
    )
    .await
    .map(|entries| reply(json!({ "entries": entries })))
    .map_err(|e| err(StatusCode::INTERNAL_SERVER_ERROR, e))?
}

pub(crate) async fn empty_trash(State(m): State<App>) -> ApiResult<wire::Ack> {
    super::ack_result(crate::application::stored_state::empty_trash(&m).await)
}

pub(crate) async fn delete_stored_state(
    State(m): State<App>,
    Path((kind, key)): Path<(String, String)>,
) -> ApiResult<wire::Ack> {
    super::ack_result(crate::application::stored_state::delete_stored_state(&m, &kind, &key).await)
}

pub(crate) async fn restore_stored_state(
    State(m): State<App>,
    Path(key): Path<String>,
) -> ApiResult<wire::SessionResult> {
    SessionEdits::new(&m)
        .restore_stored_state(&key)
        .await
        .map(|session| reply(json!({ "ok": true, "session": session })))
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?
}

pub(crate) async fn set_label(
    State(m): State<App>,
    Extension(cap): Extension<Cap>,
    Path(name): Path<String>,
    Proto(q): Proto<wire::LabelReq>,
) -> ApiResult<wire::Ack> {
    let q: super::super::types::LabelReq = domain(q)?;
    super::guard(&m, &cap, &name, Level::Rw).await?;
    super::ack_result(SessionEdits::new(&m).set_label(&name, q.label).await)
}

pub(crate) async fn set_reader_pinned(
    State(m): State<App>,
    Extension(cap): Extension<Cap>,
    Path(name): Path<String>,
    Proto(q): Proto<wire::ReaderPinnedReq>,
) -> ApiResult<wire::Ack> {
    super::guard(&m, &cap, &name, Level::Rw).await?;
    super::ack_result(m.sessions.set_reader_pinned(&name, q.pinned).await)
}

#[cfg(test)]
#[path = "sessions_tests.rs"]
mod tests;
