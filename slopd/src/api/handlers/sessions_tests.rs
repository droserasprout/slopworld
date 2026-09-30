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
                    let (status, Proto(body)) = $action(State(manager.clone()), Extension(cap), Path(name.clone())).await.unwrap_err();
        let body = serde_json::to_value(body).unwrap();
                    assert_eq!(status, StatusCode::FORBIDDEN);
                    assert_eq!(body, json!({ "error": format!("not allowed: {name}") }));
                }
                for cap in [Cap::Root, Cap::Scoped(Grant {
                    grantor: "caller".into(), sessions: [name.clone()].into(), level: Level::Rw, revoked: Default::default(),
                })] {
                    let response = $action(State(manager.clone()), Extension(cap), Path(name.clone())).await;
                    match response {
                        Ok(Proto(body)) => {
                            let body = serde_json::to_value(body).unwrap();
                            assert_eq!($status, StatusCode::OK);
                            assert_eq!(body, json!({ "ok": true }));
                        }
                        Err((status, Proto(body))) => {
                            let body = serde_json::to_value(body).unwrap();
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
        let (status, Proto(body)) = update(
            State(manager.clone()),
            Extension(cap),
            Path("agent".into()),
            Proto(
                serde_json::from_value(serde_json::to_value(replacement.clone()).unwrap()).unwrap(),
            ),
        )
        .await
        .expect_err("scoped configuration update must be rejected");
        let body = serde_json::to_value(body).unwrap();
        assert_eq!(status, StatusCode::FORBIDDEN);
        assert_eq!(
            body,
            json!({ "error": "Only the root token can replace session configuration." })
        );
        assert_eq!(
            serde_json::to_value(manager.config().await.sessions).unwrap(),
            serde_json::to_value(&original.sessions).unwrap()
        );
    }

    update(
        State(manager.clone()),
        Extension(Cap::Root),
        Path("agent".into()),
        Proto(serde_json::from_value(serde_json::to_value(replacement).unwrap()).unwrap()),
    )
    .await
    .expect("root configuration update should remain available");
    let saved = manager.config().await;
    assert_eq!(saved.sessions[0].name, "renamed");
    assert_eq!(saved.sessions[0].project, "other");
    assert_eq!(saved.sessions[0].sandbox, ["slopworld-debug"]);
    std::fs::remove_dir_all(directory).unwrap();
}
