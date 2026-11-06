use super::*;
use crate::credentials::Cap;
use axum::Extension;
use axum::{body::Body, http::Request};
use tower::ServiceExt;

#[tokio::test]
async fn planner_routes_reject_agent_credentials_before_reading_or_writing() {
    let app = crate::application::test_application(crate::config::Config::default());
    for (method, path) in [
        ("GET", "/api/planner"),
        ("PUT", "/api/planner"),
        ("GET", "/api/planner/items/id/description"),
        ("PUT", "/api/planner/items/id/description"),
        ("PUT", "/api/planner/items/id/links"),
    ] {
        let cap = Cap::Scoped(crate::credentials::AgentCredential {
            grantor: "agent".into(),
            sessions: Default::default(),
            level: crate::credentials::Level::Rw,
            revoked: Default::default(),
        });
        let router = crate::api::router(app.clone()).layer(Extension(cap));
        let response = router
            .oneshot(
                Request::builder()
                    .method(method)
                    .uri(path)
                    .body(Body::empty())
                    .unwrap(),
            )
            .await
            .unwrap();
        assert_eq!(response.status(), StatusCode::FORBIDDEN, "{method} {path}");
    }
}

#[tokio::test]
async fn removed_planner_actions_have_no_route() {
    let app = crate::application::test_application(crate::config::Config::default());
    let router = crate::api::router(app).layer(Extension(Cap::Root));
    let response = router
        .oneshot(
            Request::builder()
                .method("POST")
                .uri("/api/planner/action")
                .body(Body::empty())
                .unwrap(),
        )
        .await
        .unwrap();
    assert_eq!(response.status(), StatusCode::NOT_FOUND);
}

async fn request<T: prost::Message + Default>(
    app: &App,
    method: &str,
    path: &str,
    body: Vec<u8>,
) -> (StatusCode, T) {
    let response = crate::api::router(app.clone())
        .layer(Extension(Cap::Root))
        .oneshot(
            Request::builder()
                .method(method)
                .uri(path)
                .header("content-type", "application/x-protobuf")
                .body(Body::from(body))
                .unwrap(),
        )
        .await
        .unwrap();
    let status = response.status();
    let bytes = axum::body::to_bytes(response.into_body(), usize::MAX)
        .await
        .unwrap();
    if !status.is_success() {
        eprintln!(
            "{method} {path}: {status}: {}",
            <wire::Error as prost::Message>::decode(bytes.clone())
                .unwrap()
                .error
        );
    }
    (status, T::decode(bytes).unwrap())
}

#[tokio::test]
async fn root_round_trips_backlog_and_description_without_overwriting_stale_edits() {
    use prost::Message;
    let app = crate::application::test_application(crate::config::Config::default());
    let (status, empty) =
        request::<wire::PlannerDocument>(&app, "GET", "/api/planner", vec![]).await;
    assert_eq!(status, StatusCode::OK);
    let save = wire::PlannerSaveReq {
        text: "(A) Fix startup +repo custom:value\n".into(),
        revision: empty.revision.clone(),
    };
    let (status, saved) =
        request::<wire::PlannerDocument>(&app, "PUT", "/api/planner", save.encode_to_vec()).await;
    assert_eq!(status, StatusCode::OK);
    assert!(saved.text.starts_with(save.text.trim_end()));
    assert_eq!(saved.items.len(), 1);
    let (status, _) =
        request::<wire::Error>(&app, "PUT", "/api/planner", save.encode_to_vec()).await;
    assert_eq!(status, StatusCode::CONFLICT);
    let (_, read) = request::<wire::PlannerDocument>(&app, "GET", "/api/planner", vec![]).await;
    assert_eq!(read, saved);

    let path = format!("/api/planner/items/{}/description", saved.items[0].id);
    let (status, empty_description) =
        request::<wire::PlannerDescription>(&app, "GET", &path, vec![]).await;
    assert_eq!(status, StatusCode::OK);
    let description = wire::PlannerSaveReq {
        text: "Steps\n\nKeep this detail.".into(),
        revision: empty_description.revision,
    };
    let (status, written) =
        request::<wire::PlannerDescription>(&app, "PUT", &path, description.encode_to_vec()).await;
    assert_eq!(status, StatusCode::OK);
    assert_eq!(written.text, description.text);
    let (status, _) = request::<wire::Error>(&app, "PUT", &path, description.encode_to_vec()).await;
    assert_eq!(status, StatusCode::CONFLICT);
    let (_, read) = request::<wire::PlannerDescription>(&app, "GET", &path, vec![]).await;
    assert_eq!(read, written);

    let (_, removed) = request::<wire::PlannerDocument>(
        &app,
        "PUT",
        "/api/planner",
        wire::PlannerSaveReq {
            text: String::new(),
            revision: saved.revision,
        }
        .encode_to_vec(),
    )
    .await;
    assert!(removed.items.is_empty());
    let (status, _) = request::<wire::Error>(
        &app,
        "PUT",
        &path,
        wire::PlannerSaveReq {
            text: "lost edit".into(),
            revision: written.revision,
        }
        .encode_to_vec(),
    )
    .await;
    assert_eq!(status, StatusCode::CONFLICT);
    let (status, _) = request::<wire::Error>(&app, "GET", &path, vec![]).await;
    assert_eq!(status, StatusCode::BAD_REQUEST);
    let (status, _) = request::<wire::PlannerDocument>(
        &app,
        "PUT",
        "/api/planner",
        wire::PlannerSaveReq {
            text: saved.text,
            revision: removed.revision,
        }
        .encode_to_vec(),
    )
    .await;
    assert_eq!(status, StatusCode::OK);
    let (_, retained) = request::<wire::PlannerDescription>(&app, "GET", &path, vec![]).await;
    assert_eq!(retained.text, description.text);
    let (status, _) = request::<wire::Error>(
        &app,
        "GET",
        "/api/planner/items/invalid/description",
        vec![],
    )
    .await;
    assert_eq!(status, StatusCode::BAD_REQUEST);
}

async fn linked_worker_fixture() -> (App, crate::tasks::Task, String) {
    let app = crate::application::test_application(crate::config::Config {
        sessions: vec![crate::config::SessionCfg {
            name: "worker".into(),
            worker: true,
            ..Default::default()
        }],
        ..Default::default()
    });
    let sender = app
        .sessions
        .task_participant(crate::tasks::HOST)
        .await
        .unwrap();
    let recipient = app.sessions.task_participant("worker").await.unwrap();
    let worker_id = recipient.identity.clone();
    let task = app
        .sessions
        .create_task_owned(
            sender,
            recipient,
            "Work".into(),
            Some(crate::tasks::WorkerTask {
                session: "worker".into(),
                parent: crate::tasks::HOST.into(),
                durable: true,
            }),
        )
        .await
        .unwrap();
    (app, task, worker_id)
}

#[tokio::test]
async fn explicit_links_validate_targets_and_revisions_and_removal_preserves_task() {
    use prost::Message;
    let (app, task, worker_id) = linked_worker_fixture().await;
    let empty = get_planner(State(app.clone())).await.unwrap().0;
    let saved = save_planner(
        State(app.clone()),
        Proto(wire::PlannerSaveReq {
            text: "Manual backlog".into(),
            revision: empty.revision,
        }),
    )
    .await
    .unwrap()
    .0;
    let item = &saved.items[0];
    let path = format!("/api/planner/items/{}/links", item.id);
    let links = vec![
        wire::PlannerLink {
            kind: "task".into(),
            id: task.id.clone(),
            label: "Work".into(),
            ..Default::default()
        },
        wire::PlannerLink {
            kind: "worker".into(),
            id: worker_id,
            label: "Worker".into(),
            ..Default::default()
        },
    ];
    for invalid in [
        vec![wire::PlannerLink {
            kind: "unknown".into(),
            ..links[0].clone()
        }],
        vec![wire::PlannerLink {
            id: "missing".into(),
            ..links[0].clone()
        }],
        vec![links[0].clone(), links[0].clone()],
    ] {
        let (status, _) = request::<wire::Error>(
            &app,
            "PUT",
            &path,
            wire::PlannerLinksReq {
                links: invalid,
                revision: item.links_revision.clone(),
            }
            .encode_to_vec(),
        )
        .await;
        assert_eq!(status, StatusCode::CONFLICT);
    }
    let update = wire::PlannerLinksReq {
        links,
        revision: item.links_revision.clone(),
    };
    let (status, linked) =
        request::<wire::PlannerDocument>(&app, "PUT", &path, update.encode_to_vec()).await;
    assert_eq!(status, StatusCode::OK);
    assert_eq!(linked.items[0].links.len(), 2);
    assert_eq!(linked.items[0].links[0].status, "queued");
    assert!(linked.items[0].status.is_empty());
    let (status, _) = request::<wire::Error>(&app, "PUT", &path, update.encode_to_vec()).await;
    assert_eq!(status, StatusCode::CONFLICT);
    let (status, cleared) = request::<wire::PlannerDocument>(
        &app,
        "PUT",
        &path,
        wire::PlannerLinksReq {
            links: vec![],
            revision: linked.items[0].links_revision.clone(),
        }
        .encode_to_vec(),
    )
    .await;
    assert_eq!(status, StatusCode::OK);
    assert!(cleared.items[0].links.is_empty());
    assert!(
        get_planner(State(app.clone())).await.unwrap().0.items[0]
            .links
            .is_empty()
    );
    assert_eq!(
        app.sessions.task_store().all_tasks_async().await.unwrap()[0].id,
        task.id
    );
    assert!(app.sessions.session_cfg("worker").await.is_some());
}

#[tokio::test]
async fn failed_backlog_write_preserves_revision_and_retry_succeeds() {
    let app = crate::application::test_application(crate::config::Config::default());
    let empty = get_planner(State(app.clone())).await.unwrap().0;
    let fault = crate::paths::fail_writes(std::path::Path::new(&empty.path));
    let request = wire::PlannerSaveReq {
        text: "Retry safely".into(),
        revision: empty.revision.clone(),
    };
    let error = save_planner(State(app.clone()), Proto(request.clone()))
        .await
        .unwrap_err();
    assert_eq!(error.0, StatusCode::CONFLICT);
    assert!(error.1.0.error.contains("injected persistence failure"));
    assert_eq!(get_planner(State(app.clone())).await.unwrap().0, empty);
    drop(fault);
    let saved = save_planner(State(app.clone()), Proto(request))
        .await
        .unwrap()
        .0;
    assert_eq!(saved.items.len(), 1);
    assert_eq!(get_planner(State(app)).await.unwrap().0, saved);
}
