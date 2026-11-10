use super::*;
use crate::tests::http_fixture::serve;

#[test]
fn create_posts_explicit_values_and_empty_defaults_to_the_selected_project() {
    for supplied in [false, true] {
        let (endpoint, server) = serve(
            "200 OK",
            r#"{"id":"tree","name":"feature","branch":"topic","path":"/tmp/tree"}"#,
        );
        let result = run(
            &endpoint,
            "agent",
            supplied,
            WorktreeArgs::Create {
                project: "my/repo & café".into(),
                name: supplied.then(|| "feature name".into()),
                base: supplied.then(|| "origin/main".into()),
                path: supplied.then(|| "/tmp/existing checkout".into()),
            },
        );
        let request = server.join().unwrap().unwrap();
        result.unwrap();
        assert!(
            request.starts_with(
                "POST /api/worktrees?project=my%2Frepo%20%26%20caf%C3%A9 HTTP/1.1\r\n"
            ),
            "{request}"
        );
        let headers = request.to_lowercase();
        assert!(headers.contains("x-slop-session: agent\r\n"));
        assert!(headers.contains("x-slop-token: secret\r\n"));
        let body: Value = serde_json::from_str(request.split_once("\r\n\r\n").unwrap().1).unwrap();
        assert_eq!(
            body,
            json!({"project": "my/repo & café",
            "name": if supplied { "feature name" } else { "" },
            "base": if supplied { "origin/main" } else { "" },
            "path": if supplied { "/tmp/existing checkout" } else { "" }})
        );
    }
}

#[test]
fn human_rows_include_checkout_errors_and_attached_sessions() {
    let row = json!({"id": "tree", "name": "feature", "branch": "topic", "path": "/tmp/tree",
        "error": "checkout missing", "attachments": ["coder", false, "reviewer"]});
    let expected =
        "tree  feature  topic\n  /tmp/tree\n  checkout missing\n  attached: coder, reviewer\n";
    assert_eq!(render_worktrees(&row, false), expected);
    assert_eq!(
        render_worktrees(
            &json!({"worktrees": [row, {"id": "bare", "error": "", "attachments": []}]}),
            false
        ),
        format!("{expected}bare    \n  \n")
    );
    assert_eq!(render_worktrees(&json!({"worktrees": []}), false), "");
    assert_eq!(
        render_worktrees(&json!({}), true),
        "Worktree removed. Branches retained.\n"
    );
}

#[test]
fn list_and_remove_use_project_scoped_routes_without_request_bodies() {
    for removed in [false, true] {
        let (endpoint, server) = serve(
            "200 OK",
            if removed {
                r#"{"ok":true}"#
            } else {
                r#"{"worktrees":[]}"#
            },
        );
        let args = if removed {
            WorktreeArgs::Remove {
                project: "my/repo".into(),
                id: "tree/one".into(),
            }
        } else {
            WorktreeArgs::List {
                project: "my/repo".into(),
            }
        };
        let result = run(&endpoint, "host", false, args);
        let request = server.join().unwrap().unwrap();
        result.unwrap();
        let route = if removed {
            "DELETE /api/worktrees/tree%2Fone"
        } else {
            "GET /api/worktrees"
        };
        assert!(
            request.starts_with(&format!("{route}?project=my%2Frepo HTTP/1.1\r\n")),
            "{request}"
        );
        assert_eq!(request.split_once("\r\n\r\n").unwrap().1, "");
    }
}

#[test]
fn create_preserves_daemon_refusal_instead_of_printing_success() {
    let (endpoint, server) = serve(
        "403 Forbidden",
        r#"{"error":"worktree permission required"}"#,
    );
    let result = run(
        &endpoint,
        "agent",
        false,
        WorktreeArgs::Create {
            project: "repo".into(),
            name: None,
            base: None,
            path: None,
        },
    );
    server.join().unwrap().unwrap();
    assert_eq!(result.unwrap_err(), "worktree permission required");
}
