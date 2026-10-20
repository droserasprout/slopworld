use super::*;
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
            crate::sandbox::shell_split(command).unwrap()
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
async fn highlighter_preserves_text_and_removes_its_temporary_input() {
    for (language, extension) in [
        (" .rs/../../escape", "rs"),
        ("../bad", "txt"),
        ("c++", "c++"),
    ] {
        // Emit the input path as well as its contents so cleanup is observable.
        let output = highlight_text(
            HighlightCommand::parse("sh -c 'echo \"$1\"; cat \"$1\"' sh %s").unwrap(),
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
        HighlightCommand::parse("sh -c 'echo \"$1\" >&2; exit 7' sh %s").unwrap(),
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
        highlight_text(HighlightCommand::parse("  ").unwrap(), "rs", "input")
            .await
            .unwrap_err()
            .to_string(),
        "The syntax highlighter is disabled."
    );
}

#[tokio::test]
async fn highlighter_rejects_invalid_output_missing_program_and_timeout() {
    let fixture = crate::test_support::fixture("highlighter");
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
                    .unwrap()
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
            HighlightCommand::parse("sh -c 'exec sleep 30'").unwrap(),
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
            .unwrap()
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
            .unwrap()
            .with_file(path)
            .unwrap()
            .argv,
        ["tool", "--file=/tmp/slopworld/highlight/code file.rs"]
    );
    assert!(
        HighlightCommand::parse("   ")
            .unwrap()
            .with_file(path)
            .is_err()
    );
}

#[test]
fn highlighter_rejects_incomplete_command_quoting() {
    assert!(HighlightCommand::parse("bat 'unfinished").is_err());
    assert!(themed_command("bat 'unfinished", "bat", "").is_err());
}
