//! Durable task storage owned by the session manager.

use super::super::*;
use crate::tasks::{Participant, Status, Task, WorkerTask};
use anyhow::anyhow;

/// Blocking workers own task-file access and retain this mutex through accepted
/// publication. Test seeding uses the same blocking owner.
pub(crate) struct TaskStore(Arc<std::sync::Mutex<crate::tasks::Tasks>>);

impl TaskStore {
    pub(crate) fn new(tasks: crate::tasks::Tasks) -> Self {
        Self(Arc::new(std::sync::Mutex::new(tasks)))
    }

    #[cfg(test)]
    pub(crate) async fn create_task(&self, from: String, to: String, body: String) -> Result<Task> {
        self.run(move |tasks| tasks.create(from, to, body)).await
    }

    /// Capture the caller's authorization boundary before entering the blocking
    /// pool. A dropped requester cannot release it before disk and memory commit.
    async fn run<T: Send + 'static>(
        &self,
        operation: impl FnOnce(&mut crate::tasks::Tasks) -> Result<T> + Send + 'static,
    ) -> Result<T> {
        let store = self.0.clone();
        let guards = super::boundary::task_io_guards();
        tokio::task::spawn_blocking(move || {
            let _guards = guards;
            let mut store = store
                .lock()
                .map_err(|error| anyhow!("task store lock poisoned: {error}"))?;
            operation(&mut store)
        })
        .await
        .context("task I/O owner panicked")?
    }
    pub(crate) async fn create_owned_async(
        &self,
        from: Participant,
        to: Participant,
        body: String,
        worker: Option<WorkerTask>,
        reserved: Vec<String>,
    ) -> Result<Task> {
        self.run(move |tasks| {
            tasks.reserve_references(reserved);
            tasks.create_owned(from, to, body, worker)
        })
        .await
    }
    pub(crate) async fn all_tasks_async(&self) -> Result<Vec<Task>> {
        self.run(|tasks| Ok(tasks.all())).await
    }
    pub(crate) async fn tasks_for_async(&self, who: &str) -> Result<Vec<Task>> {
        let who = who.to_owned();
        self.run(move |tasks| Ok(tasks.visible(&who))).await
    }
    pub(crate) async fn task_for_async(&self, who: &str, id: &str) -> Result<Option<Task>> {
        let (who, id) = (who.to_owned(), id.to_owned());
        self.run(move |tasks| Ok(tasks.get(&who, &id))).await
    }
    pub(super) async fn participant_identities_async(
        &self,
    ) -> Result<std::collections::HashSet<String>> {
        self.run(|tasks| Ok(tasks.participant_identities())).await
    }
    pub(crate) async fn update_task_async(
        &self,
        who: &str,
        id: &str,
        status: Status,
        note: Option<String>,
    ) -> Result<Task> {
        let (who, id) = (who.to_owned(), id.to_owned());
        self.run(move |tasks| tasks.update(&who, &id, status, note))
            .await
    }
    pub(crate) async fn remove_task_async(&self, who: &str, id: &str, force: bool) -> Result<Task> {
        let (who, id) = (who.to_owned(), id.to_owned());
        self.run(move |tasks| tasks.remove(&who, &id, force)).await
    }
    pub(crate) async fn cancel_tasks_async(
        &self,
        who: &str,
        ids: &[String],
        force: bool,
    ) -> Result<crate::tasks::BatchResult> {
        let (who, ids) = (who.to_owned(), ids.to_vec());
        self.run(move |tasks| tasks.cancel_many(&who, &ids, force))
            .await
    }
    pub(crate) async fn remove_tasks_async(
        &self,
        who: &str,
        ids: &[String],
        force: bool,
    ) -> Result<crate::tasks::BatchResult> {
        let (who, ids) = (who.to_owned(), ids.to_vec());
        self.run(move |tasks| tasks.remove_many(&who, &ids, force))
            .await
    }
    pub(crate) async fn prune_tasks_async(
        &self,
        who: &str,
        all: bool,
    ) -> Result<crate::tasks::BatchResult> {
        let who = who.to_owned();
        self.run(move |tasks| tasks.prune(&who, all)).await
    }
    pub(super) async fn summary_stamp(
        &self,
        task: Task,
    ) -> Result<Option<crate::tasks::TaskStamp>> {
        self.run(move |tasks| Ok(tasks.stamp(&task))).await
    }
    pub(super) async fn set_summary_checked(
        &self,
        stamp: crate::tasks::TaskStamp,
        summary: String,
    ) -> Result<Option<Task>> {
        self.run(move |tasks| tasks.set_summary_checked(&stamp, summary))
            .await
    }
    async fn fail_worker_async(
        &self,
        id: &str,
        identity: &str,
        note: String,
    ) -> Result<Option<Task>> {
        let (id, identity) = (id.to_owned(), identity.to_owned());
        self.run(move |tasks| tasks.fail_worker(&id, &identity, note))
            .await
    }
}

impl Manager {
    pub(crate) async fn create_task_owned(
        &self,
        from: Participant,
        to: Participant,
        body: String,
        worker: Option<WorkerTask>,
    ) -> Result<Task> {
        let mut reserved: Vec<_> = self
            .cfg
            .read()
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
        self.tasks
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
            .tasks
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
