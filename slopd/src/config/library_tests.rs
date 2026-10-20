use super::*;
use crate::config::ProjectCfg;

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
fn an_entry_must_have_something_to_send() {
    let mut cfg = Config::default();
    cfg.projects.push(ProjectCfg {
        name: "repo".into(),
        dir: "/home/you/git/repo".into(),
        ..Default::default()
    });

    let errand = LibraryItemCfg {
        name: "view-main-rs".into(),
        kind: LibraryItemKind::Shell,
        project: "repo".into(),
        command: Some("less -R -- /home/you/git/repo/main.rs".into()),
        text: String::new(),
        ..Default::default()
    };
    assert!(check_library_item(&cfg, &errand).is_err());

    let nowhere = LibraryItemCfg {
        project: String::new(),
        text: "hello".into(),
        ..errand.clone()
    };
    assert!(check_library_item(&cfg, &nowhere).is_err());

    let gone = LibraryItemCfg {
        project: "not-a-project".into(),
        text: "hello".into(),
        ..errand.clone()
    };
    assert!(check_library_item(&cfg, &gone).is_err());

    let fine = LibraryItemCfg {
        text: "hello".into(),
        ..errand
    };
    check_library_item(&cfg, &fine).unwrap();
}
