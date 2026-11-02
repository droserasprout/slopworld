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

pub(super) fn timer_config(trigger: &str, schedule: &str) -> crate::config::Config {
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
async fn failed_claim_write_preserves_deadline_and_retries_same_tick() {
    let fixture = super::super::test_support::Fixture::new();
    let timers = Timers::new(fixture.root.join("timers.json"));
    let app = crate::application::test_application(timer_config("interval", "10s"));
    timers.tick_at(&app.sessions, &app, 1000, 0).await.unwrap();
    let failure = crate::paths::fail_writes(&timers.records.path);
    let error = timers
        .tick_at(&app.sessions, &app, 1010, 10)
        .await
        .unwrap_err();
    assert!(error.to_string().contains("injected persistence failure"));
    assert!(timers.records.load().await.unwrap().is_empty());
    {
        let runtime = timers.runtime.lock().await;
        assert_eq!(runtime.activations["Timer"].next, Some(10));
        assert_eq!(runtime.checked, Some((1000, 0)));
    }
    drop(failure);
    assert!(timers.tick_at(&app.sessions, &app, 1010, 10).await.unwrap());
    assert_eq!(
        timers.runtime.lock().await.activations["Timer"].next,
        Some(20)
    );
    assert_eq!(timers.records.load().await.unwrap()["Timer"].at, 1010);
    assert!(!timers.tick_at(&app.sessions, &app, 1010, 10).await.unwrap());
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

#[tokio::test]
async fn prompt_actions_resolve_stable_identity_and_target_each_agent_once() {
    let app = crate::application::test_application(crate::config::Config {
        projects: vec![crate::config::ProjectCfg {
            name: "repo".into(),
            dir: std::env::temp_dir().to_string_lossy().into(),
            ..Default::default()
        }],
        library: vec![crate::config::LibraryItemCfg {
            name: "Review".into(),
            kind: LibraryItemKind::Prompt,
            text: "Review the project".into(),
            ..Default::default()
        }],
        sessions: vec![crate::config::SessionCfg {
            name: "renamed".into(),
            project: "repo".into(),
            state_id: "0123456789abcdef".into(),
            ..Default::default()
        }],
        ..Default::default()
    });
    let cfg = Configuration::new(&app).config().await;
    let mut targeted = HashSet::new();
    let mut timer = timer_config("active", "1s").library[0]
        .timer
        .clone()
        .unwrap();
    assert_eq!(
        actions::execute(&cfg, &app, &mut targeted, &timer)
            .await
            .unwrap(),
        "Skipped: busy, down or not ready"
    );
    assert_eq!(
        actions::execute(&cfg, &app, &mut targeted, &timer)
            .await
            .unwrap(),
        "Skipped: another Timer targeted this agent"
    );
    timer.target = "renamed".into();
    assert_eq!(
        actions::execute(&cfg, &app, &mut targeted, &timer)
            .await
            .unwrap(),
        "Skipped: target missing"
    );
    timer.action = "unknown".into();
    assert!(
        actions::execute(&cfg, &app, &mut targeted, &timer)
            .await
            .unwrap_err()
            .to_string()
            .contains("Choose a Timer action")
    );
}

#[tokio::test]
async fn status_projection_distinguishes_activation_waiting_elapsed_disabled_and_invalid() {
    let fixture = super::super::test_support::Fixture::new();
    let timers = Timers::new(fixture.root.join("timers.json"));
    let cfg = timer_config("active", "1s");
    let app = crate::application::test_application(cfg.clone());
    let mut items = cfg.library.clone();
    timers.decorate(&mut items).await;
    assert_eq!(items[0].timer_status.as_ref().unwrap().state, "Activating");
    timers.tick_at(&app.sessions, &app, 1000, 0).await.unwrap();
    timers.decorate(&mut items).await;
    let waiting = items[0].timer_status.as_ref().unwrap();
    assert_eq!(waiting.state, "Waiting");
    assert!(waiting.next_at > 0);
    assert_eq!(waiting.outcome, "No attempts yet");
    timers.tick_at(&app.sessions, &app, 1001, 1).await.unwrap();
    timers.decorate(&mut items).await;
    let elapsed = items[0].timer_status.as_ref().unwrap();
    assert_eq!(elapsed.state, "Elapsed");
    assert_eq!(elapsed.next_at, 0);
    assert_eq!(elapsed.last_at, 1001);
    assert_eq!(elapsed.outcome, "Skipped: target missing");
    items[0].timer.as_mut().unwrap().enabled = false;
    timers.decorate(&mut items).await;
    assert_eq!(items[0].timer_status.as_ref().unwrap().state, "Disabled");
    items[0].timer.as_mut().unwrap().enabled = true;
    items[0].timer.as_mut().unwrap().schedule = "invalid".into();
    timers.decorate(&mut items).await;
    let invalid = items[0].timer_status.as_ref().unwrap();
    assert_eq!(invalid.state, "Invalid");
    assert!(invalid.outcome.starts_with("Invalid:"));
    items[0].timer = None;
    items[0].timer_status = None;
    timers.decorate(&mut items).await;
    assert!(items[0].timer_status.is_none());
    assert!(cfg.library[0].timer_status.is_none());
}

#[tokio::test]
async fn worker_and_shell_actions_launch_selected_library_content() {
    let Some(_) = crate::test_support::isolated_worker_runtime() else {
        return;
    };
    let (runtime, root, _) = crate::application::workers::tests::allocation_fixture();
    runtime.application.templates.store_for_test().write().await.templates = vec![serde_json::from_value(serde_json::json!({
        "name": "review", "defaults": { "cmd": "/bin/sleep 60", "network": "host", "persistent_tmp": false }
    })).unwrap()];
    runtime
        .config_authority
        .write()
        .await
        .daemon
        .instructions
        .worker_prompt
        .clear();
    let app = crate::application::Application::from_runtime(&runtime);
    let mut cfg = Configuration::new(&app).config().await;
    cfg.library = vec![
        crate::config::LibraryItemCfg {
            name: "Review".into(),
            kind: LibraryItemKind::Prompt,
            text: "Inspect the project".into(),
            ..Default::default()
        },
        crate::config::LibraryItemCfg {
            name: "Wait".into(),
            kind: LibraryItemKind::Shell,
            text: "sleep 60".into(),
            host: true,
            project: "repo".into(),
            ..Default::default()
        },
    ];
    for item in cfg.library.clone() {
        crate::application::library::Library::new(&app)
            .add_library_item(item)
            .await
            .unwrap();
    }
    let mut timer = TimerCfg {
        action: "worker".into(),
        prompt: "Review".into(),
        project: "repo".into(),
        template: "review".into(),
        ..Default::default()
    };
    let result = actions::execute(&cfg, &app, &mut HashSet::new(), &timer)
        .await
        .unwrap();
    let name = result.strip_prefix("Worker created: ").unwrap();
    assert!(runtime.tmux.exists(name).await);
    let tasks = runtime.task_store().all_tasks_async().await.unwrap();
    assert_eq!(tasks.len(), 1);
    assert_eq!(tasks[0].body, "Inspect the project");
    assert!(!tasks[0].worker.as_ref().unwrap().durable);
    assert_eq!(tasks[0].from, crate::tasks::HOST);
    runtime.stop(name).await.unwrap();
    timer.action = "shell".into();
    timer.shell = "Wait".into();
    let result = actions::execute(&cfg, &app, &mut HashSet::new(), &timer)
        .await
        .unwrap();
    let name = result.strip_prefix("Shell started: ").unwrap();
    assert!(runtime.tmux.exists(name).await);
    runtime.stop(name).await.unwrap();
    std::fs::remove_dir_all(root).unwrap();
}
