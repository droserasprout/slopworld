use super::*;
use crate::application::timers::{runtime::Runtime, tests::timer_config};

#[test]
fn overdue_interval_plan_retains_anchor_watermark_and_action_snapshot() {
    let mut runtime = Runtime::default();
    runtime.reconcile(&timer_config("interval", "10s"), 1000, 0);
    let active = &runtime.activations["Timer"];
    let record = Occurrence {
        calendar_at: 900,
        ..Default::default()
    };
    let plan = scheduled(active, Some(&record), true, 800, 45).unwrap();
    let claim = plan.claim.unwrap();
    assert_eq!(claim.key, format!("{}:10", active.id));
    assert_eq!(claim.at, 800);
    assert_eq!(claim.calendar_at, 900);
    assert_eq!(plan.next_deadline, Some(50));
    assert_eq!(plan.action, active.config);
    // Planning does not consume a deadline; a failed persistence can retry it.
    assert_eq!(active.next, Some(10));
    assert_eq!(
        scheduled(active, Some(&record), true, 800, 45)
            .unwrap()
            .claim
            .unwrap()
            .key,
        claim.key
    );
    // A claim persisted before interruption advances without another action.
    let resumed = scheduled(active, Some(&claim), true, 801, 46).unwrap();
    assert!(resumed.claim.is_none());
    assert_eq!(resumed.next_deadline, Some(50));
}

#[test]
fn calendar_plan_requires_prior_check_current_minute_and_new_watermark() {
    let mut cfg = timer_config("calendar", "09:00");
    cfg.library[0].timer.as_mut().unwrap().timezone = "UTC".into();
    let nine = 9 * 3600;
    let mut runtime = Runtime::default();
    runtime.reconcile(&cfg, nine - 60, 0);
    let active = &runtime.activations["Timer"];
    assert!(scheduled(active, None, false, nine, 60).is_none());
    assert!(scheduled(active, None, true, nine + 60, 120).is_none());
    let plan = scheduled(active, None, true, nine + 15, 75).unwrap();
    assert_eq!(plan.next_deadline, None);
    let claim = plan.claim.unwrap();
    assert_eq!(claim.calendar_at, nine);
    assert_eq!(claim.key, format!("{}:{nine}", active.id));
    assert!(scheduled(active, Some(&claim), true, nine + 30, 90).is_none());
    assert!(scheduled(active, None, true, nine - 1, 59).is_none());
    // Enabling in the scheduled minute must not backfill its occurrence.
    runtime.reconcile(&crate::config::Config::default(), nine, 60);
    runtime.reconcile(&cfg, nine, 60);
    assert!(scheduled(&runtime.activations["Timer"], None, true, nine + 1, 61).is_none());
}

#[test]
fn manual_claim_preserves_scheduled_identity_and_calendar_watermark() {
    let record = Occurrence {
        at: 100,
        key: "activation:10".into(),
        calendar_at: 60,
        outcome: "Submitted".into(),
    };
    let claim = manual(Some(&record), 200);
    assert_eq!(claim.at, 200);
    assert_eq!(claim.key, record.key);
    assert_eq!(claim.calendar_at, record.calendar_at);
    let initial = manual(None, 200);
    assert!(initial.key.is_empty());
    assert_eq!(initial.calendar_at, 0);
}
