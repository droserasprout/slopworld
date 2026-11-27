//! Exercise the public protobuf routes with the same codec as slopctl.
use super::*;
use crate::{
    credentials::{AgentCredential, Level},
    shared::{http_wire, protocol::routes},
};
use axum::{body::Body, http::Request};
use serde_json::{Value, json};
use tower::ServiceExt;

async fn call(app: &App, cap: &Cap, body: Option<Value>) -> Value {
    let method = if body.is_some() { "POST" } else { "GET" };
    let bytes = body
        .map(|value| http_wire::encode_request(method, routes::BOARDS, value).unwrap())
        .unwrap_or_default();
    let reply = crate::api::router(app.clone())
        .layer(Extension(cap.clone()))
        .oneshot(
            Request::builder()
                .method(method)
                .uri(routes::BOARDS)
                .header("content-type", http_wire::CONTENT_TYPE)
                .body(Body::from(bytes))
                .unwrap(),
        )
        .await
        .unwrap();
    assert_eq!(reply.status(), StatusCode::OK);
    let data = axum::body::to_bytes(reply.into_body(), usize::MAX)
        .await
        .unwrap();
    http_wire::decode_response(method, routes::BOARDS, &data).unwrap()
}

#[tokio::test]
async fn agent_and_human_observe_one_ordered_card_with_its_discussion() {
    let app = crate::application::test_application(Default::default());
    let agent = Cap::Scoped(AgentCredential {
        grantor: "reviewer".into(),
        level: Level::Rw,
        sessions: Default::default(),
        revoked: Default::default(),
    });
    let edit = |action: &str| json!({"action":action,"board":"root"});
    let mut add = edit("add-column");
    add["text"] = json!("Review");
    let catalog = call(&app, &Cap::Root, Some(add)).await;
    let columns = &catalog["boards"][0]["columns"];
    let first = &columns[0]["id"];
    let review = &columns[4]["id"];
    let mut add = edit("add-card");
    add["column"] = first.clone();
    add["text"] = json!("Check UI");
    let catalog = call(&app, &Cap::Root, Some(add)).await;
    let id = &catalog["boards"][0]["columns"][0]["cards"][0]["id"];
    for (action, text) in [
        ("assign", "reviewer"),
        ("link", "project-boards"),
        ("comment", "Ready for review"),
    ] {
        let mut op = edit(action);
        op["card"] = id.clone();
        op["text"] = json!(text);
        op["kind"] = json!("worktree");
        call(&app, &agent, Some(op)).await;
    }
    let mut move_column = edit("move-column");
    move_column["column"] = review.clone();
    move_column["before"] = first.clone();
    call(&app, &Cap::Root, Some(move_column)).await;
    let mut move_card = edit("move-card");
    move_card["card"] = id.clone();
    move_card["column"] = review.clone();
    let committed = call(&app, &Cap::Root, Some(move_card)).await;
    assert_eq!(call(&app, &agent, None).await, committed);
    let column = &committed["boards"][0]["columns"][0];
    assert_eq!(&column["id"], review);
    let card = &column["cards"][0];
    assert_eq!(&card["id"], id);
    assert_eq!(card["assignee"], "reviewer");
    assert_eq!(card["comments"][0]["author"], "reviewer");
    assert_eq!(card["links"][0]["target"], "project-boards");
}
