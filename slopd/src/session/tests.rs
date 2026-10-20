use super::LaunchContext;
use crate::session::{AgentLifetime, SessionRole};
use std::collections::HashMap;
use std::sync::Arc;

use super::{
    INPUT_BATCH, Input, Live, LiveCapture, LiveInput, State, TitleCapture, check_name, free_name,
    free_project_name, merge_input, settle, slug, strip_sgr,
};
use crate::config::{Config, ProjectCfg, SessionCfg};

#[test]
fn merges_a_run_of_mouse_reports() {
    let batch = merge_input(vec![
        Input::Bytes(b"\x1b[<64;1;1M".to_vec()),
        Input::Bytes(b"\x1b[<64;1;1M".to_vec()),
        Input::Bytes(b"\x1b[<64;1;1M".to_vec()),
    ]);
    assert_eq!(batch.len(), 1);
    match &batch[0] {
        Input::Bytes(b) => assert_eq!(b, &b"\x1b[<64;1;1M".repeat(3)),
        _ => panic!("wrong kind"),
    }
}

#[test]
fn stops_merging_at_the_command_ceiling() {
    let items: Vec<Input> = (0..600).map(|_| Input::Bytes(vec![b'x'; 2])).collect();
    let batch = merge_input(items);
    assert!(batch.len() > 1, "1200 bytes must not become one command");
    let bytes: Vec<_> = batch
        .iter()
        .flat_map(|item| match item {
            Input::Bytes(bytes) => bytes.clone(),
            _ => panic!("wrong kind"),
        })
        .collect();
    assert_eq!(bytes, vec![b'x'; 1200]);
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
    assert!(matches!(&batch[0], Input::Bytes(bytes) if bytes == b"ab"));
    assert!(matches!(&batch[2], Input::Bytes(bytes) if bytes == b"cd"));
    assert!(matches!(&batch[3], Input::Keys { keys, literal: false } if keys == &["Enter"]));
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
    assert!(matches!(&batch[1], Input::Keys { keys, literal: true } if keys == &["hi"]));
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
        launch: LaunchContext::default(),
        role: SessionRole::Agent(AgentLifetime::Temporary),
        host_path: String::new(),
        state: State::Down,
        process_running: false,
        seq: 0,
        retick_seq: 0,
        hash: 0,
        activity_hash: 0,
        last_change: 0,
        activity_at: None,
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
fn project_mounts_round_trip_through_toml() {
    let project: ProjectCfg = toml::from_str(
        r#"
        name = "main"
        dir = "/tmp"
        mounts = [{ from = "/tmp", to = "/mnt/lib", mode = "ro" }]
    "#,
    )
    .unwrap();
    let project: ProjectCfg = toml::from_str(&toml::to_string(&project).unwrap()).unwrap();
    assert_eq!(project.mounts.len(), 1);
    assert_eq!(project.mounts[0].from, "/tmp");
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
    assert!(
        matches!(result[1], Input::Gap(duration) if duration == std::time::Duration::from_millis(1))
    );
    assert!(matches!(&result[2], Input::Bytes(bytes) if bytes == &[5]));
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
    assert!(live.role.is_durable_agent());
    assert!(!live.role.is_host());
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
