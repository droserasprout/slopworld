use super::*;
use crate::application::test_support::Fixture;

fn worker(id: &str, planner: &str) -> Task {
    Task {
        id: id.into(),
        from: crate::tasks::HOST.into(),
        to: "worker".into(),
        from_id: crate::tasks::HOST.into(),
        to_id: "0123456789abcdef".into(),
        body: format!("{}\nWork order", marker(planner)),
        status: crate::tasks::Status::Failed,
        note: Some("Failed".into()),
        history: vec![],
        summary: None,
        created_ms: 1,
        updated_ms: 2,
        worker: Some(crate::tasks::WorkerTask {
            session: "worker".into(),
            parent: crate::tasks::HOST.into(),
            durable: true,
        }),
    }
}

#[tokio::test]
async fn removing_implicit_links_does_not_resurrect_failed_tasks_after_reopening() {
    let fixture = Fixture::new();
    let path = fixture.root.join("TODO.txt");
    let store = Store::new(path.clone());
    let document = store
        .save("Fix bug sw-task:pending".into(), revision(""))
        .await
        .unwrap();
    let id = document.items[0].id.clone();
    let text = document.text.clone();
    let tasks = vec![worker("task-1", &id)];
    let projected = store.project_links(document, &tasks).await.unwrap();
    assert_eq!(projected.text, text);
    assert_eq!(projected.items[0].links.len(), 2);
    assert_eq!(projected.items[0].links[0].status, "failed");
    assert!(projected.items[0].status.is_empty());
    let revision = &projected.items[0].links_revision;
    let keep_worker = vec![projected.items[0].links[1].reference.clone()];
    let document = store
        .save_links(&id, keep_worker, revision, &tasks, &[])
        .await
        .unwrap();
    let projected = store.project_links(document, &tasks).await.unwrap();
    assert_eq!(projected.items[0].links.len(), 1);
    assert_eq!(projected.items[0].links[0].reference.kind, LinkKind::Worker);
    store
        .save_links(&id, vec![], revision, &tasks, &[])
        .await
        .unwrap_err();
    store
        .save_links(&id, vec![], &projected.items[0].links_revision, &tasks, &[])
        .await
        .unwrap();
    let reopened = Store::new(path);
    let document = reopened
        .project_links(reopened.read().await.unwrap(), &tasks)
        .await
        .unwrap();
    assert!(document.items[0].links.is_empty());
    assert!(document.items[0].task_id.is_empty());
}

#[tokio::test]
async fn spawning_appends_both_links_and_failed_writes_preserve_previous_links() {
    let fixture = Fixture::new();
    let store = Store::new(fixture.root.join("TODO.txt"));
    let document = store.save("Fix bug".into(), revision("")).await.unwrap();
    let id = document.items[0].id.clone();
    let task = worker("task-1", &id);
    store.link_worker(&id, &task, &[]).await.unwrap();
    store.link_worker(&id, &task, &[]).await.unwrap();
    let projected = store
        .project_links(store.read().await.unwrap(), std::slice::from_ref(&task))
        .await
        .unwrap();
    assert_eq!(projected.items[0].links.len(), 2);
    let fault = crate::paths::fail_writes(&store.links_path(&id).unwrap());
    store
        .save_links(
            &id,
            vec![],
            &projected.items[0].links_revision,
            std::slice::from_ref(&task),
            &[],
        )
        .await
        .unwrap_err();
    drop(fault);
    let projected = store
        .project_links(store.read().await.unwrap(), &[task])
        .await
        .unwrap();
    assert_eq!(projected.items[0].links.len(), 2);
    store.save(String::new(), document.revision).await.unwrap();
    store
        .link_worker(&id, &worker("task-2", &id), &[])
        .await
        .unwrap_err();
}

#[tokio::test]
async fn live_task_status_is_projected_without_changing_stored_link_revisions() {
    let fixture = Fixture::new();
    let store = Store::new(fixture.root.join("TODO.txt"));
    let document = store.save("Review".into(), revision("")).await.unwrap();
    let id = document.items[0].id.clone();
    let mut task = worker("task-1", &id);
    store.link_worker(&id, &task, &[]).await.unwrap();
    let path = store.links_path(&id).unwrap();
    let before = tokio::fs::read_to_string(&path).await.unwrap();
    let stored: serde_json::Value = serde_json::from_str(&before).unwrap();
    assert_eq!(stored[0]["kind"], "task");
    assert!(stored[0].get("status").is_none());

    let mut revisions = Vec::new();
    for (status, expected) in [
        (crate::tasks::Status::Working, "working"),
        (crate::tasks::Status::Done, "done"),
    ] {
        task.status = status;
        let projected = store
            .project_links(store.read().await.unwrap(), std::slice::from_ref(&task))
            .await
            .unwrap();
        let item = &projected.items[0];
        assert_eq!(item.links[0].status, expected);
        let wire = serde_json::to_value(&item.links[0]).unwrap();
        assert_eq!(wire["kind"], "task");
        assert_eq!(wire["id"], task.id);
        revisions.push(item.links_revision.clone());
    }
    assert_eq!(revisions[0], revisions[1]);
    assert_eq!(tokio::fs::read_to_string(path).await.unwrap(), before);
}

#[tokio::test]
async fn invalid_links_never_replace_accepted_associations_and_corruption_is_reported() {
    let fixture = Fixture::new();
    let store = Store::new(fixture.root.join("TODO.txt"));
    let document = store.save("Review".into(), revision("")).await.unwrap();
    let id = &document.items[0].id;
    let task = worker("task-1", id);
    let tasks = [task];
    store.link_worker(id, &tasks[0], &[]).await.unwrap();
    let projected = store
        .project_links(store.read().await.unwrap(), &tasks)
        .await
        .unwrap();
    let expected = &projected.items[0].links_revision;
    let link = projected.items[0].links[0].reference.clone();
    let path = store.links_path(id).unwrap();
    let original = tokio::fs::read_to_string(&path).await.unwrap();
    for links in [
        vec![link.clone(), link.clone()],
        vec![LinkReference {
            id: "missing".into(),
            ..link.clone()
        }],
        vec![LinkReference {
            label: "x".repeat(65536),
            ..link
        }],
    ] {
        store
            .save_links(id, links, expected, &tasks, &[])
            .await
            .unwrap_err();
        assert_eq!(tokio::fs::read_to_string(&path).await.unwrap(), original);
    }
    for corrupt in ["invalid json".into(), " ".repeat(65537)] {
        tokio::fs::write(&path, corrupt).await.unwrap();
        store
            .project_links(store.read().await.unwrap(), &tasks)
            .await
            .unwrap_err();
    }
    tokio::fs::write(&path, &original).await.unwrap();
    assert_eq!(
        store.project_links(document, &tasks).await.unwrap().items[0]
            .links
            .len(),
        2
    );
}

#[tokio::test]
async fn completed_rows_and_missing_legacy_tasks_project_without_mutating_backlog() {
    let fixture = Fixture::new();
    let store = Store::new(fixture.root.join("TODO.txt"));
    let document = store
        .save(
            "x Finished sw-task:missing\nUnfinished".into(),
            revision(""),
        )
        .await
        .unwrap();
    let projected = store
        .project_links(store.read().await.unwrap(), &[])
        .await
        .unwrap();
    assert_eq!(projected.text, document.text);
    assert_eq!(projected.items[0].status, "complete");
    assert_eq!(projected.items[0].links[0].status, "unavailable");
    assert_eq!(projected.items[0].links[0].reference.id, "missing");
    assert!(projected.items[1].status.is_empty());
    store
        .save_links(
            &projected.items[0].id,
            vec![],
            &projected.items[0].links_revision,
            &[],
            &[],
        )
        .await
        .unwrap();
    let reread = store
        .project_links(store.read().await.unwrap(), &[])
        .await
        .unwrap();
    assert!(reread.items[0].links.is_empty());
    assert_eq!(reread.text, document.text);
}
