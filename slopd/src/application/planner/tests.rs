use super::*;
use std::sync::Arc;

fn fixture() -> (Application, super::super::test_support::Fixture) {
    let mut fixture = super::super::test_support::Fixture::new();
    Arc::get_mut(&mut fixture.services).unwrap().planner =
        Store::new(fixture.root.join("TODO.txt"));
    let mut app = crate::application::test_application(crate::config::Config::default());
    app.services = fixture.services.clone();
    (app, fixture)
}
fn action(revision: String) -> Action {
    Action {
        action: "execute".into(),
        revision,
        line: 0,
        project: "missing".into(),
        template: "missing".into(),
    }
}

#[tokio::test]
async fn planner_dispatch_rejects_stale_credentials_revision_and_policy_before_reserving() {
    let (app, _fixture) = fixture();
    let original = app.services.planner.read().await.unwrap();
    let document = app
        .services
        .planner
        .save("Task".into(), original.revision.clone())
        .await
        .unwrap();
    let generation = app.config_authority.root_generation();
    assert!(
        dispatch(
            &app,
            action(document.revision.clone()),
            generation + 1,
            "Work".into()
        )
        .await
        .err()
        .unwrap()
        .to_string()
        .contains("credentials")
    );
    assert!(
        dispatch(&app, action(original.revision), generation, "Work".into())
            .await
            .err()
            .unwrap()
            .to_string()
            .contains("changed")
    );
    assert!(
        dispatch(&app, action(document.revision), generation, "Work".into())
            .await
            .err()
            .unwrap()
            .to_string()
            .contains("project")
    );
    assert_eq!(
        app.services.planner.read().await.unwrap().text,
        document.text
    );
}

#[tokio::test]
async fn planner_dispatch_reservations_survive_restart_and_prevent_repeated_launches() {
    let (app, fixture) = fixture();
    let empty = app.services.planner.read().await.unwrap();
    let document = app
        .services
        .planner
        .save("Task sw-task:pending".into(), empty.revision)
        .await
        .unwrap();
    let restored = Store::new(fixture.root.join("TODO.txt"))
        .read()
        .await
        .unwrap();
    assert_eq!(restored.text, document.text);
    let result = dispatch(
        &app,
        action(restored.revision),
        app.config_authority.root_generation(),
        "Work".into(),
    )
    .await;
    assert!(
        result
            .err()
            .unwrap()
            .to_string()
            .contains("already been dispatched")
    );
}

#[tokio::test]
async fn planner_rejects_invalid_worker_policy_without_calling_provider_or_reserving() {
    let (app, _fixture) = fixture();
    let empty = app.services.planner.read().await.unwrap();
    let saved = app
        .services
        .planner
        .save("Task".into(), empty.revision)
        .await
        .unwrap();
    let result = act_with_completion(
        &app,
        action(saved.revision.clone()),
        app.config_authority.root_generation(),
        |_, _, _, _| async { panic!("invalid dispatch must not spend a provider call") },
    )
    .await;
    assert!(result.unwrap_err().to_string().contains("project"));
    assert_eq!(app.services.planner.read().await.unwrap().text, saved.text);
}

#[tokio::test]
async fn planner_advisory_actions_reject_provider_output_after_document_changes() {
    for name in ["summarize", "plan"] {
        let (app, _fixture) = fixture();
        let empty = app.services.planner.read().await.unwrap();
        let saved = app
            .services
            .planner
            .save("Original".into(), empty.revision)
            .await
            .unwrap();
        let request = Action {
            action: name.into(),
            ..action(saved.revision.clone())
        };
        let result = act_with_completion(
            &app,
            request,
            app.config_authority.root_generation(),
            |input, _, _, _| {
                let app = &app;
                async move {
                    assert_eq!(input, saved.text);
                    // Simulate a save while provider I/O is in flight; the provider holds no gate.
                    app.services
                        .planner
                        .save("New scope".into(), saved.revision)
                        .await
                        .unwrap();
                    Ok("Obsolete advice".into())
                }
            },
        )
        .await;
        assert!(result.unwrap_err().to_string().contains("changed"));
        assert!(
            app.services
                .planner
                .read()
                .await
                .unwrap()
                .text
                .starts_with("New scope")
        );
    }
}

#[tokio::test]
async fn cancelling_provider_wait_leaves_the_document_unchanged_and_releases_ownership() {
    let (app, _fixture) = fixture();
    let empty = app.services.planner.read().await.unwrap();
    let saved = app
        .services
        .planner
        .save("Original".into(), empty.revision)
        .await
        .unwrap();
    let (entered, waiting) = tokio::sync::oneshot::channel();
    let request = Action {
        action: "plan".into(),
        ..action(saved.revision.clone())
    };
    let running_app = app.clone();
    let running = tokio::spawn(async move {
        act_with_completion(
            &running_app,
            request,
            running_app.config_authority.root_generation(),
            |_, _, _, _| async {
                entered.send(()).unwrap();
                std::future::pending::<Result<String>>().await
            },
        )
        .await
    });
    waiting.await.unwrap();
    running.abort();
    assert!(running.await.unwrap_err().is_cancelled());
    assert_eq!(app.services.planner.read().await.unwrap().text, saved.text);
    app.services
        .planner
        .save("Still editable".into(), saved.revision)
        .await
        .unwrap();
}

#[tokio::test]
async fn planner_save_reply_retains_recovered_worker_link_and_live_status() {
    use axum::{Extension, body::Body, http::Request};
    use prost::Message;
    use tower::ServiceExt;
    let (app, _fixture) = fixture();
    let empty = app.services.planner.read().await.unwrap();
    let saved = app
        .services
        .planner
        .save("Task sw-task:pending".into(), empty.revision)
        .await
        .unwrap();
    let body = format!("{}\nWork order", store::marker(&saved.items[0].id));
    let task = app
        .sessions
        .task_store()
        .run(move |tasks| {
            tasks.create_worker(
                crate::tasks::HOST.into(),
                "worker".into(),
                body,
                crate::tasks::HOST.into(),
                true,
            )
        })
        .await
        .unwrap();
    let request = crate::shared::wire::PlannerSaveReq {
        text: saved.text.clone(),
        revision: saved.revision,
    };
    let response = crate::api::router(app.clone())
        .layer(Extension(crate::grant::Cap::Root))
        .oneshot(
            Request::builder()
                .method("PUT")
                .uri("/api/planner")
                .header("content-type", "application/x-protobuf")
                .body(Body::from(request.encode_to_vec()))
                .unwrap(),
        )
        .await
        .unwrap();
    assert_eq!(response.status(), axum::http::StatusCode::OK);
    let bytes = axum::body::to_bytes(response.into_body(), 1024 * 1024)
        .await
        .unwrap();
    let document = crate::shared::wire::PlannerDocument::decode(bytes).unwrap();
    assert_eq!(document.text, saved.text);
    assert_eq!(document.items[0].task_id, task.id);
    assert_eq!(document.items[0].status, "queued");
}

async fn executable_fixture() -> (Application, super::super::test_support::Fixture) {
    let (app, fixture) = fixture();
    tokio::fs::create_dir_all(&fixture.root).await.unwrap();
    app.config_authority
        .write()
        .await
        .projects
        .push(crate::config::ProjectCfg {
            id: "repo".into(),
            name: "repo".into(),
            dir: fixture.root.to_string_lossy().into(),
            ..Default::default()
        });
    app.services
        .templates
        .store_for_test()
        .write()
        .await
        .templates
        .push(
            serde_json::from_value(serde_json::json!({
                "name": "worker", "defaults": {"cmd": "true", "network": "private"}
            }))
            .unwrap(),
        );
    (app, fixture)
}

#[tokio::test]
async fn planner_execute_rechecks_scope_after_provider_without_creating_a_worker() {
    let (app, _fixture) = executable_fixture().await;
    let empty = app.services.planner.read().await.unwrap();
    let saved = app
        .services
        .planner
        .save("Original".into(), empty.revision)
        .await
        .unwrap();
    let request = Action {
        project: "repo".into(),
        template: "worker".into(),
        ..action(saved.revision.clone())
    };
    let result = act_with_completion(
        &app,
        request,
        app.config_authority.root_generation(),
        |_, _, _, _| async {
            app.services
                .planner
                .save("Changed scope".into(), saved.revision)
                .await
                .unwrap();
            Ok("Obsolete work order".into())
        },
    )
    .await;
    assert!(result.unwrap_err().to_string().contains("changed"));
    assert!(
        !app.services
            .planner
            .read()
            .await
            .unwrap()
            .text
            .contains("sw-task:")
    );
    assert!(
        app.sessions
            .task_store()
            .all_tasks_async()
            .await
            .unwrap()
            .is_empty()
    );
}

#[tokio::test]
async fn planner_missing_checkout_does_not_spend_provider_call_or_reserve_item() {
    let (app, fixture) = executable_fixture().await;
    app.config_authority.write().await.projects[0].dir =
        fixture.root.join("missing").to_string_lossy().into();
    let empty = app.services.planner.read().await.unwrap();
    let saved = app
        .services
        .planner
        .save("Task".into(), empty.revision)
        .await
        .unwrap();
    let request = Action {
        project: "repo".into(),
        template: "worker".into(),
        ..action(saved.revision)
    };
    let result = act_with_completion(
        &app,
        request,
        app.config_authority.root_generation(),
        |_, _, _, _| async { panic!("missing checkout must be rejected before provider I/O") },
    )
    .await;
    assert!(result.unwrap_err().to_string().contains("checkout"));
    assert_eq!(app.services.planner.read().await.unwrap().text, saved.text);
}
