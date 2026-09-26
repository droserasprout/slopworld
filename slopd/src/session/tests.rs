use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::sync::Arc;

use super::{
    check_library_item, check_name, check_project, compile_rules, free_name, free_project_name,
    hold_action_command, json_to_toml, match_rules, merge_input, merge_toml,
    normalize_action_command, normalize_path, project_action_path, prompt_is_long_enough,
    render_template, render_template_with, settle, slug, strip_sgr, title_agent, title_settings,
    Composer, Input, Live, LiveCapture, LiveInput, State, Submission, TemplateVars, TitleAgent,
    TitleCapture, INPUT_BATCH, TAIL_LINES,
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
fn codex_composer_recovers_the_submitted_prompt() {
    let mut c = Composer::ready();
    c.literal("fix teh parser");
    for _ in 0..8 {
        c.key("Left");
    }
    c.key("BSpace");
    c.literal("he");
    c.key("DC");
    let Some(Submission::Prompt(prompt)) = c.key("Enter") else {
        panic!("expected a prompt")
    };
    assert_eq!(prompt, "fix the parser");
}

#[test]
fn codex_composer_rearms_new_and_skips_uncertain_input() {
    let mut c = Composer::ready();
    c.literal("/new parser work");
    let Some(Submission::New(name)) = c.key("Enter") else {
        panic!("expected a boundary")
    };
    assert_eq!(name.as_deref(), Some("parser work"));

    c.literal("history entry");
    c.key("Up");
    assert!(c.key("Enter").is_none());
    c.literal("fresh prompt");
    assert!(matches!(c.key("Enter"), Some(Submission::Prompt(_))));
}

#[test]
fn codex_composer_handles_common_controls_and_resynchronizes() {
    let mut c = Composer::ready();
    c.literal("fix parser");
    c.key("Home");
    c.key("C-f");
    c.key("C-d");
    c.literal("i");
    let Some(Submission::Prompt(prompt)) = c.key("C-j") else {
        panic!("expected Ctrl-J to submit")
    };
    assert_eq!(prompt, "fix parser");

    c.literal("stale input");
    c.key("Escape");
    c.literal("replacement");
    assert!(c.key("Enter").is_none());
    c.literal("fresh prompt");
    assert!(matches!(c.key("Enter"), Some(Submission::Prompt(_))));
}

#[test]
fn waiting_dialog_answers_are_not_prompt_titles() {
    assert!(super::is_dialog_answer(" yes "));
    assert!(super::is_dialog_answer("1"));
    assert!(!super::is_dialog_answer("fix the parser"));
}

#[test]
fn prompt_minimum_counts_unicode_characters() {
    assert!(prompt_is_long_enough("commit", 0));
    assert!(prompt_is_long_enough("12345678901234567890", 20));
    assert!(prompt_is_long_enough("áéíóú", 5));
    assert!(!prompt_is_long_enough("commit", 20));
}

#[test]
fn title_agents_cover_presets_and_explicit_commands() {
    let mut cfg = Config::default();
    cfg.daemon.title_model = "shared-title".into();

    let codex = SessionCfg {
        cmd: Some("codex --yolo".into()),
        ..Default::default()
    };
    assert!(matches!(title_agent(&cfg, &codex), Some(TitleAgent::Codex)));

    let pi = SessionCfg {
        cmd: Some("pi --model test".into()),
        ..Default::default()
    };
    assert!(matches!(title_agent(&cfg, &pi), Some(TitleAgent::Pi)));
    assert_eq!(title_settings(&cfg, &pi, false).unwrap().1, "shared-title");

    let labeled = SessionCfg {
        label: Some("keep this name".into()),
        cmd: Some("pi --model test".into()),
        ..Default::default()
    };
    assert!(title_settings(&cfg, &labeled, false).is_none());

    let host = SessionCfg {
        cmd: Some("bash".into()),
        ..Default::default()
    };
    assert!(title_settings(&cfg, &host, true).is_none());

    let other = SessionCfg {
        cmd: Some("opencode".into()),
        ..Default::default()
    };
    assert!(title_agent(&cfg, &other).is_none());
}

#[test]
fn once_title_is_consumed_before_the_worker_finishes() {
    let mut capture = TitleCapture::default();
    assert!(capture.once_available());
    capture.consume_once();
    assert!(!capture.once_available());
    assert!(capture.override_title.is_none());
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

fn seeded_rules() -> Vec<(State, regex::Regex)> {
    let cfg = Config::parse(
        r#"
[[state_rule]]
state = "waiting"
pattern = '(?i)(do you want|❯\s*1\.|yes, and don.t ask again|press enter to continue)'

[[state_rule]]
state = "working"
pattern = '(?i)(esc to interrupt|to interrupt\))'
"#,
    )
    .expect("config parses");
    compile_rules(&cfg)
}

#[test]
fn work_started_beats_the_question_that_started_it() {
    let screen = "\
> fix the parser

  Do you want to make this edit to lexer.rs?
  ❯ 1. Yes
    2. No

  Updated lexer.rs with 3 additions

* Thinking… (12s · esc to interrupt)
";
    assert_eq!(match_rules(&seeded_rules(), screen), Some(State::Working));
}

#[test]
fn a_question_with_nothing_under_it_is_waiting() {
    let screen = "\
  Updated lexer.rs with 3 additions

  Do you want to make this edit to parser.rs?
  ❯ 1. Yes
    2. No
";
    assert_eq!(match_rules(&seeded_rules(), screen), Some(State::Waiting));
}

#[test]
fn trailing_blanks_do_not_spend_the_tail() {
    let mut screen = String::from("* Working… (esc to interrupt)\n");
    screen.push_str(&"\n".repeat(30));
    assert_eq!(match_rules(&seeded_rules(), &screen), Some(State::Working));
}

#[test]
fn a_rule_out_of_reach_of_the_tail_says_nothing() {
    let mut screen = String::from("  Do you want to make this edit?\n");
    for i in 0..20 {
        screen.push_str(&format!("  line {i}\n"));
    }
    screen.push_str("> \n");
    assert_eq!(match_rules(&seeded_rules(), &screen), None);
}

#[test]
fn blank_rows_inside_the_tail_count_toward_its_limit() {
    let mut screen = String::from("  Do you want to make this edit?\n");
    screen.push_str(&"\n".repeat(TAIL_LINES - 1));
    screen.push_str("ordinary output\n");
    assert_eq!(match_rules(&seeded_rules(), &screen), None);
}

#[test]
fn an_all_blank_screen_has_no_rule_match() {
    assert_eq!(match_rules(&seeded_rules(), "\n\n\n"), None);
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
        rule_cache: None,
        state_since: 0,
        bell: false,
        cols: Live::BOOT_COLS,
        rows: Live::BOOT_ROWS,
        plain: Arc::new(String::new()),
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
    assert!(check_library_item(&cfg, &fine).is_ok());
}

#[test]
fn every_tip_mention_is_its_own_draw() {
    let tips: Vec<String> = ["one", "two", "three", "four", "five"]
        .iter()
        .map(|s| s.to_string())
        .collect();
    let out = render_template(
        "- {{ random_tip }}\n- {{random_tip}}\n- {{ random_tip}}\n\
             - {{ random_tip }}\n- {{ random_tip }}",
        &tips,
    );
    for tip in &tips {
        assert_eq!(out.matches(tip.as_str()).count(), 1, "{tip} exactly once");
    }

    let short = render_template(
        "{{ random_tip }}/{{ random_tip }}/{{ random_tip }}",
        &tips[..2],
    );
    assert_eq!(short, "one/two/one");
}

#[test]
fn a_template_with_nothing_to_fill_it_is_left_alone() {
    let text = "- {{ random_tip }} and {{ whatever }}";
    assert_eq!(render_template(text, &[]), text);

    let tips = vec!["a tip".to_string()];
    assert_eq!(render_template(text, &tips), "- a tip and {{ whatever }}");

    assert_eq!(
        render_template("keep {{ random_tip", &tips),
        "keep {{ random_tip"
    );
}

#[test]
fn breadcrumb_context_variables_render_and_unknown_variables_survive() {
    let vars = TemplateVars {
        agent: "Ada",
        project: "slopworld",
        directory: "/src/slopworld",
        command: "codex",
    };
    assert_eq!(
        render_template_with(
            "{{ agent }} in {{ project }} at {{ directory }} via {{ command }}; {{ later }}",
            &[],
            Some(&vars),
        ),
        "Ada in slopworld at /src/slopworld via codex; {{ later }}"
    );
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
    assert!(check_name("claude").is_ok());
    assert!(check_name("claude-2").is_ok());
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
    assert!(json_to_toml(serde_json::Value::Null).is_err());
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
    assert!(live.input.breadcrumbs.is_empty());
    assert!(!live.input.breadcrumbs_pending);
}
