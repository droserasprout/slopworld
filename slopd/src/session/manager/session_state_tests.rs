use super::*;
use crate::clock::unix_ms;

#[tokio::test]
async fn activity_decay_ignores_changes_to_the_epoch_timestamp() {
    let (manager, mut emu) = quiet_session().await;
    emu.feed(b"fresh activity");
    manager.apply_frame("agent", emu.render()).await;
    // An old wall-clock timestamp must not expire a fresh monotonic interval.
    manager
        .live
        .write()
        .await
        .get_mut("agent")
        .unwrap()
        .last_change = 0;
    manager.retick().await;
    assert_eq!(manager.live.read().await["agent"].state, State::Working);
}

#[tokio::test]
async fn future_epoch_timestamp_cannot_delay_monotonic_idle_deadline() {
    let (manager, _) = quiet_session().await;
    {
        let mut live = manager.live.write().await;
        let row = live.get_mut("agent").unwrap();
        row.last_change = u64::MAX;
        row.activity_at = Some(Instant::now() - Duration::from_millis(IDLE_MS));
        row.retick_seq = row.seq;
    }
    assert_eq!(manager.maintenance_delay().await, Duration::ZERO);
    manager.retick().await;
    let live = manager.live.read().await;
    assert_eq!(live["agent"].state, State::Idle);
    assert_eq!(live["agent"].last_change, u64::MAX);
}

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
        live.activity_at = None;
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
    let snapshots = manager.retick_snapshots(Instant::now()).await;
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
    let snapshots = manager.retick_snapshots(Instant::now()).await;
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
    let snapshots = manager.retick_snapshots(Instant::now()).await;
    let previous = &snapshots[0];
    {
        let mut live = manager.live.write().await;
        let live = live.get_mut("agent").unwrap();
        live.run_id += 1;
        live.last_change = unix_ms();
        live.activity_at = Some(Instant::now());
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
    let last_change = Instant::now();
    let deadline = last_change + Duration::from_millis(IDLE_MS);
    assert_eq!(
        classify_activity(
            false,
            Some(last_change),
            deadline - Duration::from_millis(1)
        ),
        State::Working
    );
    assert_eq!(
        classify_activity(false, Some(last_change), deadline),
        State::Idle
    );
    assert_eq!(
        classify_activity(true, Some(last_change), deadline),
        State::Working
    );
    assert_eq!(classify_activity(false, None, deadline), State::Idle);
}

#[tokio::test]
async fn classification_deadlines_cover_decay_and_inactive_sessions() {
    let (manager, _) = quiet_session().await;
    manager.retick().await;
    let mut sessions = manager.live.write().await;
    let live = sessions.get_mut("agent").unwrap();
    let now = Instant::now();
    assert_eq!(classification_deadline(live, now), None);
    for state in [State::Working, State::Waiting] {
        live.set_state(state);
        live.activity_at = Some(now);
        assert_eq!(
            classification_deadline(live, now),
            Some(now + Duration::from_millis(IDLE_MS))
        );
    }
    live.seq += 1;
    assert_eq!(classification_deadline(live, now), Some(now));
    live.set_state(State::Down);
    assert_eq!(classification_deadline(live, now), None);
    live.set_state(State::Working);
    live.screen = None;
    assert_eq!(classification_deadline(live, now), None);
}

#[tokio::test]
async fn waiting_state_decays_without_new_output() {
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

#[tokio::test]
async fn delayed_activity_cannot_overwrite_newer_state_or_restore_cleared_history() {
    let Some(_root) = crate::test_support::isolated() else {
        return;
    };
    let (manager, _) = quiet_session().await;
    let older = ActivityRecord::from_live(&manager.live.read().await["agent"]);
    let ordering = manager.activity_mutation.lock().await;
    let old_write = manager.persist_activity("agent", older);
    tokio::pin!(old_write);
    assert!(futures::poll!(old_write.as_mut()).is_pending());
    let newer = {
        let mut live = manager.live.write().await;
        let row = live.get_mut("agent").unwrap();
        row.set_state(State::Idle);
        row.state_since = 42;
        ActivityRecord::from_live(row)
    };
    let new_write = manager.persist_activity("agent", newer);
    tokio::pin!(new_write);
    assert!(futures::poll!(new_write.as_mut()).is_pending());
    drop(ordering);
    old_write.await;
    new_write.await;
    let saved = manager.activity_cache.get("agent").unwrap();
    assert_eq!(saved.state, State::Idle);
    assert_eq!(saved.state_since, 42);

    let older = ActivityRecord::from_live(&manager.live.read().await["agent"]);
    let ordering = manager.activity_mutation.lock().await;
    let old_write = manager.persist_activity("agent", older);
    tokio::pin!(old_write);
    assert!(futures::poll!(old_write.as_mut()).is_pending());
    super::super::lifecycle::stop::reset_process_state(
        manager.live.write().await.get_mut("agent").unwrap(),
    );
    drop(ordering);
    tokio::join!(manager.clear_activity("agent"), old_write);
    assert!(manager.activity_cache.get("agent").is_none());
}

#[tokio::test]
async fn retick_does_not_classify_a_replacement_identity_with_matching_counters() {
    let (manager, _) = quiet_session().await;
    let previous = manager.retick_snapshots(Instant::now()).await.remove(0);
    manager
        .live
        .write()
        .await
        .get_mut("agent")
        .unwrap()
        .cfg
        .state_id = crate::storage_id::draft_identity();
    assert_eq!(
        manager.commit_retick(&previous, State::Idle).await,
        (false, None)
    );
    assert_eq!(manager.live.read().await["agent"].state, State::Working);
}
