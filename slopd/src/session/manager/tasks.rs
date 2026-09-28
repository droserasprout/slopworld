//! Durable task storage owned by the session manager.

use super::super::*;
use crate::tasks::{Participant, Status, Task, WorkerTask};

/// Keep synchronous task-file access here with its mutex and store.
/// The configuration manager and other callers use this owner's typed operations.
pub(crate) struct TaskStore(std::sync::Mutex<crate::tasks::Tasks>);

impl TaskStore {
    pub(crate) fn new(tasks: crate::tasks::Tasks) -> Self {
        Self(std::sync::Mutex::new(tasks))
    }

    #[cfg(test)]
    pub(crate) fn create_task(&self, from: String, to: String, body: String) -> Result<Task> {
        self.0.lock().unwrap().create(from, to, body)
    }

    #[cfg(test)]
    pub(crate) fn create_worker(
        &self,
        from: String,
        to: String,
        body: String,
        parent: String,
        durable: bool,
    ) -> Result<Task> {
        self.0
            .lock()
            .unwrap()
            .create_worker(from, to, body, parent, durable)
    }

    pub(crate) fn create_owned(
        &self,
        from: Participant,
        to: Participant,
        body: String,
        worker: Option<WorkerTask>,
    ) -> Result<Task> {
        self.0.lock().unwrap().create_owned(from, to, body, worker)
    }

    pub(crate) fn tasks_for(&self, who: &str) -> Vec<Task> {
        self.0.lock().unwrap().visible(who)
    }

    pub(crate) fn all_tasks(&self) -> Vec<Task> {
        self.0.lock().unwrap().all()
    }

    pub(crate) fn task_for(&self, who: &str, id: &str) -> Option<Task> {
        self.0.lock().unwrap().get(who, id)
    }

    pub(crate) fn update_task(
        &self,
        who: &str,
        id: &str,
        status: Status,
        note: Option<String>,
    ) -> Result<Task> {
        self.0.lock().unwrap().update(who, id, status, note)
    }

    pub(crate) fn set_task_summary(&self, id: &str, summary: String) -> Result<Option<Task>> {
        self.0.lock().unwrap().set_summary(id, summary)
    }

    pub(crate) fn cancel_tasks(&self, who: &str, ids: &[String], force: bool) -> Result<Vec<Task>> {
        self.0.lock().unwrap().cancel_many(who, ids, force)
    }

    pub(crate) fn remove_task(&self, who: &str, id: &str, force: bool) -> Result<Task> {
        self.0.lock().unwrap().remove(who, id, force)
    }

    pub(crate) fn remove_tasks(&self, who: &str, ids: &[String], force: bool) -> Result<usize> {
        self.0.lock().unwrap().remove_many(who, ids, force)
    }

    pub(crate) fn prune_tasks(&self, who: &str, all: bool) -> Result<usize> {
        self.0.lock().unwrap().prune(who, all)
    }

    fn fail_worker(&self, task_id: &str, note: String) -> Result<Option<Task>> {
        self.0.lock().unwrap().fail_worker(task_id, note)
    }
}

impl Manager {
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

    pub(crate) fn fail_worker_task(&self, task_id: &str, note: impl Into<String>) {
        if task_id.trim().is_empty() {
            return;
        }
        match self.tasks.fail_worker(task_id, note.into()) {
            Ok(Some(task)) if task.status == Status::Failed => {
                tracing::info!(task = %task.id, "task-owned worker task marked failed")
            }
            Ok(_) => {}
            Err(error) => {
                tracing::error!(task = %task_id, "could not persist worker task failure: {error:#}")
            }
        }
    }
}

#[cfg(test)]
#[path = "tasks_tests.rs"]
mod tests;
