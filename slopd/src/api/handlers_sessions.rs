use crate::api::protobuf::{domain, reply, Proto};
use crate::shared::wire;
use axum::extract::{Path, State};
use axum::http::StatusCode;
use axum::Extension;
use serde_json::json;

use crate::grant::{Cap, Level};

use super::{err, ApiResult, Mgr};

pub(crate) async fn health(State(_m): State<Mgr>) -> ApiResult<wire::Health> {
    reply(json!({
        "ok": true,
        "protocol_version": 2,
        "version": env!("SLOPWORLD_VERSION"),
        "hostname": crate::runtime::hostname(),
        "tmux_socket": crate::config::tmux_socket(),
    }))
}

pub(crate) async fn list(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
) -> ApiResult<wire::SessionsReply> {
    let mut sessions = Vec::new();
    for session in m.views().await {
        if m.cap_ok(&cap, &session.name, Level::Ro).await {
            sessions.push(session);
        }
    }
    reply(json!({ "sessions": sessions }))
}

pub(crate) async fn one(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    Path(name): Path<String>,
) -> ApiResult<wire::SessionView> {
    super::guard(&m, &cap, &name, Level::Ro).await?;
    m.views()
        .await
        .into_iter()
        .find(|s| s.name == name)
        .map(|s| reply(json!(s)))
        .ok_or_else(|| err(StatusCode::NOT_FOUND, format!("no such session: {name}")))?
}

pub(crate) async fn cwd(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    Path(name): Path<String>,
) -> ApiResult<wire::PathResult> {
    super::guard(&m, &cap, &name, Level::Ro).await?;
    let path = m
        .tmux
        .current_path(&name)
        .await
        .ok_or_else(|| err(StatusCode::NOT_FOUND, format!("no such session: {name}")))?;
    reply(json!({ "path": path }))
}

pub(crate) async fn sandbox(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    Path(name): Path<String>,
) -> ApiResult<wire::SandboxReport> {
    super::guard(&m, &cap, &name, Level::Ro).await?;
    if !m.session_known(&name).await {
        return Err(err(
            StatusCode::NOT_FOUND,
            format!("no such session: {name}"),
        ));
    }
    m.sandbox_inspect(&name)
        .await
        .map(reply)
        .map_err(|error| err(StatusCode::INTERNAL_SERVER_ERROR, error))?
}

pub(crate) async fn create(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    Proto(value): Proto<wire::SessionConfig>,
) -> ApiResult<wire::Ack> {
    super::guard_create(&cap)?;
    let s = crate::api::parse_session(domain(value)?)?;
    super::ok_json(m.add(s).await)
}

pub(crate) async fn update(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    Path(name): Path<String>,
    Proto(value): Proto<wire::SessionConfig>,
) -> ApiResult<wire::Ack> {
    super::guard_root(&cap)?;
    let s = crate::api::parse_session(domain(value)?)?;
    super::ok_json(m.update(&name, s).await)
}

pub(crate) async fn destroy(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    Path(name): Path<String>,
) -> ApiResult<wire::Ack> {
    super::guard(&m, &cap, &name, Level::Rw).await?;
    super::ok_json(m.remove(&name).await)
}

macro_rules! session_action {
    ($($action:ident),+ $(,)?) => {
        $(pub(crate) async fn $action(
            State(m): State<Mgr>,
            Extension(cap): Extension<Cap>,
            Path(name): Path<String>,
        ) -> ApiResult {
            super::guard(&m, &cap, &name, Level::Rw).await?;
            super::ok_json(m.$action(&name).await)
        })+
    };
}

session_action!(start, stop, reset_state, restart);

pub(crate) async fn stored_states(State(m): State<Mgr>) -> ApiResult<wire::StoredStates> {
    m.stored_states()
        .await
        .map(|entries| reply(json!({ "entries": entries })))
        .map_err(|e| err(StatusCode::INTERNAL_SERVER_ERROR, e))?
}

pub(crate) async fn empty_trash(State(m): State<Mgr>) -> ApiResult<wire::Ack> {
    super::ok_json(m.empty_trash().await)
}

pub(crate) async fn delete_stored_state(
    State(m): State<Mgr>,
    Path((kind, key)): Path<(String, String)>,
) -> ApiResult<wire::Ack> {
    super::ok_json(m.delete_stored_state(&kind, &key).await)
}

pub(crate) async fn restore_stored_state(
    State(m): State<Mgr>,
    Path(key): Path<String>,
) -> ApiResult<wire::SessionResult> {
    m.restore_stored_state(&key)
        .await
        .map(|session| reply(json!({ "ok": true, "session": session })))
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?
}

pub(crate) async fn set_label(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    Path(name): Path<String>,
    Proto(q): Proto<wire::LabelReq>,
) -> ApiResult<wire::Ack> {
    let q: super::super::types::LabelReq = domain(q)?;
    super::guard(&m, &cap, &name, Level::Rw).await?;
    super::ok_json(m.set_label(&name, q.label).await)
}

#[cfg(test)]
#[path = "handlers_sessions_tests.rs"]
mod tests;
