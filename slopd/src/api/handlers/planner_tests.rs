use super::*;
use crate::grant::Cap;
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
        let cap = Cap::Scoped(crate::grant::Grant {
            grantor: "agent".into(),
            sessions: Default::default(),
            level: crate::grant::Level::Rw,
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
