use super::take_json_flag;
use super::USAGE;
use crate::commands::{
    command_help, parse_command, run_spawn, task_is_terminal, wait_for_task, Command, InboxFilter,
    SpawnArgs, UpdateAction, ACCEPT_USAGE, DELEGATE_USAGE, FAIL_USAGE, FINISH_USAGE, INBOX_USAGE,
    PEERS_USAGE, PROGRESS_USAGE, PRUNE_USAGE, REMOVE_USAGE, SPAWN_USAGE, STATUS_USAGE, TASK_USAGE,
    WAIT_USAGE,
};
use crate::http::{request, Endpoint};
use crate::logs::{
    clean_log_line, expand_home, parse_logs_args, source_command, write_log_line, LogSelection,
    LogSource, LogsOptions, DEFAULT_LOG_LINES, LOGS_USAGE,
};
use serde_json::{json, Value};
use std::io::{BufRead, BufReader, Read, Write};
use std::net::TcpListener;
use std::path::PathBuf;
use std::thread;
use std::time::Duration;

fn words(text: &str) -> Vec<String> {
    text.split_whitespace().map(str::to_string).collect()
}

fn serve(status: &str, body: &str) -> (Endpoint, thread::JoinHandle<String>) {
    let listener = TcpListener::bind(("127.0.0.1", 0)).unwrap();
    let address = listener.local_addr().unwrap();
    let status = status.to_string();
    let body = body.to_string();
    let server = thread::spawn(move || {
        let (mut socket, _) = listener.accept().unwrap();
        let mut reader = BufReader::new(socket.try_clone().unwrap());
        let mut request = String::new();
        let mut content_length = 0;
        loop {
            let mut line = String::new();
            reader.read_line(&mut line).unwrap();
            if let Some(value) = line.to_ascii_lowercase().strip_prefix("content-length:") {
                content_length = value.trim().parse().unwrap();
            }
            request.push_str(&line);
            if line == "\r\n" {
                break;
            }
        }
        let mut request_body = vec![0; content_length];
        reader.read_exact(&mut request_body).unwrap();
        request.push_str(&String::from_utf8_lossy(&request_body));

        write!(
            socket,
            "HTTP/1.1 {status}\r\nContent-Length: {}\r\nConnection: close\r\n\r\n{body}",
            body.len()
        )
        .unwrap();
        request
    });
    (
        Endpoint {
            url: format!("http://{address}"),
            token: "secret".into(),
        },
        server,
    )
}

#[test]
fn command_parser_builds_delegation_and_update_commands() {
    assert_eq!(
        parse_command(&words("delegate agent fix the pane")),
        Ok(Command::Delegate {
            to: "agent".to_string(),
            body: "fix the pane".to_string(),
        })
    );
    assert_eq!(
        parse_command(&words("finish task-7 shipped safely")),
        Ok(Command::Update {
            action: UpdateAction::Finish,
            id: "task-7".to_string(),
            note: Some("shipped safely".to_string()),
        })
    );
    assert_eq!(
        parse_command(&words("wait task-7")),
        Ok(Command::Wait {
            id: "task-7".to_string(),
        })
    );
    assert_eq!(
        parse_command(&words("spawn --durable parent inspect the build")),
        Ok(Command::Spawn {
            parent: "parent".to_string(),
            durable: true,
            body: "inspect the build".to_string(),
        })
    );
    assert_eq!(
        parse_command(&words("worker parent run the checks")),
        Ok(Command::Spawn {
            parent: "parent".to_string(),
            durable: false,
            body: "run the checks".to_string(),
        })
    );
}

#[test]
fn task_text_preserves_help_words_and_flags() {
    for text in ["help", "please help me", "--help", "-h"] {
        let mut args = words("delegate agent");
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
            let mut args = words(&format!("{command} task-7"));
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
fn spawn_preserves_task_text_after_parent() {
    for command in ["spawn", "worker"] {
        for durable in [false, true] {
            for text in [
                "- investigate the failure",
                "--durable",
                "help",
                "--help",
                "-h",
            ] {
                // Check both a quoted body and a body spread over several arguments.
                for body_args in [vec![text.to_string()], words(text)] {
                    let mut args = vec![command.to_string()];
                    if durable {
                        args.push("--durable".into());
                    }
                    args.push("parent".into());
                    args.extend(body_args);
                    assert_eq!(
                        parse_command(&args),
                        Ok(Command::Spawn {
                            parent: "parent".into(),
                            durable,
                            body: text.into(),
                        })
                    );
                }
            }
        }
    }
}

#[test]
fn every_command_has_nested_help() {
    let commands = [
        ("delegate", DELEGATE_USAGE),
        ("spawn", SPAWN_USAGE),
        ("worker", SPAWN_USAGE),
        ("inbox", INBOX_USAGE),
        ("task", TASK_USAGE),
        ("wait", WAIT_USAGE),
        ("accept", ACCEPT_USAGE),
        ("progress", PROGRESS_USAGE),
        ("finish", FINISH_USAGE),
        ("fail", FAIL_USAGE),
        ("rm", REMOVE_USAGE),
        ("prune", PRUNE_USAGE),
        ("peers", PEERS_USAGE),
        ("status", STATUS_USAGE),
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
fn spawn_posts_parent_and_task_body_to_worker_endpoint() {
    let (endpoint, server) = serve(
        "200 OK",
        r#"{"task":{"id":"task-7"},"worker":{"name":"child"}}"#,
    );
    run_spawn(
        &endpoint,
        "host",
        true,
        SpawnArgs {
            parent: "parent",
            durable: true,
            body: "inspect the build",
        },
    )
    .unwrap();
    let request = server.join().unwrap();
    assert!(request.starts_with("POST /api/workers HTTP/1.1\r\n"));
    let body = request.split("\r\n\r\n").nth(1).unwrap();
    let body: Value = serde_json::from_str(body).unwrap();
    assert_eq!(body["parent"], "parent");
    assert_eq!(body["durable"], true);
    assert_eq!(body["body"], "inspect the build");
    assert!(body.get("project").is_none());
    assert!(body.get("template").is_none());
}

#[test]
fn command_parser_validates_fixed_arity_and_flags() {
    assert_eq!(
        parse_command(&words("peers now")).unwrap_err(),
        "unexpected argument: now\n\n".to_string() + USAGE
    );
    assert!(parse_command(&words("task")).is_err());
    assert!(parse_command(&words("prune --wat")).is_err());
    assert!(parse_command(&words("inbox --status")).is_err());
    assert!(parse_command(&words("spawn --template codex parent task")).is_err());
    assert!(parse_command(&words("wait")).is_err());
    assert!(parse_command(&words("wait task-7 extra")).is_err());
}

#[test]
fn global_json_flag_is_removed_before_command_parsing() {
    let mut args = words("--json inbox --all --json");
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
    assert!(parse_logs_args(&words("game --daemon")).is_err());
    assert!(parse_logs_args(&words("--lines 0")).is_err());
    assert!(parse_logs_args(&words("--lines 100001")).is_err());
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
    assert!(InboxFilter::parse(&words("--unknown")).is_err());
}

#[test]
fn task_completion_accepts_only_known_terminal_states() {
    for status in ["queued", "accepted", "working"] {
        assert!(!task_is_terminal(&json!({
            "task": { "status": status }
        }))
        .unwrap());
    }
    for status in ["done", "failed", "canceled"] {
        assert!(task_is_terminal(&json!({
            "task": { "status": status }
        }))
        .unwrap());
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
    let request = server.join().unwrap();
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
        ("POST", Some(json!({ "task": "check" }))),
    ] {
        let (endpoint, server) = serve("200 OK", r#"{"ok":true}"#);
        assert_eq!(
            request(&endpoint, "caller", method, "/api/test", body).unwrap(),
            json!({ "ok": true })
        );
        let received = server.join().unwrap();
        assert!(received.starts_with(&format!("{method} /api/test HTTP/1.1\r\n")));
        let lower = received.to_ascii_lowercase();
        assert!(lower.contains("x-slop-token: secret\r\n"));
        assert!(lower.contains("x-slop-session: caller\r\n"));
        if method == "POST" {
            let (_, body) = received.split_once("\r\n\r\n").unwrap();
            assert_eq!(
                serde_json::from_str::<Value>(body).unwrap(),
                json!({ "task": "check" })
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
fn request_preserves_structured_and_plain_refusal_details() {
    let (endpoint, server) = serve("400 Bad Request", r#"{"error":"bad task"}"#);
    assert_eq!(
        request(&endpoint, "caller", "GET", "/api/test", None).unwrap_err(),
        "bad task"
    );
    server.join().unwrap();

    let (endpoint, server) = serve("401 Unauthorized", "");
    let error = request(&endpoint, "caller", "GET", "/api/test", None).unwrap_err();
    assert!(error.contains("refused: 401"), "{error}");
    server.join().unwrap();

    let (endpoint, server) = serve("502 Bad Gateway", "upstream broke");
    let error = request(&endpoint, "caller", "GET", "/api/test", None).unwrap_err();
    assert!(
        error.contains("502") && error.contains("upstream broke"),
        "{error}"
    );
    server.join().unwrap();

    let (endpoint, server) = serve("200 OK", "not json");
    let error = request(&endpoint, "caller", "GET", "/api/test", None).unwrap_err();
    assert!(
        error.contains("response 200") && error.contains("expected"),
        "{error}"
    );
    server.join().unwrap();
}
