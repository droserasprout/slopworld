use axum::extract::{Path, State};
use axum::http::StatusCode;
use axum::{Extension, Json};
use serde_json::json;

use crate::config::SessionCfg;
use crate::grant::{Cap, Level};

use super::{err, ApiResult, Mgr};

pub(crate) async fn health(State(_m): State<Mgr>) -> ApiResult {
    Ok(Json(json!({
        "ok": true,
        "version": env!("SLOPWORLD_VERSION"),
        "hostname": crate::runtime::hostname(),
        "tmux_socket": crate::config::tmux_socket(),
    })))
}

pub(crate) async fn list(State(m): State<Mgr>, Extension(cap): Extension<Cap>) -> ApiResult {
    // Filtered to what the caller may see: a grant lists the sessions it names and no others, so
    // a name it cannot touch is a name it never learns. Host-ness is not carried on a view and
    // is not needed here - a grant never names a host session, so `can_see` refuses it by
    // membership alone.
    let sessions: Vec<_> = m
        .views()
        .await
        .into_iter()
        .filter(|s| cap.can_see(&s.name, s.host))
        .collect();
    Ok(Json(json!({ "sessions": sessions })))
}

pub(crate) async fn one(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    Path(name): Path<String>,
) -> ApiResult {
    super::guard(&m, &cap, &name, Level::Ro).await?;
    m.views()
        .await
        .into_iter()
        .find(|s| s.name == name)
        .map(|s| Json(json!(s)))
        .ok_or_else(|| err(StatusCode::NOT_FOUND, format!("no such session: {name}")))
}

pub(crate) async fn cwd(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    Path(name): Path<String>,
) -> ApiResult {
    super::guard(&m, &cap, &name, Level::Ro).await?;
    let path = m
        .tmux
        .current_path(&name)
        .await
        .ok_or_else(|| err(StatusCode::NOT_FOUND, format!("no such session: {name}")))?;
    Ok(Json(json!({ "path": path })))
}

pub(crate) async fn create(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    Json(s): Json<SessionCfg>,
) -> ApiResult {
    super::guard_create(&cap)?;
    super::ok_json(m.add(s).await)
}

pub(crate) async fn update(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    Path(name): Path<String>,
    Json(s): Json<SessionCfg>,
) -> ApiResult {
    super::guard(&m, &cap, &name, Level::Rw).await?;
    super::ok_json(m.update(&name, s).await)
}

pub(crate) async fn destroy(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    Path(name): Path<String>,
) -> ApiResult {
    super::guard(&m, &cap, &name, Level::Rw).await?;
    super::ok_json(m.remove(&name).await)
}

pub(crate) async fn start(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    Path(name): Path<String>,
) -> ApiResult {
    super::guard(&m, &cap, &name, Level::Rw).await?;
    super::ok_json(m.start(&name).await)
}

pub(crate) async fn stop(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    Path(name): Path<String>,
) -> ApiResult {
    super::guard(&m, &cap, &name, Level::Rw).await?;
    super::ok_json(m.stop(&name).await)
}

pub(crate) async fn reset_state(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    Path(name): Path<String>,
) -> ApiResult {
    super::guard(&m, &cap, &name, Level::Rw).await?;
    super::ok_json(m.reset_state(&name).await)
}

pub(crate) async fn restart(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    Path(name): Path<String>,
) -> ApiResult {
    super::guard(&m, &cap, &name, Level::Rw).await?;
    super::ok_json(m.restart(&name).await)
}

pub(crate) async fn stored_states(State(m): State<Mgr>) -> ApiResult {
    m.stored_states()
        .await
        .map(|entries| Json(json!({ "entries": entries })))
        .map_err(|e| err(StatusCode::INTERNAL_SERVER_ERROR, e))
}

pub(crate) async fn empty_trash(State(m): State<Mgr>) -> ApiResult {
    super::ok_json(m.empty_trash().await)
}

pub(crate) async fn delete_stored_state(
    State(m): State<Mgr>,
    Path((kind, key)): Path<(String, String)>,
) -> ApiResult {
    super::ok_json(m.delete_stored_state(&kind, &key).await)
}

pub(crate) async fn restore_stored_state(
    State(m): State<Mgr>,
    Path(key): Path<String>,
) -> ApiResult {
    m.restore_stored_state(&key)
        .await
        .map(|session| Json(json!({ "ok": true, "session": session })))
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))
}

pub(crate) async fn set_label(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    Path(name): Path<String>,
    Json(q): Json<super::super::types::LabelReq>,
) -> ApiResult {
    super::guard(&m, &cap, &name, Level::Rw).await?;
    super::ok_json(m.set_label(&name, q.label).await)
}
