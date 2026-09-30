use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::sync::Arc;

use super::{
    check_library_item, check_name, check_project, free_name, free_project_name,
    hold_action_command, json_to_toml, merge_input, merge_toml, normalize_action_command,
    normalize_path, project_action_path, settle, slug, strip_sgr, Input, Live, LiveCapture,
    LiveInput, State, TitleCapture, INPUT_BATCH,
};
use crate::config::{Config, LibraryItemCfg, LibraryItemKind, ProjectCfg, SessionCfg};

#[test]
fn file_action_paths_normalize_the_absolute_placeholder() {
    let path = normalize_path(Path::new("/tmp/slop/../repo/file name"));
    assert_eq!(path, PathBuf::from("/tmp/repo/file name"));
    assert_eq!(
        normalize_action_command(&path, "du -sh {{ absolute_path }}"),
        "du -sh '/tmp/repo/file name'"
    );
    assert_eq!(
        crate::sandbox::shell_split(&hold_action_command("du -sh '/tmp/repo/file name'")),
        vec![
            "bash",
            "-lc",
            "du -sh '/tmp/repo/file name'; exec \"${SHELL:-bash}\""
        ]
    );
}

#[test]
fn config_patches_merge_nested_fields_without_resetting_unmentioned_values() {
    let mut document: toml::Value = toml::from_str(
        r#"
[daemon]
bind = "127.0.0.1:7717"
token = "secret"
future = "keep"

[defaults]
agent = "claude"
shell = "bash"
"#,
    )
    .expect("config parses");

    let patch = json_to_toml(serde_json::json!({
        "daemon": { "usage_poll_secs": 120 },
    }))
    .expect("patch converts");
    merge_toml(&mut document, patch);

    assert_eq!(
        document["daemon"]["usage_poll_secs"].as_integer(),
        Some(120)
    );
    assert_eq!(document["daemon"]["future"].as_str(), Some("keep"));
    assert_eq!(document["defaults"]["agent"].as_str(), Some("claude"));
    assert_eq!(document["daemon"]["token"].as_str(), Some("secret"));
}

#[test]
fn merges_a_run_of_mouse_reports() {
    let batch = merge_input(vec![
        Input::Bytes(b"\x1b[<64;1;1M".to_vec()),
        Input::Bytes(b"\x1b[<64;1;1M".to_vec()),
        Input::Bytes(b"\x1b[<64;1;1M".to_vec()),
    ]);
    assert_eq!(batch.len(), 1);
    match &batch[0] {
        Input::Bytes(b) => assert_eq!(b.len(), 30),
        _ => panic!("wrong kind"),
    }
}

#[test]
fn stops_merging_at_the_command_ceiling() {
    let items: Vec<Input> = (0..600).map(|_| Input::Bytes(vec![b'x'; 2])).collect();
    let batch = merge_input(items);
    assert!(batch.len() > 1, "1200 bytes must not become one command");
    for item in &batch {
        match item {
            Input::Bytes(b) => assert!(b.len() <= INPUT_BATCH),
            _ => panic!("wrong kind"),
        }
    }
}

#[test]
fn a_paste_is_never_merged_and_never_reordered() {
    let batch = merge_input(vec![
        Input::Bytes(b"ab".to_vec()),
        Input::Paste {
            bytes: b"hello".to_vec(),
        },
        Input::Bytes(b"cd".to_vec()),
        Input::Keys {
            keys: vec!["Enter".into()],
            literal: false,
        },
    ]);
    assert_eq!(batch.len(), 4);
    assert!(matches!(
        &batch[1],
        Input::Paste {
            bytes
        } if bytes == b"hello"
    ));
    assert!(matches!(&batch[3], Input::Keys { .. }));
}

#[test]
fn literal_and_named_keys_do_not_share_a_command() {
    let batch = merge_input(vec![
        Input::Keys {
            keys: vec!["Up".into()],
            literal: false,
        },
        Input::Keys {
            keys: vec!["Down".into()],
            literal: false,
        },
        Input::Keys {
            keys: vec!["hi".into()],
            literal: true,
        },
    ]);
    assert_eq!(batch.len(), 2);
    match &batch[0] {
        Input::Keys { keys, literal } => {
            assert_eq!(keys, &["Up".to_string(), "Down".to_string()]);
            assert!(!literal);
        }
        _ => panic!("wrong kind"),
    }
}

fn placeholder() -> Live {
    Live {
        cfg: SessionCfg::default(),
        ephemeral: true,
        host: false,
        persistent_host: false,
        host_path: String::new(),
        state: State::Down,
        process_running: false,
        seq: 0,
        retick_seq: 0,
        hash: 0,
        activity_hash: 0,
        last_change: 0,
        state_since: 0,
        bell: false,
        cols: Live::BOOT_COLS,
        rows: Live::BOOT_ROWS,
        screen: None,
        capture: LiveCapture::default(),
        input: LiveInput::default(),
        run_id: 0,
        title: TitleCapture::default(),
    }
}

#[test]
fn slugs_are_names_tmux_accepts() {
    assert_eq!(slug("review diff"), "review-diff");
    assert_eq!(slug("run make test"), "run-make-test");
    assert_eq!(slug("v1.2 checks"), "v1-2-checks");
    assert_eq!(slug("  spaced  out  "), "spaced-out");
    assert_eq!(slug(" . "), "library");
    assert_eq!(slug(""), "library");

    for name in ["review diff", "v1.2 checks", "a/b", "", " . "] {
        assert!(check_name(&slug(name)).is_ok(), "slug of {name:?}");
    }
}

#[test]
fn free_name_counts_up_past_config_live_and_host_tables() {
    let mut cfg = Config::default();
    let mut live: HashMap<String, Live> = HashMap::new();

    assert_eq!(free_name(&live, &cfg, "review-diff"), "review-diff");

    cfg.sessions.push(SessionCfg {
        name: "review-diff".into(),
        ..Default::default()
    });
    assert_eq!(free_name(&live, &cfg, "review-diff"), "review-diff-2");

    live.insert("review-diff-2".into(), placeholder());
    assert_eq!(free_name(&live, &cfg, "review-diff"), "review-diff-3");

    cfg.host_terminals.push(crate::config::HostTerminalCfg {
        name: "review-diff-3".into(),
        ..Default::default()
    });
    assert_eq!(free_name(&live, &cfg, "review-diff"), "review-diff-4");
}

#[test]
fn temp_projects_settle_on_a_path_under_the_root() {
    let mut p = ProjectCfg {
        name: "scratch pad".into(),
        temp: true,
        ..Default::default()
    };
    settle(&mut p);
    assert_eq!(p.dir, "/tmp/slopworld/scratch-pad");

    p.dir = "/home/you/git/repo".into();
    settle(&mut p);
    assert_eq!(p.dir, "/tmp/slopworld/scratch-pad");

    let mut plain = ProjectCfg {
        name: "repo".into(),
        dir: "/home/you/git/repo".into(),
        ..Default::default()
    };
    settle(&mut plain);
    assert_eq!(plain.dir, "/home/you/git/repo");
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
fn temp_project_names_dodge_both_tables() {
    let mut cfg = Config::default();
    let mut temp: HashMap<String, ProjectCfg> = HashMap::new();

    assert_eq!(free_project_name(&cfg, &temp, "review-diff"), "review-diff");

    cfg.projects.push(ProjectCfg {
        name: "review-diff".into(),
        ..Default::default()
    });
    assert_eq!(
        free_project_name(&cfg, &temp, "review-diff"),
        "review-diff-2"
    );

    temp.insert("review-diff-2".into(), ProjectCfg::default());
    assert_eq!(
        free_project_name(&cfg, &temp, "review-diff"),
        "review-diff-3"
    );
}

#[test]
fn rejects_names_tmux_would_read_as_targets() {
    check_name("claude").unwrap();
    check_name("claude-2").unwrap();
    assert!(check_name("").is_err());
    assert!(check_name("two words").is_err());
    assert!(check_name("win:pane").is_err());
    assert!(check_name("dot.ted").is_err());
}

#[test]
fn strips_color_and_keeps_text() {
    assert_eq!(strip_sgr("\x1b[31mred\x1b[0m done"), "red done");
    assert_eq!(strip_sgr("esc to interrupt"), "esc to interrupt");
    assert_eq!(strip_sgr("\x1b]0;title\x07body"), "body");
    assert_eq!(strip_sgr("\x1b[38;5;214m❯ 1.\x1b[m"), "❯ 1.");
}

#[test]
fn json_patch_conversion_rejects_null_and_preserves_nested_values() {
    let value = json_to_toml(serde_json::json!({
        "enabled": true,
        "count": 3,
        "nested": ["one", false],
    }))
    .unwrap();
    assert_eq!(value["enabled"].as_bool(), Some(true));
    assert_eq!(value["count"].as_integer(), Some(3));
    assert_eq!(value["nested"][0].as_str(), Some("one"));
    assert_eq!(value["nested"][1].as_bool(), Some(false));
    json_to_toml(serde_json::Value::Null).unwrap_err();
}

#[test]
fn a_toml_table_patch_can_replace_a_scalar() {
    let mut base = toml::Value::String("old".into());
    merge_toml(
        &mut base,
        toml::toml! {
            replacement = "new"
        }
        .into(),
    );
    assert_eq!(base["replacement"].as_str(), Some("new"));
}

#[test]
fn file_actions_stay_inside_the_project_root() {
    let project = ProjectCfg {
        name: "repo".into(),
        dir: "/tmp/slopworld-project".into(),
        ..Default::default()
    };
    assert_eq!(
        project_action_path(&project, "/tmp/slopworld-project/src/main.rs").unwrap(),
        PathBuf::from("/tmp/slopworld-project/src/main.rs")
    );
    let error = project_action_path(&project, "/tmp/slopworld-project-other/file")
        .unwrap_err()
        .to_string();
    assert!(error.contains("inside project"), "{error}");
}

#[test]
fn check_project_rejects_unsafe_names() {
    for name in ["../escape", "one/two", "/tmp/escape", ".", "..", r"one\two"] {
        let error = check_project(&ProjectCfg {
            name: name.into(),
            dir: "/tmp".into(),
            ..Default::default()
        })
        .unwrap_err()
        .to_string();
        assert!(error.contains("path component"), "{name:?}: {error}");
    }
}

#[test]
fn project_mounts_round_trip_through_toml() {
    let cfg = Config::parse(
        r#"
            [[project]]
            name = "main"
            dir = "/tmp"
            mounts = [{ from = "/tmp", to = "/mnt/lib", mode = "ro" }]

            [[project]]
            name = "lib"
            dir = "/tmp"
            "#,
    )
    .unwrap();

    let project = cfg.project("main").unwrap();
    assert_eq!(project.mounts.len(), 1);
    assert_eq!(project.mounts[0].to, "/mnt/lib");
    assert_eq!(project.mounts[0].mode, crate::config::MountMode::Ro);
}

#[test]
fn input_tracing_preserves_merging_and_each_request_identity() {
    let first = Arc::new(crate::latency::InputTrace::new("a", 1));
    let second = Arc::new(crate::latency::InputTrace::new("b", 2));
    let result = merge_input(vec![
        Input::Bytes(vec![1]),
        Input::Traced(Box::new(Input::Bytes(vec![2])), vec![first.clone()]),
        Input::Traced(Box::new(Input::Bytes(vec![3])), vec![second.clone()]),
        Input::Bytes(vec![4]),
        Input::Gap(std::time::Duration::from_millis(1)),
        Input::Bytes(vec![5]),
    ]);
    assert_eq!(result.len(), 3);
    let Input::Traced(item, traces) = &result[0] else {
        panic!("missing trace")
    };
    assert!(matches!(item.as_ref(), Input::Bytes(bytes) if bytes == &[1, 2, 3, 4]));
    assert_eq!(traces.len(), 2);
    assert!(Arc::ptr_eq(&traces[0], &first));
    assert!(Arc::ptr_eq(&traces[1], &second));
    assert!(matches!(result[1], Input::Gap(_)));
}

#[test]
fn new_live_starts_as_a_boot_placeholder() {
    let cfg = SessionCfg {
        name: "agent".into(),
        project: "project".into(),
        ..Default::default()
    };
    let live = Live::new(cfg.clone(), TitleCapture::default());

    assert_eq!(live.cfg.name, cfg.name);
    assert_eq!(live.cfg.project, cfg.project);
    assert!(!live.ephemeral);
    assert!(!live.host);
    assert!(live.host_path.is_empty());
    assert_eq!(live.state, State::Down);
    assert_eq!((live.cols, live.rows), (Live::BOOT_COLS, Live::BOOT_ROWS));
    assert_eq!(live.seq, 0);
    assert_eq!(live.state_since, 0);
    assert!(!live.bell);
    assert!(live.screen.is_none());
    assert!(live.capture.emu.is_none());
    assert!(live.capture.reader.is_none());
    assert!(live.input.sender.is_none());
}
