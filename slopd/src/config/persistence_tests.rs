use super::*;
use crate::config::catalog::{load_library, validate_library_name};

async fn save_library(
    dirs: &[(LibraryItemKind, PathBuf)],
    library: &[LibraryItemCfg],
) -> Result<()> {
    let path = dirs[0].1.parent().unwrap().join("config.toml");
    let binding = crate::config::fixtures::binding(&path)?;
    let changes = crate::config::catalog::replacement_changes(&binding, library).await?;
    let gate = std::sync::Arc::new(tokio::sync::Mutex::new(()))
        .lock_owned()
        .await;
    crate::storage::transaction::commit_in_operation(&binding, changes, &gate).await
}

const FIRST_ID: &str = "1111111111114111";

struct LibraryFixture(PathBuf);

impl LibraryFixture {
    fn new() -> Self {
        Self(std::env::temp_dir().join(format!("slopd-library-{}", uuid::Uuid::new_v4())))
    }

    fn dirs(&self) -> Vec<(LibraryItemKind, PathBuf)> {
        Config::library_dirs_for(&self.0.join("config.toml"))
    }

    async fn write(&self, relative: &str, text: &str) {
        let path = self.0.join(relative);
        tokio::fs::create_dir_all(path.parent().unwrap())
            .await
            .unwrap();
        tokio::fs::write(path, text).await.unwrap();
    }
}

impl Drop for LibraryFixture {
    fn drop(&mut self) {
        drop(std::fs::remove_dir_all(&self.0));
    }
}

#[test]
fn library_names_reject_traversal_and_controls_but_allow_readable_names() {
    for name in [
        "",
        " \t",
        ".",
        "..",
        "../escape",
        "/absolute",
        "a/b",
        "a\\b",
        "a\0b",
        "a\nb",
        "a\u{7f}b",
    ] {
        assert!(validate_library_name(name).is_err(), "accepted {name:?}");
    }
    for name in ["review diff", "日本語", ".hidden", "a..b"] {
        validate_library_name(name).unwrap();
    }
}

#[tokio::test]
async fn library_load_ignores_missing_directories_and_non_toml_files() {
    let fixture = LibraryFixture::new();
    assert!(load_library(&fixture.dirs()).await.unwrap().is_empty());
    fixture.write("prompts/notes.txt", "not TOML").await;
    fixture
        .write(
            "prompts/z.toml",
            "name = 'z'\nlink = 'project'\ntext = 'last'",
        )
        .await;
    fixture
        .write(
            "prompts/a.toml",
            "name = 'a'\nlink = 'project'\ntext = 'first'",
        )
        .await;
    let loaded = load_library(&fixture.dirs()).await.unwrap();
    assert_eq!(
        loaded
            .iter()
            .map(|item| item.name.as_str())
            .collect::<Vec<_>>(),
        ["a", "z"]
    );
    assert!(loaded.iter().all(|item| !item.builtin));
}

#[tokio::test]
async fn library_load_rejects_invalid_catalog_files_with_path_context() {
    for (text, expected) in [
        ("not valid TOML", "parsing library item"),
        (
            "name = 'entry'\nlink = 'project'\nkind = 'breadcrumb'",
            "belongs in",
        ),
        ("name = 'different'\nlink = 'project'", "expected \"entry\""),
        ("name = '../escape'\nlink = 'project'", "one safe file name"),
    ] {
        let fixture = LibraryFixture::new();
        fixture.write("prompts/entry.toml", text).await;
        let error = format!("{:#}", load_library(&fixture.dirs()).await.unwrap_err());
        assert!(error.contains(expected), "{error}");
        assert!(error.contains("entry.toml"), "{error}");
    }
}

#[tokio::test]
async fn library_load_rejects_duplicate_names_across_kinds() {
    let fixture = LibraryFixture::new();
    fixture
        .write("prompts/shared.toml", "name = 'shared'\nlink = 'project'")
        .await;
    fixture
        .write(
            "breadcrumbs/shared.toml",
            "name = 'shared'\nlink = 'project'\nkind = 'breadcrumb'",
        )
        .await;
    let error = load_library(&fixture.dirs()).await.unwrap_err();
    assert_eq!(
        error.to_string(),
        "Declare library item \"shared\" only once."
    );
}

#[tokio::test]
async fn invalid_library_saves_leave_existing_catalog_files_untouched() {
    let fixture = LibraryFixture::new();
    let original = "name = 'keep'\nlink = 'project'\ntext = 'original'";
    fixture.write("prompts/keep.toml", original).await;
    let item = LibraryItemCfg {
        name: "duplicate".into(),
        ..Default::default()
    };
    for items in [
        vec![LibraryItemCfg {
            name: "../escape".into(),
            ..Default::default()
        }],
        vec![
            item.clone(),
            LibraryItemCfg {
                kind: LibraryItemKind::Breadcrumb,
                ..item
            },
        ],
    ] {
        assert!(save_library(&fixture.dirs(), &items).await.is_err());
        assert_eq!(
            tokio::fs::read_to_string(fixture.0.join("prompts/keep.toml"))
                .await
                .unwrap(),
            original
        );
        assert!(!fixture.0.join("prompts/duplicate.toml").exists());
        assert!(!fixture.0.join("breadcrumbs/duplicate.toml").exists());
        assert!(!fixture.0.join("escape.toml").exists());
    }
}

#[tokio::test]
async fn missing_main_config_keeps_existing_library_catalog() {
    let fixture = LibraryFixture::new();
    fixture
        .write(
            "prompts/keep.toml",
            "name = 'keep'\nlink = 'project'\ntext = 'saved'",
        )
        .await;
    let path = fixture.0.join("config.toml");
    let loaded = Config::load(&path).await.unwrap();
    assert_eq!(loaded.library.len(), 1);
    assert_eq!(loaded.library[0].text, "saved");
    assert!(fixture.0.join("prompts/keep.toml").exists());
}

#[tokio::test]
async fn malformed_existing_config_is_never_replaced_by_typed_save() {
    let fixture = LibraryFixture::new();
    let path = fixture.0.join("config.toml");
    fixture.write("config.toml", "[daemon\nrecover me").await;
    assert!(
        crate::config::fixtures::save(&Config::default(), &path)
            .await
            .is_err()
    );
    assert_eq!(
        tokio::fs::read_to_string(&path).await.unwrap(),
        "[daemon\nrecover me"
    );
}

#[tokio::test]
async fn library_save_moves_kinds_and_removes_stale_toml_only() {
    let fixture = LibraryFixture::new();
    fixture
        .write("prompts/moved.toml", "name = 'moved'\nlink = 'project'")
        .await;
    fixture
        .write("prompts/deleted.toml", "name = 'deleted'\nlink = 'project'")
        .await;
    fixture.write("prompts/notes.txt", "keep me").await;
    let item = LibraryItemCfg {
        name: "moved".into(),
        kind: LibraryItemKind::Breadcrumb,
        text: "updated".into(),
        ..Default::default()
    };
    save_library(&fixture.dirs(), &[item]).await.unwrap();
    assert!(!fixture.0.join("prompts/moved.toml").exists());
    assert!(!fixture.0.join("prompts/deleted.toml").exists());
    assert_eq!(
        tokio::fs::read_to_string(fixture.0.join("prompts/notes.txt"))
            .await
            .unwrap(),
        "keep me"
    );
    let loaded = load_library(&fixture.dirs()).await.unwrap();
    assert_eq!(loaded.len(), 1);
    assert_eq!(loaded[0].name, "moved");
    assert_eq!(loaded[0].kind, LibraryItemKind::Breadcrumb);
    assert_eq!(loaded[0].text, "updated");
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        let metadata = std::fs::metadata(fixture.0.join("breadcrumbs/moved.toml")).unwrap();
        assert_eq!(metadata.permissions().mode() & 0o777, 0o600);
    }
}

#[tokio::test]
async fn clearing_modeled_settings_preserves_only_unknown_fields() {
    let root = std::env::temp_dir().join(format!("slopd-clear-settings-{}", uuid::Uuid::new_v4()));
    tokio::fs::create_dir_all(&root).await.unwrap();
    let path = root.join("config.toml");
    let mut cfg = Config {
        projects: vec![ProjectCfg {
            name: "repo".into(),
            dir: "/tmp".into(),
            ..Default::default()
        }],
        sessions: vec![
            toml::from_str(&format!(
                r#"
            name = "agent"
            project = "repo"
            state_id = "{FIRST_ID}"
            sandbox = ["git"]
            persistent_tmp = true
            cmd = "old command"
            label = "old label"
            [limits]
            memory_mb = 512
            pids = 100
        "#
            ))
            .unwrap(),
        ],
        ..Default::default()
    };
    cfg.daemon.bind = "127.0.0.1:7777".into();
    cfg.daemon.token = "secret".into();
    crate::config::fixtures::save(&cfg, &path).await.unwrap();
    let agent_path = root.join(format!("data/agents/{FIRST_ID}.toml"));
    let text = tokio::fs::read_to_string(&agent_path).await.unwrap();
    tokio::fs::write(&agent_path, format!("future_agent = 'keep'\n{text}"))
        .await
        .unwrap();
    let text = tokio::fs::read_to_string(&path)
        .await
        .unwrap()
        .replace("[daemon]", "[daemon]\nfuture_policy = 'keep'");
    tokio::fs::write(&path, text).await.unwrap();
    let original = tokio::fs::read_to_string(&path).await.unwrap();
    let mut config = Config::load(&path).await.unwrap();
    assert_eq!(tokio::fs::read_to_string(&path).await.unwrap(), original);
    let agent = &mut config.sessions[0];
    agent.sandbox.clear();
    agent.persistent_tmp = false;
    agent.cmd = None;
    agent.label = None;
    agent.limits.memory_mb = None;
    crate::config::fixtures::save(&config, &path).await.unwrap();
    let mut reloaded = Config::load(&path).await.unwrap();
    let agent = &reloaded.sessions[0];
    assert!(agent.sandbox.is_empty());
    assert!(!agent.persistent_tmp);
    assert!(agent.cmd.is_none() && agent.label.is_none());
    assert_eq!(agent.limits.memory_mb, None);
    assert_eq!(agent.limits.pids, Some(100));
    reloaded.sessions[0].limits = Limits::default();
    crate::config::fixtures::save(&reloaded, &path)
        .await
        .unwrap();
    assert!(
        Config::load(&path).await.unwrap().sessions[0]
            .limits
            .is_empty()
    );
    let text = tokio::fs::read_to_string(&path).await.unwrap();
    assert!(text.contains("future_policy = \"keep\""));
    assert!(
        tokio::fs::read_to_string(&agent_path)
            .await
            .unwrap()
            .contains("future_agent")
    );
    assert_eq!(reloaded.daemon.token, "secret");
    tokio::fs::remove_dir_all(root).await.unwrap();
}

#[tokio::test]
async fn library_items_round_trip_as_one_file_each() {
    let root = std::env::temp_dir().join(format!(
        "slopd-library-files-{}-{}",
        std::process::id(),
        uuid::Uuid::new_v4()
    ));
    let path = root.join("config.toml");
    let mut cfg = Config::default();
    cfg.library.push(LibraryItemCfg {
        name: "review diff".into(),
        text: "review it".into(),
        ..Default::default()
    });
    cfg.library.push(LibraryItemCfg {
        name: "run tests".into(),
        kind: LibraryItemKind::Shell,
        text: "make test".into(),
        ..Default::default()
    });
    cfg.library.push(LibraryItemCfg {
        name: "tips".into(),
        kind: LibraryItemKind::Breadcrumb,
        text: "remember the tests".into(),
        ..Default::default()
    });
    cfg.library.push(LibraryItemCfg {
        name: "show size".into(),
        kind: LibraryItemKind::FileAction,
        command: Some("du -sh".into()),
        ..Default::default()
    });

    crate::config::fixtures::save(&cfg, &path).await.unwrap();
    let document = tokio::fs::read_to_string(&path).await.unwrap();
    assert!(!document.contains("[[library]]"));
    let item_path = root.join("prompts/review diff.toml");
    assert!(item_path.is_file());
    assert!(root.join("shell_scripts/run tests.toml").is_file());
    assert!(root.join("breadcrumbs/tips.toml").is_file());
    assert!(root.join("file_actions/show size.toml").is_file());
    let loaded = Config::load(&path).await.unwrap();
    assert_eq!(loaded.library.len(), cfg.library.len());
    for expected in &cfg.library {
        let actual = loaded.library_item(&expected.name).unwrap();
        assert_eq!(actual.kind, expected.kind);
        assert_eq!(actual.text, expected.text);
        assert_eq!(actual.command, expected.command);
    }
    tokio::fs::remove_dir_all(root).await.unwrap();
}

#[tokio::test]
async fn inline_workspace_is_rejected_without_rewriting_it() {
    let fixture = LibraryFixture::new();
    let path = fixture.0.join("config.toml");
    std::fs::create_dir_all(&fixture.0).unwrap();
    for section in ["project", "session", "host_terminal", "library"] {
        let text = format!("[[{section}]]\nname = 'retired'\n");
        tokio::fs::write(&path, &text).await.unwrap();
        Config::load(&path).await.unwrap_err();
        assert!(
            crate::config::fixtures::save(&Config::default(), &path)
                .await
                .is_err()
        );
        assert_eq!(tokio::fs::read_to_string(&path).await.unwrap(), text);
    }
}

#[test]
fn legacy_state_rules_are_ignored_and_not_exposed_in_modeled_config() {
    let cfg = super::super::settings::document::replace(
        &Config::default(),
        r#"
[[state_rule]]
state = "waiting"
pattern = '['
"#,
    )
    .expect("obsolete rules must not prevent startup")
    .candidate;
    let value = serde_json::to_value(&cfg).unwrap();
    assert!(value.get("state_rule").is_none());
    assert!(!toml::to_string(&cfg).unwrap().contains("state_rule"));
}
