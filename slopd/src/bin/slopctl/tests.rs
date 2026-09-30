use super::USAGE;
use super::take_json_flag;
use crate::commands::{
    AGENT_CREATE_USAGE, AGENT_USAGE, Command, InboxFilter, PEERS_USAGE, SANDBOX_INSPECT_USAGE,
    SANDBOX_USAGE, SPAWN_USAGE, STATUS_USAGE, SpawnArgs, TASK_LIST_USAGE, TASK_SHOW_USAGE,
    TASK_USAGE, TEMPLATE_SHOW_USAGE, TEMPLATE_USAGE, TEMPLATES_USAGE, UpdateAction, WORKER_USAGE,
    command_help, parse_command, parse_command_with_task_id, run_agent_create, run_spawn,
    task_is_terminal, wait_for_task,
};
use crate::http::{Endpoint, request};
use crate::logs::{
    DEFAULT_LOG_LINES, LOGS_USAGE, LogSelection, LogSource, LogsOptions, clean_log_line,
    expand_home, parse_logs_args, source_command, write_log_line,
};
use serde_json::{Value, json};
use std::path::PathBuf;
use std::time::Duration;

fn words(text: &str) -> Vec<String> {
    text.split_whitespace().map(str::to_string).collect()
}

#[path = "http_fixture.rs"]
mod http_fixture;
use http_fixture::{serve, serve_raw};

#[test]
fn command_parser_builds_delegation_and_update_commands() {
    assert_eq!(
        parse_command(&words("task delegate agent fix the pane")),
        Ok(Command::Delegate {
            to: "agent".to_string(),
            body: "fix the pane".to_string(),
        })
    );
    assert_eq!(
        parse_command(&words("task finish task-7 shipped safely")),
        Ok(Command::Update {
            action: UpdateAction::Finish,
            id: "task-7".to_string(),
            note: Some("shipped safely".to_string()),
        })
    );
    assert_eq!(
        parse_command(&words("task wait task-7")),
        Ok(Command::Wait {
            id: "task-7".to_string(),
        })
    );
    assert_eq!(
        parse_command(&words(
            "worker spawn --durable --project repo --template codex inspect the build"
        )),
        Ok(Command::Spawn {
            worktree: Default::default(),
            project: "repo".to_string(),
            template: "codex".to_string(),
            durable: true,
            body: "inspect the build".to_string(),
        })
    );
    assert_eq!(
        parse_command(&words(
            "worker spawn --project repo --template codex run the checks"
        )),
        Ok(Command::Spawn {
            worktree: Default::default(),
            project: "repo".to_string(),
            template: "codex".to_string(),
            durable: true,
            body: "run the checks".to_string(),
        })
    );
    assert_eq!(
        parse_command(&words("sandbox inspect agent")),
        Ok(Command::SandboxInspect {
            name: "agent".into(),
        })
    );
    assert_eq!(
        parse_command(&words(
            "agent create worker --project repo --template team-review --start"
        )),
        Ok(Command::AgentCreate {
            name: "worker".into(),
            project: "repo".into(),
            template: "team-review".into(),
            start: true,
        })
    );
    assert_eq!(
        parse_command(&words("template show team-review")),
        Ok(Command::TemplateShow {
            name: "team-review".into(),
            project: None,
        })
    );
    assert_eq!(
        parse_command(&words("template show team-review --project repo")),
        Ok(Command::TemplateShow {
            name: "team-review".into(),
            project: Some("repo".into()),
        })
    );
    assert_eq!(
        parse_command(&words("template list")),
        Ok(Command::Templates { project: None })
    );
    assert_eq!(
        parse_command(&words("template list --project repo")),
        Ok(Command::Templates {
            project: Some("repo".into())
        })
    );
}

#[test]
fn compatibility_command_aliases_are_rejected() {
    for command in [
        "delegate agent fix the pane",
        "spawn --project repo --template codex inspect the build",
        "wait task-7",
        "accept task-7 accepted",
        "progress task-7 working",
        "finish task-7 done",
        "fail task-7 failed",
        "rm task-7",
        "prune --all",
        "inbox --all",
        "templates",
    ] {
        assert!(
            parse_command(&words(command)).is_err(),
            "accepted alias: {command}"
        );
    }
    parse_command(&words("worker --project repo --template codex task")).unwrap_err();
    parse_command(&words("task task-7")).unwrap_err();
}

#[test]
fn task_commands_require_explicit_subcommands() {
    assert_eq!(
        parse_command(&words("task wait")).unwrap_err(),
        "task wait needs a task id"
    );
    assert_eq!(
        parse_command(&words("task show task-7")),
        Ok(Command::Task {
            id: "task-7".into()
        })
    );
    parse_command(&words("task task-7")).unwrap_err();
}

#[test]
fn task_commands_use_the_worker_task_id_when_id_is_omitted() {
    let task_id = Some("task-7");
    assert_eq!(
        parse_command_with_task_id(&words("task show"), task_id),
        Ok(Command::Task {
            id: "task-7".into()
        })
    );
    assert_eq!(
        parse_command_with_task_id(&words("task wait"), task_id),
        Ok(Command::Wait {
            id: "task-7".into()
        })
    );
    for (command, action) in [
        ("task accept", UpdateAction::Accept),
        ("task progress", UpdateAction::Progress),
        ("task finish", UpdateAction::Finish),
        ("task fail", UpdateAction::Fail),
    ] {
        assert_eq!(
            parse_command_with_task_id(&words(command), task_id),
            Ok(Command::Update {
                action,
                id: "task-7".into(),
                note: None,
            })
        );
    }
    assert_eq!(
        parse_command_with_task_id(&words("task remove"), task_id),
        Ok(Command::Remove {
            id: "task-7".into()
        })
    );
    assert_eq!(
        parse_command_with_task_id(&words("task show other-task"), task_id),
        Ok(Command::Task {
            id: "other-task".into()
        })
    );
}

#[test]
fn command_tree_has_group_and_leaf_help() {
    assert_eq!(
        parse_command(&words("task --help")),
        Ok(Command::Help { usage: TASK_USAGE })
    );
    assert_eq!(
        parse_command(&words("task list --help")),
        Ok(Command::Help {
            usage: TASK_LIST_USAGE
        })
    );
    assert_eq!(
        parse_command(&words("task show --help")),
        Ok(Command::Help {
            usage: TASK_SHOW_USAGE
        })
    );
    assert_eq!(
        parse_command(&words("worker --help")),
        Ok(Command::Help {
            usage: WORKER_USAGE
        })
    );
    assert_eq!(
        parse_command(&words("worker spawn --help")),
        Ok(Command::Help { usage: SPAWN_USAGE })
    );
    assert_eq!(
        parse_command(&words("template --help")),
        Ok(Command::Help {
            usage: TEMPLATE_USAGE
        })
    );
    assert_eq!(
        parse_command(&words("template list --help")),
        Ok(Command::Help {
            usage: TEMPLATES_USAGE
        })
    );
    assert_eq!(
        parse_command(&words("template show --help")),
        Ok(Command::Help {
            usage: TEMPLATE_SHOW_USAGE
        })
    );
    assert_eq!(
        parse_command(&words("agent create --help")),
        Ok(Command::Help {
            usage: AGENT_CREATE_USAGE
        })
    );
    assert_eq!(
        parse_command(&words("sandbox inspect --help")),
        Ok(Command::Help {
            usage: SANDBOX_INSPECT_USAGE
        })
    );
}

#[test]
fn task_text_preserves_help_words_and_flags() {
    for text in ["help", "please help me", "--help", "-h"] {
        let mut args = words("task delegate agent");
        args.extend(words(text));
        assert_eq!(
            parse_command(&args),
            Ok(Command::Delegate {
                to: "agent".into(),
                body: text.into(),
            })
        );
        for (command, action) in [
            ("accept", UpdateAction::Accept),
            ("progress", UpdateAction::Progress),
            ("finish", UpdateAction::Finish),
            ("fail", UpdateAction::Fail),
        ] {
            let mut args = words(&format!("task {command} task-7"));
            args.extend(words(text));
            assert_eq!(
                parse_command(&args),
                Ok(Command::Update {
                    action,
                    id: "task-7".into(),
                    note: Some(text.into()),
                })
            );
        }
    }
}

#[test]
fn spawn_preserves_task_text_after_template_options() {
    for durable in [false, true] {
        for text in ["- investigate the failure", "help", "--help", "-h"] {
            // Check both a quoted body and a body spread over several arguments.
            for body_args in [vec![text.to_string()], words(text)] {
                let mut args = words("worker spawn");
                if durable {
                    args.push("--durable".into());
                } else {
                    args.push("--one-shot".into());
                }
                args.push("--project".into());
                args.push("repo".into());
                args.push("--template".into());
                args.push("codex".into());
                args.extend(body_args);
                assert_eq!(
                    parse_command(&args),
                    Ok(Command::Spawn {
                        worktree: Default::default(),
                        project: "repo".into(),
                        template: "codex".into(),
                        durable,
                        body: text.into(),
                    })
                );
            }
        }
    }
}

#[test]
fn spawn_delimiter_preserves_options_as_literal_task_text() {
    for durable in [false, true] {
        for json in [false, true] {
            for body in [
                "--durable",
                "--template other --project elsewhere",
                "--json",
                "--",
            ] {
                let mut args = words(&format!(
                    "worker spawn --project repo --template review {} {} -- {body}",
                    if durable { "--durable" } else { "--one-shot" },
                    if json { "--json" } else { "" },
                ));
                assert_eq!(take_json_flag(&mut args), json);
                assert_eq!(
                    parse_command(&args),
                    Ok(Command::Spawn {
                        worktree: Default::default(),
                        project: "repo".into(),
                        template: "review".into(),
                        durable,
                        body: body.into(),
                    })
                );
            }
        }
    }
    parse_command(&words("worker spawn --project repo --template review --")).unwrap_err();
}

#[test]
fn every_command_has_nested_help() {
    let commands = [
        ("worker", WORKER_USAGE),
        ("template", TEMPLATE_USAGE),
        ("agent", AGENT_USAGE),
        ("task", TASK_USAGE),
        ("peers", PEERS_USAGE),
        ("status", STATUS_USAGE),
        ("sandbox", SANDBOX_USAGE),
        ("logs", LOGS_USAGE),
    ];

    for (command, usage) in commands {
        assert_eq!(command_help(command), Some(usage));
        for flag in ["-h", "--help", "help"] {
            assert_eq!(
                parse_command(&words(&format!("{command} {flag}"))),
                Ok(Command::Help { usage })
            );
        }
    }
}

#[test]
fn spawn_posts_template_and_task_body_to_worker_endpoint() {
    let (endpoint, server) = serve(
        "200 OK",
        r#"{"task":{"id":"task-7"},"worker":{"name":"child"}}"#,
    );
    run_spawn(
        &endpoint,
        "host",
        true,
        SpawnArgs {
            worktree: &Default::default(),
            project: "repo",
            template: "codex",
            durable: true,
            body: "inspect the build",
        },
    )
    .unwrap();
    let request = server.join().unwrap().unwrap();
    assert!(request.starts_with("POST /api/workers HTTP/1.1\r\n"));
    let body = request.split("\r\n\r\n").nth(1).unwrap();
    let body: Value = serde_json::from_str(body).unwrap();
    assert_eq!(body["project"], "repo");
    assert_eq!(body["template"], "codex");
    assert_eq!(body["durable"], true);
    assert_eq!(body["body"], "inspect the build");
}

#[test]
fn agent_create_posts_template_project_and_start_to_catalog_endpoint() {
    let (endpoint, server) = serve("200 OK", r#"{"ok":true,"session":"worker"}"#);
    run_agent_create(
        &endpoint,
        "host",
        true,
        "worker",
        "repo",
        "team-review",
        true,
    )
    .unwrap();
    let request = server.join().unwrap().unwrap();
    assert!(request.starts_with("POST /api/templates/team-review/create HTTP/1.1\r\n"));
    let body = request.split("\r\n\r\n").nth(1).unwrap();
    let body: Value = serde_json::from_str(body).unwrap();
    assert_eq!(body["name"], "worker");
    assert_eq!(body["project"], "repo");
    assert_eq!(body["start"], true);
}

#[test]
fn command_parser_validates_fixed_arity_and_flags() {
    assert_eq!(
        parse_command(&words("peers now")).unwrap_err(),
        "unexpected argument: now\n\n".to_string() + USAGE
    );
    parse_command(&words("task")).unwrap_err();
    parse_command(&words("task prune --wat")).unwrap_err();
    parse_command(&words("task list --status")).unwrap_err();
    parse_command(&words("worker spawn parent task")).unwrap_err();
    parse_command(&words("template list --wat")).unwrap_err();
    parse_command(&words("task wait")).unwrap_err();
    parse_command(&words("task wait task-7 extra")).unwrap_err();
}

#[test]
fn global_json_flag_is_removed_before_command_parsing() {
    let mut args = words("--json task list --all --json");
    assert!(take_json_flag(&mut args));
    assert_eq!(
        parse_command(&args),
        Ok(Command::Inbox {
            filter: InboxFilter {
                all: true,
                sent: false,
                received: false,
                status: None,
            },
        })
    );
}

#[test]
fn logs_defaults_to_both_and_a_bounded_tail() {
    assert_eq!(
        parse_logs_args(&[]).unwrap(),
        LogsOptions {
            selection: LogSelection::All,
            lines: DEFAULT_LOG_LINES,
            follow: false,
        }
    );
}

#[test]
fn logs_accepts_source_flags_and_follow() {
    assert_eq!(
        parse_logs_args(&words("--daemon -f --lines=37")).unwrap(),
        LogsOptions {
            selection: LogSelection::One(LogSource::Daemon),
            lines: 37,
            follow: true,
        }
    );
}

#[test]
fn logs_accepts_positional_source() {
    assert_eq!(
        parse_logs_args(&words("game -n 9")).unwrap(),
        LogsOptions {
            selection: LogSelection::One(LogSource::Game),
            lines: 9,
            follow: false,
        }
    );
}

#[test]
fn logs_rejects_duplicate_source_and_invalid_line_count() {
    parse_logs_args(&words("game --daemon")).unwrap_err();
    parse_logs_args(&words("--lines 0")).unwrap_err();
    parse_logs_args(&words("--lines 100001")).unwrap_err();
}

#[test]
fn inbox_filters_direction_status_and_newest_first() {
    let inbox = json!({
        "tasks": [
            { "id": "sent", "from": "me", "to": "agent", "status": "working", "created_ms": 1 },
            { "id": "done", "from": "me", "to": "agent", "status": "done", "created_ms": 2 },
            { "id": "received", "from": "agent", "to": "me", "status": "queued", "created_ms": 3 },
            { "id": "other", "from": "a", "to": "b", "status": "queued", "created_ms": 4 }
        ]
    });
    let ids = |filter: InboxFilter| {
        filter
            .apply(&inbox, "me")
            .into_iter()
            .map(|task| task["id"].as_str().unwrap())
            .collect::<Vec<_>>()
    };

    assert_eq!(ids(InboxFilter::default()), ["other", "received", "sent"]);
    assert_eq!(ids(InboxFilter::parse(&words("--sent")).unwrap()), ["sent"]);
    assert_eq!(
        ids(InboxFilter::parse(&words("--received")).unwrap()),
        ["received"]
    );
    assert_eq!(
        ids(InboxFilter::parse(&words("--all --sent --received")).unwrap()),
        ["other", "received", "done", "sent"]
    );
    assert_eq!(
        ids(InboxFilter::parse(&words("--status done")).unwrap()),
        ["done"]
    );
    InboxFilter::parse(&words("--unknown")).unwrap_err();
}

#[test]
fn task_completion_accepts_only_known_terminal_states() {
    for status in ["queued", "accepted", "working"] {
        assert!(
            !task_is_terminal(&json!({
                "task": { "status": status }
            }))
            .unwrap()
        );
    }
    for status in ["done", "failed", "canceled"] {
        assert!(
            task_is_terminal(&json!({
                "task": { "status": status }
            }))
            .unwrap()
        );
    }
    assert_eq!(
        task_is_terminal(&json!({ "task": { "status": "stalled" } })).unwrap_err(),
        "unknown task status: stalled"
    );
    assert_eq!(
        task_is_terminal(&json!({})).unwrap_err(),
        "response is missing task"
    );
}

#[test]
fn wait_returns_an_already_completed_task() {
    let (endpoint, server) = serve("200 OK", r#"{"task":{"id":"task-7","status":"done"}}"#);
    let task = wait_for_task(&endpoint, "caller", "task-7", Duration::ZERO).unwrap();
    assert_eq!(task["task"]["id"], "task-7");
    assert_eq!(task["task"]["status"], "done");
    let request = server.join().unwrap().unwrap();
    assert!(request.starts_with("GET /api/tasks/task-7 HTTP/1.1\r\n"));
}

#[test]
fn peer_names_are_sorted_unique_and_always_include_the_host() {
    let sessions = json!({
        "sessions": [
            { "name": "zeta" },
            { "name": "host" },
            { "name": "alpha" },
            { "name": "alpha" },
            { "not_name": "ignored" }
        ]
    });
    assert_eq!(
        crate::commands::peer_names(&sessions),
        ["alpha", "host", "zeta"]
    );
    assert_eq!(crate::commands::peer_names(&json!({})), ["host"]);
}

#[test]
fn log_lines_are_cleaned_and_rendered_in_all_output_modes() {
    assert_eq!(clean_log_line("line\r\n".into()), "line");
    assert_eq!(clean_log_line("line\n".into()), "line");
    assert_eq!(clean_log_line("line".into()), "line");

    let mut output = Vec::new();
    write_log_line(&mut output, LogSource::Game, "plain", false, false).unwrap();
    write_log_line(&mut output, LogSource::Daemon, "prefixed", true, false).unwrap();
    write_log_line(&mut output, LogSource::Game, "structured", false, true).unwrap();
    let output = String::from_utf8(output).unwrap();
    let lines = output.lines().collect::<Vec<_>>();

    assert_eq!(lines[0], "plain");
    assert_eq!(lines[1], "[daemon] prefixed");
    assert_eq!(
        serde_json::from_str::<Value>(lines[2]).unwrap(),
        json!({ "source": "game", "line": "structured" })
    );
}

#[test]
fn log_commands_select_the_expected_local_tools() {
    let options = LogsOptions {
        selection: LogSelection::All,
        lines: 17,
        follow: true,
    };
    let game = source_command(LogSource::Game, options).unwrap();
    let game_args = game
        .get_args()
        .map(|arg| arg.to_string_lossy().into_owned())
        .collect::<Vec<_>>();
    assert_eq!(game.get_program(), "tail");
    assert!(game_args.windows(2).any(|args| args == ["--lines", "17"]));
    assert!(game_args.iter().any(|arg| arg == "--follow=name"));

    let daemon = source_command(LogSource::Daemon, options).unwrap();
    let daemon_args = daemon
        .get_args()
        .map(|arg| arg.to_string_lossy().into_owned())
        .collect::<Vec<_>>();
    assert_eq!(daemon.get_program(), "journalctl");
    assert!(daemon_args.windows(2).any(|args| args == ["--lines", "17"]));
    assert!(daemon_args.iter().any(|arg| arg == "--follow"));

    assert_eq!(expand_home("/tmp/log").unwrap(), PathBuf::from("/tmp/log"));
    assert_eq!(expand_home("~").unwrap(), dirs::home_dir().unwrap());
}

#[test]
fn request_sends_headers_and_supports_each_used_method() {
    for (method, body) in [
        ("GET", None),
        ("DELETE", None),
        ("POST", Some(json!({ "to": "host", "body": "check" }))),
    ] {
        let path = if method == "GET" {
            "/api/health"
        } else if method == "POST" {
            "/api/tasks"
        } else {
            "/api/sessions/test"
        };
        let (endpoint, server) = serve(
            "200 OK",
            if method == "POST" {
                r#"{"task":{"id":"check"}}"#
            } else {
                r#"{"ok":true}"#
            },
        );
        let value = request(&endpoint, "caller", method, path, body).unwrap();
        if method == "POST" {
            assert_eq!(value["task"]["id"], "check");
        } else {
            assert_eq!(value["ok"], true);
        }
        let received = server.join().unwrap().unwrap();
        assert!(received.starts_with(&format!("{method} {path} HTTP/1.1\r\n")));
        let lower = received.to_ascii_lowercase();
        assert!(lower.contains("x-slop-token: secret\r\n"));
        assert!(lower.contains("x-slop-session: caller\r\n"));
        if method == "POST" {
            let (_, body) = received.split_once("\r\n\r\n").unwrap();
            assert_eq!(
                serde_json::from_str::<Value>(body).unwrap(),
                json!({ "to": "host", "body": "check" })
            );
        }
    }
    assert_eq!(
        request(
            &Endpoint {
                url: String::new(),
                token: String::new(),
            },
            "caller",
            "PUT",
            "/unused",
            None,
        )
        .unwrap_err(),
        "unsupported request: PUT"
    );
}

#[test]
fn request_preserves_protobuf_refusal_details() {
    let (endpoint, server) = serve("400 Bad Request", r#"{"error":"bad task"}"#);
    assert_eq!(
        request(&endpoint, "caller", "GET", "/api/health", None).unwrap_err(),
        "bad task"
    );
    server.join().unwrap().unwrap();

    let (endpoint, server) = serve("401 Unauthorized", "");
    let error = request(&endpoint, "caller", "GET", "/api/health", None).unwrap_err();
    assert!(error.contains("refused: 401"), "{error}");
    server.join().unwrap().unwrap();

    let (endpoint, server) = serve("502 Bad Gateway", "upstream broke");
    let error = request(&endpoint, "caller", "GET", "/api/health", None).unwrap_err();
    assert!(
        error.contains("502") && error.contains("upstream broke"),
        "{error}"
    );
    server.join().unwrap().unwrap();

    let (endpoint, server) = serve("200 OK", "not json");
    let error = request(&endpoint, "caller", "GET", "/api/health", None).unwrap_err();
    assert!(error.contains("response 200"), "{error}");
    server.join().unwrap().unwrap();
}

#[test]
fn lifecycle_commands_send_the_intended_status_and_note() {
    for (action, status, note) in [
        (UpdateAction::Accept, "accepted", None),
        (UpdateAction::Progress, "working", Some("café: halfway")),
        (UpdateAction::Finish, "done", Some("")),
        (UpdateAction::Fail, "failed", Some("build failed")),
    ] {
        let (endpoint, server) = serve("200 OK", r#"{"task":{"id":"t1","status":"done"}}"#);
        let result = Command::Update {
            action,
            id: "t1".into(),
            note: note.map(str::to_owned),
        }
        .run(&endpoint, "agent", true);
        let request = server.join().unwrap().unwrap();
        result.unwrap();
        assert!(request.starts_with("POST /api/tasks/t1 HTTP/1.1\r\n"));
        let body: Value = serde_json::from_str(request.split_once("\r\n\r\n").unwrap().1).unwrap();
        assert_eq!(body["status"], status);
        assert_eq!(body["note"], json!(note));
    }
}

#[test]
fn task_dispatch_uses_read_delete_and_prune_routes() {
    for (command, route, response) in [
        (
            Command::Task { id: "t1".into() },
            "GET /api/tasks/t1",
            r#"{"task":{"id":"t1","status":"done"}}"#,
        ),
        (
            Command::Wait { id: "t1".into() },
            "GET /api/tasks/t1",
            r#"{"task":{"id":"t1","status":"canceled"}}"#,
        ),
        (
            Command::Remove { id: "t1".into() },
            "DELETE /api/tasks/t1",
            r#"{"task":{"id":"t1","status":"done"}}"#,
        ),
        (
            Command::Prune { all: false },
            "DELETE /api/tasks",
            r#"{"removed":2}"#,
        ),
        (
            Command::Prune { all: true },
            "DELETE /api/tasks?all=true",
            r#"{"removed":3}"#,
        ),
    ] {
        let (endpoint, server) = serve("200 OK", response);
        let result = command.run(&endpoint, "agent", true);
        let request = server.join().unwrap().unwrap();
        result.unwrap();
        assert!(
            request.starts_with(&format!("{route} HTTP/1.1\r\n")),
            "{request}"
        );
    }
}

#[test]
fn template_lookup_respects_caller_scope_and_encodes_project() {
    for (caller, route) in [
        ("host", "/api/templates"),
        ("agent", "/api/templates/spawnable"),
    ] {
        for show in [false, true] {
            let (endpoint, server) = serve("200 OK", r#"{"templates":[{"name":"coder"}]}"#);
            let project = Some("a & café/?".into());
            let command = if show {
                Command::TemplateShow {
                    name: "coder".into(),
                    project,
                }
            } else {
                Command::Templates { project }
            };
            let result = command.run(&endpoint, caller, true);
            let request = server.join().unwrap().unwrap();
            result.unwrap();
            assert!(
                request.starts_with(&format!(
                    "GET {route}?project=a%20%26%20caf%C3%A9%2F%3F HTTP/1.1\r\n"
                )),
                "{request}"
            );
        }
    }
    let (endpoint, server) = serve("200 OK", r#"{"templates":[]}"#);
    let result = Command::TemplateShow {
        name: "missing".into(),
        project: None,
    }
    .run(&endpoint, "agent", true);
    server.join().unwrap().unwrap();
    assert_eq!(result, Err("no accessible agent template: missing".into()));
}

#[test]
fn command_dispatch_propagates_daemon_rejection() {
    let (endpoint, server) = serve("403 Forbidden", r#"{"error":"outside grant"}"#);
    let result = Command::Delegate {
        to: "other".into(),
        body: "do work".into(),
    }
    .run(&endpoint, "agent", true);
    let request = server.join().unwrap().unwrap();
    assert!(result.unwrap_err().contains("outside grant"));
    let body: Value = serde_json::from_str(request.split_once("\r\n\r\n").unwrap().1).unwrap();
    assert_eq!(body["to"], "other");
    assert_eq!(body["body"], "do work");
}

#[test]
fn construction_commands_report_missing_values_and_unknown_options() {
    for (input, message) in [
        ("worker spawn --project", "--project needs a value"),
        (
            "worker spawn --project repo --template",
            "--template needs a value",
        ),
        (
            "worker spawn --project repo work",
            "requires --project PROJECT and --template TEMPLATE",
        ),
        (
            "worker spawn --project repo --template coder",
            "needs a task body",
        ),
        ("agent create", "needs a name"),
        ("agent create worker --project", "--project needs a value"),
        ("agent create worker --template", "--template needs a value"),
        ("agent create worker --template coder", "needs --project"),
        ("agent create worker --project repo", "needs --template"),
        (
            "agent create worker --unknown",
            "unknown agent create option",
        ),
        ("agent delete worker", "needs the create subcommand"),
        ("template show", "needs a template name"),
        ("template show coder --project", "--project needs a value"),
        ("template list --project", "--project needs a value"),
        ("template show coder --unknown", "unexpected argument"),
        (
            "template list --project repo extra",
            "unexpected argument: extra",
        ),
        ("template delete coder", "needs the list or show subcommand"),
        ("sandbox inspect", "needs a session name"),
        ("sandbox inspect agent extra", "unexpected argument: extra"),
        ("sandbox delete agent", "needs the inspect subcommand"),
    ] {
        let error = parse_command_with_task_id(&words(input), None).unwrap_err();
        assert!(error.contains(message), "{input}: {error}");
    }
}

#[test]
fn agent_create_preserves_names_and_defaults_to_stopped() {
    assert_eq!(
        parse_command(&[
            "agent".into(),
            "create".into(),
            "review worker".into(),
            "--template".into(),
            "team/café".into(),
            "--project".into(),
            "my repo".into(),
        ]),
        Ok(Command::AgentCreate {
            name: "review worker".into(),
            project: "my repo".into(),
            template: "team/café".into(),
            start: false,
        })
    );
}

#[test]
fn agent_creation_encodes_template_path_and_preserves_body_fields() {
    let (endpoint, server) = serve("200 OK", r#"{"session":"review worker"}"#);
    let result = run_agent_create(
        &endpoint,
        "host",
        true,
        "review worker",
        "my repo",
        "team/café?x#y",
        false,
    );
    let request = server.join().unwrap().unwrap();
    result.unwrap();
    assert!(
        request.starts_with("POST /api/templates/team%2Fcaf%C3%A9%3Fx%23y/create HTTP/1.1\r\n")
    );
    let body: Value = serde_json::from_str(request.split_once("\r\n\r\n").unwrap().1).unwrap();
    assert_eq!(body["name"], "review worker");
    assert_eq!(body["project"], "my repo");
    assert_eq!(body["start"], false);
}

#[test]
fn worktree_options_preserve_selection_and_reject_ambiguous_requests() {
    use super::commands::WorktreeChoice;
    assert_eq!(
        parse_command(&words(
            "worker spawn --project repo --template codex --new-worktree --base HEAD --worktree-name feature task"
        )),
        Ok(Command::Spawn {
            project: "repo".into(),
            template: "codex".into(),
            durable: true,
            body: "task".into(),
            worktree: WorktreeChoice {
                new_worktree: true,
                base: "HEAD".into(),
                worktree_name: "feature".into(),
                ..Default::default()
            }
        })
    );
    parse_command(&words(
        "worker spawn --project repo --template codex --worktree abc --new-worktree task",
    ))
    .unwrap_err();
    parse_command(&words(
        "worker spawn --project repo --template codex --base HEAD task",
    ))
    .unwrap_err();
    assert_eq!(
        parse_command(&words("worktree remove abc --project repo")),
        Ok(Command::Worktree {
            action: "remove".into(),
            project: "repo".into(),
            id: "abc".into(),
            name: String::new(),
            base: String::new(),
            path: String::new()
        })
    );
    assert_eq!(
        parse_command(&words("worktree rename abc --project repo --name feature")),
        Ok(Command::Worktree {
            action: "rename".into(),
            project: "repo".into(),
            id: "abc".into(),
            name: "feature".into(),
            base: String::new(),
            path: String::new()
        })
    );
}

#[test]
fn request_rejects_a_literal_plain_http_refusal() {
    let (endpoint, server) = serve_raw("502 Bad Gateway", "text/plain", b"upstream broke");
    let error = request(&endpoint, "caller", "GET", "/api/health", None).unwrap_err();
    assert!(
        error.contains("502") && error.contains("expected Protobuf"),
        "{error}"
    );
    server.join().unwrap().unwrap();
}

#[test]
fn worktree_rename_dispatches_put_with_encoded_identity_and_body() {
    let (endpoint, server) = serve("200 OK", r#"{"id":"tree","name":"renamed"}"#);
    let command = parse_command_with_task_id(
        &words("worktree rename tree/one --project=my/repo --name=renamed"),
        None,
    )
    .unwrap();
    let result = command.run(&endpoint, "host", true);
    let received = server.join().unwrap().unwrap();
    result.unwrap();
    assert!(received.starts_with("PUT /api/worktrees/tree%2Fone?project=my%2Frepo HTTP/1.1\r\n"));
    let body: Value = serde_json::from_str(received.split_once("\r\n\r\n").unwrap().1).unwrap();
    assert_eq!(body["name"], "renamed");
}

#[test]
fn sandbox_inspection_encodes_the_session_as_one_segment() {
    let (endpoint, server) = serve("200 OK", r#"{"session":"agent","host":true}"#);
    let result = Command::SandboxInspect {
        name: "a/b?c#d%".into(),
    }
    .run(&endpoint, "host", true);
    let received = server.join().unwrap().unwrap();
    result.unwrap();
    assert!(received.starts_with("GET /api/sessions/a%2Fb%3Fc%23d%25/sandbox HTTP/1.1\r\n"));
}

#[test]
fn construction_options_reject_flags_as_values_and_accept_inline_dash_values() {
    for flag in ["--project", "--template"] {
        for next in ["--project", "--template", "--start", "--"] {
            parse_command_with_task_id(&words(&format!("agent create n {flag} {next}")), None)
                .unwrap_err();
        }
    }
    for flag in [
        "--project",
        "--template",
        "--worktree",
        "--base",
        "--worktree-name",
    ] {
        for next in ["--one-shot", "--new-worktree", "--base", "--"] {
            parse_command_with_task_id(
                &words(&format!(
                    "worker spawn --project p --template t --new-worktree {flag} {next} task"
                )),
                None,
            )
            .unwrap_err();
        }
    }
    for action in ["list", "create", "rename id", "remove id"] {
        parse_command_with_task_id(&words(&format!("worktree {action}")), None).unwrap_err();
        parse_command_with_task_id(
            &words(&format!("worktree {action} --project --name n")),
            None,
        )
        .unwrap_err();
    }
    parse_command_with_task_id(&words("worktree rename id --project p"), None).unwrap_err();
    assert_eq!(
        parse_command_with_task_id(
            &words("agent create n --project=--start --template=-t"),
            None
        ),
        Ok(Command::AgentCreate {
            name: "n".into(),
            project: "--start".into(),
            template: "-t".into(),
            start: false
        })
    );
    let command = parse_command_with_task_id(&words("worker spawn --project=-p --template=-t --new-worktree --base=-- --worktree-name=-n -- --one-shot task"), None).unwrap();
    match command {
        Command::Spawn {
            project,
            template,
            body,
            worktree,
            ..
        } => {
            assert_eq!(project, "-p");
            assert_eq!(template, "-t");
            assert_eq!(worktree.base, "--");
            assert_eq!(worktree.worktree_name, "-n");
            assert_eq!(body, "--one-shot task");
        }
        other => panic!("unexpected {other:?}"),
    }
    parse_command_with_task_id(
        &words("worktree create --project=-p --name=-n --base=-b --path=-d"),
        None,
    )
    .unwrap();
}

#[test]
fn injected_task_updates_preserve_notes_and_allow_explicit_targeting() {
    for (verb, action) in [
        ("accept", UpdateAction::Accept),
        ("progress", UpdateAction::Progress),
        ("finish", UpdateAction::Finish),
        ("fail", UpdateAction::Fail),
    ] {
        for (tail, id, note) in [
            ("", "injected", None),
            ("Reviewing", "injected", Some("Reviewing")),
            ("Reviewing the code", "injected", Some("Reviewing the code")),
            (
                "--id other Reviewing the code",
                "other",
                Some("Reviewing the code"),
            ),
            ("--id=other", "other", None),
            ("-- --help", "injected", Some("--help")),
        ] {
            // Build the action each time because it is an owned command value.
            let parsed = parse_command_with_task_id(
                &words(&format!("task {verb} {tail}")),
                Some("injected"),
            )
            .unwrap();
            match parsed {
                Command::Update {
                    action: actual,
                    id: actual_id,
                    note: actual_note,
                } => {
                    assert_eq!(actual, action);
                    assert_eq!(actual_id, id);
                    assert_eq!(actual_note.as_deref(), note);
                }
                other => panic!("unexpected {other:?}"),
            }
        }
    }
    parse_command_with_task_id(&words("task progress --id"), Some("injected")).unwrap_err();
}
