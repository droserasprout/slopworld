//! Task policy tested against the current record store.
use super::*;
use std::{fs, path::PathBuf};

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
    assert_eq!(canceled.tasks[0].status, Status::Canceled);
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
        .unwrap()
        .committed
        .len(),
        2
    );

    let other = s
        .create("carol".into(), "dave".into(), "theirs".into())
        .unwrap();
    s.update("dave", &other.id, Status::Failed, None).unwrap();
    assert_eq!(s.prune("alice", false).unwrap().committed.len(), 0); // not alice's to see
    assert_eq!(s.prune("carol", false).unwrap().committed.len(), 1);

    let mut s = Tasks::load(&config).unwrap();
    let mine = s
        .create("alice".into(), "bob".into(), "mine".into())
        .unwrap();
    s.update("bob", &mine.id, Status::Done, None).unwrap();
    assert_eq!(s.prune("nobody", true).unwrap().committed.len(), 1); // `all` ignores who is asking
    assert!(Tasks::load(&config).unwrap().visible("alice").is_empty());
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
        .fail_worker(&task.id, &task.to_id, "worker exited".into())
        .unwrap()
        .unwrap();
    assert_eq!(failed.status, Status::Failed);
    assert_eq!(failed.note.as_deref(), Some("worker exited"));

    let reloaded = Tasks::load(&config).unwrap();
    let persisted = reloaded.get("parent", &task.id).unwrap();
    assert_eq!(persisted.status, Status::Failed);
    assert_eq!(persisted.worker.unwrap().session, "parent-worker");
    let unchanged = tasks
        .fail_worker(&task.id, &task.to_id, "a later exit".into())
        .unwrap()
        .unwrap();
    assert_eq!(unchanged.note.as_deref(), Some("worker exited"));
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
    assert_eq!(tasks.prune("second-id", false).unwrap().committed.len(), 0);
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
fn records_without_explicit_authority_are_rejected() {
    let (dir, mut tasks) = isolated_store();
    let task = tasks
        .create("host".into(), "worker".into(), "missing authority".into())
        .unwrap();
    let path = dir.join("data/tasks").join(format!("{}.toml", task.id));
    let mut record: toml::Value = toml::from_str(&fs::read_to_string(&path).unwrap()).unwrap();
    record.as_table_mut().unwrap().remove("from_id");
    record.as_table_mut().unwrap().remove("to_id");
    fs::write(&path, toml::to_string(&record).unwrap()).unwrap();
    assert!(Tasks::load(&dir.join("config.toml")).is_err());
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
