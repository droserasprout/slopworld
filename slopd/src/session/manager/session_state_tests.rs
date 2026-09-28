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
    let snapshots = manager.retick_snapshots(unix_ms()).await;
    let previous = &snapshots[0];

    // Retick holds the old frame snapshot. Commit new output before its classification resumes.
    emu.feed(b"\r\nnew output");
    manager.apply_frame("agent", emu.render()).await;
    assert_eq!(
        manager.commit_retick(previous, State::Idle).await,
        (false, None)
    );

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
    let snapshots = manager.retick_snapshots(unix_ms()).await;
    let previous = &snapshots[0];
    manager
        .live
        .write()
        .await
        .get_mut("agent")
        .unwrap()
        .set_state(State::Down);
    assert_eq!(
        manager.commit_retick(previous, State::Idle).await,
        (false, None)
    );

    let live = manager.live.read().await;
    assert_eq!(live["agent"].state, State::Down);
    assert_eq!(live["agent"].retick_seq, 0);
}

#[tokio::test]
async fn retick_does_not_classify_a_replacement_run_with_the_same_sequence() {
    let (manager, _) = quiet_session().await;
    let snapshots = manager.retick_snapshots(unix_ms()).await;
    let previous = &snapshots[0];
    {
        let mut live = manager.live.write().await;
        let live = live.get_mut("agent").unwrap();
        live.run_id += 1;
        live.last_change = unix_ms();
    }
    assert_eq!(
        manager.commit_retick(previous, State::Idle).await,
        (false, None)
    );

    let live = manager.live.read().await;
    assert_eq!(live["agent"].state, State::Working);
    assert_eq!(live["agent"].retick_seq, 0);
}

#[test]
fn activity_decay_has_an_exact_deadline() {
    let last_change = 100;
    let deadline = last_change + IDLE_MS;
    assert_eq!(
        classify_activity(false, last_change, deadline - 1),
        State::Working
    );
    assert_eq!(classify_activity(false, last_change, deadline), State::Idle);
    assert_eq!(
        classify_activity(true, last_change, deadline),
        State::Working
    );
    assert_eq!(classify_activity(false, last_change, 0), State::Working);
}

#[tokio::test]
async fn classification_deadlines_cover_decay_and_inactive_sessions() {
    let (manager, _) = quiet_session().await;
    manager.retick().await;
    let mut sessions = manager.live.write().await;
    let live = sessions.get_mut("agent").unwrap();
    assert_eq!(classification_deadline(live), None);
    for state in [State::Working, State::Waiting] {
        live.set_state(state);
        live.last_change = 100;
        assert_eq!(classification_deadline(live), Some(100 + IDLE_MS));
    }
    live.seq += 1;
    assert_eq!(classification_deadline(live), Some(0));
    live.set_state(State::Down);
    assert_eq!(classification_deadline(live), None);
    live.set_state(State::Working);
    live.screen = None;
    assert_eq!(classification_deadline(live), None);
}

#[tokio::test]
async fn legacy_waiting_state_decays_without_new_output() {
    let (manager, _) = quiet_session().await;
    manager
        .live
        .write()
        .await
        .get_mut("agent")
        .unwrap()
        .set_state(State::Waiting);
    manager.retick().await;
    assert_eq!(manager.live.read().await["agent"].state, State::Idle);
}
