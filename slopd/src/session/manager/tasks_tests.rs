use super::*;

#[tokio::test]
async fn task_owner_preserves_visibility_authority_and_durable_mutations() {
    let m = crate::session::test_manager(Config::default());
    let task = m
        .task_store()
        .create_task("sender".into(), "recipient".into(), "Review changes".into())
        .await
        .unwrap();
    assert_eq!(
        m.task_store()
            .tasks_for_async("sender")
            .await
            .unwrap()
            .len(),
        1
    );
    assert_eq!(
        m.task_store()
            .tasks_for_async("recipient")
            .await
            .unwrap()
            .len(),
        1
    );
    assert!(
        m.task_store()
            .tasks_for_async("stranger")
            .await
            .unwrap()
            .is_empty()
    );
    assert!(
        m.task_store()
            .task_for_async("stranger", &task.id)
            .await
            .unwrap()
            .is_none()
    );
    m.task_store()
        .update_task_async("sender", "sender", &task.id, Status::Accepted, None)
        .await
        .unwrap_err();
    assert_task(&m, &task.id, Status::Queued, None).await;
    m.task_store()
        .remove_task_async("sender", &task.id, false)
        .await
        .unwrap_err();
    assert_task(&m, &task.id, Status::Queued, None).await;
    let accepted = m
        .task_store()
        .update_task_async(
            "recipient",
            "recipient",
            &task.id,
            Status::Accepted,
            Some("Reviewing".into()),
        )
        .await
        .unwrap();
    assert_eq!(accepted.status, Status::Accepted);
    assert_eq!(accepted.note.as_deref(), Some("Reviewing"));
    assert_task(&m, &task.id, Status::Accepted, Some("Reviewing")).await;
    m.task_store()
        .cancel_tasks_async("sender", "sender", std::slice::from_ref(&task.id), false)
        .await
        .unwrap_err();
    assert_task(&m, &task.id, Status::Accepted, Some("Reviewing")).await;
    let canceled = m
        .task_store()
        .cancel_tasks_async(
            "recipient",
            "recipient",
            std::slice::from_ref(&task.id),
            false,
        )
        .await
        .unwrap();
    assert_eq!(canceled.tasks[0].status, Status::Canceled);
    assert_eq!(canceled.committed, std::slice::from_ref(&task.id));
    assert!(canceled.failed.is_empty() && canceled.unattempted.is_empty());
    let reloaded = crate::tasks::Tasks::load(m.cfg_path()).unwrap();
    assert_eq!(
        reloaded.get("sender", &task.id).unwrap().status,
        Status::Canceled
    );
    assert_eq!(
        m.task_store()
            .remove_task_async("sender", &task.id, false)
            .await
            .unwrap()
            .id,
        task.id
    );
    assert!(m.task_store().all_tasks_async().await.unwrap().is_empty());
    assert!(
        crate::tasks::Tasks::load(m.cfg_path())
            .unwrap()
            .all()
            .is_empty()
    );
}

#[tokio::test]
async fn bulk_removal_prevalidates_authority_and_pruning_respects_visibility() {
    let m = crate::session::test_manager(Config::default());
    let a = m
        .task_store()
        .create_task("host".into(), "agent".into(), "First".into())
        .await
        .unwrap();
    let b = m
        .task_store()
        .create_task("other".into(), "worker".into(), "Second".into())
        .await
        .unwrap();
    m.task_store()
        .update_task_async("agent", "agent", &a.id, Status::Done, None)
        .await
        .unwrap();
    m.task_store()
        .update_task_async("worker", "worker", &b.id, Status::Failed, None)
        .await
        .unwrap();
    m.task_store()
        .remove_tasks_async("host", &[a.id.clone(), b.id.clone()], false)
        .await
        .unwrap_err();
    assert_eq!(m.task_store().all_tasks_async().await.unwrap().len(), 2);
    let pruned = m
        .task_store()
        .prune_tasks_async("host", false)
        .await
        .unwrap();
    assert_eq!(pruned.committed, [a.id]);
    assert!(pruned.failed.is_empty() && pruned.unattempted.is_empty());
    assert_eq!(m.task_store().all_tasks_async().await.unwrap()[0].id, b.id);
    let removed = m
        .task_store()
        .remove_tasks_async("host", std::slice::from_ref(&b.id), true)
        .await
        .unwrap();
    assert_eq!(removed.committed, [b.id]);
    assert!(removed.failed.is_empty() && removed.unattempted.is_empty());
    assert!(
        crate::tasks::Tasks::load(m.cfg_path())
            .unwrap()
            .all()
            .is_empty()
    );
}

#[tokio::test]
async fn worker_failure_only_changes_unfinished_worker_tasks() {
    let m = crate::session::test_manager(Config::default());
    let ordinary = m
        .task_store()
        .create_task("host".into(), "agent".into(), "Ordinary".into())
        .await
        .unwrap();
    let worker = m
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
    let metadata = worker.worker.as_ref().unwrap();
    assert_eq!(metadata.session, "child");
    assert_eq!(metadata.parent, "parent");
    assert!(metadata.durable);
    for id in ["", "  ", "missing", &ordinary.id] {
        m.fail_worker_task_checked(id, "agent", "Exited").await;
    }
    assert_task(&m, &ordinary.id, Status::Queued, None).await;
    m.fail_worker_task_checked(&worker.id, "replacement", "Stale exit")
        .await;
    assert_task(&m, &worker.id, Status::Queued, None).await;
    m.fail_worker_task_checked(&worker.id, "child", "Exited")
        .await;
    assert_task(&m, &ordinary.id, Status::Queued, None).await;
    m.fail_worker_task_checked(&worker.id, "child", "Must not overwrite terminal result")
        .await;
    assert_task(&m, &ordinary.id, Status::Queued, None).await;
    let saved = crate::tasks::Tasks::load(m.cfg_path())
        .unwrap()
        .get("host", &worker.id)
        .unwrap();
    assert_eq!(saved.status, Status::Failed);
    assert_eq!(saved.note.as_deref(), Some("Exited"));
}

async fn assert_task(manager: &Manager, id: &str, status: Status, note: Option<&str>) {
    let live = manager
        .task_store()
        .all_tasks_async()
        .await
        .unwrap()
        .into_iter()
        .find(|task| task.id == id)
        .unwrap();
    let saved = crate::tasks::Tasks::load(manager.cfg_path())
        .unwrap()
        .all()
        .into_iter()
        .find(|task| task.id == id)
        .unwrap();
    for task in [live, saved] {
        assert_eq!(task.status, status);
        assert_eq!(task.note.as_deref(), note);
    }
}

#[tokio::test]
async fn disconnected_task_io_retains_authorization_guard_through_publication() {
    let manager = crate::session::test_manager(Config::default());
    let data = manager.cfg_path().parent().unwrap().join("data");
    let (committed, reached) = tokio::sync::oneshot::channel();
    let (release, wait) = std::sync::mpsc::channel();
    let request = tokio::spawn({
        let manager = manager.clone();
        async move {
            manager
                .session_operation(async {
                    manager
                        .task_store()
                        .run(move |tasks| {
                            let task =
                                tasks.create("host".into(), "agent".into(), "committed".into())?;
                            committed.send(task.clone()).unwrap();
                            wait.recv().unwrap();
                            Ok(task)
                        })
                        .await
                })
                .await
        }
    });
    let task = tokio::time::timeout(std::time::Duration::from_secs(5), reached)
        .await
        .unwrap()
        .unwrap();
    request.abort();
    manager.session_boundary.try_write().unwrap_err();
    assert!(
        data.join("tasks")
            .join(format!("{}.toml", task.id))
            .is_file()
    );
    release.send(()).unwrap();
    let _boundary = tokio::time::timeout(
        std::time::Duration::from_secs(5),
        manager.session_boundary.write(),
    )
    .await
    .unwrap();
    assert_eq!(
        manager
            .task_store()
            .task_for_async("host", &task.id)
            .await
            .unwrap()
            .unwrap()
            .body,
        "committed"
    );
    assert_eq!(
        crate::tasks::Tasks::load_records(&data)
            .unwrap()
            .all()
            .len(),
        1
    );
}
