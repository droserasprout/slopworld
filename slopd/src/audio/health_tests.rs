use super::*;

#[test]
fn reporting_aggregates_both_underrun_causes_without_retiring_the_device() {
    let health = OutputHealth::new();
    health.record(&rodio::cpal::StreamError::BufferUnderrun);
    health.source_underruns.fetch_add(3, Ordering::Relaxed);
    health.report();
    assert_eq!(health.backend_underruns.load(Ordering::Relaxed), 1);
    assert_eq!(health.source_underruns.load(Ordering::Relaxed), 3);
    *health.last_report.lock().unwrap() =
        Instant::now().checked_sub(Duration::from_secs(6)).unwrap();
    health.report();
    assert_eq!(health.backend_underruns.load(Ordering::Relaxed), 0);
    assert_eq!(health.source_underruns.load(Ordering::Relaxed), 0);
    assert!(!health.failed.load(Ordering::Acquire));
    health.record(&rodio::cpal::StreamError::BufferUnderrun);
    health.report();
    assert_eq!(health.backend_underruns.load(Ordering::Relaxed), 1);
}
