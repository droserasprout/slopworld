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

#[cfg(test)]
mod tests {
    use super::*;
    use crate::grant::Grant;

    #[tokio::test]
    async fn session_actions_require_write_access_and_keep_response_shapes() {
        let manager = crate::session::test_manager(crate::config::Config::default());
        let name = format!("missing-{}", uuid::Uuid::new_v4());
        macro_rules! check {
            ($action:ident, $status:expr) => {
                for level in [Level::Ro, Level::Rw] {
                    let cap = Cap::Scoped(Grant {
                        grantor: "caller".into(),
                        sessions: if level == Level::Ro { [name.clone()].into() } else { Default::default() },
                        level,
                    });
                    let (status, Json(body)) = $action(State(manager.clone()), Extension(cap), Path(name.clone())).await.unwrap_err();
                    assert_eq!(status, StatusCode::FORBIDDEN);
                    assert_eq!(body, json!({ "error": format!("not allowed: {name}") }));
                }
                for cap in [Cap::Root, Cap::Scoped(Grant {
                    grantor: "caller".into(), sessions: [name.clone()].into(), level: Level::Rw,
                })] {
                    let response = $action(State(manager.clone()), Extension(cap), Path(name.clone())).await;
                    match response {
                        Ok(Json(body)) => {
                            assert_eq!($status, StatusCode::OK);
                            assert_eq!(body, json!({ "ok": true }));
                        }
                        Err((status, Json(body))) => {
                            assert_eq!(status, $status);
                            assert!(body["error"].as_str().unwrap().contains(&name));
                        }
                    }
                }
            };
        }
        check!(start, StatusCode::BAD_REQUEST);
        check!(stop, StatusCode::OK);
        check!(restart, StatusCode::BAD_REQUEST);
        check!(reset_state, StatusCode::BAD_REQUEST);
    }
}
