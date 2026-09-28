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

        let input = crate::title::SummaryInput {
            prompt: task.body.clone(),
            model: cfg.daemon.title_model.clone(),
            summary_prompt: cfg.daemon.summary_prompt.clone(),
            key_file: cfg.daemon.openrouter_key_file.clone(),
        };
        let Ok(summary) = self.title_cache.resolve(&input).await else {
            tracing::debug!(target: "slopd::task_summaries", task = %task.id,
                outcome = "request_failed", "task summary request failed");
            return;
        };

        let current = self.config().await;
        if current.daemon.task_summaries == TitlePolicy::Never {
            return;
        }

        self.title_cache.store(&input, &summary, None);

        match self.tasks.set_task_summary(&task.id, summary.text) {
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
#[path = "task_summary_tests.rs"]
mod tests;
