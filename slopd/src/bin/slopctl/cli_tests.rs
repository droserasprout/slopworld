use super::*;

#[test]
fn command_schema_is_consistent() {
    Cli::command().debug_assert();
}

#[test]
fn short_help_is_concise_and_long_help_explains_connections() {
    let help = |flag: &str| {
        let Command::Help { usage } = parse(vec![flag.into()], None).unwrap().command else {
            panic!("expected help")
        };
        usage.to_string()
    };
    let short = help("-h");
    let long = help("--help");
    for heading in ["Connection:", "Output:"] {
        assert!(short.contains(heading));
        assert!(long.contains(heading));
    }
    assert!(!short.contains("Connection precedence:"));
    assert!(long.contains("Connection precedence:"));
}

#[test]
fn task_text_keeps_global_options_and_help_literal() {
    for globals in ["", "--json --sidecar --endpoint tank"] {
        for (prefix, update) in [
            ("worker spawn --project p --template t", false),
            ("task delegate agent", false),
            ("task finish --id task-7", true),
        ] {
            for body in [
                "fix --json --endpoint ../invalid --sidecar --help --id other",
                "-- --json --endpoint=../invalid --sidecar --help",
                "help with --endpoint",
            ] {
                let args = format!("{globals} {prefix} {body}")
                    .split_whitespace()
                    .map(str::to_owned)
                    .collect();
                let parsed = parse(args, None).unwrap();
                assert_eq!(parsed.json, !globals.is_empty());
                assert_eq!(
                    parsed.endpoint.as_deref(),
                    (!globals.is_empty()).then_some("tank")
                );
                assert_eq!(
                    parsed.mode,
                    if globals.is_empty() {
                        Mode::Native
                    } else {
                        Mode::Sidecar
                    }
                );
                let expected = body.strip_prefix("-- ").unwrap_or(body);
                match parsed.command {
                    Command::Remote(RemoteCommand::Spawn(args)) => assert_eq!(args.body, expected),
                    Command::Remote(RemoteCommand::Delegate { body, .. }) => {
                        assert_eq!(body, expected)
                    }
                    Command::Remote(RemoteCommand::Update { id, note, .. }) if update => {
                        assert_eq!(id, "task-7");
                        assert_eq!(note.as_deref(), Some(expected));
                    }
                    other => panic!("unexpected command: {other:?}"),
                }
            }
        }
    }
}

#[test]
fn task_update_positionals_are_always_notes() {
    for verb in ["accept", "progress", "finish", "fail"] {
        let args = format!("task {verb} task-7 completed")
            .split_whitespace()
            .map(str::to_owned)
            .collect::<Vec<_>>();
        assert!(
            parse_command_with_task_id(&args, None)
                .unwrap_err()
                .contains("--id ID")
        );
        let Command::Remote(RemoteCommand::Update { id, note, .. }) =
            parse_command_with_task_id(&args, Some("injected")).unwrap()
        else {
            panic!()
        };
        assert_eq!(id, "injected");
        assert_eq!(note.as_deref(), Some("task-7 completed"));

        for injected in [None, Some("injected")] {
            let args = format!("task {verb} --id explicit completed")
                .split_whitespace()
                .map(str::to_owned)
                .collect::<Vec<_>>();
            let Command::Remote(RemoteCommand::Update { id, note, .. }) =
                parse_command_with_task_id(&args, injected).unwrap()
            else {
                panic!()
            };
            assert_eq!(id, "explicit");
            assert_eq!(note.as_deref(), Some("completed"));
        }
    }
}

#[test]
fn removed_worker_and_log_options_are_rejected() {
    for option in [
        "--durable",
        "--new-worktree",
        "--base HEAD",
        "--worktree-name feature",
    ] {
        let args = format!("worker spawn --project p --template t {option} task")
            .split_whitespace()
            .map(str::to_owned)
            .collect();
        assert!(parse(args, None).is_err(), "{option}");
    }
    for option in ["--game", "--daemon", "--all"] {
        assert!(
            parse(vec!["logs".into(), option.into()], None).is_err(),
            "{option}"
        );
    }
}

#[test]
fn endpoint_options_validate_names_and_missing_or_duplicate_values() {
    for input in [
        "--endpoint status",
        "--endpoint= status",
        "--endpoint ../tank status",
        "--endpoint /tank status",
        "--endpoint tank.toml status",
        "--endpoint tank --endpoint other status",
    ] {
        assert!(
            parse(input.split_whitespace().map(str::to_owned).collect(), None).is_err(),
            "{input}"
        );
    }
}

#[test]
fn help_works_before_task_text_with_global_options() {
    for input in [
        "--json worker spawn --help",
        "worker spawn --sidecar --help",
        "task delegate agent --help",
        "task finish --endpoint tank --help",
        "config set --help",
    ] {
        let parsed = parse(input.split_whitespace().map(str::to_owned).collect(), None).unwrap();
        assert!(matches!(parsed.command, Command::Help { .. }), "{input}");
    }
}
