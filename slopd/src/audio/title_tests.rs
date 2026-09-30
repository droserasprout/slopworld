use super::*;

#[test]
fn pending_clear_survives_activation_and_repeated_commit() {
    let state = Arc::new(Mutex::new(AudioState {
        title: Some("old".into()),
        ..Default::default()
    }));
    let sink = TitleSink::pending(Some(state.clone()), GENERATION.load(Ordering::SeqCst));
    sink.set(Some("candidate".into()));
    sink.set(None);
    sink.commit();
    assert_eq!(state.lock().unwrap().title, None);
    sink.set(Some("new".into()));
    sink.commit();
    assert_eq!(state.lock().unwrap().title.as_deref(), Some("new"));
}

#[test]
fn activation_keeps_publication_order_while_a_new_title_waits() {
    let state = Arc::new(Mutex::new(AudioState::default()));
    let sink = TitleSink::pending(Some(state.clone()), GENERATION.load(Ordering::SeqCst));
    sink.set(Some("pending".into()));
    let state_guard = state.lock().unwrap();
    let committing = sink.clone();
    let commit = std::thread::spawn(move || committing.commit());
    // Hold AudioState so commit must retain the phase lock while publishing the pending title.
    let deadline = Instant::now() + std::time::Duration::from_secs(2);
    loop {
        if matches!(
            sink.phase.try_lock(),
            Err(std::sync::TryLockError::WouldBlock)
        ) {
            break;
        }
        assert!(
            Instant::now() < deadline,
            "commit did not enter publication"
        );
        std::thread::yield_now();
    }
    let newer = sink.clone();
    let update = std::thread::spawn(move || newer.set(Some("newest".into())));
    drop(state_guard);
    commit.join().unwrap();
    update.join().unwrap();
    assert_eq!(state.lock().unwrap().title.as_deref(), Some("newest"));
}
