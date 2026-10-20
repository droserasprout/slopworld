use super::*;
use serde_json::json;

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
    let manager = crate::application::test_application(cfg);
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
        assert_eq!(
            manager.sessions.config().await.commands.highlighter,
            original
        );
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
    let manager = crate::application::test_application(cfg);
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
    assert_eq!(
        manager.sessions.config().await.commands.highlighter,
        original
    );
    let request = serde_json::from_value(json!({"text":"sample", "language":"rs", "engine":"bat", "theme":"DraftTheme", "command":command})).unwrap();
    let Proto(preview) = highlight(State(manager.clone()), Proto(request))
        .await
        .unwrap();
    assert!(preview.text.contains("--theme=DraftTheme"));
    assert_eq!(
        manager.sessions.config().await.commands.highlighter,
        original
    );
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
    assert_eq!(
        manager.sessions.config().await.commands.highlighter,
        original
    );
    let request = serde_json::from_value(json!({"text":"sample", "command":""})).unwrap();
    let Proto(preview) = highlight(State(manager.clone()), Proto(request))
        .await
        .unwrap();
    assert_eq!(preview.text, "sample");
    assert_eq!(preview.bytes, 6);
    assert_eq!(
        manager.sessions.config().await.commands.highlighter,
        original
    );
}
