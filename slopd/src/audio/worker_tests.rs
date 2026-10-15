use super::*;
use crate::audio::GENERATION;
use crate::audio::test_support::{FakeOutputFactory, FakeOutputState};
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
        recovery: OutputRecovery::default(),
    };
    spawn_open_with(
        OpenRequest {
            source: "fixture".into(),
            generation,
            title,
            needs_output: true,
        },
        factory,
        &tx,
        |_| Err(std::io::Error::other("synthetic opener failure")),
    );
    worker.handle(rx.recv_timeout(std::time::Duration::from_secs(2)).unwrap());
    assert!(worker.pending.is_none());
    assert_eq!(control.snapshot(), (None, generation));
    assert!(cancel.load(Ordering::Acquire));
    assert!(
        state
            .lock()
            .unwrap()
            .error
            .as_deref()
            .unwrap()
            .contains("synthetic opener failure")
    );
    // No source remains to trigger AlreadyPending or reuse a failed generation on the next play.
    assert!(!control.is_current("fixture", generation));
}

struct BrokenOutput(Arc<AtomicUsize>);

impl Drop for BrokenOutput {
    fn drop(&mut self) {
        self.0.fetch_add(1, Ordering::AcqRel);
    }
}

impl AudioOutput for BrokenOutput {
    fn append(&self, _: Box<dyn rodio::Source + Send>) {}
    fn set_volume(&self, _: f32) {}
    fn play(&self) {}
    fn failed(&self) -> bool {
        true
    }
}

fn recovering_worker() -> (AudioWorker, Receiver<WorkerMsg>, Arc<AtomicUsize>) {
    let (tx, rx) = std::sync::mpsc::channel();
    let state = Arc::new(Mutex::new(AudioState::default()));
    let control = Arc::new(Control::new());
    let generation = GENERATION.load(Ordering::SeqCst);
    let source = std::path::Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("../mod/Sounds/SlopWorld/silent.ogg")
        .to_string_lossy()
        .into_owned();
    control
        .publish_current(
            &state,
            generation,
            SourceRequirement::None,
            |request, current| {
                request.source = Some(source.clone());
                current.source = Some(source);
                current.playing = true;
            },
        )
        .unwrap();
    let dropped = Arc::new(AtomicUsize::new(0));
    let worker = AudioWorker {
        state,
        control,
        tx,
        factory: Arc::new(FakeOutputFactory(Arc::new(FakeOutputState {
            opens: AtomicUsize::new(0),
            appends: AtomicUsize::new(0),
            volumes: Mutex::new(Vec::new()),
        }))),
        output: Some(Box::new(BrokenOutput(dropped.clone()))),
        pending: None,
        active_cancel: Some(Arc::new(AtomicBool::new(false))),
        active_generation: Some(generation),
        recovery: OutputRecovery::default(),
    };
    (worker, rx, dropped)
}

#[test]
fn backend_failure_retires_output_then_reopens_current_source_and_volume() {
    let (mut worker, rx, dropped) = recovering_worker();
    let cancel = worker.active_cancel.as_ref().unwrap().clone();
    let now = Instant::now();
    worker.check_output(now);
    assert_eq!(dropped.load(Ordering::Acquire), 1);
    assert!(cancel.load(Ordering::Acquire));
    assert!(!worker.state.lock().unwrap().playing);
    worker
        .control
        .volume
        .store(0.3f32.to_bits(), Ordering::Release);
    worker.check_output(now + Duration::from_secs(1));
    assert!(worker.pending.is_some());
    worker.handle(rx.recv_timeout(Duration::from_secs(2)).unwrap());
    assert!(worker.output.is_some());
    let state = worker.state.lock().unwrap();
    assert!(state.playing);
    assert_eq!(state.volume.to_bits(), 0.3f32.to_bits());
    assert!(state.error.is_none());
    drop(state);
    worker.shutdown();
}

#[test]
fn failed_reopens_back_off_and_eventually_publish_a_terminal_error() {
    let (mut worker, _rx, _) = recovering_worker();
    let now = Instant::now();
    worker.check_output(now);
    for attempt in 1..=MAX_RECOVERIES {
        let retry = worker.recovery.retry.take().unwrap();
        assert_eq!(
            retry.at.duration_since(now),
            Duration::from_secs(attempt as u64)
        );
        worker.schedule_recovery(retry.source, retry.generation, now, "device unavailable");
    }
    assert!(worker.recovery.retry.is_none());
    assert!(worker.control.snapshot().0.is_none());
    assert!(
        worker
            .state
            .lock()
            .unwrap()
            .error
            .as_deref()
            .unwrap()
            .contains("recovery exhausted")
    );
    worker.check_output(now + Duration::from_secs(100));
    assert!(worker.pending.is_none());
}

#[test]
fn request_retired_during_backoff_is_not_restarted() {
    let (mut worker, _rx, _) = recovering_worker();
    let now = Instant::now();
    worker.check_output(now);
    // Exercise the request-side cancellation before the queued stop reaches the worker.
    worker.control.shutdown();
    worker.check_output(now + Duration::from_secs(5));
    assert!(worker.recovery.retry.is_none());
    assert!(worker.pending.is_none());
    assert!(worker.output.is_none());
}

#[test]
fn stable_output_restores_the_recovery_budget() {
    let (mut worker, _rx, _) = recovering_worker();
    worker.output = Some(worker.factory.open().unwrap());
    worker.recovery.recoveries = MAX_RECOVERIES;
    let now = Instant::now();
    worker.recovery.healthy_since = Some(now);
    worker.check_output(now + STABLE_OUTPUT);
    assert_eq!(worker.recovery.recoveries, 0);
}

#[test]
fn source_replaced_during_backoff_discards_the_old_retry() {
    let (mut worker, _rx, _) = recovering_worker();
    let now = Instant::now();
    worker.check_output(now);
    let (source, generation) = worker.control.snapshot();
    worker
        .control
        .publish_current(
            &worker.state,
            generation,
            SourceRequirement::Exact(source.as_deref().unwrap()),
            |request, _| {
                request.source = Some("replacement".into());
            },
        )
        .unwrap();
    worker.check_output(now + Duration::from_secs(5));
    assert!(worker.recovery.retry.is_none());
    assert!(worker.pending.is_none());
    assert_eq!(worker.control.snapshot().0.as_deref(), Some("replacement"));
}

#[test]
fn reopen_failure_keeps_request_alive_until_the_retry_budget_is_exhausted() {
    let (mut worker, _rx, _) = recovering_worker();
    worker.check_output(Instant::now());
    let retry = worker.recovery.retry.take().unwrap();
    worker.pending = Some(PendingOpen {
        source: retry.source.clone(),
        generation: retry.generation,
        cancel: Arc::new(AtomicBool::new(false)),
    });
    let cancel = worker.pending.as_ref().unwrap().cancel.clone();
    worker.opened(
        &retry.source,
        retry.generation,
        &cancel,
        Err(anyhow::anyhow!("device unavailable")),
        None,
    );
    assert!(worker.control.is_current(&retry.source, retry.generation));
    assert_eq!(worker.recovery.recoveries, 2);
    assert!(worker.recovery.retry.is_some());
    assert!(worker.pending.is_none());
}

#[test]
fn late_opener_from_before_output_failure_cannot_replace_the_recovery_opener() {
    let (mut worker, _rx, _) = recovering_worker();
    let (source, generation) = worker.control.snapshot();
    let source = source.unwrap();
    let old_cancel = Arc::new(AtomicBool::new(false));
    worker.pending = Some(PendingOpen {
        source: source.clone(),
        generation,
        cancel: old_cancel.clone(),
    });
    worker.check_output(Instant::now());
    assert!(old_cancel.load(Ordering::Acquire));
    let new_cancel = Arc::new(AtomicBool::new(false));
    worker.pending = Some(PendingOpen {
        source: source.clone(),
        generation,
        cancel: new_cancel.clone(),
    });
    let dropped = Arc::new(AtomicUsize::new(0));
    worker.opened(
        &source,
        generation,
        &old_cancel,
        Err(anyhow::anyhow!("stale completion")),
        Some(Box::new(BrokenOutput(dropped.clone()))),
    );
    assert!(Arc::ptr_eq(
        &worker.pending.as_ref().unwrap().cancel,
        &new_cancel
    ));
    assert_eq!(dropped.load(Ordering::Acquire), 1);
    assert!(worker.output.is_none());
    assert!(worker.control.is_current(&source, generation));
}

#[test]
fn stale_stop_does_not_cancel_recovery_for_the_current_request() {
    let (mut worker, _rx, _) = recovering_worker();
    worker.check_output(Instant::now());
    let generation = worker.control.snapshot().1;
    worker.stop(generation.wrapping_sub(1));
    assert!(worker.recovery.retry.is_some());
    assert_eq!(worker.recovery.recoveries, 1);
}
