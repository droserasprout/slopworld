use super::*;

#[test]
fn stale_success_and_failure_cannot_overwrite_the_replacement() {
    let control = Control::new();
    let state = Arc::new(Mutex::new(AudioState {
        playing: true,
        source: Some("replacement".into()),
        title: Some("new title".into()),
        ..Default::default()
    }));
    let generation = control.snapshot().1;
    control.request.lock().unwrap().source = Some("replacement".into());
    let original = state.lock().unwrap().clone();
    assert!(
        control
            .publish_current(
                &state,
                generation,
                SourceRequirement::Exact("old"),
                |_, _| { panic!("stale success must not publish") }
            )
            .is_none()
    );
    control.fail_current(&state, "old", generation, "old failure".into());
    control.fail_current(
        &state,
        "replacement",
        generation.wrapping_sub(1),
        "old generation".into(),
    );
    assert_eq!(*state.lock().unwrap(), original);
}

#[test]
fn publication_holds_request_identity_until_state_is_updated() {
    let control = Control::new();
    let state = Arc::new(Mutex::new(AudioState::default()));
    let generation = control.snapshot().1;
    control
        .publish_current(&state, generation, SourceRequirement::None, |_, current| {
            assert!(matches!(
                control.request.try_lock(),
                Err(std::sync::TryLockError::WouldBlock)
            ));
            current.title = Some("published under identity lock".into());
        })
        .unwrap();
    assert!(state.lock().unwrap().title.is_some());
}

#[test]
fn final_handle_shutdown_cancels_its_request_without_advancing_global_generation() {
    let control = Control::new();
    let generation = control.snapshot().1;
    let cancel = Arc::new(std::sync::atomic::AtomicBool::new(false));
    {
        let mut request = control.request.lock().unwrap();
        request.source = Some("fixture".into());
        request.cancel = Some(cancel.clone());
    }
    control.shutdown();
    control.shutdown();
    assert!(cancel.load(Ordering::Acquire));
    assert_eq!(control.snapshot(), (None, generation));
    assert_eq!(GENERATION.load(Ordering::SeqCst), generation);
}
