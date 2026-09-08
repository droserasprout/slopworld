//! Durable task storage owned by the session manager.

use super::super::*;

/// The task file has one synchronous mutation boundary. Keep its mutex and concrete store out
/// of the manager's configuration owner; callers still use Manager's public task façade.
pub(crate) struct TaskStore(std::sync::Mutex<crate::tasks::Tasks>);

impl TaskStore {
    pub(crate) fn new(tasks: crate::tasks::Tasks) -> Self {
        Self(std::sync::Mutex::new(tasks))
    }

    fn with<T>(&self, f: impl FnOnce(&mut crate::tasks::Tasks) -> T) -> T {
        f(&mut self.0.lock().unwrap())
    }

    fn with_ref<T>(&self, f: impl FnOnce(&crate::tasks::Tasks) -> T) -> T {
        f(&self.0.lock().unwrap())
    }

    fn create(&self, from: String, to: String, body: String) -> Result<crate::tasks::Task> {
        self.with(|tasks| tasks.create(from, to, body))
    }

    pub(super) fn create_worker(
        &self,
        from: String,
        to: String,
        body: String,
        parent: String,
        durable: bool,
    ) -> Result<crate::tasks::Task> {
        self.with(|tasks| tasks.create_worker(from, to, body, parent, durable))
    }

    fn visible(&self, who: &str) -> Vec<crate::tasks::Task> {
        self.with_ref(|tasks| tasks.visible(who))
    }

    fn all(&self) -> Vec<crate::tasks::Task> {
        self.with_ref(crate::tasks::Tasks::all)
    }

    fn get(&self, who: &str, id: &str) -> Option<crate::tasks::Task> {
        self.with_ref(|tasks| tasks.get(who, id))
    }

    fn update(
        &self,
        who: &str,
        id: &str,
        status: crate::tasks::Status,
        note: Option<String>,
    ) -> Result<crate::tasks::Task> {
        self.with(|tasks| tasks.update(who, id, status, note))
    }

    fn cancel_many(
        &self,
        who: &str,
        ids: &[String],
        force: bool,
    ) -> Result<Vec<crate::tasks::Task>> {
        self.with(|tasks| tasks.cancel_many(who, ids, force))
    }

    fn remove(&self, who: &str, id: &str, force: bool) -> Result<crate::tasks::Task> {
        self.with(|tasks| tasks.remove(who, id, force))
    }

    fn remove_many(&self, who: &str, ids: &[String], force: bool) -> Result<usize> {
        self.with(|tasks| tasks.remove_many(who, ids, force))
    }

    fn prune(&self, who: &str, all: bool) -> Result<usize> {
        self.with(|tasks| tasks.prune(who, all))
    }

    fn fail_worker(&self, task_id: &str, note: String) -> Result<Option<crate::tasks::Task>> {
        self.with(|tasks| tasks.fail_worker(task_id, note))
    }
}

impl Manager {
    pub fn create_task(
        &self,
        from: String,
        to: String,
        body: String,
    ) -> Result<crate::tasks::Task> {
        self.tasks.create(from, to, body)
    }

    pub fn tasks_for(&self, who: &str) -> Vec<crate::tasks::Task> {
        self.tasks.visible(who)
    }

    pub fn all_tasks(&self) -> Vec<crate::tasks::Task> {
        self.tasks.all()
    }

    pub fn task_for(&self, who: &str, id: &str) -> Option<crate::tasks::Task> {
        self.tasks.get(who, id)
    }

    pub fn update_task(
        &self,
        who: &str,
        id: &str,
        status: crate::tasks::Status,
        note: Option<String>,
    ) -> Result<crate::tasks::Task> {
        self.tasks.update(who, id, status, note)
    }

    pub fn cancel_tasks(
        &self,
        who: &str,
        ids: &[String],
        force: bool,
    ) -> Result<Vec<crate::tasks::Task>> {
        self.tasks.cancel_many(who, ids, force)
    }

    pub fn remove_task(&self, who: &str, id: &str, force: bool) -> Result<crate::tasks::Task> {
        self.tasks.remove(who, id, force)
    }

    pub fn remove_tasks(&self, who: &str, ids: &[String], force: bool) -> Result<usize> {
        self.tasks.remove_many(who, ids, force)
    }

    pub fn prune_tasks(&self, who: &str, all: bool) -> Result<usize> {
        self.tasks.prune(who, all)
    }

    pub fn fail_worker_task(&self, task_id: &str, note: impl Into<String>) {
        if task_id.trim().is_empty() {
            return;
        }
        match self.tasks.fail_worker(task_id, note.into()) {
            Ok(Some(task)) if task.status == crate::tasks::Status::Failed => {
                tracing::info!(task = %task.id, "task-owned worker task marked failed")
            }
            Ok(_) => {}
            Err(error) => {
                tracing::error!(task = %task_id, "could not persist worker task failure: {error:#}")
            }
        }
    }
}
