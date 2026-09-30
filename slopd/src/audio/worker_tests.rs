use super::*;
use crate::audio::test_support::{FakeOutputFactory, FakeOutputState};
use crate::audio::GENERATION;
use std::sync::atomic::AtomicUsize;

#[test]
fn failed_opener_start_clears_the_pending_identity_and_allows_retry() {
    let (tx, rx) = std::sync::mpsc::channel();
    let state = Arc::new(Mutex::new(AudioState::default()));
    let control = Arc::new(Control::new());
    let generation = GENERATION.load(Ordering::SeqCst);
    control
        .publish_current(&state, generation, SourceRequirement::None, |request, _| {
            request.source = Some("fixture".into());
        })
        .unwrap();
    let title = station::TitleSink::pending_for(Some(state.clone()), generation, control.clone());
    let cancel = title.cancelled.clone();
    control
        .publish_current(
            &state,
            generation,
            SourceRequirement::Exact("fixture"),
            |request, _| {
                request.cancel = Some(cancel.clone());
            },
        )
        .unwrap();
    let factory = Arc::new(FakeOutputFactory(Arc::new(FakeOutputState {
        opens: AtomicUsize::new(0),
        appends: AtomicUsize::new(0),
        volumes: Mutex::new(Vec::new()),
    })));
    let mut worker = AudioWorker {
        state: state.clone(),
        control: control.clone(),
        tx: tx.clone(),
        factory: factory.clone(),
        output: None,
        pending: Some(PendingOpen {
            source: "fixture".into(),
            generation,
            cancel: cancel.clone(),
        }),
        active_cancel: None,
        active_generation: None,
    };
    spawn_open_with(
        OpenRequest {
            source: "fixture".into(),
            generation,
            title,
            needs_output: true,
        },
        factory,
        tx,
        |_| Err(std::io::Error::other("synthetic opener failure")),
    );
    worker.handle(rx.recv_timeout(std::time::Duration::from_secs(2)).unwrap());
    assert!(worker.pending.is_none());
    assert_eq!(control.snapshot(), (None, generation));
    assert!(cancel.load(Ordering::Acquire));
    assert!(state
        .lock()
        .unwrap()
        .error
        .as_deref()
        .unwrap()
        .contains("synthetic opener failure"));
    // No source remains to trigger AlreadyPending or reuse a failed generation on the next play.
    assert!(!control.is_current("fixture", generation));
}
