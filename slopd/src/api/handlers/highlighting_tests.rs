use super::*;

#[test]
fn themes_are_request_local_and_preserve_command_arguments() {
    for (command, kind, theme, option) in [
        (
            "highlight --out-format=xterm256 %s",
            "highlight",
            "base16/monokai",
            "--style=base16/monokai",
        ),
        (
            "pygmentize -f terminal256 -O style=monokai",
            "pygments",
            "friendly",
            "style=friendly",
        ),
        (
            "'/opt/my tools/bat' --color=always -- %s",
            "bat",
            "Monokai Extended",
            "--theme=Monokai Extended",
        ),
    ] {
        assert_eq!(themed_command(command, kind, "").unwrap(), command);
        let themed = themed_command(command, kind, theme).unwrap();
        let argv = crate::sandbox::shell_split(&themed);
        assert!(argv.iter().any(|s| s == option));
        let before = crate::sandbox::shell_split(command);
        assert_eq!(argv[0], before[0]);
        let position = argv.iter().position(|s| s == option).unwrap();
        if let Some(file) = argv.iter().position(|s| s == "%s") {
            assert!(position < file);
        }
        if let Some(end) = argv.iter().position(|s| s == "--") {
            assert!(position < end);
        }
    }
    assert!(themed_command("bat", "highlight", "monokai").is_err());
    assert!(themed_command("wrapper bat", "bat", "monokai").is_err());
    for theme in ["../../tmp/theme", "/tmp/theme", "x\n--other", "$(whoami)"] {
        assert!(themed_command("highlight", "highlight", theme).is_err());
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
    assert!(parse_themes("pygments", "{}").is_err());
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
        let Proto(result) = super::super::files::highlight(State(manager.clone()), Proto(request))
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
    let request = serde_json::from_value(json!({"text":"sample", "language":"rs", "engine":"bat", "theme":"DraftTheme", "command":command})).unwrap();
    let Proto(preview) = super::super::files::highlight(State(manager.clone()), Proto(request))
        .await
        .unwrap();
    assert!(preview.text.contains("--theme=DraftTheme"));
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
    let request = serde_json::from_value(json!({"text":"sample", "command":""})).unwrap();
    assert!(
        super::super::files::highlight(State(manager.clone()), Proto(request))
            .await
            .is_err()
    );
    assert_eq!(manager.config().await.commands.highlighter, original);
}
