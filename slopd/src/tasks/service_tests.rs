use super::*;
use std::{fs, path::PathBuf};
struct Fixture {
    root: PathBuf,
    tasks: Tasks,
}
impl Fixture {
    fn new() -> Self {
        let root =
            std::env::temp_dir().join(format!("slopd-task-records-{}", uuid::Uuid::new_v4()));
        let tasks = Tasks::load_records(&root).unwrap();
        Self { root, tasks }
    }
    fn path(&self, id: &str) -> PathBuf {
        self.root.join("tasks").join(format!("{id}.toml"))
    }
    fn create(&mut self, body: &str) -> Task {
        self.tasks
            .create("host".into(), "agent".into(), body.into())
            .unwrap()
    }
}
impl Drop for Fixture {
    fn drop(&mut self) {
        drop(fs::remove_dir_all(&self.root));
    }
}

#[test]
fn full_record_updates_preserve_siblings_extensions_and_explicit_order() {
    let mut f = Fixture::new();
    let first = f.create("first");
    let second = f.create("second");
    assert!(crate::storage_id::valid(&first.id));
    let sibling = fs::read(f.path(&second.id)).unwrap();
    let stamp = fs::metadata(f.path(&second.id))
        .unwrap()
        .modified()
        .unwrap();
    let mut text = fs::read_to_string(f.path(&first.id)).unwrap();
    text.push_str("future = 'retained'\n");
    fs::write(f.path(&first.id), text).unwrap();
    f.tasks = Tasks::load_records(&f.root).unwrap();
    f.tasks
        .update(
            "agent",
            "agent",
            &first.id,
            Status::Working,
            Some("progress".into()),
        )
        .unwrap();
    f.tasks.set_summary(&first.id, "summary").unwrap();
    f.tasks
        .update("agent", "agent", &first.id, Status::Done, None)
        .unwrap();
    let raw: toml::Value = toml::from_str(&fs::read_to_string(f.path(&first.id)).unwrap()).unwrap();
    assert_eq!(raw["future"].as_str(), Some("retained"));
    assert!(raw.get("note").is_none());
    assert_eq!(raw["summary"].as_str(), Some("summary"));
    assert_eq!(fs::read(f.path(&second.id)).unwrap(), sibling);
    assert_eq!(
        fs::metadata(f.path(&second.id))
            .unwrap()
            .modified()
            .unwrap(),
        stamp
    );
    assert_eq!(
        Tasks::load_records(&f.root)
            .unwrap()
            .all()
            .iter()
            .map(|t| &t.id)
            .collect::<Vec<_>>(),
        [&first.id, &second.id]
    );
    assert!(!f.root.join("tasks.journal").exists());
    assert!(!f.root.join("tasks.toml").exists());
}

#[test]
fn batches_prevalidate_then_report_exact_partial_commits_and_safe_retry() {
    let mut f = Fixture::new();
    let a = f.create("a");
    let b = f.create("b");
    let c = f.create("c");
    let ids = vec![c.id.clone(), a.id.clone(), b.id.clone(), a.id.clone()];
    f.tasks
        .cancel_many("stranger", "stranger", &ids, false)
        .unwrap_err();
    let fault = crate::paths::fail_writes(&f.path(&b.id));
    let result = f.tasks.cancel_many("agent", "agent", &ids, false).unwrap();
    assert_eq!(result.committed, std::slice::from_ref(&a.id));
    assert_eq!(result.failed[0].id, b.id);
    assert_eq!(result.unattempted, std::slice::from_ref(&c.id));
    assert_eq!(f.tasks.get("agent", &b.id).unwrap().status, Status::Queued);
    drop(fault);
    let retry = f.tasks.cancel_many("agent", "agent", &ids, false).unwrap();
    assert_eq!(retry.unchanged, std::slice::from_ref(&a.id));
    assert_eq!(retry.committed, [b.id.clone(), c.id.clone()]);
    // A removal failure on the middle record leaves the last one unattempted.
    let bytes = fs::read(f.path(&b.id)).unwrap();
    fs::remove_file(f.path(&b.id)).unwrap();
    fs::create_dir(f.path(&b.id)).unwrap();
    let result = f.tasks.remove_many("agent", &ids, false).unwrap();
    assert_eq!(result.committed, std::slice::from_ref(&a.id));
    assert_eq!(result.failed[0].id, b.id);
    assert_eq!(result.unattempted, std::slice::from_ref(&c.id));
    fs::remove_dir(f.path(&b.id)).unwrap();
    fs::write(f.path(&b.id), bytes).unwrap();
    let retry = f.tasks.remove_many("agent", &ids, false).unwrap();
    assert_eq!(retry.absent, std::slice::from_ref(&a.id));
    assert_eq!(retry.committed, [b.id, c.id]);
    assert!(Tasks::load_records(&f.root).unwrap().all().is_empty());
}

#[test]
fn stale_summary_and_worker_callbacks_never_recreate_deleted_or_reassigned_tasks() {
    let mut f = Fixture::new();
    let task = f
        .tasks
        .create_worker(
            "host".into(),
            "worker".into(),
            "body".into(),
            "host".into(),
            true,
        )
        .unwrap();
    let stamp = f.tasks.stamp(&task).unwrap();
    assert!(
        f.tasks
            .fail_worker(&task.id, "different-worker", "exit".into())
            .unwrap()
            .is_none()
    );
    f.tasks.remove("host", &task.id, true).unwrap();
    assert!(
        f.tasks
            .set_summary_checked(&stamp, "late")
            .unwrap()
            .is_none()
    );
    assert!(
        f.tasks
            .fail_worker(&task.id, "worker", "exit".into())
            .unwrap()
            .is_none()
    );
    assert!(!f.path(&task.id).exists());
    // Even an explicit fixture reintroduction of identical contents has a new incarnation.
    f.tasks.persist(task.clone(), f.tasks.next_order).unwrap();
    assert!(
        f.tasks
            .set_summary_checked(&stamp, "stale")
            .unwrap()
            .is_none()
    );
    let current = f.tasks.stamp(&task).unwrap();
    f.tasks.set_summary_checked(&current, "accepted").unwrap();
    assert!(
        f.tasks
            .set_summary_checked(&current, "older result")
            .unwrap()
            .is_none()
    );
    assert_eq!(
        f.tasks.get("host", &task.id).unwrap().summary.as_deref(),
        Some("accepted")
    );
}

#[test]
fn canceled_retry_cannot_bypass_authority_or_status_prevalidation() {
    let mut f = Fixture::new();
    let a = f.create("a");
    let b = f.create("b");
    f.tasks
        .update("agent", "agent", &b.id, Status::Working, None)
        .unwrap();
    f.tasks
        .cancel_many("host", "host", &[a.id.clone(), b.id.clone()], true)
        .unwrap_err();
    assert_eq!(f.tasks.get("agent", &a.id).unwrap().status, Status::Queued);
    f.tasks
        .cancel_many("agent", "agent", std::slice::from_ref(&a.id), false)
        .unwrap();
    f.tasks
        .cancel_many("stranger", "stranger", &[a.id], false)
        .unwrap_err();
}

#[test]
fn malformed_record_identity_order_and_aliases_fail_without_repair_writes() {
    let mut f = Fixture::new();
    let task = f.create("body");
    let path = f.path(&task.id);
    let text = fs::read_to_string(&path).unwrap();
    let malformed = text.replace(&task.id, "1111111111111111");
    fs::write(&path, &malformed).unwrap();
    assert!(Tasks::load_records(&f.root).is_err());
    assert_eq!(fs::read_to_string(&path).unwrap(), malformed);
    fs::write(
        &path,
        text.replace("storage_order = 0", "storage_order = -1"),
    )
    .unwrap();
    assert!(Tasks::load_records(&f.root).is_err());
    fs::remove_file(&path).unwrap();
    let outside = f.root.join("outside");
    fs::write(&outside, text).unwrap();
    std::os::unix::fs::symlink(&outside, &path).unwrap();
    assert!(Tasks::load_records(&f.root).is_err());
}

#[test]
#[ignore = "storage scaling measurement; run explicitly through just daemon-test"]
fn benchmark_ten_thousand_records_and_one_hundred_targeted_deletions() {
    let mut f = Fixture::new();
    for _ in 0..10_000 {
        f.create(&"x".repeat(3072));
    }
    let start = std::time::Instant::now();
    f.tasks = Tasks::load_records(&f.root).unwrap();
    let load = start.elapsed();
    let rows = f.tasks.all();
    let sibling = fs::read(f.path(&rows[9999].id)).unwrap();
    let stamp = fs::metadata(f.path(&rows[9999].id))
        .unwrap()
        .modified()
        .unwrap();
    let ids = rows
        .iter()
        .take(100)
        .map(|t| t.id.clone())
        .collect::<Vec<_>>();
    let start = std::time::Instant::now();
    for (index, id) in ids.iter().enumerate() {
        f.tasks
            .update(
                &rows[index].to_id,
                &rows[index].to,
                id,
                Status::Working,
                Some(format!("progress {index}")),
            )
            .unwrap();
    }
    let update = start.elapsed();
    let start = std::time::Instant::now();
    let result = f.tasks.remove_many("host", &ids, true).unwrap();
    let remove = start.elapsed();
    assert_eq!(result.committed.len(), 100);
    assert!(result.failed.is_empty());
    assert_eq!(f.tasks.all().len(), 9900);
    assert_eq!(fs::read(f.path(&rows[9999].id)).unwrap(), sibling);
    assert_eq!(
        fs::metadata(f.path(&rows[9999].id))
            .unwrap()
            .modified()
            .unwrap(),
        stamp
    );
    println!(
        "TASK_RECORD_BENCH records=10000 body_bytes=3072 load_ms={:.2} update_100_ms={:.2} delete_100_ms={:.2} remaining=9900 sibling_unchanged=true",
        load.as_secs_f64() * 1000.0,
        update.as_secs_f64() * 1000.0,
        remove.as_secs_f64() * 1000.0
    );
}

#[test]
fn retired_references_and_occupied_entries_reserve_identity_without_overwrite() {
    let mut f = Fixture::new();
    let task = f.create("retained identity");
    let bytes = fs::read(f.path(&task.id)).unwrap();
    let records = &mut f.tasks.disk;
    records.put(&task, 1, false).unwrap_err();
    assert_eq!(fs::read(f.path(&task.id)).unwrap(), bytes);
    f.tasks.remove("host", &task.id, true).unwrap();
    assert!(f.tasks.reserved.contains(&task.id));
    f.tasks.reserve_references(["1111111111111111".into()]);
    assert!(f.tasks.reserved.contains("1111111111111111"));
    let path = f.path("2222222222222222");
    std::os::unix::fs::symlink(f.root.join("missing"), &path).unwrap();
    let records = &mut f.tasks.disk;
    assert!(records.occupied("2222222222222222").unwrap());
}

#[test]
fn failed_record_update_keeps_accepted_task_and_allows_retry() {
    let mut f = Fixture::new();
    let task = f.create("retry progress");
    let path = f.path(&task.id);
    let bytes = fs::read(&path).unwrap();
    fs::remove_file(&path).unwrap();
    fs::create_dir(&path).unwrap();
    f.tasks
        .update(
            &task.to_id,
            &task.to,
            &task.id,
            Status::Working,
            Some("progress".into()),
        )
        .unwrap_err();
    let accepted = f.tasks.get(&task.from_id, &task.id).unwrap();
    assert_eq!(accepted.status, task.status);
    assert_eq!(accepted.note, task.note);
    assert_eq!(accepted.history, task.history);
    assert_eq!(accepted.updated_ms, task.updated_ms);
    fs::remove_dir(&path).unwrap();
    fs::write(&path, bytes).unwrap();
    f.tasks
        .update(
            &task.to_id,
            &task.to,
            &task.id,
            Status::Working,
            Some("progress".into()),
        )
        .unwrap();
    let restored = Tasks::load_records(&f.root)
        .unwrap()
        .get(&task.from_id, &task.id)
        .unwrap();
    assert_eq!(restored.status, Status::Working);
    assert_eq!(restored.note.as_deref(), Some("progress"));
    assert_eq!(restored.history.len(), 1);
}

#[test]
fn update_chain_survives_restart_status_only_updates_and_repeated_progress() {
    let mut f = Fixture::new();
    let task = f.create("original request");
    for (status, note) in [
        (Status::Accepted, None),
        (Status::Working, Some("first")),
        (Status::Working, Some("second")),
        (Status::Working, Some("second")),
        (Status::Done, None),
    ] {
        let updated = f
            .tasks
            .update("agent", "agent", &task.id, status, note.map(str::to_owned))
            .unwrap();
        let last = updated.history.last().unwrap();
        assert_eq!(last.sender, "agent");
        assert_eq!(last.status, status);
        assert_eq!(last.note.as_deref(), note);
        assert_eq!(last.timestamp_ms, updated.updated_ms);
        f.tasks = Tasks::load_records(&f.root).unwrap();
        assert_eq!(
            f.tasks.get("agent", &task.id).unwrap().history,
            updated.history
        );
    }
    let task = f.tasks.get("agent", &task.id).unwrap();
    assert_eq!(task.body, "original request");
    assert_eq!(task.note, None);
    assert_eq!(task.history.len(), 5);
    assert_eq!(task.history[1].note.as_deref(), Some("first"));
    assert_eq!(task.history[2].note.as_deref(), Some("second"));
    let before = task.history.clone();
    f.tasks
        .update(
            "stranger",
            "stranger",
            &task.id,
            Status::Failed,
            Some("forbidden".into()),
        )
        .unwrap_err();
    assert_eq!(f.tasks.get("agent", &task.id).unwrap().history, before);
}

#[test]
fn records_require_explicit_history_even_when_no_updates_exist() {
    let mut f = Fixture::new();
    let task = f.create("task without updates");
    let path = f.path(&task.id);
    let mut raw: toml::Value = toml::from_str(&fs::read_to_string(&path).unwrap()).unwrap();
    assert_eq!(raw["history"].as_array().unwrap().len(), 0);
    raw.as_table_mut().unwrap().remove("history");
    fs::write(&path, toml::to_string(&raw).unwrap()).unwrap();
    assert!(Tasks::load_records(&f.root).is_err());
}

#[test]
fn cancel_and_worker_failure_append_once_without_repeating_the_latest_note() {
    let mut f = Fixture::new();
    let task = f.create("cancel me");
    f.tasks
        .update(
            "agent",
            "agent",
            &task.id,
            Status::Accepted,
            Some("accepted".into()),
        )
        .unwrap();
    for _ in 0..2 {
        f.tasks
            .cancel_many("host", "host", std::slice::from_ref(&task.id), true)
            .unwrap();
    }
    let canceled = Tasks::load_records(&f.root)
        .unwrap()
        .get("host", &task.id)
        .unwrap();
    assert_eq!(canceled.history.len(), 2);
    assert_eq!(canceled.history[1].sender, HOST);
    assert_eq!(canceled.history[1].status, Status::Canceled);
    assert_eq!(canceled.history[1].note, None);
    assert_eq!(canceled.note.as_deref(), Some("accepted"));
    let worker = f
        .tasks
        .create_worker(
            "host".into(),
            "worker".into(),
            "work".into(),
            "host".into(),
            false,
        )
        .unwrap();
    f.tasks
        .update(
            "worker",
            "worker",
            &worker.id,
            Status::Working,
            Some("started".into()),
        )
        .unwrap();
    for _ in 0..2 {
        f.tasks
            .fail_worker(&worker.id, "worker", "exited".into())
            .unwrap();
    }
    let failed = Tasks::load_records(&f.root)
        .unwrap()
        .get("host", &worker.id)
        .unwrap();
    assert_eq!(failed.history.len(), 2);
    assert_eq!(failed.history[0].note.as_deref(), Some("started"));
    assert_eq!(failed.history[1].sender, "slopd");
    assert_eq!(failed.history[1].status, Status::Failed);
    assert_eq!(failed.history[1].note.as_deref(), Some("exited"));
}
