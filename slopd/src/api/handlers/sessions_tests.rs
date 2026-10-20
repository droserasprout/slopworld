use super::*;
use crate::config::{Config, ProjectCfg};
use crate::grant::Grant;

fn scoped(names: &[&str], level: Level) -> Cap {
    Cap::Scoped(Grant {
        grantor: "caller".into(),
        sessions: names.iter().map(|name| (*name).to_owned()).collect(),
        level,
        revoked: Default::default(),
    })
}

async fn configured_manager() -> App {
    let manager = crate::application::test_application(Config {
        sessions: ["agent", "private"]
            .into_iter()
            .map(|name| crate::config::SessionCfg {
                name: name.into(),
                autostart: false,
                ..Default::default()
            })
            .collect(),
        ..Default::default()
    });
    manager.sessions.sync_from_config().await;
    manager
}

#[tokio::test]
async fn session_reads_filter_scope_and_stop_exposing_revoked_targets() {
    let manager = configured_manager().await;
    let root = list(State(manager.clone()), Extension(Cap::Root))
        .await
        .unwrap()
        .0;
    assert_eq!(
        root.sessions
            .iter()
            .map(|s| s.name.as_str())
            .collect::<Vec<_>>(),
        ["agent", "private"]
    );
    for level in [Level::Ro, Level::Rw] {
        let cap = scoped(&["agent"], level);
        let visible = list(State(manager.clone()), Extension(cap.clone()))
            .await
            .unwrap()
            .0;
        assert_eq!(visible.sessions.len(), 1);
        assert_eq!(visible.sessions[0].name, "agent");
        let selected = one(
            State(manager.clone()),
            Extension(cap.clone()),
            Path("agent".into()),
        )
        .await
        .unwrap()
        .0;
        assert_eq!(selected, visible.sessions[0]);
        for name in ["private", "missing"] {
            let (status, Proto(error)) = one(
                State(manager.clone()),
                Extension(cap.clone()),
                Path(name.into()),
            )
            .await
            .unwrap_err();
            assert_eq!(status, StatusCode::FORBIDDEN);
            assert_eq!(error.error, format!("not allowed: {name}"));
        }
        let Cap::Scoped(grant) = &cap else {
            panic!("test capability must be scoped")
        };
        grant
            .revoked
            .store(true, std::sync::atomic::Ordering::Release);
        assert!(
            list(State(manager.clone()), Extension(cap.clone()))
                .await
                .unwrap()
                .0
                .sessions
                .is_empty()
        );
        assert_eq!(
            one(State(manager.clone()), Extension(cap), Path("agent".into()))
                .await
                .unwrap_err()
                .0,
            StatusCode::FORBIDDEN
        );
    }
}

#[tokio::test]
async fn session_diagnostics_hide_unscoped_names_and_report_authorized_missing_targets() {
    let manager = configured_manager().await;
    macro_rules! check_read {
        ($handler:ident) => {
            for name in ["private", "missing"] {
                let (status, Proto(error)) = $handler(
                    State(manager.clone()),
                    Extension(scoped(&["agent"], Level::Ro)),
                    Path(name.into()),
                )
                .await
                .unwrap_err();
                assert_eq!(status, StatusCode::FORBIDDEN);
                assert_eq!(error.error, format!("not allowed: {name}"));
            }
            for cap in [Cap::Root, scoped(&["missing"], Level::Ro)] {
                let (status, Proto(error)) = $handler(
                    State(manager.clone()),
                    Extension(cap),
                    Path("missing".into()),
                )
                .await
                .unwrap_err();
                assert_eq!(status, StatusCode::NOT_FOUND);
                assert_eq!(error.error, "no such session: missing");
            }
        };
    }
    check_read!(one);
    check_read!(cwd);
    check_read!(sandbox);
}

#[tokio::test]
async fn label_updates_require_write_access_and_preserve_state_on_invalid_input() {
    let manager = configured_manager().await;
    for cap in [
        scoped(&["agent"], Level::Ro),
        scoped(&["private"], Level::Rw),
    ] {
        assert_eq!(
            set_label(
                State(manager.clone()),
                Extension(cap),
                Path("agent".into()),
                Proto(wire::LabelReq {
                    label: Some("denied".into())
                }),
            )
            .await
            .unwrap_err()
            .0,
            StatusCode::FORBIDDEN
        );
    }
    assert!(manager.sessions.config().await.sessions[0].label.is_none());
    for cap in [Cap::Root, scoped(&["agent"], Level::Rw)] {
        assert!(
            set_label(
                State(manager.clone()),
                Extension(cap.clone()),
                Path("agent".into()),
                Proto(wire::LabelReq {
                    label: Some("  界 label  ".into())
                }),
            )
            .await
            .unwrap()
            .0
            .ok
        );
        assert_eq!(
            one(
                State(manager.clone()),
                Extension(Cap::Root),
                Path("agent".into())
            )
            .await
            .unwrap()
            .0
            .label,
            "界 label"
        );
        let (status, Proto(error)) = set_label(
            State(manager.clone()),
            Extension(cap.clone()),
            Path("agent".into()),
            Proto(wire::LabelReq {
                label: Some("界".repeat(61)),
            }),
        )
        .await
        .unwrap_err();
        assert_eq!(status, StatusCode::BAD_REQUEST);
        assert!(error.error.contains("at most 60 characters"));
        assert_eq!(
            manager.sessions.config().await.sessions[0].label.as_deref(),
            Some("界 label")
        );
        assert!(
            set_label(
                State(manager.clone()),
                Extension(cap),
                Path("agent".into()),
                Proto(wire::LabelReq {
                    label: Some("  ".into())
                }),
            )
            .await
            .unwrap()
            .0
            .ok
        );
        assert!(manager.sessions.config().await.sessions[0].label.is_none());
    }
}

#[tokio::test]
async fn pinning_rejects_denied_missing_and_non_reader_sessions() {
    let manager = configured_manager().await;
    for (cap, name, expected) in [
        (
            scoped(&["agent"], Level::Ro),
            "agent",
            StatusCode::FORBIDDEN,
        ),
        (
            scoped(&["private"], Level::Rw),
            "agent",
            StatusCode::FORBIDDEN,
        ),
        (Cap::Root, "missing", StatusCode::BAD_REQUEST),
        (
            scoped(&["agent"], Level::Rw),
            "agent",
            StatusCode::BAD_REQUEST,
        ),
    ] {
        let (status, Proto(error)) = set_reader_pinned(
            State(manager.clone()),
            Extension(cap),
            Path(name.into()),
            Proto(wire::ReaderPinnedReq { pinned: true }),
        )
        .await
        .unwrap_err();
        assert_eq!(status, expected);
        if name == "agent" && expected == StatusCode::BAD_REQUEST {
            assert_eq!(error.error, "session agent is not a pinnable reader");
        }
    }
    assert!(
        manager
            .sessions
            .views()
            .await
            .iter()
            .all(|s| !s.reader.pinned)
    );
}

#[tokio::test]
async fn creating_sessions_is_root_only_and_duplicate_failure_keeps_existing_configuration() {
    let directory = super::super::tests::fixture("session-create");
    let manager = crate::application::test_application(Config {
        projects: vec![ProjectCfg {
            name: "repo".into(),
            dir: directory.to_string_lossy().into_owned(),
            ..Default::default()
        }],
        ..Default::default()
    });
    let request = || {
        Proto(
            serde_json::from_value(json!({
                "name": "created", "project": "repo", "autostart": false,
                "worker": true, "parent": "forged", "task_id": "forged",
            }))
            .unwrap(),
        )
    };
    for level in [Level::Ro, Level::Rw] {
        let (status, Proto(error)) = create(
            State(manager.clone()),
            Extension(scoped(&[], level)),
            request(),
        )
        .await
        .unwrap_err();
        assert_eq!(status, StatusCode::FORBIDDEN);
        assert_eq!(
            error.error,
            "Only the root token can create sessions through this request."
        );
        assert!(manager.sessions.config().await.sessions.is_empty());
    }
    assert!(
        create(State(manager.clone()), Extension(Cap::Root), request())
            .await
            .unwrap()
            .0
            .ok
    );
    let saved = manager.sessions.config().await.sessions;
    assert_eq!(saved.len(), 1);
    assert!(crate::storage_id::valid(&saved[0].state_id));
    assert!(!saved[0].worker);
    assert!(saved[0].parent.is_empty());
    assert!(saved[0].task_id.is_empty());
    let (status, Proto(error)) = create(State(manager.clone()), Extension(Cap::Root), request())
        .await
        .unwrap_err();
    assert_eq!(status, StatusCode::BAD_REQUEST);
    assert_eq!(error.error, "session created already exists");
    assert_eq!(
        serde_json::to_value(manager.sessions.config().await.sessions).unwrap(),
        serde_json::to_value(saved).unwrap()
    );
}

#[tokio::test]
async fn session_actions_require_write_access_and_keep_response_shapes() {
    let manager = crate::application::test_application(crate::config::Config::default());
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
                    if $status == StatusCode::OK {
                        let Proto(body) = response.expect("stop returns an acknowledgment");
                        assert_eq!(serde_json::to_value(body).unwrap(), json!({ "ok": true }));
                    } else {
                        let (status, Proto(body)) = response.expect_err("missing session returns an error");
                        assert_eq!(status, $status);
                        assert!(body.error.contains(&name));
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
async fn configured_session_stop_obeys_read_and_write_grants() {
    let manager = crate::application::test_application(Config {
        sessions: vec![crate::config::SessionCfg {
            name: "agent".into(),
            ..Default::default()
        }],
        ..Default::default()
    });
    for level in [Level::Ro, Level::Rw] {
        let cap = Cap::Scoped(Grant {
            grantor: "caller".into(),
            sessions: ["agent".into()].into(),
            level,
            revoked: Default::default(),
        });
        let result = stop(State(manager.clone()), Extension(cap), Path("agent".into())).await;
        if level == Level::Ro {
            assert_eq!(result.unwrap_err().0, StatusCode::FORBIDDEN);
        } else {
            assert!(result.unwrap().0.ok);
        }
    }
}

#[tokio::test]
async fn replacing_session_configuration_is_root_only_and_root_can_change_sandbox_inputs() {
    let directory =
        std::env::temp_dir().join(format!("slopd-config-boundary-{}", uuid::Uuid::new_v4()));
    std::fs::create_dir_all(&directory).unwrap();
    let manager = crate::application::test_application(Config {
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
    let original = manager.sessions.config().await;

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
            serde_json::to_value(manager.sessions.config().await.sessions).unwrap(),
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
    let saved = manager.sessions.config().await;
    assert_eq!(saved.sessions[0].name, "renamed");
    assert_eq!(saved.sessions[0].project, "other");
    assert_eq!(saved.sessions[0].sandbox, ["slopworld-debug"]);
    std::fs::remove_dir_all(directory).unwrap();
}
