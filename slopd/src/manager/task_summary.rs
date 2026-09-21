//! Background summaries for durable delegated tasks.

use super::super::*;
use std::sync::Arc;

impl Manager {
    pub(crate) fn spawn_task_summary_request(self: &Arc<Self>, task: crate::tasks::Task) {
        if task.summary.is_some() {
            return;
        }
        let manager = self.clone();
        tokio::spawn(async move { manager.run_task_summary(task).await });
    }

    async fn run_task_summary(self: Arc<Self>, task: crate::tasks::Task) {
        let cfg = self.config().await;
        if cfg.daemon.task_summaries == TitlePolicy::Never
            || task.body.chars().count() < cfg.daemon.title_min_chars
        {
            return;
        }

        let model = cfg.daemon.title_model.clone();
        let summary_prompt = cfg.daemon.summary_prompt.clone();
        let (result, cache_hit) =
            if let Some(summary) = self.title_cache.get(&task.body, &summary_prompt, &model) {
                (Ok(summary), true)
            } else {
                let result = crate::title::summarize_async(
                    &task.body,
                    &summary_prompt,
                    &cfg.daemon.openrouter_key_file,
                    &model,
                    "task summary worker",
                )
                .await;
                (result, false)
            };

        let Ok(summary) = result else {
            tracing::debug!(
                target: "slopd::task_summaries",
                task = %task.id,
                outcome = "request_failed",
                "task summary request failed"
            );
            return;
        };

        let current = self.config().await;
        if current.daemon.task_summaries == TitlePolicy::Never {
            return;
        }

        if !cache_hit {
            if let Err(error) =
                self.title_cache
                    .insert_cached(&task.body, &summary_prompt, &model, &summary)
            {
                tracing::warn!(
                    target: "slopd::task_summaries",
                    task = %task.id,
                    error = %error,
                    outcome = "cache_write_failed",
                    "could not persist task summary cache"
                );
            }
        }

        match self.tasks.set_task_summary(&task.id, summary) {
            Ok(Some(_)) => tracing::debug!(
                target: "slopd::task_summaries",
                task = %task.id,
                outcome = "applied",
                "task summary applied"
            ),
            Ok(None) => tracing::debug!(
                target: "slopd::task_summaries",
                task = %task.id,
                outcome = "task_missing",
                "task was removed before its summary completed"
            ),
            Err(error) => tracing::warn!(
                target: "slopd::task_summaries",
                task = %task.id,
                error = %error,
                outcome = "persist_failed",
                "could not persist task summary"
            ),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn fixture() -> (Arc<Manager>, crate::tasks::Task) {
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
            .unwrap();
        (manager, task)
    }

    async fn cache(m: &Manager, task: &crate::tasks::Task) {
        let cfg = m.config().await;
        m.title_cache
            .insert_cached(
                &task.body,
                &cfg.daemon.summary_prompt,
                &cfg.daemon.title_model,
                "Review tasks",
            )
            .unwrap();
    }

    #[tokio::test]
    async fn cached_summary_is_persisted_without_changing_task_lifecycle() {
        let (m, task) = fixture();
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
        let (m, mut task) = fixture();
        cache(&m, &task).await;
        m.cfg.write().await.daemon.task_summaries = TitlePolicy::Never;
        m.clone().run_task_summary(task.clone()).await;
        assert!(m
            .tasks
            .task_for("host", &task.id)
            .unwrap()
            .summary
            .is_none());
        m.cfg.write().await.daemon.task_summaries = TitlePolicy::Always;
        task.body = "猫犬鳥".into();
        cache(&m, &task).await;
        m.clone().run_task_summary(task.clone()).await;
        assert!(m
            .tasks
            .task_for("host", &task.id)
            .unwrap()
            .summary
            .is_none());
    }

    #[tokio::test]
    async fn completed_summary_does_not_resurrect_a_removed_task() {
        let (m, task) = fixture();
        cache(&m, &task).await;
        m.tasks.remove_task("host", &task.id, true).unwrap();
        m.clone().run_task_summary(task).await;
        assert!(m.tasks.all_tasks().is_empty());
        assert!(crate::tasks::Tasks::load(&m.cfg_path)
            .unwrap()
            .all()
            .is_empty());
    }

    #[tokio::test]
    async fn missing_credentials_leave_task_unchanged() {
        let Some(_) = crate::test_support::isolated() else {
            return;
        };
        let (m, task) = fixture();
        m.cfg.write().await.daemon.openrouter_key_file = m
            .cfg_path
            .with_file_name("missing-key")
            .to_string_lossy()
            .into_owned();
        m.clone().run_task_summary(task.clone()).await;
        let saved = m.tasks.task_for("host", &task.id).unwrap();
        assert!(saved.summary.is_none());
        assert_eq!(saved.status, task.status);
        assert_eq!(saved.updated_ms, task.updated_ms);
    }
}
