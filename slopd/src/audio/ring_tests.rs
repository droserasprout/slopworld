use super::*;
use crate::audio::test_support::ring;

#[test]
fn feeder_completion_survives_a_delayed_underrun_reset() {
    let (tx, mut ring) = ring();
    assert_eq!(
        ring.rx.try_recv().unwrap_err(),
        std::sync::mpsc::TryRecvError::Empty
    );
    // Force the ordering that lost ReadyOnDrop's readiness: Empty, feeder exit, reset.
    tx.send(vec![0.4, 0.8]).unwrap();
    drop(tx);
    drop(FinishedOnDrop(ring.finished.clone()));
    ring.prefill_samples = 100;
    ring.rebuffer();
    assert!(!ring.ready.load(Ordering::Acquire));
    assert_eq!(ring.next(), Some(0.4));
    assert_eq!(ring.next(), Some(0.8));
    assert_eq!(ring.next(), None);
}

#[test]
fn a_cancelled_ring_retires_before_prefill() {
    let (_tx, mut ring) = ring();
    ring.ready.store(false, Ordering::Release);
    ring.cancelled.store(true, Ordering::Release);
    assert_eq!(ring.next(), None);
}
