use super::*;

async fn fixture() -> (Arc<Manager>, crate::tasks::Task) {
    let mut cfg = Config::default();
    cfg.daemon.task_summaries = TitlePolicy::Always;
    cfg.daemon.title_min_chars = 4;
    let mut manager = crate::session::test_manager(cfg);
    let path = manager.cfg_path.with_file_name("summary-cache.toml");
    Arc::get_mut(&mut manager).unwrap().title_cache = crate::title::SummaryCache::load(path);
    let task = manager
        .tasks
        .create_task(
            "host".into(),
            "agent".into(),
            "Review the task store".into(),
        )
        .await
        .unwrap();
    (manager, task)
}

async fn cache(m: &Manager, task: &crate::tasks::Task) {
    let cfg = m.config().await;
    m.title_cache.insert_cached(
        &task.body,
        &cfg.daemon.summary_prompt,
        &cfg.daemon.title_model,
        "Review tasks",
    );
}

#[tokio::test]
async fn cached_summary_is_persisted_without_changing_task_lifecycle() {
    let (m, task) = fixture().await;
    cache(&m, &task).await;
    m.clone().run_task_summary(task.clone()).await;
    let saved = crate::tasks::Tasks::load(&m.cfg_path)
        .unwrap()
        .get("host", &task.id)
        .unwrap();
    assert_eq!(saved.summary.as_deref(), Some("Review tasks"));
    assert_eq!(saved.body, task.body);
    assert_eq!(saved.status, task.status);
    assert_eq!(saved.updated_ms, task.updated_ms);
}

#[tokio::test]
async fn disabled_and_short_tasks_ignore_even_cached_summaries() {
    let (m, mut task) = fixture().await;
    cache(&m, &task).await;
    m.cfg.write().await.daemon.task_summaries = TitlePolicy::Never;
    m.clone().run_task_summary(task.clone()).await;
    assert!(
        m.tasks
            .task_for_async("host", &task.id)
            .await
            .unwrap()
            .unwrap()
            .summary
            .is_none()
    );
    m.cfg.write().await.daemon.task_summaries = TitlePolicy::Always;
    task.body = "猫犬鳥".into();
    cache(&m, &task).await;
    m.clone().run_task_summary(task.clone()).await;
    assert!(
        m.tasks
            .task_for_async("host", &task.id)
            .await
            .unwrap()
            .unwrap()
            .summary
            .is_none()
    );
}

#[tokio::test]
async fn completed_summary_does_not_resurrect_a_removed_task() {
    let (m, task) = fixture().await;
    cache(&m, &task).await;
    m.tasks
        .remove_task_async("host", &task.id, true)
        .await
        .unwrap();
    m.clone().run_task_summary(task).await;
    assert!(m.tasks.all_tasks_async().await.unwrap().is_empty());
    assert!(
        crate::tasks::Tasks::load(&m.cfg_path)
            .unwrap()
            .all()
            .is_empty()
    );
}

#[tokio::test]
async fn missing_credentials_leave_task_unchanged() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    let (m, task) = fixture().await;
    m.cfg.write().await.daemon.openrouter_key_file = m
        .cfg_path
        .with_file_name("missing-key")
        .to_string_lossy()
        .into_owned();
    m.clone().run_task_summary(task.clone()).await;
    let saved = m
        .tasks
        .task_for_async("host", &task.id)
        .await
        .unwrap()
        .unwrap();
    assert!(saved.summary.is_none());
    assert_eq!(saved.status, task.status);
    assert_eq!(saved.updated_ms, task.updated_ms);
}
