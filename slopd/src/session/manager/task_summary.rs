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
