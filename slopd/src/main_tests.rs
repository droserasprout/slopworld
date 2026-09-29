use super::*;
use crate::config::{Daemon, ProjectCfg, SessionCfg};
use crate::grant::Level;
use crate::shared::{http_wire, wire};
use axum::body::Body;
use axum::http::Request;
use futures::{SinkExt, StreamExt};
use prost::Message;
use std::collections::BTreeSet;
use tokio_tungstenite::tungstenite::client::IntoClientRequest;
use tower::ServiceExt;

fn manager() -> Arc<Manager> {
    crate::session::test_manager(Config {
        daemon: Daemon {
            token: "root-secret".into(),
            ..Default::default()
        },
        sessions: vec![
            SessionCfg {
                name: "grantor".into(),
                ..Default::default()
            },
            SessionCfg {
                name: "target".into(),
                ..Default::default()
            },
        ],
        ..Default::default()
    })
}

fn app(manager: Arc<Manager>) -> axum::Router {
    api::router(manager.clone())
        .layer(middleware::from_fn_with_state(manager, auth))
        .layer(middleware::from_fn(api::protobuf::normalize_errors))
}

async fn get(app: &axum::Router, path: &str, token: Option<&str>) -> StatusCode {
    let mut request = Request::builder().uri(path).body(Body::empty()).unwrap();
    if let Some(token) = token {
        request
            .headers_mut()
            .insert(shared::protocol::TOKEN_HEADER, token.parse().unwrap());
    }
    app.clone().oneshot(request).await.unwrap().status()
}

fn event_has_target(event: wire::Event) -> bool {
    event.payload.is_some_and(|payload| match payload {
        wire::event::Payload::Sessions(sessions) => sessions
            .sessions
            .iter()
            .any(|session| session.name == "target"),
        wire::event::Payload::Screen(screen) => screen.name == "target",
        _ => false,
    })
}

fn message_has_target(message: &tokio_tungstenite::tungstenite::Message) -> bool {
    let tokio_tungstenite::tungstenite::Message::Binary(bytes) = message else {
        return false;
    };
    event_has_target(wire::Event::decode(bytes.as_slice()).unwrap())
}

async fn await_socket_close<S, E>(socket: &mut S)
where
    S: futures::Stream<Item = Result<tokio_tungstenite::tungstenite::Message, E>> + Unpin,
{
    while let Some(Ok(message)) = socket.next().await {
        if matches!(&message, tokio_tungstenite::tungstenite::Message::Close(_)) {
            break;
        }
        assert!(
            !message_has_target(&message),
            "socket received a replacement session"
        );
    }
}

async fn add_unrelated_sessions(manager: &Arc<Manager>) -> crate::config::Config {
    let mut cfg = manager.config().await;
    cfg.sessions
        .extend(["other", "other-owner"].map(|name| SessionCfg {
            name: name.into(),
            ..Default::default()
        }));
    cfg.projects.push(crate::config::ProjectCfg {
        name: "repo".into(),
        dir: "/tmp".into(),
        ..Default::default()
    });
    for session in &mut cfg.sessions {
        session.project = "repo".into();
    }
    manager
        .replace_config(&toml::to_string(&cfg).unwrap())
        .await
        .unwrap();
    cfg
}

#[tokio::test]
async fn stalled_scoped_uploads_allow_revocation_and_recheck_authority_on_completion() {
    for level in [Level::Ro, Level::Rw] {
        let manager = manager();
        let token = manager
            .mint_grant("grantor".into(), vec!["target".into()], level)
            .await
            .unwrap();
        let app = app(manager.clone());
        let (entered, observed) = tokio::sync::oneshot::channel();
        let (release, resume) = tokio::sync::oneshot::channel();
        let stream = futures::stream::once(async move {
            entered.send(()).unwrap();
            resume.await.unwrap();
            Ok::<_, std::io::Error>(
                http_wire::encode_request(
                    "POST",
                    "/api/tasks",
                    serde_json::json!({"to":"target","body":"queued upload"}),
                )
                .unwrap(),
            )
        });
        let request = Request::builder()
            .method("POST")
            .uri(shared::protocol::routes::TASKS)
            .header(shared::protocol::TOKEN_HEADER, &token)
            .header("content-type", http_wire::CONTENT_TYPE)
            .body(Body::from_stream(stream))
            .unwrap();
        let upload = tokio::spawn(app.clone().oneshot(request));
        tokio::time::timeout(std::time::Duration::from_secs(1), observed)
            .await
            .expect("request body was not polled")
            .unwrap();

        let revoke = Request::builder()
            .method("DELETE")
            .uri("/api/grants/grantor")
            .header(shared::protocol::TOKEN_HEADER, "root-secret")
            .body(Body::empty())
            .unwrap();
        let response = tokio::time::timeout(std::time::Duration::from_secs(1), app.oneshot(revoke))
            .await
            .expect("stalled upload blocked grant revocation")
            .unwrap();
        assert_eq!(response.status(), StatusCode::OK);

        release.send(()).unwrap();
        let response = tokio::time::timeout(std::time::Duration::from_secs(1), upload)
            .await
            .expect("completed upload did not finish")
            .unwrap()
            .unwrap();
        assert_eq!(response.status(), StatusCode::UNAUTHORIZED);
        assert!(manager.tasks.all_tasks().is_empty());
    }
}

#[tokio::test]
async fn scoped_uploads_preserve_body_limits_and_accept_valid_json() {
    let manager = manager();
    let token = manager
        .mint_grant("grantor".into(), vec!["target".into()], Level::Ro)
        .await
        .unwrap();
    let app = app(manager.clone());
    for (body, expected) in [
        (
            vec![0u8; 2 * 1024 * 1024 + 1],
            StatusCode::PAYLOAD_TOO_LARGE,
        ),
        (
            http_wire::encode_request(
                "POST",
                "/api/tasks",
                serde_json::json!({"to":"target","body":"valid upload"}),
            )
            .unwrap(),
            StatusCode::OK,
        ),
    ] {
        let request = Request::builder()
            .method("POST")
            .uri(shared::protocol::routes::TASKS)
            .header(shared::protocol::TOKEN_HEADER, &token)
            .header("content-type", http_wire::CONTENT_TYPE)
            .body(Body::from(body))
            .unwrap();
        assert_eq!(
            app.clone().oneshot(request).await.unwrap().status(),
            expected
        );
    }
    assert_eq!(manager.tasks.all_tasks().len(), 1);
}

#[tokio::test]
async fn api_auth_requires_the_configured_root_token() {
    let app = app(manager());

    assert_eq!(
        get(&app, shared::protocol::routes::HEALTH, None).await,
        StatusCode::UNAUTHORIZED
    );
    assert_eq!(
        get(&app, shared::protocol::routes::HEALTH, Some("wrong")).await,
        StatusCode::UNAUTHORIZED
    );
    assert_eq!(
        get(&app, shared::protocol::routes::HEALTH, Some("root-secret")).await,
        StatusCode::OK
    );
}

#[tokio::test]
async fn scoped_tokens_reach_shared_routes_but_not_root_routes() {
    let manager = manager();
    let scoped = manager
        .mint_grant("grantor".into(), vec!["target".into()], Level::Ro)
        .await
        .unwrap();
    let app = app(manager);

    assert_eq!(
        get(&app, shared::protocol::routes::HEALTH, Some(&scoped)).await,
        StatusCode::OK
    );
    assert_eq!(
        get(&app, shared::protocol::routes::USAGE, Some(&scoped)).await,
        StatusCode::FORBIDDEN
    );
    assert_eq!(
        get(&app, shared::protocol::routes::USAGE, Some("root-secret")).await,
        StatusCode::OK
    );
}

#[tokio::test]
async fn scoped_worker_catalog_is_allowlisted_and_project_scoped() {
    let manager = crate::session::test_manager(Config {
        daemon: Daemon {
            token: "root-secret".into(),
            worker_templates: BTreeSet::from(["review".to_string()]),
            ..Default::default()
        },
        projects: vec![ProjectCfg {
            name: "repo".into(),
            dir: "/tmp".into(),
            ..Default::default()
        }],
        sessions: vec![SessionCfg {
            name: "caller".into(),
            project: "repo".into(),
            ..Default::default()
        }],
        ..Default::default()
    });
    let template: crate::session::AgentTemplate = serde_json::from_value(serde_json::json!({
        "name": "review",
        "defaults": {"cmd": "echo review"}
    }))
    .unwrap();
    manager
        .create_agent_template_definition(template)
        .await
        .unwrap();
    let token = manager
        .mint_grant("caller".into(), vec!["caller".into()], Level::Ro)
        .await
        .unwrap();
    let app = app(manager);
    let request = Request::builder()
        .uri(shared::protocol::routes::SPAWNABLE_TEMPLATES)
        .header(shared::protocol::TOKEN_HEADER, token)
        .body(Body::empty())
        .unwrap();
    let response = app.oneshot(request).await.unwrap();
    assert_eq!(response.status(), StatusCode::OK);
    let body = axum::body::to_bytes(response.into_body(), 1024 * 1024)
        .await
        .unwrap();
    let value: serde_json::Value =
        http_wire::decode_response("GET", shared::protocol::routes::SPAWNABLE_TEMPLATES, &body)
            .unwrap();
    assert_eq!(value["project"], "repo");
    assert_eq!(value["templates"][0]["name"], "review");
}

#[tokio::test]
async fn revoking_a_grant_closes_an_existing_websocket_before_read_or_write() {
    let manager = manager();
    manager.sync_from_config().await;
    let scoped = manager
        .mint_grant("grantor".into(), vec!["target".into()], Level::Rw)
        .await
        .unwrap();
    let app = app(manager.clone());
    let revoke_app = app.clone();
    let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
    let address = listener.local_addr().unwrap();
    let server = tokio::spawn(async move {
        axum::serve(listener, app).await.unwrap();
    });

    let mut request = format!("ws://{address}{}", shared::protocol::WS_PATH)
        .into_client_request()
        .unwrap();
    request
        .headers_mut()
        .insert(shared::protocol::TOKEN_HEADER, scoped.parse().unwrap());
    request.headers_mut().insert(
        "sec-websocket-protocol",
        "slopworld.protobuf.v2".parse().unwrap(),
    );
    let (mut socket, _) = tokio_tungstenite::connect_async(request).await.unwrap();

    let initial = tokio::time::timeout(std::time::Duration::from_secs(1), socket.next())
        .await
        .expect("websocket initial snapshot timed out")
        .expect("websocket closed before its initial snapshot")
        .expect("websocket initial snapshot failed");
    assert!(
        matches!(initial, tokio_tungstenite::tungstenite::Message::Binary(text) if wire::Event::decode(text.as_slice()).unwrap().payload.is_some_and(|p| match p { wire::event::Payload::Sessions(s) => s.sessions.iter().any(|s| s.name == "target"), wire::event::Payload::Screen(s) => s.name == "target", _ => false }))
    );

    socket
        .send(tokio_tungstenite::tungstenite::Message::Binary(
            wire::ClientMessage {
                payload: Some(wire::client_message::Payload::Sub(wire::NameReq {
                    name: Some("target".into()),
                })),
            }
            .encode_to_vec(),
        ))
        .await
        .unwrap();
    socket
        .send(tokio_tungstenite::tungstenite::Message::Binary(
            wire::ClientMessage {
                payload: Some(wire::client_message::Payload::Keys(wire::KeysReq {
                    name: Some("target".into()),
                    keys: vec!["Enter".into()],
                    ..Default::default()
                })),
            }
            .encode_to_vec(),
        ))
        .await
        .unwrap();

    let revoke = Request::builder()
        .method("DELETE")
        .uri(format!("{}/grantor", shared::protocol::routes::GRANTS))
        .header(shared::protocol::TOKEN_HEADER, "root-secret")
        .body(Body::empty())
        .unwrap();
    assert_eq!(
        revoke_app.oneshot(revoke).await.unwrap().status(),
        StatusCode::OK
    );

    let terminal = tokio::time::timeout(std::time::Duration::from_secs(1), socket.next())
        .await
        .expect("revoked websocket stayed open")
        .expect("revoked websocket did not terminate");
    assert!(matches!(
        terminal,
        Err(_) | Ok(tokio_tungstenite::tungstenite::Message::Close(_))
    ));
    assert!(socket
        .send(tokio_tungstenite::tungstenite::Message::Binary(
            wire::ClientMessage {
                payload: Some(wire::client_message::Payload::Sub(wire::NameReq {
                    name: Some("target".into())
                }))
            }
            .encode_to_vec()
        ))
        .await
        .is_err());
    assert!(socket
        .send(tokio_tungstenite::tungstenite::Message::Binary(
            wire::ClientMessage {
                payload: Some(wire::client_message::Payload::Keys(wire::KeysReq {
                    name: Some("target".into()),
                    keys: vec!["Enter".into()],
                    ..Default::default()
                }))
            }
            .encode_to_vec()
        ))
        .await
        .is_err());

    server.abort();
}
#[tokio::test]
async fn disappearing_identities_close_sockets_and_do_not_follow_reused_names() {
    for level in [Level::Ro, Level::Rw] {
        for subject in ["target", "grantor"] {
            for rename in [false, true] {
                let manager = manager();
                let mut cfg = add_unrelated_sessions(&manager).await;
                let token = manager
                    .mint_grant(
                        "grantor".into(),
                        vec!["target".into(), "other".into()],
                        level,
                    )
                    .await
                    .unwrap();
                let unrelated = manager
                    .mint_grant("other-owner".into(), vec!["other".into()], level)
                    .await
                    .unwrap();
                let app = app(manager.clone());
                let rest = app.clone();
                let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
                let address = listener.local_addr().unwrap();
                let server = tokio::spawn(async move {
                    axum::serve(listener, app).await.unwrap();
                });
                let mut request = format!("ws://{address}{}", shared::protocol::WS_PATH)
                    .into_client_request()
                    .unwrap();
                request
                    .headers_mut()
                    .insert(shared::protocol::TOKEN_HEADER, token.parse().unwrap());
                request.headers_mut().insert(
                    "sec-websocket-protocol",
                    "slopworld.protobuf.v2".parse().unwrap(),
                );
                let (mut socket, _) = tokio_tungstenite::connect_async(request).await.unwrap();
                let initial = socket.next().await.unwrap().unwrap();
                assert!(message_has_target(&initial));
                socket
                    .send(tokio_tungstenite::tungstenite::Message::Binary(
                        wire::ClientMessage {
                            payload: Some(wire::client_message::Payload::Sub(wire::NameReq {
                                name: Some("target".into()),
                            })),
                        }
                        .encode_to_vec(),
                    ))
                    .await
                    .unwrap();

                let old = cfg
                    .sessions
                    .iter()
                    .find(|s| s.name == subject)
                    .unwrap()
                    .clone();
                if rename {
                    let mut renamed = old.clone();
                    renamed.name = "renamed".into();
                    manager.update(subject, renamed).await.unwrap();
                    cfg = manager.config().await;
                } else {
                    cfg.sessions.retain(|s| s.name != subject);
                    tokio::fs::write(&manager.cfg_path, toml::to_string(&cfg).unwrap())
                        .await
                        .unwrap();
                    assert!(manager.reload_if_changed().await);
                }
                cfg.sessions.push(SessionCfg {
                    name: subject.into(),
                    ..Default::default()
                });
                manager
                    .replace_config(&toml::to_string(&cfg).unwrap())
                    .await
                    .unwrap();
                tokio::time::timeout(
                    std::time::Duration::from_secs(2),
                    await_socket_close(&mut socket),
                )
                .await
                .expect("invalidated socket stayed connected");
                assert_eq!(
                    get(&rest, "/api/sessions/target", Some(&token)).await,
                    StatusCode::UNAUTHORIZED
                );
                assert_eq!(
                    get(&rest, "/api/sessions/other", Some(&unrelated)).await,
                    StatusCode::OK
                );
                assert_eq!(
                    get(&rest, "/api/sessions/other", Some(&token)).await,
                    StatusCode::UNAUTHORIZED
                );
                server.abort();
            }
        }
    }
}
