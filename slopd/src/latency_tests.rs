use super::*;

#[test]
fn correlation_survives_coalescing_and_rejects_old_capture_and_run() {
    let trace = Arc::new(InputTrace::new("0123456789abcdef0123456789abcdef", now()));
    let start = trace.timing.lock().unwrap().received_us;
    trace.timing.lock().unwrap().tmux_us = start + 10;
    let mut pending = Pending::default();
    pending.add(7, trace.clone());
    assert!(pending.capture(7, 1, start + 9).is_empty());
    let first = pending.capture(7, 2, start + 20);
    assert_eq!(first[0].first_seq, 2);
    let next = pending.capture(7, 4, start + 40);
    assert_eq!(next[0].first_seq, 2);
    assert_eq!(next[0].first_capture_us, start + 20);
    assert_eq!(next[0].capture_us, start + 40);
    assert_eq!(next[0].run_id, 7);
    assert!(pending.capture(8, 5, start + 50).is_empty());
}

#[test]
fn pending_is_bounded_deduplicated_and_expires() {
    let mut pending = Pending::default();
    let trace = Arc::new(InputTrace::new("0123456789abcdef0123456789abcdef", now()));
    pending.add(1, trace.clone());
    pending.add(1, trace);
    assert_eq!(pending.entries.len(), 1);
    for _ in 0..LIMIT + 10 {
        let trace = Arc::new(InputTrace::new("0123456789abcdef0123456789abcdef", now()));
        pending.add(1, trace.clone());
        pending.add(1, trace);
    }
    assert_eq!(pending.entries.len(), LIMIT);
    assert!(pending.capture(1, 1, now() + TTL_US).is_empty());
    assert!(pending.entries.is_empty());
}

#[test]
fn cache_eviction_before_dispatch_prevents_frame_correlation() {
    let mut pending = Pending::default();
    let oldest = Arc::new(InputTrace::new("00000000000000000000000000000000", now()));
    pending.add(1, oldest.clone());
    for index in 1..=LIMIT {
        pending.add(
            1,
            Arc::new(InputTrace::new(&format!("{index:032x}"), now())),
        );
    }
    // A retained trace cannot restore its association after the cache evicts it.
    oldest.dispatch();
    oldest.done(true);
    for seq in 1..=3 {
        assert!(pending.capture(1, seq, now()).is_empty());
    }
    assert_eq!(pending.entries.len(), LIMIT);
}
