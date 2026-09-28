use super::*;
use crate::clock::unix_ms;

// Seed a captured, overdue session without activity persistence or unrelated polling.
async fn quiet_session() -> (Arc<Manager>, crate::emu::SessionEmu) {
    let manager = crate::session::test_manager(Config::default());
    let mut live = Live::new(
        SessionCfg {
            name: "agent".into(),
            ..Default::default()
        },
        TitleCapture::default(),
    );
    live.ephemeral = true;
    manager.live.write().await.insert("agent".into(), live);
    let mut emu = crate::emu::SessionEmu::new(80, 24);
    emu.feed(b"old output");
    manager.apply_frame("agent", emu.render()).await;
    {
        let mut live = manager.live.write().await;
        let live = live.get_mut("agent").unwrap();
        live.last_change = 0;
        live.state_since = 0;
    }
    // Prevent unrelated polling from suspending retick before it reaches classification.
    manager
        .config_state
        .config_checked
        .store(u64::MAX, Ordering::Relaxed);
    manager
        .config_state
        .presets_checked
        .store(u64::MAX, Ordering::Relaxed);
    manager
        .config_state
        .jukebox_checked
        .store(u64::MAX, Ordering::Relaxed);
    manager
        .host_metadata
        .checked
        .store(u64::MAX, Ordering::Relaxed);
    (manager, emu)
}

#[tokio::test]
async fn retick_does_not_overwrite_fresh_terminal_activity() {
    let (manager, mut emu) = quiet_session().await;
    let rules = manager.rules.compiled.write().await;
    let mut tick = Box::pin(manager.retick());
    assert!(futures::poll!(tick.as_mut()).is_pending());
    drop(rules);

    // Retick holds the old frame snapshot. Commit new output before its classification resumes.
    emu.feed(b"\r\nnew output");
    manager.apply_frame("agent", emu.render()).await;
    tick.await;

    {
        let live = manager.live.read().await;
        let live = &live["agent"];
        assert_eq!(live.state, State::Working);
        assert_ne!(
            live.retick_seq, live.seq,
            "new output was never classified by retick"
        );
    }
    manager.retick().await;
    let live = manager.live.read().await;
    assert_eq!(live["agent"].state, State::Working);
    assert_eq!(live["agent"].retick_seq, live["agent"].seq);
}

#[tokio::test]
async fn retick_does_not_revive_a_stopped_session() {
    let (manager, _) = quiet_session().await;
    let rules = manager.rules.compiled.write().await;
    let mut tick = Box::pin(manager.retick());
    assert!(futures::poll!(tick.as_mut()).is_pending());
    manager
        .live
        .write()
        .await
        .get_mut("agent")
        .unwrap()
        .set_state(State::Down);
    drop(rules);
    tick.await;

    let live = manager.live.read().await;
    assert_eq!(live["agent"].state, State::Down);
    assert_eq!(live["agent"].retick_seq, 0);
}

#[tokio::test]
async fn retick_does_not_classify_a_replacement_run_with_the_same_sequence() {
    let (manager, _) = quiet_session().await;
    let rules = manager.rules.compiled.write().await;
    let mut tick = Box::pin(manager.retick());
    assert!(futures::poll!(tick.as_mut()).is_pending());
    {
        let mut live = manager.live.write().await;
        let live = live.get_mut("agent").unwrap();
        live.run_id += 1;
        live.last_change = unix_ms();
    }
    drop(rules);
    tick.await;

    let live = manager.live.read().await;
    assert_eq!(live["agent"].state, State::Working);
    assert_eq!(live["agent"].retick_seq, 0);
}

#[tokio::test]
async fn retick_rejects_a_classification_from_before_rules_reload() {
    let (manager, _) = quiet_session().await;
    let mut rules = manager.rules.compiled.write().await;
    let mut tick = Box::pin(manager.retick());
    assert!(futures::poll!(tick.as_mut()).is_pending());
    *rules = vec![(State::Waiting, regex::Regex::new("old output").unwrap())];
    manager.rules.revision.fetch_add(1, Ordering::AcqRel);
    drop(rules);
    tick.await;

    let live = manager.live.read().await;
    assert_eq!(live["agent"].retick_seq, 0);
    drop(live);
    manager.retick().await;
    let live = manager.live.read().await;
    assert_eq!(live["agent"].state, State::Waiting);
    assert_eq!(live["agent"].retick_seq, live["agent"].seq);
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
fn classification_decay_has_an_exact_deadline() {
    let last_change = 100;
    let deadline = last_change + IDLE_MS;
    for matched in [None, Some(State::Working)] {
        assert_eq!(
            classify_match(matched, false, last_change, deadline - 1),
            State::Working
        );
        assert_eq!(
            classify_match(matched, false, last_change, deadline),
            State::Idle
        );
        assert_eq!(
            classify_match(matched, true, last_change, deadline),
            State::Working
        );
        assert_eq!(
            classify_match(matched, false, last_change, 0),
            State::Working
        );
    }
    for state in [State::Waiting, State::Idle] {
        assert_eq!(
            classify_match(Some(state), false, last_change, deadline),
            state
        );
        assert_eq!(
            classify_match(Some(state), true, last_change, deadline),
            state
        );
    }
}

#[tokio::test]
async fn classification_deadlines_cover_decay_invalidation_and_inactive_sessions() {
    let (manager, _) = quiet_session().await;
    manager.retick().await;
    let mut sessions = manager.live.write().await;
    let live = sessions.get_mut("agent").unwrap();
    let revision = manager.rules.revision.load(Ordering::Acquire);
    assert_eq!(classification_deadline(live, revision), None);

    live.set_state(State::Working);
    live.last_change = 100;
    assert_eq!(classification_deadline(live, revision), Some(100 + IDLE_MS));
    for state in [State::Waiting, State::Idle] {
        live.rule_cache.as_mut().unwrap().matched = Some(state);
        assert_eq!(classification_deadline(live, revision), None);
    }
    assert_eq!(classification_deadline(live, revision + 1), Some(0));
    live.seq += 1;
    assert_eq!(classification_deadline(live, revision), Some(0));
    live.retick_seq = live.seq;
    live.plain = Arc::new("different text".into());
    assert_eq!(classification_deadline(live, revision), Some(0));
    live.rule_cache = None;
    assert_eq!(classification_deadline(live, revision), Some(0));
    live.set_state(State::Down);
    assert_eq!(classification_deadline(live, revision), None);
    live.set_state(State::Working);
    live.screen = None;
    assert_eq!(classification_deadline(live, revision), None);
}
