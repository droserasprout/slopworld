//! Session/task integration: participant identity, worker references and failure consequences.
use super::*;
#[cfg(test)]
use crate::tasks::Status;
use crate::tasks::{Participant, Task, WorkerTask};

impl SessionRuntime {
    pub(crate) async fn create_task_owned(
        &self,
        from: Participant,
        to: Participant,
        body: String,
        worker: Option<WorkerTask>,
    ) -> Result<Task> {
        let mut reserved: Vec<_> = self
            .config_authority
            .read_config()
            .await
            .sessions
            .iter()
            .map(|s| s.task_id.clone())
            .collect();
        reserved.extend(
            self.live
                .read()
                .await
                .values()
                .map(|live| live.cfg.task_id.clone()),
        );
        self.task_store()
            .create_owned_async(from, to, body, worker, reserved)
            .await
    }

    pub(crate) async fn fail_worker_task_checked(
        &self,
        task_id: &str,
        identity: &str,
        note: impl Into<String>,
    ) {
        if task_id.is_empty() {
            return;
        }
        if let Err(error) = self
            .task_store()
            .fail_worker_async(task_id, identity, note.into())
            .await
        {
            tracing::error!(task = %task_id, "could not persist worker failure: {error:#}");
        }
    }

    /// Call under the session boundary, after transport authorization.
    pub(crate) async fn task_participant(&self, name: &str) -> Result<Participant> {
        let identity = if name == crate::tasks::HOST {
            crate::tasks::HOST.to_string()
        } else {
            let session = self
                .session_cfg(name)
                .await
                .context("task session no longer exists")?;
            anyhow::ensure!(
                !session.state_id.is_empty(),
                "task session has no stable identity"
            );
            session.state_id
        };
        Ok(Participant {
            name: name.to_string(),
            identity,
        })
    }
}

#[cfg(test)]
#[path = "tasks_tests.rs"]
mod tests;
