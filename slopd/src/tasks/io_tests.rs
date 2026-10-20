use super::*;

#[tokio::test]
async fn cancelled_request_retains_supplied_lease_until_publication() {
    let root = std::env::temp_dir().join(format!("slopd-task-io-{}", uuid::Uuid::new_v4()));
    let store = TaskStore::load(root.clone()).await.unwrap();
    let boundary = Arc::new(tokio::sync::RwLock::new(()));
    let lease = boundary.clone().read_owned().await;
    let operation = store.access(lease);
    let (entered, reached) = tokio::sync::oneshot::channel();
    let (release, wait) = std::sync::mpsc::channel();
    let request = tokio::spawn(operation.run(move |tasks| {
        entered.send(()).unwrap();
        wait.recv().unwrap();
        tasks.create("host".into(), "agent".into(), "survives disconnect".into())
    }));
    tokio::time::timeout(std::time::Duration::from_secs(5), reached)
        .await
        .unwrap()
        .unwrap();
    request.abort();
    assert!(request.await.unwrap_err().is_cancelled());
    boundary.try_write().unwrap_err();
    release.send(()).unwrap();
    let _guard = tokio::time::timeout(std::time::Duration::from_secs(5), boundary.write())
        .await
        .unwrap();
    let accepted = store.access(()).all_tasks_async().await.unwrap();
    let reloaded = TaskStore::load(root.clone())
        .await
        .unwrap()
        .access(())
        .all_tasks_async()
        .await
        .unwrap();
    assert_eq!(accepted.len(), 1);
    assert_eq!(accepted[0].id, reloaded[0].id);
    assert_eq!(reloaded[0].body, "survives disconnect");
    std::fs::remove_dir_all(root).unwrap();
}
