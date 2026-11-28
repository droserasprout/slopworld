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
    let (status, value) = response(app, cap, body).await;
    assert_eq!(status, StatusCode::OK);
    value
}

async fn response(app: &App, cap: &Cap, body: Option<Value>) -> (StatusCode, Value) {
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
    if reply.status() != StatusCode::OK {
        return (reply.status(), Value::Null);
    }
    let data = axum::body::to_bytes(reply.into_body(), usize::MAX)
        .await
        .unwrap();
    (
        StatusCode::OK,
        http_wire::decode_response(method, routes::BOARDS, &data).unwrap(),
    )
}

async fn board_app() -> App {
    let app = crate::application::test_application(crate::config::Config {
        projects: vec![
            crate::config::ProjectCfg {
                id: "0000000000000002".into(),
                name: "A".into(),
                temp: true,
                ..Default::default()
            },
            crate::config::ProjectCfg {
                id: "0000000000000001".into(),
                name: "Another".into(),
                temp: true,
                ..Default::default()
            },
        ],
        sessions: vec![
            crate::config::SessionCfg {
                name: "reviewer".into(),
                project: "Another".into(),
                ..Default::default()
            },
            crate::config::SessionCfg {
                name: "moderator".into(),
                project: "Another".into(),
                permissions: crate::config::AgentPermissions {
                    board_access: crate::config::BoardAccess::Admin,
                    visible_boards: ["0000000000000002".into()].into(),
                    ..Default::default()
                },
                ..Default::default()
            },
        ],
        ..Default::default()
    });
    for (board, name, prefix) in [
        ("0000000000000001", "Another", "Another"),
        ("0000000000000002", "A", "root"),
    ] {
        call(
            &app,
            &Cap::Root,
            Some(json!({"board":board,"action":"enable-board","text":name,"prefix":prefix})),
        )
        .await;
    }
    app
}

fn scoped(name: &str) -> Cap {
    Cap::Scoped(AgentCredential {
        grantor: name.into(),
        level: Level::Rw,
        sessions: Default::default(),
        revoked: Default::default(),
    })
}

#[tokio::test]
async fn agent_and_human_observe_one_ordered_card_with_its_discussion() {
    let app = board_app().await;
    let agent = scoped("reviewer");
    let edit = |action: &str| json!({"action":action,"board":"0000000000000001"});
    let mut add = edit("add-column");
    add["text"] = json!("Review");
    let catalog = call(&app, &Cap::Root, Some(add)).await;
    let columns = &catalog["boards"][1]["columns"];
    let first = &columns[0]["id"];
    let review = &columns[4]["id"];
    let mut add = edit("add-card");
    add["column"] = first.clone();
    add["text"] = json!("Check UI");
    let catalog = call(&app, &Cap::Root, Some(add)).await;
    let id = &catalog["boards"][1]["columns"][0]["cards"][0]["id"];
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
    assert_eq!(
        call(&app, &agent, None).await["boards"],
        json!([committed["boards"][1]])
    );
    let column = &committed["boards"][1]["columns"][0];
    assert_eq!(&column["id"], review);
    let card = &column["cards"][0];
    assert_eq!(&card["id"], id);
    assert_eq!(card["assignee"], "reviewer");
    assert_eq!(card["comments"][0]["author"], "reviewer");
    assert_eq!(card["links"][0]["target"], "project-boards");
    let other = &committed["boards"][0];
    let cross = json!({"action":"move-card","board":other["id"],"column":other["columns"][0]["id"],"card":card["key"]});
    assert_eq!(
        response(&app, &Cap::Root, Some(cross)).await.0,
        StatusCode::CONFLICT
    );
    assert_eq!(call(&app, &Cap::Root, None).await, committed);
    let original = &card["comments"][0];
    let mut revision = edit("edit-comment");
    revision["card"] = id.clone();
    revision["entry_id"] = original["id"].clone();
    revision["text"] = json!("Reviewed");
    assert_eq!(
        response(&app, &agent, Some(revision.clone())).await.0,
        StatusCode::FORBIDDEN
    );
    let revised = call(&app, &Cap::Root, Some(revision)).await;
    let mut expected = original.clone();
    expected["text"] = json!("Reviewed");
    assert_eq!(
        revised["boards"][1]["columns"][0]["cards"][0]["comments"],
        json!([expected])
    );
    let mut repeated = edit("title");
    repeated["card"] = card["key"].clone();
    repeated["text"] = json!("Updated");
    call(&app, &agent, Some(repeated.clone())).await;
    let snapshot = call(&app, &agent, Some(repeated)).await;
    let card = &snapshot["boards"][0]["columns"][0]["cards"][0];
    for (action, field) in [
        ("remove-comment", "comments"),
        ("remove-activity", "activity"),
    ] {
        let entries = card[field].as_array().unwrap();
        let target = entries.last().unwrap()["id"].as_i64().unwrap();
        let mut op = edit(action);
        op["card"] = card["key"].clone();
        op["text"] = json!(target.to_string());
        let before = call(&app, &agent, None).await;
        assert_eq!(
            response(&app, &agent, Some(op.clone())).await.0,
            StatusCode::FORBIDDEN
        );
        assert_eq!(call(&app, &agent, None).await, before);
        let moderator = scoped("moderator");
        let after = call(&app, &moderator, Some(op)).await;
        let retained = after["boards"][1]["columns"][0]["cards"][0][field]
            .as_array()
            .unwrap();
        assert_eq!(retained, &entries[..entries.len() - 1]);
    }
}

#[tokio::test]
async fn saved_board_and_attachment_permissions_apply_to_the_same_credential() {
    use crate::config::{AgentPermissions, transactions::ConfigMutation};
    let app = board_app().await;
    let cap = scoped("reviewer");
    let save = |permissions: AgentPermissions| {
        let app = app.clone();
        async move {
            app.sessions
                .configuration()
                .update_cfg(ConfigMutation::Agents, |cfg| {
                    let agent = cfg
                        .sessions
                        .iter_mut()
                        .find(|agent| agent.name == "reviewer")
                        .unwrap();
                    agent.permissions = permissions;
                    Ok(())
                })
                .await
                .unwrap()
        }
    };
    let catalog = call(&app, &cap, None).await;
    let column = &catalog["boards"][0]["columns"][0]["id"];
    let catalog = call(
        &app,
        &cap,
        Some(json!({"board":"0000000000000001","action":"add-card","column":column,"text":"Test"})),
    )
    .await;
    assert_eq!(
        catalog["boards"][0]["columns"][0]["cards"][0]["key"],
        "Another-1"
    );
    let link = json!({"board":"0000000000000001","card":"Another-1","action":"link","kind":"task","text":"assignment"});
    save(AgentPermissions {
        board_access: crate::config::BoardAccess::Read,
        ..Default::default()
    })
    .await;
    assert_eq!(
        response(&app, &cap, Some(link.clone())).await.0,
        StatusCode::FORBIDDEN
    );
    let read = call(&app, &cap, None).await;
    assert_eq!(read["boards"], catalog["boards"]);
    assert_eq!(read["can_write"], false);
    save(AgentPermissions::default()).await;
    call(&app, &cap, Some(link)).await;
    save(AgentPermissions {
        board_access: crate::config::BoardAccess::Off,
        ..Default::default()
    })
    .await;
    assert_eq!(response(&app, &cap, None).await.0, StatusCode::FORBIDDEN);
    assert_eq!(
        get_boards(
            State(app),
            Extension(cap),
            Query(BoardFilter {
                task: Some("assignment".into())
            })
        )
        .await
        .unwrap_err()
        .0,
        StatusCode::FORBIDDEN
    );
}

#[tokio::test]
async fn reverse_lookup_and_mutations_only_expose_selected_boards() {
    let app = board_app().await;
    let reviewer = scoped("reviewer");
    let moderator = scoped("moderator");
    let root = call(&app, &Cap::Root, None).await;
    for board in root["boards"].as_array().unwrap() {
        let id = board["id"].as_str().unwrap();
        let add = json!({"board":id,"action":"add-card","column":board["columns"][0]["id"],"text":"review"});
        call(&app, &Cap::Root, Some(add)).await;
        let link = json!({"board":id,"card":format!("{}-1",board["prefix"].as_str().unwrap()),"action":"link","kind":"task","text":"assignment"});
        call(&app, &Cap::Root, Some(link)).await;
    }
    let linked = |cap| {
        get_boards(
            State(app.clone()),
            Extension(cap),
            Query(BoardFilter {
                task: Some("assignment".into()),
            }),
        )
    };
    assert_eq!(linked(moderator.clone()).await.unwrap().0.boards.len(), 2);
    let projected = linked(reviewer.clone()).await.unwrap().0;
    assert_eq!(projected.boards.len(), 1);
    assert_eq!(projected.boards[0].id, "0000000000000001");
    let hidden =
        json!({"board":"0000000000000002","card":"root-1","action":"comment","text":"hidden"});
    assert_eq!(
        response(&app, &reviewer, Some(hidden)).await.0,
        StatusCode::FORBIDDEN
    );
    for action in [
        "set-prefix",
        "remove-card",
        "remove-column",
        "remove-comment",
        "remove-activity",
        "delete-archive",
    ] {
        let edit =
            json!({"board":"0000000000000001","card":"Another-1","action":action,"text":"1"});
        assert_eq!(
            response(&app, &reviewer, Some(edit)).await.0,
            StatusCode::FORBIDDEN
        );
    }
    let edit = json!({"board":"0000000000000002","card":"root-1","action":"remove-card"});
    call(&app, &moderator, Some(edit)).await;
    assert_eq!(linked(moderator).await.unwrap().0.boards.len(), 1);
}

#[tokio::test]
async fn disabling_board_hides_cards_until_project_enables_it_again() {
    let app = board_app().await;
    let id = "0000000000000001";
    let catalog = call(&app, &Cap::Root, None).await;
    let column = &catalog["boards"][1]["columns"][0]["id"];
    call(
        &app,
        &Cap::Root,
        Some(json!({"board":id,"action":"add-card","column":column,"text":"Keep"})),
    )
    .await;
    let disable = json!({"board":id,"action":"disable-board"});
    assert_eq!(
        response(&app, &scoped("moderator"), Some(disable.clone()))
            .await
            .0,
        StatusCode::FORBIDDEN
    );
    let hidden = call(&app, &Cap::Root, Some(disable)).await;
    assert_eq!(hidden["boards"].as_array().unwrap().len(), 1);
    let restored = call(
        &app,
        &Cap::Root,
        Some(json!({"board":id,"action":"enable-board","text":"Tasks","prefix":"T"})),
    )
    .await;
    assert_eq!(restored["boards"][1]["name"], "Tasks");
    assert_eq!(
        restored["boards"][1]["columns"][0]["cards"][0]["key"],
        "T-1"
    );
}
