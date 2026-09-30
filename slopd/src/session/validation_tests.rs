use super::*;

#[test]
fn every_library_kind_requires_a_nonblank_name() {
    for kind in [
        LibraryItemKind::Prompt,
        LibraryItemKind::Shell,
        LibraryItemKind::Breadcrumb,
        LibraryItemKind::FileAction,
    ] {
        let item = LibraryItemCfg {
            name: " \t\n".into(),
            kind,
            text: "content".into(),
            command: Some("pwd".into()),
            ..Default::default()
        };
        let error = check_library_item(&Config::default(), &item).unwrap_err();
        assert_eq!(error.to_string(), "Enter a name for the library item.");
    }
}

#[test]
fn breadcrumbs_require_text_but_no_execution_context() {
    let mut item = LibraryItemCfg {
        name: "guidance".into(),
        kind: LibraryItemKind::Breadcrumb,
        text: "Read the project instructions".into(),
        ..Default::default()
    };
    let cfg = Config::default();
    check_library_item(&cfg, &item).unwrap();
    item.text = " \n\t".into();
    assert_eq!(
        check_library_item(&cfg, &item).unwrap_err().to_string(),
        "Enter text for breadcrumb \"guidance\"."
    );
}

#[test]
fn runnable_library_items_cannot_choose_both_host_and_template() {
    for kind in [
        LibraryItemKind::Prompt,
        LibraryItemKind::Shell,
        LibraryItemKind::FileAction,
    ] {
        let item = LibraryItemCfg {
            name: "run".into(),
            kind,
            host: true,
            agent_template: "worker".into(),
            ..Default::default()
        };
        assert_eq!(
            check_library_item(&Config::default(), &item)
                .unwrap_err()
                .to_string(),
            "Choose either Host or an agent template for library item \"run\"."
        );
    }
}

#[test]
fn file_actions_require_a_command_instead_of_prompt_text() {
    let cfg = Config::default();
    let mut item = LibraryItemCfg {
        name: "inspect".into(),
        kind: LibraryItemKind::FileAction,
        host: true,
        ..Default::default()
    };
    for command in [None, Some(String::new()), Some(" \n\t".into())] {
        item.command = command;
        assert_eq!(
            check_library_item(&cfg, &item).unwrap_err().to_string(),
            "Enter a command for file action \"inspect\"."
        );
    }
    item.command = Some("cat {{ absolute_path }}".into());
    check_library_item(&cfg, &item).unwrap();
    item.text = " \t".into();
    check_library_item(&cfg, &item).unwrap();
    item.text = "unexpected prompt".into();
    assert_eq!(
        check_library_item(&cfg, &item).unwrap_err().to_string(),
        "Remove text from file action \"inspect\"."
    );
}

#[test]
fn deferred_library_destinations_allow_no_project_but_reject_unknown_projects() {
    let cfg = Config::default();
    for link in [LibraryItemLink::Ask, LibraryItemLink::Temp] {
        let mut item = LibraryItemCfg {
            name: "review".into(),
            link,
            text: "Review the changes".into(),
            ..Default::default()
        };
        check_library_item(&cfg, &item).unwrap();
        item.project = "missing".into();
        assert_eq!(
            check_library_item(&cfg, &item).unwrap_err().to_string(),
            "Project \"missing\" does not exist."
        );
    }
}

#[test]
fn config_patches_reject_null_at_any_depth() {
    for patch in [
        serde_json::json!({"daemon": {"bind": null}}),
        serde_json::json!({"project": [{"name": "repo"}, null]}),
    ] {
        assert_eq!(
            json_to_toml(patch).unwrap_err().to_string(),
            "The config patch cannot contain null values."
        );
    }
}

#[test]
fn config_patch_conversion_preserves_signed_integers_and_floats() {
    let converted = json_to_toml(serde_json::json!({
        "min": i64::MIN,
        "max": i64::MAX,
        "fraction": 1.25,
    }))
    .unwrap();
    assert_eq!(converted["min"].as_integer(), Some(i64::MIN));
    assert_eq!(converted["max"].as_integer(), Some(i64::MAX));
    assert_eq!(converted["fraction"].as_float(), Some(1.25));
}

#[test]
fn config_patches_replace_arrays_and_preserve_unknown_nested_fields() {
    let mut document = json_to_toml(serde_json::json!({
        "daemon": {"bind": "127.0.0.1:9999", "extension": {"enabled": true}},
        "project": [{"name": "old"}],
        "untouched": "keep",
    }))
    .unwrap();
    merge_toml(
        &mut document,
        json_to_toml(serde_json::json!({
            "daemon": {"bind": "127.0.0.1:8888", "new_field": 42},
            "project": [],
        }))
        .unwrap(),
    );
    assert_eq!(
        document,
        json_to_toml(serde_json::json!({
            "daemon": {
                "bind": "127.0.0.1:8888",
                "extension": {"enabled": true},
                "new_field": 42,
            },
            "project": [],
            "untouched": "keep",
        }))
        .unwrap()
    );
}

#[test]
fn file_action_paths_are_normalized_before_checking_project_containment() {
    let project = ProjectCfg {
        name: "repo".into(),
        dir: "/tmp/work/../repo".into(),
        ..Default::default()
    };
    assert_eq!(
        project_action_path(&project, "/tmp/repo/src/../file.rs").unwrap(),
        PathBuf::from("/tmp/repo/file.rs")
    );
    assert_eq!(
        project_action_path(&project, "/tmp/repo/../outside/file.rs")
            .unwrap_err()
            .to_string(),
        "File action path must be inside project \"repo\"."
    );
    assert_eq!(
        project_action_path(&project, " \t")
            .unwrap_err()
            .to_string(),
        "Enter a path for the file action."
    );
}
