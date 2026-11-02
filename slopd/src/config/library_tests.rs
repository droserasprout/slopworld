use super::*;
use crate::config::ProjectCfg;

#[test]
fn every_library_kind_requires_a_nonblank_name() {
    for kind in [
        LibraryItemKind::Prompt,
        LibraryItemKind::Shell,
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
fn prompts_require_text_but_no_execution_context() {
    let mut item = LibraryItemCfg {
        name: "guidance".into(),
        kind: LibraryItemKind::Prompt,
        text: "Read the project instructions".into(),
        ..Default::default()
    };
    let cfg = Config::default();
    check_library_item(&cfg, &item).unwrap();
    item.host = true;
    assert_eq!(
        check_library_item(&cfg, &item).unwrap_err().to_string(),
        "Choose a project for library item \"guidance\"."
    );
    item.host = false;
    item.text = " \n\t".into();
    assert_eq!(
        check_library_item(&cfg, &item).unwrap_err().to_string(),
        "Enter text for library item \"guidance\"."
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
            host: true,
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

#[test]
fn timers_require_an_explicit_agent_identity() {
    let cfg = Config {
        library: vec![LibraryItemCfg {
            name: "Review".into(),
            text: "Review progress".into(),
            ..Default::default()
        }],
        ..Default::default()
    };
    let mut item = LibraryItemCfg {
        name: "Scheduled review".into(),
        kind: LibraryItemKind::Timer,
        timer: Some(TimerCfg {
            enabled: true,
            action: "prompt".into(),
            trigger: "interval".into(),
            schedule: "15min".into(),
            target: String::new(),
            prompt: "Review".into(),
            ..Default::default()
        }),
        ..Default::default()
    };
    assert_eq!(
        check_library_item(&cfg, &item).unwrap_err().to_string(),
        "Choose a target agent."
    );
    item.timer.as_mut().unwrap().target = "0123456789abcdef".into();
    check_library_item(&cfg, &item).unwrap();
}

#[test]
fn timer_actions_require_their_own_inputs_and_unattended_shell_location() {
    let mut cfg = Config {
        projects: vec![super::super::ProjectCfg {
            name: "repo".into(),
            dir: "/repo".into(),
            ..Default::default()
        }],
        library: vec![
            LibraryItemCfg {
                name: "Review".into(),
                text: "Review changes".into(),
                ..Default::default()
            },
            LibraryItemCfg {
                name: "Cleanup".into(),
                kind: LibraryItemKind::Shell,
                text: "true".into(),
                host: true,
                link: LibraryItemLink::Temp,
                ..Default::default()
            },
        ],
        ..Default::default()
    };
    let mut item = LibraryItemCfg {
        name: "Timer".into(),
        kind: LibraryItemKind::Timer,
        timer: Some(TimerCfg {
            trigger: "interval".into(),
            schedule: "1h".into(),
            action: "worker".into(),
            template: "Reviewer".into(),
            project: "repo".into(),
            prompt: "Review".into(),
            ..Default::default()
        }),
        ..Default::default()
    };
    check_library_item(&cfg, &item).unwrap();
    item.timer.as_mut().unwrap().project.clear();
    assert!(
        check_library_item(&cfg, &item)
            .unwrap_err()
            .to_string()
            .contains("project")
    );
    let timer = item.timer.as_mut().unwrap();
    timer.action = "shell".into();
    timer.prompt.clear();
    timer.template.clear();
    timer.shell = "Cleanup".into();
    check_library_item(&cfg, &item).unwrap();
    cfg.library[1].link = LibraryItemLink::Ask;
    assert!(
        check_library_item(&cfg, &item)
            .unwrap_err()
            .to_string()
            .contains("saved location")
    );
    item.timer.as_mut().unwrap().shell = "Review".into();
    assert!(check_library_item(&cfg, &item).is_err());
    item.timer.as_mut().unwrap().action = "unknown".into();
    assert!(
        check_library_item(&cfg, &item)
            .unwrap_err()
            .to_string()
            .contains("Timer action")
    );
}
