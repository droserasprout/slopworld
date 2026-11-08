use super::*;

#[test]
fn sandbox_arguments_escape_controls_without_merging_argument_boundaries() {
    assert_eq!(shell_quote("simple/path"), "simple/path");
    assert_eq!(shell_quote(""), "''");
    assert_eq!(shell_quote("two words"), "'two words'");
    assert_eq!(shell_quote("a'b"), "'a'\\''b'");
    let raw = "mount\nENV=value\r\t\x1b[31m\u{85}";
    let displayed = shell_quote(raw);
    assert_eq!(displayed, "'mount\\nENV=value\\r\\t\\u{1b}[31m\\u{85}'");
    assert!(!displayed.chars().any(char::is_control));
    // Display escaping never mutates the values offered to JSON output.
    let value = json!({"plan": {"mounts": [raw]}});
    assert_eq!(value["plan"]["mounts"][0], raw);
}

#[test]
fn peers_sort_deduplicate_include_host_and_mark_only_the_caller() {
    let peers = json!({"sessions": [
        {"name": "zed"}, {"name": "agent"}, {"name": "host"}, {"name": "agent"}, {}, {"name": 12}
    ]});
    assert_eq!(render_peers(&peers, "agent"), "agent (you)\nhost\nzed\n");
    assert_eq!(render_peers(&json!({}), "host"), "host (you)\n");
    assert_eq!(render_peers(&json!({"sessions": []}), "absent"), "host\n");
}

#[test]
fn peers_request_caller_visible_sessions_in_both_output_modes_and_preserve_refusals() {
    use crate::tests::http_fixture::serve;
    for json in [false, true] {
        let (endpoint, server) = serve("200 OK", r#"{"sessions":[{"name":"agent"}]}"#);
        let result = run_peers(&endpoint, "agent", json);
        let request = server.join().unwrap().unwrap();
        result.unwrap();
        assert!(request.starts_with("GET /api/sessions HTTP/1.1\r\n"));
        assert!(request.to_lowercase().contains("x-slop-session: agent\r\n"));
    }
    let (endpoint, server) = serve("403 Forbidden", r#"{"error":"session denied"}"#);
    let result = run_peers(&endpoint, "agent", false);
    server.join().unwrap().unwrap();
    assert_eq!(result.unwrap_err(), "session denied");
}

#[test]
fn status_requests_health_before_tasks_and_keeps_failures_as_diagnostics() {
    use crate::tests::http_fixture::serve_sequence;
    for json in [false, true] {
        for (health_ok, tasks_ok) in [(true, true), (true, false), (false, false)] {
            let responses = if health_ok {
                vec![
                    ("200 OK", json!({"version": "1.7"}).to_string()),
                    if tasks_ok {
                        ("200 OK", json!({"tasks": []}).to_string())
                    } else {
                        (
                            "403 Forbidden",
                            json!({"error": "tasks denied"}).to_string(),
                        )
                    },
                ]
            } else {
                vec![(
                    "503 Unavailable",
                    json!({"error": "daemon offline"}).to_string(),
                )]
            };
            let (endpoint, server) = serve_sequence(responses);
            let result = run_status(&endpoint, "agent", json);
            let requests = server.join().unwrap().unwrap();
            result.unwrap();
            assert!(requests.starts_with("GET /api/health HTTP/1.1\r\n"));
            assert_eq!(requests.contains("GET /api/tasks HTTP/1.1\r\n"), health_ok);
            assert_eq!(
                requests
                    .to_lowercase()
                    .matches("x-slop-session: agent\r\n")
                    .count(),
                if health_ok { 2 } else { 1 }
            );
        }
    }
}

#[test]
fn sandbox_host_stops_before_launch_and_live_sections() {
    assert_eq!(
        render_sandbox(&json!({"session": "host", "host": true,
        "plan": {"command": ["hidden"]}, "live": {"pane_pid": 99}})),
        "session   host\nsandbox   not applicable (host terminal)\n"
    );
}

#[test]
fn sandbox_without_saved_plan_displays_unknown_live_defaults() {
    for value in [json!({}), json!({"plan": null})] {
        assert_eq!(
            render_sandbox(&value),
            "session   ?\nplan      no saved sandbox launch\nlive      unknown\ncompare   unknown\n"
        );
    }
}

#[test]
fn sandbox_plan_and_processes_keep_argument_boundaries_and_escape_control_text() {
    assert_eq!(
        render_sandbox(&json!({
            "session": "agent", "plan": {"limits": [], "pasta": ["--dns", "two words"],
                "bwrap": [null], "environment": ["A=line\nnext"], "mounts": [], "command": ["a'b", ""]},
            "live": {"status": "running", "pane_pid": 42,
                "processes": [{"pid": 43, "ppid": 42, "argv": ["tool", "two words", false]}, {}]},
            "comparison": "matches"
        })),
        "session   agent\nlimits:\n  (none)\npasta:\n  --dns\n  'two words'\nbwrap:\n  '<arg>'\nenvironment:\n  'A=line\\nnext'\nmounts:\n  (none)\ncommand:\n  'a'\\''b'\n  ''\nlive      running\npane pid  42\ncompare   matches\nprocess   43 (parent 42)\n  tool\n  'two words'\n  '<arg>'\nprocess   0 (parent 0)\n"
    );
    assert_eq!(
        render_sandbox(&json!({"session": "agent", "plan": {}})),
        "session   agent\nlimits:\npasta:\nbwrap:\nenvironment:\nmounts:\ncommand:\nlive      unknown\ncompare   unknown\n"
    );
}

#[test]
fn human_sandbox_inspection_fetches_encoded_session_and_propagates_refusal() {
    use crate::tests::http_fixture::serve;
    for success in [false, true] {
        let (endpoint, server) = if success {
            serve("200 OK", r#"{"session":"host","host":true}"#)
        } else {
            serve("403 Forbidden", r#"{"error":"inspection denied"}"#)
        };
        let result = run_sandbox_inspect(&endpoint, "host", false, "a/b?c");
        let request = server.join().unwrap().unwrap();
        assert!(request.starts_with("GET /api/sessions/a%2Fb%3Fc/sandbox HTTP/1.1\r\n"));
        if success {
            result.unwrap();
        } else {
            assert_eq!(result.unwrap_err(), "inspection denied");
        }
    }
}
