//! Durable task storage owned by the session manager.

use super::super::*;
use crate::tasks::{Participant, Status, Task, WorkerTask};
use anyhow::anyhow;

/// Blocking workers own task-file access and retain this mutex through accepted
/// publication. Synchronous helpers below are only for test fixture construction.
pub(crate) struct TaskStore(Arc<std::sync::Mutex<crate::tasks::Tasks>>);

impl TaskStore {
    #[cfg(test)]
    fn lock(&self) -> Result<std::sync::MutexGuard<'_, crate::tasks::Tasks>> {
        self.0
            .lock()
            .map_err(|error| anyhow!("task store lock poisoned: {error}"))
    }

    pub(crate) fn new(tasks: crate::tasks::Tasks) -> Self {
        Self(Arc::new(std::sync::Mutex::new(tasks)))
    }

    #[cfg(test)]
    pub(crate) fn select_record_fixture(&self, data: &Path) -> Result<()> {
        *self.lock()? = crate::tasks::Tasks::load_records(data)?;
        Ok(())
    }

    #[cfg(test)]
    pub(crate) fn create_task(&self, from: String, to: String, body: String) -> Result<Task> {
        self.lock()?.create(from, to, body)
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
        self.lock()?.create_worker(from, to, body, parent, durable)
    }

    #[cfg(test)]
    pub(crate) fn create_owned(
        &self,
        from: Participant,
        to: Participant,
        body: String,
        worker: Option<WorkerTask>,
    ) -> Result<Task> {
        self.lock()?.create_owned(from, to, body, worker)
    }

    #[cfg(test)]
    pub(crate) fn tasks_for(&self, who: &str) -> Vec<Task> {
        self.0
            .lock()
            .expect("task store lock poisoned")
            .visible(who)
    }

    #[cfg(test)]
    pub(crate) fn all_tasks(&self) -> Vec<Task> {
        self.0.lock().expect("task store lock poisoned").all()
    }

    #[cfg(test)]
    pub(crate) fn task_for(&self, who: &str, id: &str) -> Option<Task> {
        self.0
            .lock()
            .expect("task store lock poisoned")
            .get(who, id)
    }

    #[cfg(test)]
    pub(crate) fn update_task(
        &self,
        who: &str,
        id: &str,
        status: Status,
        note: Option<String>,
    ) -> Result<Task> {
        self.lock()?.update(who, id, status, note)
    }

    #[cfg(test)]
    pub(crate) fn cancel_tasks(&self, who: &str, ids: &[String], force: bool) -> Result<Vec<Task>> {
        Ok(self.lock()?.cancel_many(who, ids, force)?.tasks)
    }

    #[cfg(test)]
    pub(crate) fn remove_task(&self, who: &str, id: &str, force: bool) -> Result<Task> {
        self.lock()?.remove(who, id, force)
    }

    #[cfg(test)]
    pub(crate) fn remove_tasks(&self, who: &str, ids: &[String], force: bool) -> Result<usize> {
        Ok(self.lock()?.remove_many(who, ids, force)?.committed.len())
    }

    #[cfg(test)]
    pub(crate) fn prune_tasks(&self, who: &str, all: bool) -> Result<usize> {
        Ok(self.lock()?.prune(who, all)?.committed.len())
    }

    #[cfg(test)]
    fn fail_worker(&self, task_id: &str, note: String) -> Result<Option<Task>> {
        {
            let mut tasks = self.lock()?;
            let expected = tasks
                .all()
                .into_iter()
                .find(|task| task.id == task_id)
                .map(|task| task.to_id)
                .unwrap_or_default();
            tasks.fail_worker(task_id, &expected, note)
        }
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

    #[cfg(test)]
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
