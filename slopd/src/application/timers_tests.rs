use super::*;

#[tokio::test]
async fn invalid_schedule_does_not_block_later_timers_on_repeated_ticks() {
    let fixture = super::super::test_support::Fixture::new();
    let timers = Timers::new(fixture.root.join("timers.json"));
    let mut config = timer_config("interval", "1min");
    let mut invalid = config.library[0].clone();
    invalid.name = "Invalid".into();
    invalid.timer.as_mut().unwrap().schedule = "invalid".into();
    config.library.insert(0, invalid);
    let app = crate::application::test_application(config);
    timers.tick_at(&app.sessions, &app, 6000, 0).await.unwrap();
    for elapsed in [60, 120] {
        timers
            .tick_at(&app.sessions, &app, 6000 + elapsed, elapsed)
            .await
            .unwrap();
        let records = timers.records.load().await.unwrap();
        assert!(!records.contains_key("Invalid"));
        assert_eq!(records["Timer"].at, 6000 + elapsed);
        assert_eq!(records["Timer"].outcome, "Skipped: target missing");
    }
}

#[tokio::test]
async fn occurrence_claims_survive_reopening_and_failed_write_does_not_claim() {
    let fixture = super::super::test_support::Fixture::new();
    let timers = Timers::new(fixture.root.join("timers.json"));
    let records = BTreeMap::from([(
        "Timer".into(),
        Occurrence {
            at: 40,
            outcome: "Claimed".into(),
            ..Default::default()
        },
    )]);
    timers.records.save(&records).await.unwrap();
    let reopened = Timers::new(timers.records.path.clone());
    assert_eq!(reopened.records.load().await.unwrap()["Timer"].at, 40);
    let blocked = Timers::new(fixture.root.clone());
    blocked.records.save(&records).await.unwrap_err();
}

#[tokio::test]
async fn calendar_restart_clock_rollback_and_missed_minutes_do_not_replay_claims() {
    let fixture = super::super::test_support::Fixture::new();
    let timers = Timers::new(fixture.root.join("timers.json"));
    let mut config = timer_config("calendar", "09:00");
    config.library[0].timer.as_mut().unwrap().timezone = "UTC".into();
    let app = crate::application::test_application(config);
    let nine = 9 * 3600;
    timers
        .tick_at(&app.sessions, &app, nine - 60, 0)
        .await
        .unwrap();
    timers.tick_at(&app.sessions, &app, nine, 60).await.unwrap();
    let saved = tokio::fs::read_to_string(&timers.records.path)
        .await
        .unwrap();
    timers.tick_at(&app.sessions, &app, nine, 60).await.unwrap();
    let reopened = Timers::new(timers.records.path.clone());
    reopened
        .tick_at(&app.sessions, &app, nine - 60, 0)
        .await
        .unwrap();
    reopened
        .tick_at(&app.sessions, &app, nine, 60)
        .await
        .unwrap();
    assert_eq!(
        tokio::fs::read_to_string(&timers.records.path)
            .await
            .unwrap(),
        saved
    );
    // A missed minute is skipped; the next daily occurrence still runs.
    reopened
        .tick_at(&app.sessions, &app, nine + 86400 + 60, 86400)
        .await
        .unwrap();
    assert_eq!(
        tokio::fs::read_to_string(&timers.records.path)
            .await
            .unwrap(),
        saved
    );
    reopened
        .tick_at(&app.sessions, &app, nine + 2 * 86400, 2 * 86400)
        .await
        .unwrap();
    assert_eq!(
        reopened.records.load().await.unwrap()["Timer"].at,
        nine + 2 * 86400
    );
}

fn timer_config(trigger: &str, schedule: &str) -> crate::config::Config {
    crate::config::Config {
        library: vec![crate::config::LibraryItemCfg {
            name: "Timer".into(),
            kind: LibraryItemKind::Timer,
            timer: Some(crate::config::TimerCfg {
                enabled: true,
                action: "prompt".into(),
                trigger: trigger.into(),
                schedule: schedule.into(),
                target: "0123456789abcdef".into(),
                prompt: "Review".into(),
                ..Default::default()
            }),
            ..Default::default()
        }],
        ..Default::default()
    }
}

#[tokio::test]
async fn intervals_follow_monotonic_time_skip_backlogs_and_rearm_on_restart() {
    let fixture = super::super::test_support::Fixture::new();
    let timers = Timers::new(fixture.root.join("timers.json"));
    let app = crate::application::test_application(timer_config("interval", "10s"));
    timers.tick_at(&app.sessions, &app, 1000, 0).await.unwrap();
    assert!(timers.records.load().await.unwrap().is_empty());
    timers.tick_at(&app.sessions, &app, 900, 10).await.unwrap();
    assert_eq!(timers.records.load().await.unwrap()["Timer"].at, 900);
    // The wall clock can stand still while the monotonic clock advances.
    timers.tick_at(&app.sessions, &app, 900, 45).await.unwrap();
    assert_eq!(
        timers.runtime.lock().await.activations["Timer"].next,
        Some(50)
    );
    let records = tokio::fs::read_to_string(&timers.records.path)
        .await
        .unwrap();
    timers.tick_at(&app.sessions, &app, 901, 49).await.unwrap();
    assert_eq!(
        tokio::fs::read_to_string(&timers.records.path)
            .await
            .unwrap(),
        records
    );
    let restarted = Timers::new(timers.records.path.clone());
    restarted
        .tick_at(&app.sessions, &app, 2000, 0)
        .await
        .unwrap();
    assert_eq!(
        tokio::fs::read_to_string(&timers.records.path)
            .await
            .unwrap(),
        records
    );
    restarted
        .tick_at(&app.sessions, &app, 2010, 10)
        .await
        .unwrap();
    assert_eq!(restarted.records.load().await.unwrap()["Timer"].at, 2010);
}

#[tokio::test]
async fn startup_and_activation_delays_are_one_shot_with_distinct_anchors() {
    let fixture = super::super::test_support::Fixture::new();
    for (trigger, due) in [("startup", 50), ("active", 60)] {
        let timers = Timers::new(fixture.root.join(trigger));
        let app = crate::application::test_application(timer_config(trigger, "10s"));
        timers.tick_at(&app.sessions, &app, 1000, 50).await.unwrap();
        assert_eq!(
            !timers.records.load().await.unwrap().is_empty(),
            trigger == "startup"
        );
        timers
            .tick_at(&app.sessions, &app, 1010, due)
            .await
            .unwrap();
        let saved = tokio::fs::read_to_string(&timers.records.path)
            .await
            .unwrap();
        timers
            .tick_at(&app.sessions, &app, 2000, 500)
            .await
            .unwrap();
        assert_eq!(
            tokio::fs::read_to_string(&timers.records.path)
                .await
                .unwrap(),
            saved
        );
    }
}

#[tokio::test]
async fn failed_claim_retries_and_interrupted_claim_is_not_resubmitted() {
    let fixture = super::super::test_support::Fixture::new();
    let path = fixture.root.join("blocked");
    tokio::fs::create_dir_all(&path).await.unwrap();
    let timers = Timers::new(path.clone());
    let app = crate::application::test_application(timer_config("active", "10s"));
    timers
        .tick_at(&app.sessions, &app, 1000, 0)
        .await
        .unwrap_err();
    tokio::fs::remove_dir(&path).await.unwrap();
    timers.tick_at(&app.sessions, &app, 1000, 0).await.unwrap();
    // Simulate cancellation immediately after the durable claim and before the
    // in-memory deadline advances. The next pass must retain the uncertain result.
    let key = {
        let runtime = timers.runtime.lock().await;
        format!("{}:10", runtime.activations["Timer"].id)
    };
    timers
        .records
        .save(&BTreeMap::from([(
            "Timer".into(),
            Occurrence {
                at: 1010,
                key,
                outcome: "Claimed; uncertain".into(),
                ..Default::default()
            },
        )]))
        .await
        .unwrap();
    timers.tick_at(&app.sessions, &app, 1011, 11).await.unwrap();
    assert_eq!(
        timers.records.load().await.unwrap()["Timer"].outcome,
        "Claimed; uncertain"
    );
    assert_eq!(timers.runtime.lock().await.activations["Timer"].next, None);
}

#[test]
fn disabling_editing_and_removing_rearm_or_retire_activation() {
    let mut cfg = timer_config("interval", "10s");
    let mut runtime = Runtime::default();
    assert!(runtime.reconcile(&cfg, 0, 0));
    assert!(!runtime.reconcile(&cfg, 5, 5));
    assert_eq!(runtime.activations["Timer"].next, Some(10));
    cfg.library[0].timer.as_mut().unwrap().enabled = false;
    assert!(runtime.reconcile(&cfg, 5, 5));
    assert!(runtime.activations.is_empty());
    cfg.library[0].timer.as_mut().unwrap().enabled = true;
    runtime.reconcile(&cfg, 6, 6);
    assert_eq!(runtime.activations["Timer"].next, Some(16));
    cfg.library[0].timer.as_mut().unwrap().schedule = "20s".into();
    runtime.reconcile(&cfg, 7, 7);
    assert_eq!(runtime.activations["Timer"].next, Some(27));
    cfg.library.clear();
    runtime.reconcile(&cfg, 8, 8);
    assert!(runtime.activations.is_empty());
}

#[tokio::test]
async fn manual_runs_preserve_disabled_state_and_scheduled_deadline() {
    let fixture = super::super::test_support::Fixture::new();
    let timers = Timers::new(fixture.root.join("timers.json"));
    let app = crate::application::test_application(timer_config("active", "10s"));
    timers.tick_at(&app.sessions, &app, 1000, 0).await.unwrap();
    timers.run_now(&app.sessions, &app, "Timer").await.unwrap();
    assert_eq!(
        timers.runtime.lock().await.activations["Timer"].next,
        Some(10)
    );
    assert_eq!(
        timers.records.load().await.unwrap()["Timer"].outcome,
        "Skipped: target missing"
    );
    let mut disabled = timer_config("active", "10s");
    disabled.library[0].timer.as_mut().unwrap().enabled = false;
    let app = crate::application::test_application(disabled);
    timers.run_now(&app.sessions, &app, "Timer").await.unwrap();
    assert!(
        !Configuration::new(&app).config().await.library[0]
            .timer
            .as_ref()
            .unwrap()
            .enabled
    );
    assert!(
        timers
            .run_now(&app.sessions, &app, "Missing")
            .await
            .is_err()
    );
}

#[tokio::test]
async fn failed_worker_and_shell_actions_consume_one_shot_and_allow_later_timers() {
    let fixture = super::super::test_support::Fixture::new();
    let timers = Timers::new(fixture.root.join("timers.json"));
    let mut config = timer_config("active", "1s");
    for action in ["worker", "shell"] {
        let mut item = config.library[0].clone();
        item.name = action.into();
        item.timer.as_mut().unwrap().action = action.into();
        config.library.push(item);
    }
    let app = crate::application::test_application(config);
    timers.tick_at(&app.sessions, &app, 1000, 0).await.unwrap();
    timers.tick_at(&app.sessions, &app, 1001, 1).await.unwrap();
    let records = timers.records.load().await.unwrap();
    assert_eq!(records["Timer"].outcome, "Skipped: target missing");
    assert!(records["worker"].outcome.contains("Library Prompt"));
    assert!(records["shell"].outcome.contains("Library shell script"));
    timers.tick_at(&app.sessions, &app, 1002, 2).await.unwrap();
    assert!(
        timers
            .records
            .load()
            .await
            .unwrap()
            .values()
            .all(|r| r.at == 1001)
    );
}
