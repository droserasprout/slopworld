use super::*;
use crate::audio::test_support::{FakeOutput, FakeOutputState};
use std::time::Duration;

fn candidate() -> OpenedSource {
    let source = Path::new(env!("CARGO_MANIFEST_DIR")).join("../mod/Sounds/SlopWorld/silent.ogg");
    open(
        &source.to_string_lossy(),
        TitleSink::pending(None, GENERATION.load(Ordering::SeqCst)),
    )
    .unwrap()
}

fn output() -> FakeOutput {
    FakeOutput(Arc::new(FakeOutputState {
        opens: AtomicUsize::new(0),
        appends: AtomicUsize::new(0),
        volumes: std::sync::Mutex::new(Vec::new()),
    }))
}

#[test]
fn feeder_start_failure_does_not_change_output() {
    let output = output();
    let error = commit_with_spawn(candidate(), &output, 0.7, |_| {
        Err(std::io::Error::other("synthetic spawn failure"))
    })
    .err()
    .expect("startup fails");
    assert!(error.to_string().contains("starting the feeder"));
    assert_eq!(output.0.appends.load(Ordering::Acquire), 0);
    assert!(output.0.volumes.lock().unwrap().is_empty());
}

#[test]
fn cancellation_during_startup_discards_the_gated_feeder() {
    let output = output();
    let opened = candidate();
    let title = opened.title.clone();
    let (done_tx, done_rx) = std::sync::mpsc::channel();
    let committed = commit_with_spawn(opened, &output, 0.7, |feed| {
        std::thread::spawn(move || {
            feed();
            done_tx.send(()).unwrap();
        });
        title.cancel();
        Ok(())
    })
    .unwrap();
    assert!(committed.is_none());
    done_rx.recv_timeout(Duration::from_secs(2)).unwrap();
    assert_eq!(output.0.appends.load(Ordering::Acquire), 0);
    assert!(output.0.volumes.lock().unwrap().is_empty());
}
