use super::*;
use axum::{body::Body, http::Request};
use tower::ServiceExt;

#[tokio::test]
async fn planner_routes_reject_agent_credentials_before_reading_or_writing() {
    let app = crate::application::test_application(crate::config::Config::default());
    for (method, path) in [
        ("GET", "/api/planner"),
        ("PUT", "/api/planner"),
        ("POST", "/api/planner/action"),
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
async fn planner_action_rejects_rotated_root_credentials_before_provider_io() {
    let app = crate::application::test_application(crate::config::Config::default());
    let generation = app.config_authority.root_generation() + 1;
    let result = planner_action(
        State(app),
        Extension(Cap::Root),
        Some(Extension(RootGeneration(generation))),
        Proto(wire::PlannerActionReq::default()),
    )
    .await;
    assert_eq!(result.unwrap_err().0, StatusCode::UNAUTHORIZED);
}
