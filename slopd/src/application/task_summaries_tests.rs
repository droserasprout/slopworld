use super::*;
use crate::config::Config;
use crate::tasks::io::TaskIo;
struct Fixture {
    config_authority: Arc<crate::application::ConfigAuthority>,
    title_cache: Arc<SummaryCache>,
    cfg_path: std::path::PathBuf,
    // Drop caches before the fixture removes their directory.
    application: crate::application::test_support::Fixture,
}
impl Fixture {
    fn task_store(&self) -> TaskIo {
        self.application.services.tasks(())
    }
    async fn run_task_summary(&self, task: Task) {
        TaskSummaryRequest {
            tasks: self.application.services.clone(),
            settings: self.config_authority.daemon_settings(),
            cache: self.title_cache.clone(),
        }
        .run(task)
        .await;
    }
}
async fn fixture() -> (Fixture, Task) {
    let application = crate::application::test_support::Fixture::new();
    let cfg_path = application.root.join("config.toml");
    let mut cfg = Config::default();
    cfg.daemon.task_summaries = TitlePolicy::Always;
    cfg.daemon.title_min_chars = 4;
    let manager = Fixture {
        config_authority: Arc::new(crate::application::ConfigAuthority::new(
            cfg,
            Default::default(),
        )),
        title_cache: Arc::new(SummaryCache::load(
            application.root.join("summary-cache.toml"),
        )),
        cfg_path,
        application,
    };
    let task = manager
        .task_store()
        .create_task(
            "host".into(),
            "agent".into(),
            "Review the task store".into(),
        )
        .await
        .unwrap();
    (manager, task)
}

async fn cache(m: &Fixture, task: &crate::tasks::Task) {
    let cfg = m.config_authority.read_config().await;
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
    m.run_task_summary(task.clone()).await;
    let saved = crate::tasks::Tasks::load_records(&m.application.root)
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
    m.config_authority.write().await.daemon.task_summaries = TitlePolicy::Never;
    m.run_task_summary(task.clone()).await;
    assert!(
        m.task_store()
            .task_for_async("host", &task.id)
            .await
            .unwrap()
            .unwrap()
            .summary
            .is_none()
    );
    m.config_authority.write().await.daemon.task_summaries = TitlePolicy::Always;
    task.body = "猫犬鳥".into();
    cache(&m, &task).await;
    m.run_task_summary(task.clone()).await;
    assert!(
        m.task_store()
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
    m.task_store()
        .remove_task_async("host", &task.id, true)
        .await
        .unwrap();
    m.run_task_summary(task).await;
    assert!(m.task_store().all_tasks_async().await.unwrap().is_empty());
    assert!(
        crate::tasks::Tasks::load_records(&m.application.root)
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
    m.config_authority.write().await.daemon.openrouter_key_file = m
        .cfg_path
        .with_file_name("missing-key")
        .to_string_lossy()
        .into_owned();
    m.run_task_summary(task.clone()).await;
    let saved = m
        .task_store()
        .task_for_async("host", &task.id)
        .await
        .unwrap()
        .unwrap();
    assert!(saved.summary.is_none());
    assert_eq!(saved.status, task.status);
    assert_eq!(saved.updated_ms, task.updated_ms);
}

async fn resolved_request(
    m: &Fixture,
    task: &Task,
) -> (
    TaskSummaryRequest,
    crate::tasks::TaskStamp,
    crate::title::SummaryInput,
    crate::title::Summary,
) {
    let stamp = m
        .task_store()
        .summary_stamp(task.clone())
        .await
        .unwrap()
        .unwrap();
    let settings = m.config_authority.read_config().await;
    let input = crate::title::SummaryInput {
        prompt: task.body.clone(),
        model: settings.daemon.title_model.clone(),
        summary_prompt: settings.daemon.summary_prompt.clone(),
        key_file: settings.daemon.openrouter_key_file.clone(),
    };
    let request = TaskSummaryRequest {
        tasks: m.application.services.clone(),
        settings: m.config_authority.daemon_settings(),
        cache: m.title_cache.clone(),
    };
    (
        request,
        stamp,
        input,
        crate::title::Summary {
            text: "Resolved summary".into(),
            cache_hit: false,
        },
    )
}

#[tokio::test]
async fn disabling_summaries_after_resolution_prevents_publication_and_caching() {
    let (m, task) = fixture().await;
    let (request, stamp, input, summary) = resolved_request(&m, &task).await;
    let saved_input = input.clone();
    m.config_authority.write().await.daemon.task_summaries = TitlePolicy::Never;
    request.publish(&task, stamp, input, summary).await;
    assert!(
        m.task_store()
            .task_for_async("host", &task.id)
            .await
            .unwrap()
            .unwrap()
            .summary
            .is_none()
    );
    assert!(
        m.title_cache
            .get(
                &saved_input.prompt,
                &saved_input.summary_prompt,
                &saved_input.model
            )
            .is_none()
    );
}

#[tokio::test]
async fn deleting_task_after_resolution_rejects_late_publication() {
    let (m, task) = fixture().await;
    let (request, stamp, input, summary) = resolved_request(&m, &task).await;
    m.task_store()
        .remove_task_async("host", &task.id, true)
        .await
        .unwrap();
    request.publish(&task, stamp, input, summary).await;
    assert!(m.task_store().all_tasks_async().await.unwrap().is_empty());
    assert!(
        crate::tasks::Tasks::load_records(&m.application.root)
            .unwrap()
            .all()
            .is_empty()
    );
}
