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
    let mut sessions = Vec::new();
    for session in m.views().await {
        if m.cap_ok(&cap, &session.name, Level::Ro).await {
            sessions.push(session);
        }
    }
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

pub(crate) async fn sandbox(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    Path(name): Path<String>,
) -> ApiResult {
    super::guard(&m, &cap, &name, Level::Ro).await?;
    if !m.session_known(&name).await {
        return Err(err(
            StatusCode::NOT_FOUND,
            format!("no such session: {name}"),
        ));
    }
    m.sandbox_inspect(&name)
        .await
        .map(Json)
        .map_err(|error| err(StatusCode::INTERNAL_SERVER_ERROR, error))
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
    super::guard_root(&cap)?;
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
    use crate::config::{Config, ProjectCfg};
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
                        revoked: Default::default(),
                    });
                    let (status, Json(body)) = $action(State(manager.clone()), Extension(cap), Path(name.clone())).await.unwrap_err();
                    assert_eq!(status, StatusCode::FORBIDDEN);
                    assert_eq!(body, json!({ "error": format!("not allowed: {name}") }));
                }
                for cap in [Cap::Root, Cap::Scoped(Grant {
                    grantor: "caller".into(), sessions: [name.clone()].into(), level: Level::Rw, revoked: Default::default(),
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

    #[tokio::test]
    async fn replacing_session_configuration_is_root_only_and_root_can_change_sandbox_inputs() {
        let directory =
            std::env::temp_dir().join(format!("slopd-config-boundary-{}", uuid::Uuid::new_v4()));
        std::fs::create_dir_all(&directory).unwrap();
        let manager = crate::session::test_manager(Config {
            projects: vec![
                ProjectCfg {
                    name: "repo".into(),
                    dir: directory.to_string_lossy().into_owned(),
                    ..Default::default()
                },
                ProjectCfg {
                    name: "other".into(),
                    dir: directory.to_string_lossy().into_owned(),
                    ..Default::default()
                },
            ],
            sessions: vec![crate::config::SessionCfg {
                name: "agent".into(),
                project: "repo".into(),
                ..Default::default()
            }],
            ..Default::default()
        });
        let replacement = crate::config::SessionCfg {
            name: "renamed".into(),
            project: "other".into(),
            sandbox: vec!["slopworld-debug".into()],
            mounts: vec![crate::config::Mount {
                project: "other".into(),
                mode: crate::config::MountMode::Ro,
            }],
            ..Default::default()
        };
        let original = manager.config().await;

        for level in [Level::Ro, Level::Rw] {
            let cap = Cap::Scoped(Grant {
                grantor: "caller".into(),
                sessions: ["agent".into()].into(),
                level,
                revoked: Default::default(),
            });
            let (status, Json(body)) = update(
                State(manager.clone()),
                Extension(cap),
                Path("agent".into()),
                Json(replacement.clone()),
            )
            .await
            .expect_err("scoped configuration update must be rejected");
            assert_eq!(status, StatusCode::FORBIDDEN);
            assert_eq!(
                body,
                json!({ "error": "only the daemon's own token may replace session configuration" })
            );
            assert_eq!(
                serde_json::to_value(manager.config().await.sessions).unwrap(),
                serde_json::to_value(&original.sessions).unwrap()
            );
        }

        let _ = update(
            State(manager.clone()),
            Extension(Cap::Root),
            Path("agent".into()),
            Json(replacement),
        )
        .await
        .expect("root configuration update should remain available");
        let saved = manager.config().await;
        assert_eq!(saved.sessions[0].name, "renamed");
        assert_eq!(saved.sessions[0].project, "other");
        assert_eq!(saved.sessions[0].sandbox, ["slopworld-debug"]);
        assert_eq!(saved.sessions[0].mounts.len(), 1);
        std::fs::remove_dir_all(directory).unwrap();
    }
}
