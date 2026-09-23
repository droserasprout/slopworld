use super::*;
use crate::config::Config;
use axum::http::StatusCode;

fn proto<T: serde::de::DeserializeOwned>(value: serde_json::Value) -> Proto<T> {
    Proto(serde_json::from_value(value).unwrap())
}

#[tokio::test]
async fn library_crud_preserves_content_and_rejects_invalid_updates() {
    let m = crate::session::test_manager(Config::default());
    let item =
        json!({"name":"test-note", "kind":"breadcrumb", "text":"remember this", "builtin":true});
    assert!(
        create_library_item(State(m.clone()), proto(item.clone()))
            .await
            .unwrap()
            .0
            .ok
    );
    assert_eq!(
        create_library_item(State(m.clone()), proto(item))
            .await
            .unwrap_err()
            .0,
        StatusCode::BAD_REQUEST
    );
    assert_eq!(
        update_library_item(
            State(m.clone()),
            Path("test-note".into()),
            proto(json!({"name":"test-note","kind":"breadcrumb","text":""}))
        )
        .await
        .unwrap_err()
        .0,
        StatusCode::BAD_REQUEST
    );
    let listed = list_library(State(m.clone())).await.unwrap().0;
    let item = listed
        .library
        .iter()
        .find(|item| item.name.as_deref() == Some("test-note"))
        .unwrap();
    assert_eq!(item.text.as_deref(), Some("remember this"));
    assert!(!item.builtin.unwrap_or(false));
    assert!(
        update_library_item(
            State(m.clone()),
            Path("test-note".into()),
            proto(json!({"name":"renamed-note","kind":"breadcrumb","text":"updated"}))
        )
        .await
        .unwrap()
        .0
        .ok
    );
    let listed = list_library(State(m.clone())).await.unwrap().0;
    assert!(!listed
        .library
        .iter()
        .any(|item| item.name.as_deref() == Some("test-note")));
    assert_eq!(
        listed
            .library
            .iter()
            .find(|item| item.name.as_deref() == Some("renamed-note"))
            .unwrap()
            .text
            .as_deref(),
        Some("updated")
    );
    assert!(
        destroy_library_item(State(m.clone()), Path("renamed-note".into()))
            .await
            .unwrap()
            .0
            .ok
    );
    assert_eq!(
        destroy_library_item(State(m.clone()), Path("renamed-note".into()))
            .await
            .unwrap_err()
            .0,
        StatusCode::BAD_REQUEST
    );
    assert!(m.config().await.library.is_empty());
}

#[tokio::test]
async fn project_crud_returns_not_found_and_persists_rename() {
    let m = crate::session::test_manager(Config::default());
    let project = json!({"name":"test-project", "dir":std::env::temp_dir().to_str().unwrap()});
    assert!(
        create_project(State(m.clone()), proto(project.clone()))
            .await
            .unwrap()
            .0
            .ok
    );
    assert_eq!(
        create_project(State(m.clone()), proto(project.clone()))
            .await
            .unwrap_err()
            .0,
        StatusCode::BAD_REQUEST
    );
    assert_eq!(
        one_project(State(m.clone()), Path("test-project".into()))
            .await
            .unwrap()
            .0
            .name
            .as_deref(),
        Some("test-project")
    );
    let mut renamed = project;
    renamed["name"] = json!("renamed-project");
    assert!(
        update_project(
            State(m.clone()),
            Path("test-project".into()),
            proto(renamed)
        )
        .await
        .unwrap()
        .0
        .ok
    );
    assert_eq!(
        one_project(State(m.clone()), Path("test-project".into()))
            .await
            .unwrap_err()
            .0,
        StatusCode::NOT_FOUND
    );
    assert_eq!(
        list_projects(State(m.clone()))
            .await
            .unwrap()
            .0
            .projects
            .len(),
        1
    );
    assert!(
        destroy_project(State(m.clone()), Path("renamed-project".into()))
            .await
            .unwrap()
            .0
            .ok
    );
    assert!(list_projects(State(m)).await.unwrap().0.projects.is_empty());
}

#[tokio::test]
async fn invalid_errands_and_file_actions_return_bad_request() {
    let m = crate::session::test_manager(Config::default());
    for (request, message) in [
        (
            json!({"kind":"prompt","command":" ","temp":true}),
            "Provide a command",
        ),
        (
            json!({"kind":"shell","command":"echo test"}),
            "Choose a project",
        ),
        (
            json!({"kind":"shell","project":"missing","path":"file","command":"cat"}),
            "does not exist",
        ),
    ] {
        let (status, Proto(error)) = run(State(m.clone()), proto(request)).await.unwrap_err();
        assert_eq!(status, StatusCode::BAD_REQUEST);
        assert!(error.error.contains(message), "{}", error.error);
    }
    for want in [None, Some(proto(json!({"temp":true})))] {
        assert_eq!(
            run_library_item(State(m.clone()), Path("missing".into()), want)
                .await
                .unwrap_err()
                .0,
            StatusCode::BAD_REQUEST
        );
    }
    assert_eq!(
        file_action(
            State(m),
            proto(json!({"project":"missing","path":"file","command":"cat"}))
        )
        .await
        .unwrap_err()
        .0,
        StatusCode::BAD_REQUEST
    );
}

#[tokio::test]
async fn preview_only_allocates_a_directory_for_temporary_projects() {
    for temp in [false, true] {
        let preview = project_preview(proto(json!({"name":"preview-project","temp":temp})))
            .await
            .unwrap()
            .0;
        assert_eq!(preview.name, "preview-project");
        assert_eq!(preview.temp, temp);
        assert_eq!(
            preview.dir,
            if temp {
                crate::config::temp_dir("preview-project")
            } else {
                String::new()
            }
        );
    }
}
