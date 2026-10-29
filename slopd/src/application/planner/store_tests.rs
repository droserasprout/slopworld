use super::*;

#[test]
fn ids_preserve_user_text_dates_unknown_metadata_and_line_endings() {
    let source = "(A) 2026-10-27 Fix reconnect +slopworld @coding due:2026-11-01 rec:1w\r\n\r\nx 2026-10-27 Finished custom:value\n";
    let saved = assign_ids(source).unwrap();
    assert_eq!(assign_ids(&saved).unwrap(), saved);
    assert!(saved.starts_with(source.lines().next().unwrap()));
    assert!(saved.contains("\r\n\r\n"));
    assert_eq!(items(&saved).unwrap().len(), 2);
    selected(&saved, 2).unwrap_err();
    selected(&saved, 1).unwrap_err();
}

#[test]
fn invalid_and_duplicate_ids_cannot_alias_worker_tasks() {
    let id = uuid::Uuid::new_v4();
    assign_ids(&format!("One sw-id:{id}\nTwo sw-id:{id}")).unwrap_err();
    assign_ids("One sw-id:nope").unwrap_err();
    assign_ids(&format!("One sw-id:{id} sw-id:{id}")).unwrap_err();
    assign_ids("One sw-task:a:b").unwrap_err();
}

#[test]
fn dispatch_reservation_keeps_user_fields_and_survives_repeated_saves() {
    let saved = assign_ids("(B) Fix bug +project due:2026-11-01\nOther task\n").unwrap();
    let reserved = link(&saved, 0, "pending").unwrap();
    assert_eq!(selected(&reserved, 0).unwrap().task_id, "pending");
    let linked = link(&reserved, 0, "task-123").unwrap();
    assert_eq!(selected(&linked, 0).unwrap().task_id, "task-123");
    assert_eq!(assign_ids(&linked).unwrap(), linked);
    assert_eq!(linked.lines().nth(1), saved.lines().nth(1));
    assert!(linked.starts_with("(B) Fix bug +project due:2026-11-01"));
    check_revision(&linked, &revision(&saved)).unwrap_err();
}

#[tokio::test]
async fn concurrent_stale_save_and_external_edits_do_not_overwrite_accepted_text() {
    let root = std::env::temp_dir().join(format!("slopd-planner-{}", uuid::Uuid::new_v4()));
    let path = root.join("TODO.txt");
    let store = Store::new(path.clone());
    let initial = store.read().await.unwrap();
    let saved = store
        .save("First".into(), initial.revision.clone())
        .await
        .unwrap();
    store
        .save("Stale".into(), initial.revision)
        .await
        .unwrap_err();
    assert_eq!(store.read().await.unwrap().text, saved.text);
    tokio::fs::write(&path, "External edit").await.unwrap();
    store
        .save("Overwritten".into(), saved.revision)
        .await
        .unwrap_err();
    assert_eq!(store.read().await.unwrap().text, "External edit");
    tokio::fs::remove_dir_all(root).await.unwrap();
}

#[tokio::test]
async fn failed_save_leaves_accepted_file_unchanged() {
    let root = std::env::temp_dir().join(format!("slopd-planner-{}", uuid::Uuid::new_v4()));
    let store = Store::new(root.join("TODO.txt"));
    let initial = store.read().await.unwrap();
    let saved = store
        .save("Original".into(), initial.revision)
        .await
        .unwrap();
    store
        .save("a".repeat(MAX_BYTES + 1), saved.revision)
        .await
        .unwrap_err();
    assert_eq!(store.read().await.unwrap().text, saved.text);
    tokio::fs::remove_dir_all(root).await.unwrap();
}

#[tokio::test]
async fn disk_failure_preserves_previous_revision_and_allows_retry() {
    let root = std::env::temp_dir().join(format!("slopd-planner-{}", uuid::Uuid::new_v4()));
    let path = root.join("TODO.txt");
    let store = Store::new(path.clone());
    let initial = store.read().await.unwrap();
    let saved = store
        .save("Original".into(), initial.revision)
        .await
        .unwrap();
    let fault = crate::paths::fail_writes(&path);
    store
        .save("Changed".into(), saved.revision.clone())
        .await
        .unwrap_err();
    assert_eq!(store.read().await.unwrap().text, saved.text);
    drop(fault);
    store.save("Changed".into(), saved.revision).await.unwrap();
    tokio::fs::remove_dir_all(root).await.unwrap();
}

#[test]
fn task_metadata_does_not_replace_lookalikes_inside_user_text() {
    let text = assign_ids("Preserve prefixsw-task:pending sw-task:pending").unwrap();
    let linked = link(&text, 0, "task-123").unwrap();
    assert!(linked.contains("prefixsw-task:pending sw-task:task-123"));
    assign_ids("Task sw-id:").unwrap_err();
}

#[test]
fn worker_progress_is_projected_without_rewriting_user_fields() {
    let text = assign_ids("(A) Fix bug due:2026-11-01 sw-task:pending").unwrap();
    let store = Store::new(PathBuf::from("unused"));
    let document = store.document(text.clone()).unwrap();
    let id = document.items[0].id.clone();
    let task = crate::tasks::Task {
        id: "task-123".into(),
        from: crate::tasks::HOST.into(),
        to: "worker".into(),
        from_id: crate::tasks::HOST.into(),
        to_id: "worker-id".into(),
        body: format!("{}\nWork order", marker(&id)),
        status: crate::tasks::Status::Done,
        note: Some("Done".into()),
        history: vec![],
        summary: None,
        created_ms: 1,
        updated_ms: 2,
        worker: Some(crate::tasks::WorkerTask {
            session: "worker".into(),
            parent: crate::tasks::HOST.into(),
            durable: true,
        }),
    };
    let document = document.with_tasks(&[task]);
    assert_eq!(document.text, text);
    assert_eq!(document.items[0].task_id, "task-123");
    assert_eq!(document.items[0].status, "done");
}
