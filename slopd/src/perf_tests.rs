use super::*;

#[test]
fn disabled_metrics_do_not_record_or_report() {
    let Some(_) = crate::test_support::isolated_with_env(|command, _| {
        command.env("SLOPWORLD_DEBUG", "false");
    }) else {
        return;
    };
    assert!(!enabled());
    drop(timer("disabled"));
    count("disabled", 9);
    maybe_report();
    assert!(report().is_none());
    assert!(METRICS.get().is_none());
    assert!(LAST_REPORT.get().is_none());
}

#[test]
fn reports_sort_names_bound_samples_and_clear_the_window() {
    let Some(_) = crate::test_support::isolated_with_env(|command, _| {
        command.env("SLOPWORLD_DEBUG", "TrUe");
    }) else {
        return;
    };
    assert!(enabled());
    assert!(report().is_none());
    // Later durations must not replace the first bounded sample set, but their
    // calls, work, and peak backlog still belong to the reporting window.
    for micros in (1..=MAX_SAMPLES as u64).rev() {
        observe_duration("z_duration", Duration::from_micros(micros), 2, 3);
    }
    observe_duration("z_duration", Duration::from_secs(5), 7, 8);
    count("a_count", 10);
    count("a_count", 20);
    assert_eq!(
        report().unwrap(),
        "perf a_count calls=2 work=30; z_duration calls=513 work=1031 first_samples=512 p50_us=256 p95_us=486 backlog=8;"
    );
    assert!(report().is_none());
    drop(timer("timer"));
    let line = report().unwrap();
    assert!(line.starts_with("perf timer calls=1 work=1 first_samples=1 p50_us="));
    assert!(report().is_none());
}

#[test]
fn periodic_reports_preserve_observations_until_the_next_window() {
    let Some(_) = crate::test_support::isolated_with_env(|command, _| {
        command.env("SLOPWORLD_DEBUG", "1");
    }) else {
        return;
    };
    count("first", 1);
    maybe_report();
    assert!(report().is_none());
    // Set the report clock explicitly so this test does not depend on sleeps.
    *LAST_REPORT.get().unwrap().lock().unwrap() = Some(Instant::now());
    count("pending", 2);
    maybe_report();
    assert_eq!(report().as_deref(), Some("perf pending calls=1 work=2;"));
    *LAST_REPORT.get().unwrap().lock().unwrap() =
        Some(Instant::now().checked_sub(REPORT_INTERVAL).unwrap());
    count("next", 3);
    maybe_report();
    assert!(report().is_none());
}
