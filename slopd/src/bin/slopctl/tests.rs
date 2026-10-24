use crate::cli::{Command, parse_command, parse_command_with_task_id};
use crate::commands::{
    InboxFilter, RemoteCommand, SpawnArgs, UpdateAction, run_agent_create, run_spawn,
    task_is_terminal, wait_for_task,
};
use crate::http::{Endpoint, request};
use crate::logs::{
    DEFAULT_LOG_LINES, LogSelection, LogSource, LogsOptions, clean_log_line, expand_home,
    parse_logs_args, source_command, write_log_line,
};
use crate::shared::application_paths;
use serde_json::{Value, json};
use std::path::PathBuf;
use std::time::Duration;

fn words(text: &str) -> Vec<String> {
    text.split_whitespace().map(str::to_string).collect()
}

#[path = "http_fixture.rs"]
pub(crate) mod http_fixture;
use http_fixture::{serve, serve_raw};

#[test]
fn command_parser_builds_delegation_and_update_commands() {
    assert_eq!(
        parse_command(&words("task delegate agent fix the pane")),
        Ok(Command::Remote(RemoteCommand::Delegate {
            to: "agent".to_string(),
            body: "fix the pane".to_string(),
        }))
    );
    assert_eq!(
        parse_command(&words("task finish --id task-7 shipped safely")),
        Ok(Command::Remote(RemoteCommand::Update {
            action: UpdateAction::Finish,
            id: "task-7".to_string(),
            note: Some("shipped safely".to_string()),
        }))
    );
    assert_eq!(
        parse_command(&words("task wait task-7")),
        Ok(Command::Remote(RemoteCommand::Wait {
            id: "task-7".to_string(),
        }))
    );
    assert_eq!(
        parse_command(&words(
            "worker spawn --project repo --template codex inspect the build"
        )),
        Ok(Command::Remote(RemoteCommand::Spawn(SpawnArgs {
            worktree: Default::default(),
            project: "repo".to_string(),
            template: "codex".to_string(),
            durable: true,
            body: "inspect the build".to_string(),
        })))
    );
    assert_eq!(
        parse_command(&words(
            "worker spawn --project repo --template codex run the checks"
        )),
        Ok(Command::Remote(RemoteCommand::Spawn(SpawnArgs {
            worktree: Default::default(),
            project: "repo".to_string(),
            template: "codex".to_string(),
            durable: true,
            body: "run the checks".to_string(),
        })))
    );
    assert_eq!(
        parse_command(&words("sandbox inspect agent")),
        Ok(Command::Remote(RemoteCommand::SandboxInspect {
            name: "agent".into(),
        }))
    );
    assert_eq!(
        parse_command(&words(
            "agent create worker --project repo --template team-review --start"
        )),
        Ok(Command::Remote(RemoteCommand::AgentCreate {
            name: "worker".into(),
            project: "repo".into(),
            template: "team-review".into(),
            start: true,
        }))
    );
    assert_eq!(
        parse_command(&words("template show team-review")),
        Ok(Command::Remote(RemoteCommand::TemplateShow {
            name: "team-review".into(),
            project: None,
        }))
    );
    assert_eq!(
        parse_command(&words("template show team-review --project repo")),
        Ok(Command::Remote(RemoteCommand::TemplateShow {
            name: "team-review".into(),
            project: Some("repo".into()),
        }))
    );
    assert_eq!(
        parse_command(&words("template list")),
        Ok(Command::Remote(RemoteCommand::Templates { project: None }))
    );
    assert_eq!(
        parse_command(&words("template list --project repo")),
        Ok(Command::Remote(RemoteCommand::Templates {
            project: Some("repo".into())
        }))
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
        Ok(Command::Remote(RemoteCommand::Task {
            id: "task-7".into()
        }))
    );
    parse_command(&words("task task-7")).unwrap_err();
}

#[test]
fn task_commands_use_the_worker_task_id_when_id_is_omitted() {
    let task_id = Some("task-7");
    assert_eq!(
        parse_command_with_task_id(&words("task show"), task_id),
        Ok(Command::Remote(RemoteCommand::Task {
            id: "task-7".into()
        }))
    );
    assert_eq!(
        parse_command_with_task_id(&words("task wait"), task_id),
        Ok(Command::Remote(RemoteCommand::Wait {
            id: "task-7".into()
        }))
    );
    for (command, action) in [
        ("task accept", UpdateAction::Accept),
        ("task progress", UpdateAction::Progress),
        ("task finish", UpdateAction::Finish),
        ("task fail", UpdateAction::Fail),
    ] {
        assert_eq!(
            parse_command_with_task_id(&words(command), task_id),
            Ok(Command::Remote(RemoteCommand::Update {
                action,
                id: "task-7".into(),
                note: None,
            }))
        );
    }
    assert_eq!(
        parse_command_with_task_id(&words("task remove"), task_id),
        Ok(Command::Remote(RemoteCommand::Remove {
            id: "task-7".into()
        }))
    );
    assert_eq!(
        parse_command_with_task_id(&words("task show other-task"), task_id),
        Ok(Command::Remote(RemoteCommand::Task {
            id: "other-task".into()
        }))
    );
}

#[test]
fn command_tree_has_group_and_leaf_help() {
    for command in [
        "task",
        "task list",
        "task show",
        "worker",
        "worker spawn",
        "template",
        "template list",
        "template show",
        "agent create",
        "sandbox inspect",
        "worktree",
        "paths",
    ] {
        let Command::Help { usage } = parse_command(&words(&format!("{command} --help"))).unwrap()
        else {
            panic!("missing help: {command}")
        };
        let usage = usage.to_string();
        assert!(usage.contains("Usage:"), "{command}: {usage}");
        assert!(
            usage.contains(&format!("slopctl {command}")),
            "{command}: {usage}"
        );
    }
}

#[test]
fn task_text_preserves_help_words_and_flags() {
    for text in ["help", "please help me", "--help", "-h"] {
        let mut args = words("task delegate agent --");
        args.extend(words(text));
        assert_eq!(
            parse_command(&args),
            Ok(Command::Remote(RemoteCommand::Delegate {
                to: "agent".into(),
                body: text.into(),
            }))
        );
        for (command, action) in [
            ("accept", UpdateAction::Accept),
            ("progress", UpdateAction::Progress),
            ("finish", UpdateAction::Finish),
            ("fail", UpdateAction::Fail),
        ] {
            let mut args = words(&format!("task {command} --id task-7 --"));
            args.extend(words(text));
            assert_eq!(
                parse_command(&args),
                Ok(Command::Remote(RemoteCommand::Update {
                    action,
                    id: "task-7".into(),
                    note: Some(text.into()),
                }))
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
                if !durable {
                    args.push("--one-shot".into());
                }
                args.push("--project".into());
                args.push("repo".into());
                args.push("--template".into());
                args.push("codex".into());
                args.push("--".into());
                args.extend(body_args);
                assert_eq!(
                    parse_command(&args),
                    Ok(Command::Remote(RemoteCommand::Spawn(SpawnArgs {
                        worktree: Default::default(),
                        project: "repo".into(),
                        template: "codex".into(),
                        durable,
                        body: text.into(),
                    })))
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
                let args = words(&format!(
                    "worker spawn --project repo --template review {} {} -- {body}",
                    if durable { "" } else { "--one-shot" },
                    if json { "--json" } else { "" },
                ));
                let parsed = crate::cli::parse(args, None).unwrap();
                assert_eq!(parsed.json, json);
                assert_eq!(
                    parsed.command,
                    Command::Remote(RemoteCommand::Spawn(SpawnArgs {
                        worktree: Default::default(),
                        project: "repo".into(),
                        template: "review".into(),
                        durable,
                        body: body.into(),
                    }))
                );
            }
        }
    }
    parse_command(&words("worker spawn --project repo --template review --")).unwrap_err();
}

#[test]
fn every_command_has_nested_help() {
    for command in [
        "worker", "template", "agent", "task", "peers", "status", "sandbox", "logs", "config",
        "worktree", "paths",
    ] {
        for flag in ["-h", "--help"] {
            assert!(
                matches!(
                    parse_command(&words(&format!("{command} {flag}"))),
                    Ok(Command::Help { .. })
                ),
                "{command} {flag}"
            );
        }
    }
}

#[test]
fn help_subcommands_resolve_the_full_target_path() {
    for (input, target) in [
        ("help task", "task"),
        ("help task wait", "task wait"),
        ("task help wait", "task wait"),
        ("help worker spawn", "worker spawn"),
        ("task help finish", "task finish"),
        ("help config set", "config set"),
    ] {
        let Command::Help { usage } = parse_command(&words(input)).unwrap() else {
            panic!("missing help: {input}")
        };
        let usage = usage.to_string();
        assert!(
            usage.contains(&format!("Usage: slopctl {target} ")),
            "{input}: {usage}"
        );
    }
    for input in ["help unknown", "help task unknown", "task help unknown"] {
        assert!(parse_command(&words(input)).is_err(), "{input}");
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
        &SpawnArgs {
            worktree: "review-checkout".into(),
            project: "repo".into(),
            template: "codex".into(),
            durable: true,
            body: "inspect the build".into(),
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
    assert_eq!(body["worktree"], "review-checkout");
    for field in ["new_worktree", "base", "worktree_name"] {
        assert!(
            body.get(field).is_none(),
            "unexpected allocation field: {field}"
        );
    }
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
    assert!(
        parse_command(&words("peers now"))
            .unwrap_err()
            .contains("unexpected argument")
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
fn global_json_flag_is_parsed_with_command_options() {
    let parsed = crate::cli::parse(words("task list --all --json"), None).unwrap();
    assert!(parsed.json);
    assert_eq!(
        parsed.command,
        Command::Remote(RemoteCommand::Inbox {
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
            mode: crate::shared::application_paths::Mode::Native,
            selection: LogSelection::All,
            lines: DEFAULT_LOG_LINES,
            follow: false,
        }
    );
}

#[test]
fn logs_accepts_daemon_source_and_follow() {
    assert_eq!(
        parse_logs_args(&words("daemon -f --lines=37")).unwrap(),
        LogsOptions {
            mode: crate::shared::application_paths::Mode::Native,
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
            mode: crate::shared::application_paths::Mode::Native,
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
    assert_eq!(ids(parse_inbox(&words("--sent")).unwrap()), ["sent"]);
    assert_eq!(
        ids(parse_inbox(&words("--received")).unwrap()),
        ["received"]
    );
    assert_eq!(
        ids(parse_inbox(&words("--all --sent --received")).unwrap()),
        ["other", "received", "done", "sent"]
    );
    assert_eq!(ids(parse_inbox(&words("--status done")).unwrap()), ["done"]);
    parse_inbox(&words("--unknown")).unwrap_err();
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
        mode: crate::shared::application_paths::Mode::Native,
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
        let result = RemoteCommand::Update {
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
            RemoteCommand::Task { id: "t1".into() },
            "GET /api/tasks/t1",
            r#"{"task":{"id":"t1","status":"done"}}"#,
        ),
        (
            RemoteCommand::Wait { id: "t1".into() },
            "GET /api/tasks/t1",
            r#"{"task":{"id":"t1","status":"canceled"}}"#,
        ),
        (
            RemoteCommand::Remove { id: "t1".into() },
            "DELETE /api/tasks/t1",
            r#"{"task":{"id":"t1","status":"done"}}"#,
        ),
        (
            RemoteCommand::Prune { all: false },
            "DELETE /api/tasks",
            r#"{"removed":2}"#,
        ),
        (
            RemoteCommand::Prune { all: true },
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
                RemoteCommand::TemplateShow {
                    name: "coder".into(),
                    project,
                }
            } else {
                RemoteCommand::Templates { project }
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
    let result = RemoteCommand::TemplateShow {
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
    let result = RemoteCommand::Delegate {
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
    for input in [
        "worker spawn --project",
        "worker spawn --project repo --template",
        "worker spawn --project repo work",
        "worker spawn --project repo --template coder",
        "agent create",
        "agent create worker --project",
        "agent create worker --template",
        "agent create worker --template coder",
        "agent create worker --project repo",
        "agent create worker --unknown",
        "agent delete worker",
        "template show",
        "template show coder --project",
        "template list --project",
        "template show coder --unknown",
        "template list --project repo extra",
        "template delete coder",
        "sandbox inspect",
        "sandbox inspect agent extra",
        "sandbox delete agent",
    ] {
        let error = parse_command_with_task_id(&words(input), None).unwrap_err();
        assert!(error.contains("error:"), "{input}: {error}");
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
        Ok(Command::Remote(RemoteCommand::AgentCreate {
            name: "review worker".into(),
            project: "my repo".into(),
            template: "team/café".into(),
            start: false,
        }))
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
    assert_eq!(
        parse_command(&words(
            "worker spawn --project repo --template codex --worktree feature task"
        )),
        Ok(Command::Remote(RemoteCommand::Spawn(SpawnArgs {
            project: "repo".into(),
            template: "codex".into(),
            durable: true,
            body: "task".into(),
            worktree: "feature".into(),
        })))
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
        Ok(Command::Remote(RemoteCommand::Worktree(
            crate::commands::WorktreeArgs::Remove {
                project: "repo".into(),
                id: "abc".into(),
            }
        )))
    );
    assert_eq!(
        parse_command(&words("worktree rename abc --project repo --name feature")),
        Ok(Command::Remote(RemoteCommand::Worktree(
            crate::commands::WorktreeArgs::Rename {
                project: "repo".into(),
                id: "abc".into(),
                name: "feature".into(),
            }
        )))
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
    let Command::Remote(command) = command else {
        panic!("expected remote command")
    };
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
    let result = RemoteCommand::SandboxInspect {
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
    for flag in ["--project", "--template", "--worktree"] {
        for next in ["--one-shot", "--worktree", "--"] {
            parse_command_with_task_id(
                &words(&format!(
                    "worker spawn --project p --template t {flag} {next} task"
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
        Ok(Command::Remote(RemoteCommand::AgentCreate {
            name: "n".into(),
            project: "--start".into(),
            template: "-t".into(),
            start: false
        }))
    );
    let command = parse_command_with_task_id(
        &words("worker spawn --project=-p --template=-t --worktree=-n -- --one-shot task"),
        None,
    )
    .unwrap();
    match command {
        Command::Remote(RemoteCommand::Spawn(SpawnArgs {
            project,
            template,
            body,
            worktree,
            ..
        })) => {
            assert_eq!(project, "-p");
            assert_eq!(template, "-t");
            assert_eq!(worktree, "-n");
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
                Command::Remote(RemoteCommand::Update {
                    action: actual,
                    id: actual_id,
                    note: actual_note,
                }) => {
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

#[test]
fn partial_task_prune_reports_failure_after_preserving_response_details() {
    let (endpoint, server) = serve(
        "200 OK",
        r#"{"removed":1,"committed":["a"],"failed":[{"id":"b","error":"disk full"}],"unattempted":["c"]}"#,
    );
    let result = RemoteCommand::Prune { all: true }.run(&endpoint, "host", true);
    server.join().unwrap().unwrap();
    assert!(
        result
            .unwrap_err()
            .contains("1 committed, 1 failed, 1 unattempted")
    );
}

#[test]
fn sidecar_log_commands_select_the_sidecar_cache_and_docker_container() {
    let options = LogsOptions {
        mode: crate::shared::application_paths::Mode::Sidecar,
        selection: LogSelection::All,
        lines: 17,
        follow: true,
    };
    let game = source_command(LogSource::Game, options).unwrap();
    let expected = crate::shared::application_paths::Mode::Sidecar.game_log_path();
    assert_eq!(
        game.get_args().last().unwrap(),
        expand_home(&expected.to_string_lossy()).unwrap()
    );
    let daemon = source_command(LogSource::Daemon, options).unwrap();
    assert_eq!(daemon.get_program(), "docker");
    let args = daemon
        .get_args()
        .map(|arg| arg.to_string_lossy().into_owned())
        .collect::<Vec<_>>();
    assert_eq!(&args[..4], ["logs", "--tail", "17", "--follow"]);
}

#[test]
fn authenticated_requests_never_follow_redirects() {
    for status in [
        "301 Moved Permanently",
        "302 Found",
        "303 See Other",
        "307 Temporary Redirect",
        "308 Permanent Redirect",
    ] {
        for (method, path, body) in [
            ("GET", "/api/health", None),
            ("DELETE", "/api/sessions/target", None),
            (
                "POST",
                "/api/tasks",
                Some(json!({"to":"host","body":"check"})),
            ),
            ("PUT", "/api/config", Some(json!({"text":""}))),
        ] {
            let (location, stop, destination) = http_fixture::redirect_target();
            let (endpoint, server) = http_fixture::serve_redirect(status, &location);
            request(&endpoint, "host", method, path, body).unwrap_err();
            let received = server.join().unwrap().unwrap();
            assert!(
                received
                    .to_ascii_lowercase()
                    .contains("x-slop-token: secret\r\n")
            );
            stop.send(()).unwrap();
            assert!(!destination.join().unwrap(), "{method} followed {status}");
        }
    }
}

fn parse_inbox(args: &[String]) -> Result<InboxFilter, String> {
    let mut command = words("task list");
    command.extend_from_slice(args);
    match parse_command(&command)? {
        Command::Remote(RemoteCommand::Inbox { filter }) => Ok(filter),
        _ => Err("expected inbox".into()),
    }
}

#[test]
fn global_options_keep_endpoint_mode_and_forwarded_text_boundaries() {
    let parsed = crate::cli::parse(words("worker spawn --project p --template t --json --endpoint tank --sidecar fix -- --json --endpoint literal --sidecar"), None).unwrap();
    assert!(parsed.json);
    assert_eq!(parsed.mode, application_paths::Mode::Sidecar);
    assert_eq!(parsed.endpoint.as_deref(), Some("tank"));
    let Command::Remote(RemoteCommand::Spawn(args)) = parsed.command else {
        panic!("expected spawn")
    };
    assert_eq!(args.body, "fix -- --json --endpoint literal --sidecar");
    for input in [
        "--endpoint ../outside status",
        "--endpoint a --endpoint b status",
        "--endpoint",
    ] {
        assert!(crate::cli::parse(words(input), None).is_err(), "{input}");
    }
}

#[test]
fn path_and_log_queries_resolve_without_loading_an_endpoint() {
    let parsed = crate::cli::parse(
        words("paths endpoint --sidecar --endpoint tank --json"),
        None,
    )
    .unwrap();
    assert!(parsed.json);
    assert_eq!(parsed.endpoint.as_deref(), Some("tank"));
    assert_eq!(parsed.mode, application_paths::Mode::Sidecar);
    assert_eq!(
        parsed.command,
        Command::Paths {
            name: "endpoint".into()
        }
    );
    let parsed = crate::cli::parse(words("logs --sidecar daemon -n 1"), None).unwrap();
    assert_eq!(
        parsed.command,
        Command::Logs {
            options: LogsOptions {
                mode: application_paths::Mode::Sidecar,
                selection: LogSelection::One(LogSource::Daemon),
                lines: 1,
                follow: false,
            }
        }
    );
}

#[test]
fn version_and_help_are_successful_display_requests() {
    let Command::Help { usage } = parse_command(&words("--version")).unwrap() else {
        panic!("expected version")
    };
    assert_eq!(
        usage.to_string(),
        format!("slopctl {}\n", env!("SLOPWORLD_VERSION"))
    );
    let Command::Help { usage } = parse_command(&[]).unwrap() else {
        panic!("expected help")
    };
    let usage = usage.to_string();
    assert!(usage.contains("--endpoint"));
    assert!(usage.contains("--json"));
}

#[test]
fn worktree_create_defaults_omit_optional_values_but_reject_empty_options() {
    let Command::Remote(RemoteCommand::Worktree(crate::commands::WorktreeArgs::Create {
        name,
        base,
        path,
        ..
    })) = parse_command(&words("worktree create --project repo")).unwrap()
    else {
        panic!("expected create")
    };
    assert_eq!((name, base, path), (None, None, None));
    for input in [
        "worktree create --project=",
        "worktree create --project p --name=",
        "worker spawn --project= --template t task",
        "agent create n --project p --template=",
    ] {
        assert!(parse_command(&words(input)).is_err(), "{input}");
    }
}

#[test]
fn injected_notes_preserve_a_literal_leading_delimiter() {
    let command =
        parse_command_with_task_id(&words("task progress -- -- --help"), Some("injected")).unwrap();
    assert_eq!(
        command,
        Command::Remote(RemoteCommand::Update {
            action: UpdateAction::Progress,
            id: "injected".into(),
            note: Some("-- --help".into())
        })
    );
}

#[test]
fn config_values_preserve_negative_numbers_and_literal_flags() {
    for text in ["-1", "--help", ""] {
        let mut args = words("config set defaults.title_interval");
        if text.starts_with("--") {
            args.push("--".into());
        }
        args.push(text.into());
        assert_eq!(
            parse_command(&args),
            Ok(Command::Remote(RemoteCommand::Config(
                crate::commands::ConfigArgs::Set {
                    key: "defaults.title_interval".into(),
                    value: text.into()
                }
            )))
        );
    }
}
