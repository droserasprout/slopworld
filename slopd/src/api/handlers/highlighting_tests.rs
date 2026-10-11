use super::*;
use serde_json::json;
use std::path::Path;

#[test]
fn themes_are_request_local_and_preserve_command_arguments() {
    for (command, kind, theme, expected) in [
        (
            "highlight --out-format=xterm256 %s",
            "highlight",
            "base16/monokai",
            vec![
                "highlight",
                "--out-format=xterm256",
                "--style=base16/monokai",
                "%s",
            ],
        ),
        (
            "pygmentize -f terminal256 -O style=monokai",
            "pygments",
            "friendly",
            vec![
                "pygmentize",
                "-f",
                "terminal256",
                "-O",
                "style=monokai",
                "-P",
                "style=friendly",
            ],
        ),
        (
            "'/opt/my tools/bat' --color=always -- %s",
            "bat",
            "Monokai Extended",
            vec![
                "/opt/my tools/bat",
                "--color=always",
                "--theme=Monokai Extended",
                "--",
                "%s",
            ],
        ),
    ] {
        assert_eq!(
            themed_command(command, kind, "").unwrap().argv,
            crate::sandbox::shell_split(command)
        );
        let themed = themed_command(command, kind, theme).unwrap();
        let argv = themed.argv;
        assert_eq!(argv, expected);
    }
    themed_command("bat", "highlight", "monokai").err().unwrap();
    themed_command("wrapper bat", "bat", "monokai")
        .err()
        .unwrap();
    for theme in ["../../tmp/theme", "/tmp/theme", "x\n--other", "$(whoami)"] {
        themed_command("highlight", "highlight", theme)
            .err()
            .unwrap();
    }
}

#[test]
fn theme_catalogs_keep_names_and_drop_headers() {
    assert_eq!(
        parse_themes(
            "highlight",
            "Installed themes (located in /themes/):\nacid   : Acid\nbase16/foo : Foo\n"
        )
        .unwrap(),
        ["acid", "base16/foo"]
    );
    assert_eq!(
        parse_themes("pygments", r#"{"styles":{"monokai":{},"friendly":{}}}"#).unwrap(),
        ["friendly", "monokai"]
    );
    assert_eq!(
        parse_themes("bat", "Monokai Extended\nOneHalfDark\n").unwrap(),
        ["Monokai Extended", "OneHalfDark"]
    );
    parse_themes("pygments", "{}").err().unwrap();
}

#[tokio::test]
async fn theme_requests_do_not_mutate_daemon_defaults() {
    use crate::api::protobuf::Proto;
    use std::os::unix::fs::PermissionsExt;
    let Some(dir) = crate::test_support::isolated() else {
        return;
    };
    let executable = dir.join("bat");
    std::fs::write(&executable, "#!/bin/sh\nif [ \"$1\" = --list-themes ]; then printf 'Monokai Extended\\nOneHalfDark\\n'; else printf '%s\\n' \"$@\"; fi\n").unwrap();
    std::fs::set_permissions(&executable, std::fs::Permissions::from_mode(0o700)).unwrap();
    let mut cfg = crate::config::Config::default();
    cfg.commands.highlighter = executable.to_string_lossy().into_owned();
    let original = cfg.commands.highlighter.clone();
    let manager = crate::session::test_manager(cfg);
    let Proto(catalog) = highlight_themes(
        State(manager.clone()),
        Query(HighlightThemesQuery::default()),
    )
    .await
    .unwrap();
    assert_eq!(catalog.engine, "bat");
    assert_eq!(catalog.themes, ["Monokai Extended", "OneHalfDark"]);
    for theme in ["Monokai Extended", "OneHalfDark"] {
        let request = serde_json::from_value(
            json!({"text":"let n = 1;", "language":"rs", "engine":"bat", "theme":theme}),
        )
        .unwrap();
        let Proto(result) = highlight(State(manager.clone()), Proto(request))
            .await
            .unwrap();
        assert!(result.text.contains(&format!("--theme={theme}")));
        assert_eq!(manager.config().await.commands.highlighter, original);
    }
}

#[tokio::test]
async fn draft_highlighter_catalog_and_preview_leave_defaults_unchanged() {
    use crate::api::protobuf::Proto;
    use std::os::unix::fs::PermissionsExt;
    let Some(dir) = crate::test_support::isolated() else {
        return;
    };
    let executable = dir.join("bat");
    std::fs::write(&executable, "#!/bin/sh\nif [ \"$1\" = --list-themes ]; then printf 'DraftTheme\\n'; else printf '%s\\n' \"$@\"; fi\n").unwrap();
    std::fs::set_permissions(&executable, std::fs::Permissions::from_mode(0o700)).unwrap();
    let mut cfg = crate::config::Config::default();
    cfg.commands.highlighter = "highlight --out-format=xterm256".into();
    let original = cfg.commands.highlighter.clone();
    let manager = crate::session::test_manager(cfg);
    let command = executable.to_string_lossy().into_owned();
    let Proto(catalog) = highlight_themes(
        State(manager.clone()),
        Query(HighlightThemesQuery {
            command: Some(command.clone()),
        }),
    )
    .await
    .unwrap();
    assert_eq!(catalog.engine, "bat");
    assert_eq!(catalog.themes, ["DraftTheme"]);
    assert_eq!(manager.config().await.commands.highlighter, original);
    let request = serde_json::from_value(json!({"text":"sample", "language":"rs", "engine":"bat", "theme":"DraftTheme", "command":command})).unwrap();
    let Proto(preview) = highlight(State(manager.clone()), Proto(request))
        .await
        .unwrap();
    assert!(preview.text.contains("--theme=DraftTheme"));
    assert_eq!(manager.config().await.commands.highlighter, original);
    let Proto(off) = highlight_themes(
        State(manager.clone()),
        Query(HighlightThemesQuery {
            command: Some(String::new()),
        }),
    )
    .await
    .unwrap();
    assert!(off.engine.is_empty());
    assert!(off.themes.is_empty());
    assert_eq!(manager.config().await.commands.highlighter, original);
    let request = serde_json::from_value(json!({"text":"sample", "command":""})).unwrap();
    let Proto(preview) = highlight(State(manager.clone()), Proto(request))
        .await
        .unwrap();
    assert_eq!(preview.text, "sample");
    assert_eq!(preview.bytes, 6);
    assert_eq!(manager.config().await.commands.highlighter, original);
}

#[tokio::test]
async fn highlighter_preserves_text_and_removes_its_temporary_input() {
    for (language, extension) in [
        (" .rs/../../escape", "rs"),
        ("../bad", "txt"),
        ("c++", "c++"),
    ] {
        // Emit the input path as well as its contents so cleanup is observable.
        let output = highlight_text(
            HighlightCommand::parse("sh -c 'echo \"$1\"; cat \"$1\"' sh %s"),
            language,
            "café\n\u{1b}[31mtext\n",
        )
        .await
        .unwrap();
        let (path, text) = output.split_once('\n').unwrap();
        assert_eq!(Path::new(path).extension().unwrap(), extension);
        assert_eq!(text, "café\n\u{1b}[31mtext\n");
        assert!(!Path::new(path).exists());
    }
}

#[tokio::test]
async fn highlighter_reports_failure_and_cleans_up_input() {
    let error = highlight_text(
        HighlightCommand::parse("sh -c 'echo \"$1\" >&2; exit 7' sh %s"),
        "rust",
        "input",
    )
    .await
    .unwrap_err()
    .to_string();
    let path = error
        .strip_prefix("The syntax highlighter failed: ")
        .unwrap();
    assert!(Path::new(path).is_absolute());
    assert!(!Path::new(path).exists());
    assert_eq!(
        highlight_text(HighlightCommand::parse("  "), "rs", "input")
            .await
            .unwrap_err()
            .to_string(),
        "The syntax highlighter is disabled."
    );
}

#[tokio::test]
async fn highlighter_rejects_invalid_output_missing_program_and_timeout() {
    let fixture = crate::api::handlers::tests::fixture("highlighter");
    let input = fixture.join("input");
    std::fs::write(&input, "text").unwrap();
    for (command, expected) in [
        (
            "sh -c 'printf \"\\377\"'",
            "The syntax highlighter output is not valid UTF-8.",
        ),
        (
            "/nonexistent-slopworld-highlighter",
            "The daemon could not start the syntax highlighter.",
        ),
        ("sh -c 'exec sleep 10'", "The syntax highlighter timed out."),
        (
            "sh -c 'head -c 2097153 /dev/zero'",
            "The syntax highlighter output exceeds the preview limit.",
        ),
    ] {
        assert_eq!(
            run_highlighter_argv(
                &HighlightCommand::parse(command)
                    .with_file(&input)
                    .unwrap()
                    .argv
            )
            .await
            .unwrap_err()
            .to_string(),
            expected
        );
        assert_eq!(std::fs::read_to_string(&input).unwrap(), "text");
    }
}

#[tokio::test]
async fn cancelled_highlight_removes_its_temporary_file() {
    let dir = PathBuf::from(crate::paths::temp_dir("highlight"));
    tokio::fs::create_dir_all(&dir).await.unwrap();
    let marker = format!("cancelled-highlight-{}", uuid::Uuid::new_v4());
    let task_marker = marker.clone();
    let mut tasks = tokio::task::JoinSet::new();
    // The highlighter appends its input filename. Let the shell consume that
    // argument instead of passing it to sleep as an invalid duration.
    tasks.spawn(async move {
        highlight_text(
            HighlightCommand::parse("sh -c 'exec sleep 30'"),
            "txt",
            &task_marker,
        )
        .await
    });

    let deadline = tokio::time::Instant::now() + std::time::Duration::from_secs(3);
    let mut temporary = None;
    while tokio::time::Instant::now() < deadline {
        let mut entries = tokio::fs::read_dir(&dir).await.unwrap();
        while let Some(entry) = entries.next_entry().await.unwrap() {
            if tokio::fs::read(entry.path()).await.ok().as_deref() == Some(marker.as_bytes()) {
                temporary = Some(entry.path());
                break;
            }
        }
        if temporary.is_some() {
            break;
        }
        tokio::time::sleep(std::time::Duration::from_millis(10)).await;
    }

    tasks.abort_all();
    let cancelled = tasks
        .join_next()
        .await
        .expect("highlight task should finish");
    assert!(matches!(cancelled, Err(error) if error.is_cancelled()));
    let temporary = temporary.expect("highlight input file should be written before cancellation");
    assert!(!temporary.exists());
}

#[test]
fn highlighter_command_appends_or_expands_the_file() {
    let path = Path::new("/tmp/slopworld/highlight/code file.rs");
    assert_eq!(
        HighlightCommand::parse("highlight --out-format=xterm256")
            .with_file(path)
            .unwrap()
            .argv,
        [
            "highlight",
            "--out-format=xterm256",
            "/tmp/slopworld/highlight/code file.rs"
        ]
    );
    assert_eq!(
        HighlightCommand::parse("tool --file=%s")
            .with_file(path)
            .unwrap()
            .argv,
        ["tool", "--file=/tmp/slopworld/highlight/code file.rs"]
    );
    assert!(HighlightCommand::parse("   ").with_file(path).is_err());
}
