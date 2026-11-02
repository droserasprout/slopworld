use super::*;

#[tokio::test]
async fn invalid_schedule_does_not_block_later_timers_on_repeated_ticks() {
    let fixture = super::super::test_support::Fixture::new();
    let path = fixture.root.join("occurrences.json");
    let library = [("Invalid", "invalid"), ("Valid", "* * * * *")]
        .into_iter()
        .map(|(name, schedule)| crate::config::LibraryItemCfg {
            name: name.into(),
            kind: LibraryItemKind::Timer,
            timer: Some(crate::config::TimerCfg {
                enabled: true,
                schedule: schedule.into(),
                target: "0123456789abcdef".into(),
                prompt: "Maintenance".into(),
            }),
            ..Default::default()
        })
        .collect();
    let app = crate::application::test_application(crate::config::Config {
        library,
        ..Default::default()
    });
    let cfg = Configuration::new(&app);
    let timers = Timers::new(path.clone());
    assert!(!timers.tick_at(&app.sessions, &cfg, 100).await.unwrap());
    for minute in [101, 102] {
        assert!(timers.tick_at(&app.sessions, &cfg, minute).await.unwrap());
        let records: BTreeMap<String, Occurrence> =
            serde_json::from_str(&tokio::fs::read_to_string(&path).await.unwrap()).unwrap();
        assert!(!records.contains_key("Invalid"));
        assert_eq!(records["Valid"].minute, minute);
        assert_eq!(records["Valid"].outcome, "Skipped: target missing");
    }
}

#[test]
fn utc_cron_supports_steps_ranges_lists_and_calendar_days() {
    // 1970-01-01 00:00 UTC, Thursday.
    assert!(Schedule::parse("*/15 * * * *").unwrap().matches(0));
    assert!(!Schedule::parse("*/15 * * * *").unwrap().matches(1));
    assert!(Schedule::parse("0,15,30 0-3 1 1 4").unwrap().matches(15));
    assert!(Schedule::parse("0 0 2 * 4").unwrap().matches(0));
    assert!(!Schedule::parse("0 0 2 * *").unwrap().matches(0));
    assert!(!Schedule::parse("0 0 * * 5").unwrap().matches(0));
}
#[test]
fn invalid_schedules_never_enter_scheduler() {
    for text in [
        "",
        "* * * *",
        "60 * * * *",
        "*/0 * * * *",
        "* 24 * * *",
        "* * 0 * *",
        "* * * 13 *",
        "* * * * 7",
        "3-1 * * * *",
        "@daily",
    ] {
        assert!(Schedule::parse(text).is_err(), "{text}");
    }
}
#[tokio::test]
async fn occurrence_claims_survive_reopening_and_failed_write_does_not_claim() {
    let fixture = super::super::test_support::Fixture::new();
    let path = fixture.root.join("timers.json");
    let timers = Timers::new(path.clone());
    let records = BTreeMap::from([(
        "Timer".into(),
        Occurrence {
            minute: 40,
            outcome: "Claimed".into(),
        },
    )]);
    timers.save(&records).await.unwrap();
    let restored: BTreeMap<String, Occurrence> =
        serde_json::from_str(&tokio::fs::read_to_string(path).await.unwrap()).unwrap();
    assert_eq!(restored["Timer"].minute, 40);
    let blocked = Timers::new(fixture.root.clone());
    assert!(blocked.save(&records).await.is_err());
}

#[tokio::test]
async fn duplicate_ticks_restart_and_clock_rollback_do_not_replay_claims() {
    let fixture = super::super::test_support::Fixture::new();
    let path = fixture.root.join("occurrences.json");
    let app = crate::application::test_application(crate::config::Config {
        library: vec![crate::config::LibraryItemCfg {
            name: "Test timer".into(),
            kind: LibraryItemKind::Timer,
            timer: Some(crate::config::TimerCfg {
                enabled: true,
                schedule: "* * * * *".into(),
                target: "0123456789abcdef".into(),
                prompt: "Scheduled maintenance".into(),
            }),
            ..Default::default()
        }],
        ..Default::default()
    });
    let cfg = Configuration::new(&app);
    let timers = Timers::new(path.clone());
    assert!(!timers.tick_at(&app.sessions, &cfg, 100).await.unwrap());
    timers.tick_at(&app.sessions, &cfg, 101).await.unwrap();
    let saved = tokio::fs::read_to_string(&path).await.unwrap();
    assert!(saved.contains("target missing"));
    assert!(!timers.tick_at(&app.sessions, &cfg, 101).await.unwrap());
    let restored = Timers::new(path.clone());
    restored.tick_at(&app.sessions, &cfg, 100).await.unwrap();
    restored.tick_at(&app.sessions, &cfg, 101).await.unwrap();
    assert_eq!(tokio::fs::read_to_string(&path).await.unwrap(), saved);
    // Downtime coalesces to the present minute, without writing intervening occurrences.
    restored.tick_at(&app.sessions, &cfg, 110).await.unwrap();
    let records: BTreeMap<String, Occurrence> =
        serde_json::from_str(&tokio::fs::read_to_string(path).await.unwrap()).unwrap();
    assert_eq!(records["Test timer"].minute, 110);
}

#[test]
fn next_occurrence_handles_year_boundaries_and_impossible_calendar_dates() {
    assert_eq!(
        Schedule::parse("*/15 * * * *").unwrap().next_after(0),
        Some(15)
    );
    assert_eq!(
        Schedule::parse("5/15 * * * *").unwrap().next_after(5),
        Some(20)
    );
    assert_eq!(
        Schedule::parse("0 0 1 1 *").unwrap().next_after(0),
        Some(365 * 1440)
    );
    assert_eq!(Schedule::parse("0 0 31 2 *").unwrap().next_after(0), None);
}
