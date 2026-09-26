use super::*;

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

    // The tick has captured the old idle deadline. Commit real output before resuming it.
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
        live.last_change = now_ms();
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
