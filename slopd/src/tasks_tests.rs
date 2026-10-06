use super::*;

#[test]
fn unknown_task_status_lists_valid_tags() {
    let error = serde_json::from_str::<Status>("\"stale\"")
        .unwrap_err()
        .to_string();
    assert!(
        error.contains("queued") && error.contains("working"),
        "{error}"
    );
}

#[test]
fn update_journal_replays_and_snapshot_generation_prevents_resurrection() {
    let dir = std::env::temp_dir().join(format!("slopd-task-journal-{}", std::process::id()));
    drop(fs::remove_dir_all(&dir));
    fs::create_dir_all(&dir).unwrap();
    let config = dir.join("config.toml");
    let mut tasks = Tasks::load(&config).unwrap();
    let task = tasks
        .create("alice".into(), "bob".into(), "work".into())
        .unwrap();
    tasks
        .update("bob", &task.id, Status::Done, Some("done".into()))
        .unwrap();
    assert_eq!(
        Tasks::load(&config)
            .unwrap()
            .get("alice", &task.id)
            .unwrap()
            .status,
        Status::Done
    );
    assert!(
        !dir.join("tasks.toml").exists(),
        "creation and progress append without rewriting snapshots"
    );

    fs::OpenOptions::new()
        .append(true)
        .open(dir.join("tasks.journal"))
        .unwrap()
        .write_all(b"incomplete")
        .unwrap();
    let mut loaded = Tasks::load(&config).unwrap();
    loaded
        .update(
            "bob",
            &task.id,
            Status::Done,
            Some("after partial tail".into()),
        )
        .unwrap();
    assert_eq!(
        Tasks::load(&config)
            .unwrap()
            .get("bob", &task.id)
            .unwrap()
            .note
            .as_deref(),
        Some("after partial tail")
    );
    let old_journal = fs::read(dir.join("tasks.journal")).unwrap();
    loaded.remove("alice", &task.id, false).unwrap();
    // Simulate a crash after replacing the snapshot but before retiring its old journal.
    fs::write(dir.join("tasks.journal"), old_journal).unwrap();
    assert!(Tasks::load(&config).unwrap().all().is_empty());
    drop(fs::remove_dir_all(dir));
}
#[test]
fn visibility_updates_and_persistence() {
    let dir = std::env::temp_dir().join(format!("slopd-tasks-{}", std::process::id()));
    drop(fs::remove_dir_all(&dir));
    fs::create_dir_all(&dir).unwrap();
    let mut s = Tasks::load(&dir.join("config.toml")).unwrap();
    let t = s
        .create("alice".into(), "bob".into(), "check".into())
        .unwrap();
    assert!(s.visible("eve").is_empty());
    s.update("alice", &t.id, Status::Done, None).unwrap_err();
    assert_eq!(s.get("alice", &t.id).unwrap().status, Status::Queued);
    assert_eq!(
        Tasks::load(&dir.join("config.toml"))
            .unwrap()
            .get("alice", &t.id)
            .unwrap()
            .status,
        Status::Queued
    );
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
    drop(fs::remove_dir_all(dir));
}

#[test]
fn summary_round_trips_without_changing_task_age() {
    let dir = std::env::temp_dir().join(format!("slopd-task-summary-{}", std::process::id()));
    drop(fs::remove_dir_all(&dir));
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
    drop(fs::remove_dir_all(dir));
}

/// Ordinary removal requires a terminal task. Root access permits removal of active tasks.
/// Pruning removes terminal tasks visible to the caller. The `all` option removes the visibility restriction.
#[test]
fn removing_and_pruning_finished_work() {
    let dir = std::env::temp_dir().join(format!("slopd-rm-tasks-{}", std::process::id()));
    drop(fs::remove_dir_all(&dir));
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
    s.update("bob", &accepted.id, Status::Done, None)
        .unwrap_err();
    s.remove("alice", &accepted.id, false).unwrap();

    s.remove("eve", &over.id, false).unwrap_err(); // not a participant
    s.remove("alice", &live.id, false).unwrap_err(); // still in flight
    s.remove("alice", &live.id, true).unwrap(); // the root reaches it
    s.remove("alice", &over.id, false).unwrap(); // finished, so either side may

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
    drop(fs::remove_dir_all(dir));
}

#[test]
fn invalid_store_is_ignored_and_loaded_ids_continue_the_sequence() {
    let dir = std::env::temp_dir().join(format!("slopd-bad-tasks-{}", std::process::id()));
    drop(fs::remove_dir_all(&dir));
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
    drop(fs::remove_dir_all(dir));
}

#[test]
fn worker_task_metadata_and_daemon_failure_survive_reload() {
    let dir = std::env::temp_dir().join(format!("slopd-worker-tasks-{}", std::process::id()));
    drop(fs::remove_dir_all(&dir));
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
    drop(fs::remove_dir_all(dir));
}

#[test]
fn compact_journal_preserves_creations_and_updates() {
    let dir = std::env::temp_dir().join(format!("slopd-task-compact-{}", uuid::Uuid::new_v4()));
    fs::create_dir_all(&dir).unwrap();
    let config = dir.join("config.toml");
    let mut tasks = Tasks::load(&config).unwrap();
    let task = tasks
        .create("alice".into(), "bob".into(), "large body".repeat(1000))
        .unwrap();
    tasks.save().unwrap();
    let generation = tasks.file.generation;
    for n in 0..140 {
        tasks
            .update(
                "bob",
                &task.id,
                Status::Working,
                Some(format!("{n}{}", "x".repeat(8192))),
            )
            .unwrap();
    }
    assert!(
        tasks.file.generation > generation,
        "size threshold compacts automatically"
    );
    assert!(fs::metadata(&tasks.journal).unwrap().len() < MAX_JOURNAL_BYTES);
    tasks.set_summary(&task.id, "summary".into()).unwrap();
    tasks.update("bob", &task.id, Status::Done, None).unwrap();
    let loaded = Tasks::load(&config).unwrap().get("bob", &task.id).unwrap();
    assert_eq!(loaded.body, task.body);
    assert_eq!(loaded.status, Status::Done);
    assert_eq!(loaded.note, None);
    assert_eq!(loaded.summary.as_deref(), Some("summary"));
    assert!(
        !fs::read_to_string(&tasks.journal)
            .unwrap()
            .contains("large body")
    );
    drop(fs::remove_dir_all(dir));
}

#[test]
fn first_creation_creates_missing_store_directory() {
    let dir = std::env::temp_dir().join(format!("slopd-task-new-{}", uuid::Uuid::new_v4()));
    let config = dir.join("nested/config.toml");
    let mut tasks = Tasks::load(&config).unwrap();
    let task = tasks
        .create("alice".into(), "bob".into(), "first".into())
        .unwrap();
    assert_eq!(
        Tasks::load(&config)
            .unwrap()
            .get("bob", &task.id)
            .unwrap()
            .body,
        "first"
    );
    fs::remove_dir_all(dir).unwrap();
}

fn isolated_store() -> (PathBuf, Tasks) {
    let directory =
        std::env::temp_dir().join(format!("slopd-task-commit-{}", uuid::Uuid::new_v4()));
    fs::create_dir_all(&directory).unwrap();
    let tasks = Tasks::load(&directory.join("config.toml")).unwrap();
    (directory, tasks)
}

#[test]
fn task_authority_survives_rename_but_not_name_reuse() {
    let (dir, mut tasks) = isolated_store();
    let participant = |name: &str, identity: &str| Participant {
        name: name.into(),
        identity: identity.into(),
    };
    let task = tasks
        .create_owned(
            participant("parent", "parent-id"),
            participant("worker", "first-id"),
            "private".into(),
            None,
        )
        .unwrap();
    assert_eq!(tasks.visible("first-id").len(), 1);
    // A new worker may have the same label but receives a new state identity.
    let next = tasks
        .create_owned(
            participant("parent", "parent-id"),
            participant("worker", "second-id"),
            "next".into(),
            None,
        )
        .unwrap();
    assert_eq!(tasks.visible("second-id")[0].id, next.id);
    assert!(tasks.get("second-id", &task.id).is_none());
    tasks
        .update("second-id", &task.id, Status::Done, None)
        .unwrap_err();
    tasks
        .cancel_many("second-id", std::slice::from_ref(&task.id), false)
        .unwrap_err();
    tasks
        .update("first-id", &task.id, Status::Done, None)
        .unwrap();
    tasks.remove("second-id", &task.id, false).unwrap_err();
    tasks
        .remove_many("second-id", std::slice::from_ref(&task.id), false)
        .unwrap_err();
    assert_eq!(tasks.prune("second-id", false).unwrap(), 0);
    // Renaming changes only the label resolved by the manager; the authority is unchanged.
    let renamed = participant("renamed", "first-id");
    assert_eq!(
        tasks.get(&renamed.identity, &task.id).unwrap().body,
        "private"
    );
    let mut reloaded = Tasks::load(&dir.join("config.toml")).unwrap();
    assert_eq!(
        reloaded
            .remove(&renamed.identity, &task.id, false)
            .unwrap()
            .id,
        task.id
    );
    fs::remove_dir_all(dir).unwrap();
}

#[test]
fn legacy_records_never_bind_to_current_names() {
    let (dir, mut tasks) = isolated_store();
    let task = tasks
        .create("host".into(), "worker".into(), "legacy".into())
        .unwrap();
    tasks.file.tasks[0].from_id.clear();
    tasks.file.tasks[0].to_id.clear();
    tasks.save().unwrap();
    let tasks = Tasks::load(&dir.join("config.toml")).unwrap();
    assert!(tasks.get("worker", &task.id).is_none());
    assert!(tasks.get("new-worker-id", &task.id).is_none());
    assert!(tasks.get(HOST, &task.id).is_some());
    assert_eq!(tasks.all().len(), 1);
    fs::remove_dir_all(dir).unwrap();
}

#[test]
fn failed_journal_updates_preserve_memory_and_can_be_retried() {
    let (dir, mut tasks) = isolated_store();
    let task = tasks
        .create_worker(
            "host".into(),
            "worker".into(),
            "work".into(),
            "host".into(),
            false,
        )
        .unwrap();
    // Move the journal aside and put a directory in its place to fail append deterministically.
    fs::rename(&tasks.journal, dir.join("saved-journal")).unwrap();
    fs::create_dir(&tasks.journal).unwrap();
    tasks
        .update("worker", &task.id, Status::Done, Some("lost".into()))
        .unwrap_err();
    tasks
        .set_summary(&task.id, "lost summary".into())
        .unwrap_err();
    tasks
        .fail_worker(&task.id, "lost failure".into())
        .unwrap_err();
    let current = tasks.get("worker", &task.id).unwrap();
    assert_eq!(current.status, Status::Queued);
    assert_eq!(current.note, None);
    assert_eq!(current.summary, None);
    fs::remove_dir(&tasks.journal).unwrap();
    fs::rename(dir.join("saved-journal"), &tasks.journal).unwrap();
    assert_eq!(
        Tasks::load(&dir.join("config.toml"))
            .unwrap()
            .get("worker", &task.id)
            .unwrap()
            .status,
        Status::Queued
    );
    tasks
        .fail_worker(&task.id, "persisted failure".into())
        .unwrap();
    assert_eq!(
        Tasks::load(&dir.join("config.toml"))
            .unwrap()
            .get("worker", &task.id)
            .unwrap()
            .status,
        Status::Failed
    );
    fs::remove_dir_all(dir).unwrap();
}

#[test]
fn unreadable_journal_never_admits_new_writes() {
    let (dir, mut tasks) = isolated_store();
    let created = tasks
        .create("host".into(), "worker".into(), "first".into())
        .unwrap();
    // read_to_string fails for invalid UTF-8. A later append must not be
    // acknowledged because replay would stop before that new record.
    use std::io::Write;
    std::fs::OpenOptions::new()
        .append(true)
        .open(&tasks.journal)
        .unwrap()
        .write_all(&[0xff])
        .unwrap();
    assert!(Tasks::load(&dir.join("config.toml")).is_err());
    assert_eq!(tasks.get("worker", &created.id).unwrap().body, "first");
    fs::remove_dir_all(dir).unwrap();
}

#[test]
fn failed_snapshots_preserve_memory_and_allow_cancel_remove_and_prune_retries() {
    let (dir, mut tasks) = isolated_store();
    let task = tasks
        .create("host".into(), "worker".into(), "work".into())
        .unwrap();
    let fault = crate::paths::fail_writes(&tasks.path);
    tasks
        .cancel_many("worker", std::slice::from_ref(&task.id), false)
        .unwrap_err();
    assert_eq!(
        tasks.get("worker", &task.id).unwrap().status,
        Status::Queued
    );
    assert_eq!(
        Tasks::load(&dir.join("config.toml"))
            .unwrap()
            .get("worker", &task.id)
            .unwrap()
            .status,
        Status::Queued
    );
    drop(fault);
    tasks
        .cancel_many("worker", std::slice::from_ref(&task.id), false)
        .unwrap();
    let fault = crate::paths::fail_writes(&tasks.path);
    tasks.remove("worker", &task.id, false).unwrap_err();
    tasks
        .remove_many("worker", std::slice::from_ref(&task.id), false)
        .unwrap_err();
    tasks.prune("worker", false).unwrap_err();
    assert_eq!(
        tasks.get("worker", &task.id).unwrap().status,
        Status::Canceled
    );
    assert_eq!(
        Tasks::load(&dir.join("config.toml"))
            .unwrap()
            .get("worker", &task.id)
            .unwrap()
            .status,
        Status::Canceled
    );
    drop(fault);
    assert_eq!(tasks.prune("worker", false).unwrap(), 1);
    assert!(
        Tasks::load(&dir.join("config.toml"))
            .unwrap()
            .all()
            .is_empty()
    );
    fs::remove_dir_all(dir).unwrap();
}

#[test]
fn snapshot_keeps_poisoning_until_journal_cleanup_succeeds() {
    let (dir, mut tasks) = isolated_store();
    tasks
        .create("host".into(), "worker".into(), "saved".into())
        .unwrap();
    fs::remove_file(&tasks.journal).unwrap();
    // A directory forces remove_file to fail even when tests run as root.
    fs::create_dir(&tasks.journal).unwrap();
    tasks.journal_poisoned = true;
    tasks.save().unwrap();
    assert!(tasks.journal_poisoned);
    fs::remove_dir(&tasks.journal).unwrap();
    fs::write(&tasks.journal, b"broken tail").unwrap();
    tasks
        .create("host".into(), "worker".into(), "blocked".into())
        .unwrap_err();
    assert_eq!(fs::read(&tasks.journal).unwrap(), b"broken tail");
    tasks.save().unwrap();
    assert!(!tasks.journal_poisoned);
    tasks
        .create("host".into(), "worker".into(), "after repair".into())
        .unwrap();
    let restored = Tasks::load(&dir.join("config.toml")).unwrap();
    assert_eq!(restored.all().len(), 2);
    fs::remove_dir_all(dir).unwrap();
}

#[test]
fn identity_namespace_retains_task_participants_after_terminal_transition() {
    let path = std::env::temp_dir().join(format!("slopd-task-identities-{}", uuid::Uuid::new_v4()));
    std::fs::create_dir_all(&path).unwrap();
    let mut tasks = Tasks::load(&path.join("config.toml")).unwrap();
    let task = tasks
        .create_owned(
            Participant {
                name: "sender".into(),
                identity: "1111111111111111".into(),
            },
            Participant {
                name: "worker".into(),
                identity: "2222222222222222".into(),
            },
            "body".into(),
            None,
        )
        .unwrap();
    tasks
        .update("2222222222222222", &task.id, Status::Done, None)
        .unwrap();
    let ids = tasks.participant_identities();
    assert!(ids.contains("1111111111111111"));
    assert!(ids.contains("2222222222222222"));
    std::fs::remove_dir_all(&path).unwrap();
}
