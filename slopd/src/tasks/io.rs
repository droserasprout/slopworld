//! Async task-store access. Blocking workers serialize policy, persistence and publication.
//! Callers supply an owned lease; this owner has no knowledge of session guards or identities.
use super::{Participant, Status, Task, WorkerTask};
use anyhow::{Context, Result, anyhow};
use std::sync::Arc;

/// Blocking workers own task-file access and retain this mutex through accepted
/// publication. Test seeding uses the same blocking owner.
#[derive(Clone)]
pub(crate) struct TaskStore(Arc<std::sync::Mutex<crate::tasks::Tasks>>);

impl TaskStore {
    /// Bind an operation to the caller's lease before entering the blocking pool.
    pub(crate) fn access(&self, lease: impl Send + 'static) -> TaskIo {
        TaskIo {
            store: self.0.clone(),
            lease: Box::new(lease),
        }
    }

    pub(crate) async fn load(data: std::path::PathBuf) -> Result<Self> {
        tokio::task::spawn_blocking(move || crate::tasks::Tasks::load_records(&data))
            .await
            .context("task startup owner panicked")?
            .map(Self::new)
    }

    pub(crate) fn new(tasks: crate::tasks::Tasks) -> Self {
        Self(Arc::new(std::sync::Mutex::new(tasks)))
    }
}

/// One operation retaining caller-owned authorization through disk and memory commit.
pub(crate) struct TaskIo {
    store: Arc<std::sync::Mutex<crate::tasks::Tasks>>,
    lease: Box<dyn Send>,
}

impl TaskIo {
    #[cfg(test)]
    pub(crate) async fn create_task(self, from: String, to: String, body: String) -> Result<Task> {
        self.run(move |tasks| tasks.create(from, to, body)).await
    }

    /// Move the supplied lease into the blocking owner. Request cancellation cannot
    /// release it before disk and accepted-state publication finish.
    pub(crate) async fn run<T: Send + 'static>(
        self,
        operation: impl FnOnce(&mut crate::tasks::Tasks) -> Result<T> + Send + 'static,
    ) -> Result<T> {
        let store = self.store;
        let guards = self.lease;
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
        self,
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
    pub(crate) async fn all_tasks_async(self) -> Result<Vec<Task>> {
        self.run(|tasks| Ok(tasks.all())).await
    }
    pub(crate) async fn tasks_for_async(self, who: &str) -> Result<Vec<Task>> {
        let who = who.to_owned();
        self.run(move |tasks| Ok(tasks.visible(&who))).await
    }
    pub(crate) async fn task_for_async(self, who: &str, id: &str) -> Result<Option<Task>> {
        let (who, id) = (who.to_owned(), id.to_owned());
        self.run(move |tasks| Ok(tasks.get(&who, &id))).await
    }
    pub(crate) async fn participant_identities_async(
        self,
    ) -> Result<std::collections::HashSet<String>> {
        self.run(|tasks| Ok(tasks.participant_identities())).await
    }
    pub(crate) async fn update_task_async(
        self,
        who: &str,
        sender: &str,
        id: &str,
        status: Status,
        note: Option<String>,
    ) -> Result<Task> {
        let (who, sender, id) = (who.to_owned(), sender.to_owned(), id.to_owned());
        self.run(move |tasks| tasks.update(&who, &sender, &id, status, note))
            .await
    }
    pub(crate) async fn remove_task_async(self, who: &str, id: &str, force: bool) -> Result<Task> {
        let (who, id) = (who.to_owned(), id.to_owned());
        self.run(move |tasks| tasks.remove(&who, &id, force)).await
    }
    pub(crate) async fn cancel_tasks_async(
        self,
        who: &str,
        sender: &str,
        ids: &[String],
        force: bool,
    ) -> Result<crate::tasks::BatchResult> {
        let (who, sender, ids) = (who.to_owned(), sender.to_owned(), ids.to_vec());
        self.run(move |tasks| tasks.cancel_many(&who, &sender, &ids, force))
            .await
    }
    pub(crate) async fn remove_tasks_async(
        self,
        who: &str,
        ids: &[String],
        force: bool,
    ) -> Result<crate::tasks::BatchResult> {
        let (who, ids) = (who.to_owned(), ids.to_vec());
        self.run(move |tasks| tasks.remove_many(&who, &ids, force))
            .await
    }
    pub(crate) async fn prune_tasks_async(
        self,
        who: &str,
        all: bool,
    ) -> Result<crate::tasks::BatchResult> {
        let who = who.to_owned();
        self.run(move |tasks| tasks.prune(&who, all)).await
    }
    pub(crate) async fn summary_stamp(self, task: Task) -> Result<Option<crate::tasks::TaskStamp>> {
        self.run(move |tasks| Ok(tasks.stamp(&task))).await
    }
    pub(crate) async fn set_summary_checked(
        self,
        stamp: crate::tasks::TaskStamp,
        summary: String,
    ) -> Result<Option<Task>> {
        self.run(move |tasks| tasks.set_summary_checked(&stamp, &summary))
            .await
    }
    pub(crate) async fn fail_worker_async(
        self,
        id: &str,
        identity: &str,
        note: String,
    ) -> Result<Option<Task>> {
        let (id, identity) = (id.to_owned(), identity.to_owned());
        self.run(move |tasks| tasks.fail_worker(&id, &identity, note))
            .await
    }
}

#[cfg(test)]
#[path = "io_tests.rs"]
mod tests;
