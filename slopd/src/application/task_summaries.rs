//! Daemon-owned task summaries, independent of session lifetime. Task stamps reject stale results.
use crate::config::{DaemonSettings, TitlePolicy};
use crate::tasks::Task;
use crate::title::SummaryCache;
use std::sync::Arc;

pub(crate) struct TaskSummaryRequest {
    pub(crate) tasks: Arc<super::Services>,
    pub(crate) settings: DaemonSettings,
    pub(crate) cache: Arc<SummaryCache>,
}
impl TaskSummaryRequest {
    pub(crate) fn spawn(self, task: Task) {
        if task.summary.is_none() {
            tokio::spawn(self.run(task));
        }
    }
    async fn run(self, task: Task) {
        let Ok(Some(stamp)) = self.tasks.tasks(()).summary_stamp(task.clone()).await else {
            return;
        };
        let cfg = self.settings.snapshot().await;
        if cfg.task_summaries == TitlePolicy::Never
            || task.body.chars().count() < cfg.title_min_chars
        {
            return;
        }

        let input = crate::title::SummaryInput {
            prompt: task.body.clone(),
            model: cfg.title_model.clone(),
            summary_prompt: cfg.summary_prompt.clone(),
            key_file: cfg.openrouter_key_file.clone(),
        };
        let Ok(summary) = self.cache.resolve(&input).await else {
            tracing::debug!(target: "slopd::task_summaries", task = %task.id,
                outcome = "request_failed", "task summary request failed");
            return;
        };

        self.publish(&task, stamp, input, summary).await;
    }

    // Provider work can outlive settings changes or task deletion. Recheck both at publication.
    async fn publish(
        self,
        task: &Task,
        stamp: crate::tasks::TaskStamp,
        input: crate::title::SummaryInput,
        summary: crate::title::Summary,
    ) {
        let current = self.settings.snapshot().await;
        if current.task_summaries == TitlePolicy::Never {
            return;
        }

        self.cache.store(&input, &summary, None);

        match self
            .tasks
            .tasks(())
            .set_summary_checked(stamp, summary.text)
            .await
        {
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
                "The daemon removed the task before its summary completed."
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
#[path = "task_summaries_tests.rs"]
mod tests;
