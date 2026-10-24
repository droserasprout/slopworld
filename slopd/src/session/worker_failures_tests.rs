use super::*;
use crate::config::Config;
use crate::tasks::Status;

#[tokio::test]
async fn startup_fails_missing_durable_and_one_shot_workers_once() {
    let runtime = crate::session::test_manager(Config::default());
    runtime.tmux.ensure_server().await.unwrap();
    let mut ids = Vec::new();
    for durable in [false, true] {
        for status in [Status::Queued, Status::Accepted, Status::Working] {
            let task = runtime
                .task_store()
                .run(move |tasks| {
                    tasks.create_worker(
                        "host".into(),
                        "lost".into(),
                        "work".into(),
                        "host".into(),
                        durable,
                    )
                })
                .await
                .unwrap();
            runtime
                .task_store()
                .update_task_async("lost", "lost", &task.id, status, None)
                .await
                .unwrap();
            ids.push(task.id);
        }
    }
    runtime.sync_from_config().await;
    runtime.recover_missing_worker_tasks().await.unwrap();
    let recovered = runtime.task_store().all_tasks_async().await.unwrap();
    runtime.recover_missing_worker_tasks().await.unwrap();
    for id in ids {
        assert_task(
            &runtime,
            &id,
            Status::Failed,
            Some("worker session lost was lost while the daemon was offline"),
        )
        .await;
    }
    let repeated = runtime.task_store().all_tasks_async().await.unwrap();
    for (first, second) in recovered.iter().zip(repeated.iter()) {
        assert_eq!(first.history, second.history);
    }
}

#[tokio::test]
async fn startup_preserves_surviving_workers_terminal_results_and_ordinary_tasks() {
    let runtime = crate::session::test_manager(Config::default());
    let survivor = worker(&runtime).await;
    runtime
        .tmux
        .spawn(
            "child",
            "/tmp",
            80,
            24,
            &["sleep".into(), "60".into()],
            false,
        )
        .await
        .unwrap();
    let ordinary = runtime
        .task_store()
        .create_task("host".into(), "missing".into(), "ordinary".into())
        .await
        .unwrap();
    for status in [Status::Done, Status::Failed, Status::Canceled] {
        let task = runtime
            .task_store()
            .run(|tasks| {
                tasks.create_worker(
                    "host".into(),
                    "missing".into(),
                    "work".into(),
                    "host".into(),
                    false,
                )
            })
            .await
            .unwrap();
        runtime
            .task_store()
            .update_task_async(
                "missing",
                "missing",
                &task.id,
                status,
                Some("original result".into()),
            )
            .await
            .unwrap();
        runtime.recover_missing_worker_tasks().await.unwrap();
        assert_task(&runtime, &task.id, status, Some("original result")).await;
    }
    assert_task(&runtime, &survivor.task_id, Status::Queued, None).await;
    assert_task(&runtime, &ordinary.id, Status::Queued, None).await;
}

#[tokio::test]
async fn startup_retries_after_tmux_listing_failure_without_failing_workers() {
    let runtime = crate::session::test_manager(Config::default());
    let task = worker(&runtime).await;
    // There is no tmux server yet, so listing cannot confirm absence.
    assert!(runtime.recover_missing_worker_tasks().await.is_err());
    assert_task(&runtime, &task.task_id, Status::Queued, None).await;
    runtime.tmux.ensure_server().await.unwrap();
    runtime.recover_missing_worker_tasks().await.unwrap();
    assert_task(
        &runtime,
        &task.task_id,
        Status::Failed,
        Some("worker session child was lost while the daemon was offline"),
    )
    .await;
}

#[tokio::test]
async fn startup_preserves_a_worker_renamed_since_task_creation() {
    let runtime = crate::session::test_manager(Config::default());
    let mut session = worker(&runtime).await;
    session.name = "renamed".into();
    session.autostart = false;
    runtime
        .config_authority
        .write()
        .await
        .sessions
        .push(session.clone());
    runtime.upsert_sessions(&runtime.config().await).await;
    runtime
        .tmux
        .spawn(
            "renamed",
            "/tmp",
            80,
            24,
            &["sleep".into(), "60".into()],
            false,
        )
        .await
        .unwrap();
    runtime.recover_missing_worker_tasks().await.unwrap();
    assert_task(&runtime, &session.task_id, Status::Queued, None).await;
    runtime.tmux.kill("renamed").await.unwrap();
    runtime.recover_missing_worker_tasks().await.unwrap();
    assert_task(
        &runtime,
        &session.task_id,
        Status::Failed,
        Some("worker session child was lost while the daemon was offline"),
    )
    .await;
}

#[tokio::test]
async fn startup_retries_a_failed_task_write() {
    let runtime = crate::session::test_manager(Config::default());
    let session = worker(&runtime).await;
    runtime.tmux.ensure_server().await.unwrap();
    let path = runtime
        .cfg_path()
        .parent()
        .unwrap()
        .join("data/tasks")
        .join(format!("{}.toml", session.task_id));
    let saved = std::fs::read(&path).unwrap();
    std::fs::remove_file(&path).unwrap();
    std::fs::create_dir(&path).unwrap();
    assert!(runtime.recover_missing_worker_tasks().await.is_err());
    assert_eq!(
        runtime.task_store().all_tasks_async().await.unwrap()[0].status,
        Status::Queued
    );
    std::fs::remove_dir(&path).unwrap();
    std::fs::write(&path, saved).unwrap();
    runtime.recover_missing_worker_tasks().await.unwrap();
    assert_task(
        &runtime,
        &session.task_id,
        Status::Failed,
        Some("worker session child was lost while the daemon was offline"),
    )
    .await;
}

async fn worker(runtime: &SessionRuntime) -> SessionCfg {
    let task = runtime
        .task_store()
        .run(|tasks| {
            tasks.create_worker(
                "host".into(),
                "child".into(),
                "Delegate".into(),
                "parent".into(),
                true,
            )
        })
        .await
        .unwrap();
    SessionCfg {
        name: "child".into(),
        state_id: task.to_id,
        task_id: task.id,
        worker: true,
        ..Default::default()
    }
}

async fn assert_task(runtime: &SessionRuntime, id: &str, status: Status, note: Option<&str>) {
    let live = runtime.task_store().all_tasks_async().await.unwrap();
    let saved = crate::tasks::Tasks::load(runtime.cfg_path()).unwrap().all();
    for tasks in [live, saved] {
        let task = tasks.iter().find(|task| task.id == id).unwrap();
        assert_eq!(task.status, status);
        assert_eq!(task.note.as_deref(), note);
    }
}

#[tokio::test]
async fn lifecycle_and_startup_failures_preserve_the_first_terminal_result() {
    let runtime = crate::session::test_manager(Config::default());
    let cases = [
        (
            WorkerFailureCause::RegistrationFailed("disk full".into()),
            "could not persist worker session: disk full",
        ),
        (
            WorkerFailureCause::StartFailed("spawn failed".into()),
            "worker failed to start: spawn failed",
        ),
        (
            WorkerFailureCause::ProcessExited {
                reason: Some("exit 1".into()),
            },
            "worker session child: exit 1",
        ),
        (
            WorkerFailureCause::DeliveryTimedOut,
            "startup input withheld: agent readiness timed out",
        ),
    ];
    for (cause, note) in cases {
        let session = worker(&runtime).await;
        runtime.record_worker_failure(&session, cause).await;
        // Exit evidence is recorded before detach cleanup; cleanup must not replace it.
        WorkerFailure::for_detach(&session, &DetachCause::Forget)
            .unwrap()
            .apply(&runtime)
            .await;
        assert_task(&runtime, &session.task_id, Status::Failed, Some(note)).await;
    }
    for (cause, note) in [
        (
            DetachCause::Stop,
            "The daemon stopped worker session child.",
        ),
        (
            DetachCause::ConfigRemoval,
            "The daemon removed worker session child.",
        ),
        (
            DetachCause::Forget,
            "Worker session child exited, or the daemon stopped it.",
        ),
        (
            DetachCause::ProcessExit {
                reader_token: std::sync::Arc::new(()),
            },
            "worker session child exited",
        ),
    ] {
        let session = worker(&runtime).await;
        WorkerFailure::for_detach(&session, &cause)
            .unwrap()
            .apply(&runtime)
            .await;
        WorkerFailure::for_detach(&session, &cause)
            .unwrap()
            .apply(&runtime)
            .await;
        assert_task(&runtime, &session.task_id, Status::Failed, Some(note)).await;
    }
}

#[tokio::test]
async fn failure_rejects_nonworkers_stale_identity_and_missing_tasks() {
    let runtime = crate::session::test_manager(Config::default());
    let session = worker(&runtime).await;
    let mut other = session.clone();
    other.worker = false;
    assert!(WorkerFailure::for_session(&other, WorkerFailureCause::Stopped).is_none());
    other.worker = true;
    other.state_id = "replacement".into();
    runtime
        .record_worker_failure(&other, WorkerFailureCause::Stopped)
        .await;
    assert_task(&runtime, &session.task_id, Status::Queued, None).await;
    for id in ["", "  ", "missing"] {
        other.task_id = id.into();
        runtime
            .record_worker_failure(&other, WorkerFailureCause::Stopped)
            .await;
    }
    let ordinary = runtime
        .task_store()
        .create_task("host".into(), "agent".into(), "Ordinary".into())
        .await
        .unwrap();
    other.task_id = ordinary.id.clone();
    other.state_id = ordinary.to_id;
    runtime
        .record_worker_failure(&other, WorkerFailureCause::Stopped)
        .await;
    assert_task(&runtime, &ordinary.id, Status::Queued, None).await;
    assert_task(&runtime, &session.task_id, Status::Queued, None).await;
}

#[tokio::test]
async fn failure_preserves_completed_and_canceled_tasks() {
    let runtime = crate::session::test_manager(Config::default());
    for status in [Status::Done, Status::Canceled] {
        let session = worker(&runtime).await;
        runtime
            .task_store()
            .update_task_async(
                "child",
                "child",
                &session.task_id,
                status,
                Some("Already resolved".into()),
            )
            .await
            .unwrap();
        runtime
            .record_worker_failure(&session, WorkerFailureCause::Stopped)
            .await;
        assert_task(&runtime, &session.task_id, status, Some("Already resolved")).await;
    }
}
