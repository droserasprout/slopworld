use super::*;
use crate::config::Config;
use axum::http::StatusCode;

fn proto<T: serde::de::DeserializeOwned>(value: serde_json::Value) -> Proto<T> {
    Proto(serde_json::from_value(value).unwrap())
}

#[tokio::test]
async fn invalid_explicit_runs_and_file_actions_return_bad_request() {
    let manager = crate::session::test_manager(Config::default());
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
        let (status, Proto(error)) = run(State(manager.clone()), proto(request))
            .await
            .unwrap_err();
        assert_eq!(status, StatusCode::BAD_REQUEST);
        assert!(error.error.contains(message), "{}", error.error);
    }
    assert_eq!(
        file_action(
            State(manager),
            proto(json!({"project":"missing","path":"file","command":"cat"}))
        )
        .await
        .unwrap_err()
        .0,
        StatusCode::BAD_REQUEST
    );
}
