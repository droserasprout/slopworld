use super::*;
#[test]
fn visibility_updates_and_persistence() {
    let dir = std::env::temp_dir().join(format!("slopd-tasks-{}", std::process::id()));
    let _ = fs::remove_dir_all(&dir);
    fs::create_dir_all(&dir).unwrap();
    let mut s = Tasks::load(&dir.join("config.toml")).unwrap();
    let t = s
        .create("alice".into(), "bob".into(), "check".into())
        .unwrap();
    assert!(s.visible("eve").is_empty());
    assert!(s.update("alice", &t.id, Status::Done, None).is_err());
    s.update("bob", &t.id, Status::Done, Some("ok".into()))
        .unwrap();
    assert_eq!(
        Tasks::load(&dir.join("config.toml"))
            .unwrap()
            .get("alice", &t.id)
            .unwrap()
            .status,
        Status::Done
    );
    let _ = fs::remove_dir_all(dir);
}

#[test]
fn summary_round_trips_without_changing_task_age() {
    let dir = std::env::temp_dir().join(format!("slopd-task-summary-{}", std::process::id()));
    let _ = fs::remove_dir_all(&dir);
    fs::create_dir_all(&dir).unwrap();
    let config = dir.join("config.toml");
    let mut tasks = Tasks::load(&config).unwrap();
    let task = tasks
        .create(
            "host".into(),
            "agent".into(),
            "inspect the sidebar layout".into(),
        )
        .unwrap();
    let updated = tasks
        .set_summary(&task.id, "Inspect sidebar layout".into())
        .unwrap()
        .unwrap();

    assert_eq!(updated.summary.as_deref(), Some("Inspect sidebar layout"));
    assert_eq!(updated.updated_ms, task.updated_ms);
    assert_eq!(
        Tasks::load(&config).unwrap().all()[0].summary.as_deref(),
        Some("Inspect sidebar layout")
    );
    let _ = fs::remove_dir_all(dir);
}

/// Ordinary removal requires a terminal task. Root access permits removal of active tasks.
/// Pruning removes terminal tasks visible to the caller. The `all` option removes the visibility restriction.
#[test]
fn removing_and_pruning_finished_work() {
    let dir = std::env::temp_dir().join(format!("slopd-rm-tasks-{}", std::process::id()));
    let _ = fs::remove_dir_all(&dir);
    fs::create_dir_all(&dir).unwrap();
    let config = dir.join("config.toml");
    let mut s = Tasks::load(&config).unwrap();
    let live = s
        .create("alice".into(), "bob".into(), "live".into())
        .unwrap();
    let over = s
        .create("alice".into(), "bob".into(), "over".into())
        .unwrap();
    s.update("bob", &over.id, Status::Done, None).unwrap();

    let accepted = s
        .create("alice".into(), "bob".into(), "accepted".into())
        .unwrap();
    s.update("bob", &accepted.id, Status::Accepted, None)
        .unwrap();
    let canceled = s
        .cancel_many("bob", std::slice::from_ref(&accepted.id), false)
        .unwrap();
    assert_eq!(canceled[0].status, Status::Canceled);
    assert!(s.update("bob", &accepted.id, Status::Done, None).is_err());
    assert!(s.remove("alice", &accepted.id, false).is_ok());

    assert!(s.remove("eve", &over.id, false).is_err()); // not a participant
    assert!(s.remove("alice", &live.id, false).is_err()); // still in flight
    assert!(s.remove("alice", &live.id, true).is_ok()); // the root reaches it
    assert!(s.remove("alice", &over.id, false).is_ok()); // finished, so either side may

    let first = s
        .create("alice".into(), "bob".into(), "first batch item".into())
        .unwrap();
    let second = s
        .create("alice".into(), "bob".into(), "second batch item".into())
        .unwrap();
    s.update("bob", &first.id, Status::Done, None).unwrap();
    s.update("bob", &second.id, Status::Failed, None).unwrap();
    assert_eq!(
        s.remove_many(
            "alice",
            &[first.id.clone(), second.id.clone(), first.id],
            false
        )
        .unwrap(),
        2
    );

    let other = s
        .create("carol".into(), "dave".into(), "theirs".into())
        .unwrap();
    s.update("dave", &other.id, Status::Failed, None).unwrap();
    assert_eq!(s.prune("alice", false).unwrap(), 0); // not alice's to see
    assert_eq!(s.prune("carol", false).unwrap(), 1);

    let mut s = Tasks::load(&config).unwrap();
    let mine = s
        .create("alice".into(), "bob".into(), "mine".into())
        .unwrap();
    s.update("bob", &mine.id, Status::Done, None).unwrap();
    assert_eq!(s.prune("nobody", true).unwrap(), 1); // `all` ignores who is asking
    assert!(Tasks::load(&config).unwrap().visible("alice").is_empty());
    let _ = fs::remove_dir_all(dir);
}

#[test]
fn invalid_store_is_ignored_and_loaded_ids_continue_the_sequence() {
    let dir = std::env::temp_dir().join(format!("slopd-bad-tasks-{}", std::process::id()));
    let _ = fs::remove_dir_all(&dir);
    fs::create_dir_all(&dir).unwrap();
    let config = dir.join("config.toml");
    fs::write(dir.join("tasks.toml"), "[").unwrap();
    assert!(Tasks::load(&config).unwrap().visible("anyone").is_empty());

    let mut tasks = Tasks::load(&config).unwrap();
    let first = tasks
        .create("alice".into(), "bob".into(), "first".into())
        .unwrap();
    let mut loaded = Tasks::load(&config).unwrap();
    let second = loaded
        .create("alice".into(), "bob".into(), "second".into())
        .unwrap();
    assert_eq!(first.id.rsplit_once('-').unwrap().1, "0001");
    assert_eq!(second.id.rsplit_once('-').unwrap().1, "0002");
    let _ = fs::remove_dir_all(dir);
}

#[test]
fn worker_task_metadata_and_daemon_failure_survive_reload() {
    let dir = std::env::temp_dir().join(format!("slopd-worker-tasks-{}", std::process::id()));
    let _ = fs::remove_dir_all(&dir);
    fs::create_dir_all(&dir).unwrap();
    let config = dir.join("config.toml");
    let mut tasks = Tasks::load(&config).unwrap();
    let task = tasks
        .create_worker(
            "parent".into(),
            "parent-worker".into(),
            "inspect the build".into(),
            "parent".into(),
            true,
        )
        .unwrap();
    let worker = task.worker.as_ref().unwrap();
    assert_eq!(worker.session, "parent-worker");
    assert_eq!(worker.parent, "parent");
    assert!(worker.durable);

    let failed = tasks
        .fail_worker(&task.id, "worker exited".into())
        .unwrap()
        .unwrap();
    assert_eq!(failed.status, Status::Failed);
    assert_eq!(failed.note.as_deref(), Some("worker exited"));

    let reloaded = Tasks::load(&config).unwrap();
    let persisted = reloaded.get("parent", &task.id).unwrap();
    assert_eq!(persisted.status, Status::Failed);
    assert_eq!(persisted.worker.unwrap().session, "parent-worker");
    let unchanged = tasks
        .fail_worker(&task.id, "a later exit".into())
        .unwrap()
        .unwrap();
    assert_eq!(unchanged.note.as_deref(), Some("worker exited"));
    let _ = fs::remove_dir_all(dir);
}
