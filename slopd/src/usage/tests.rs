use super::*;

/// A change to `sources` must trigger an update even when usage values stay the same.
/// The mod uses this list to determine which sources to display.
/// A change to `fetched_ms` alone must not trigger an update.
#[test]
fn a_source_switched_off_is_not_the_same_readout() {
    let base = Snapshot {
        ok: true,
        plan: "max".into(),
        fetched_ms: 1_000,
        sources: vec!["anthropic".into(), "openrouter".into()],
        windows: vec![Window {
            key: "five_hour".into(),
            label: "5h".into(),
            pct: 40.0,
            unit: Unit::Pct,
            amount: None,
            limit: None,
            resets_in: None,
        }],
        ..Default::default()
    };

    let mut aged = base.clone();
    aged.fetched_ms = 99_000;
    assert!(base.same_readout(&aged), "a newer poll of the same figures");

    let mut dropped = base.clone();
    dropped.sources = vec!["anthropic".into()];
    assert!(
        !base.same_readout(&dropped),
        "openrouter was switched off and the readout must be told"
    );
}

#[test]
fn failure_keeps_the_last_good_numbers() {
    let good = parse(
        &serde_json::from_str(r#"{"five_hour":{"utilization":50}}"#).unwrap(),
        "max".into(),
    );
    let bad = Snapshot::failed(&good, "network unreachable");

    assert!(!bad.ok);
    assert_eq!(bad.windows, good.windows);
    assert_eq!(bad.plan, "max");
    assert_eq!(bad.error.unwrap(), "network unreachable");
}
