//! Durable task storage owned by the session manager.

use super::super::*;
use crate::tasks::{Status, Task};

/// The task file has one synchronous boundary. Keep its mutex and concrete store out of the
/// manager's configuration owner; callers use typed operations on this owner instead.
pub(crate) struct TaskStore(std::sync::Mutex<crate::tasks::Tasks>);

impl TaskStore {
    pub(crate) fn new(tasks: crate::tasks::Tasks) -> Self {
        Self(std::sync::Mutex::new(tasks))
    }

    pub(crate) fn create_task(&self, from: String, to: String, body: String) -> Result<Task> {
        self.0.lock().unwrap().create(from, to, body)
    }

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
mod tests {
    use super::*;

    #[test]
    fn task_owner_preserves_visibility_authority_and_durable_mutations() {
        let m = crate::session::test_manager(Config::default());
        let task = m
            .tasks
            .create_task("sender".into(), "recipient".into(), "Review changes".into())
            .unwrap();
        assert_eq!(m.tasks.tasks_for("sender").len(), 1);
        assert_eq!(m.tasks.tasks_for("recipient").len(), 1);
        assert!(m.tasks.tasks_for("stranger").is_empty());
        assert!(m.tasks.task_for("stranger", &task.id).is_none());
        assert!(m
            .tasks
            .update_task("sender", &task.id, Status::Accepted, None)
            .is_err());
        assert!(m.tasks.remove_task("sender", &task.id, false).is_err());
        let accepted = m
            .tasks
            .update_task(
                "recipient",
                &task.id,
                Status::Accepted,
                Some("Reviewing".into()),
            )
            .unwrap();
        assert_eq!(accepted.status, Status::Accepted);
        assert_eq!(accepted.note.as_deref(), Some("Reviewing"));
        assert!(m
            .tasks
            .cancel_tasks("sender", std::slice::from_ref(&task.id), false)
            .is_err());
        let canceled = m
            .tasks
            .cancel_tasks("recipient", std::slice::from_ref(&task.id), false)
            .unwrap();
        assert_eq!(canceled[0].status, Status::Canceled);
        let reloaded = crate::tasks::Tasks::load(&m.cfg_path).unwrap();
        assert_eq!(
            reloaded.get("sender", &task.id).unwrap().status,
            Status::Canceled
        );
        assert_eq!(
            m.tasks.remove_task("sender", &task.id, false).unwrap().id,
            task.id
        );
        assert!(m.tasks.all_tasks().is_empty());
        assert!(crate::tasks::Tasks::load(&m.cfg_path)
            .unwrap()
            .all()
            .is_empty());
    }

    #[test]
    fn bulk_removal_is_atomic_and_pruning_respects_visibility() {
        let m = crate::session::test_manager(Config::default());
        let a = m
            .tasks
            .create_task("host".into(), "agent".into(), "First".into())
            .unwrap();
        let b = m
            .tasks
            .create_task("other".into(), "worker".into(), "Second".into())
            .unwrap();
        m.tasks
            .update_task("agent", &a.id, Status::Done, None)
            .unwrap();
        m.tasks
            .update_task("worker", &b.id, Status::Failed, None)
            .unwrap();
        assert!(m
            .tasks
            .remove_tasks("host", &[a.id.clone(), b.id.clone()], false)
            .is_err());
        assert_eq!(m.tasks.all_tasks().len(), 2);
        assert_eq!(m.tasks.prune_tasks("host", false).unwrap(), 1);
        assert_eq!(m.tasks.all_tasks()[0].id, b.id);
        assert_eq!(m.tasks.remove_tasks("host", &[b.id], true).unwrap(), 1);
        assert!(crate::tasks::Tasks::load(&m.cfg_path)
            .unwrap()
            .all()
            .is_empty());
    }

    #[test]
    fn worker_failure_only_changes_unfinished_worker_tasks() {
        let m = crate::session::test_manager(Config::default());
        let ordinary = m
            .tasks
            .create_task("host".into(), "agent".into(), "Ordinary".into())
            .unwrap();
        let worker = m
            .tasks
            .create_worker(
                "host".into(),
                "child".into(),
                "Delegate".into(),
                "parent".into(),
                true,
            )
            .unwrap();
        let metadata = worker.worker.as_ref().unwrap();
        assert_eq!(metadata.session, "child");
        assert_eq!(metadata.parent, "parent");
        assert!(metadata.durable);
        for id in ["", "  ", "missing", &ordinary.id] {
            m.fail_worker_task(id, "Exited");
        }
        assert_eq!(
            m.tasks.task_for("host", &ordinary.id).unwrap().status,
            Status::Queued
        );
        m.fail_worker_task(&worker.id, "Exited");
        m.fail_worker_task(&worker.id, "Must not overwrite terminal result");
        let saved = crate::tasks::Tasks::load(&m.cfg_path)
            .unwrap()
            .get("host", &worker.id)
            .unwrap();
        assert_eq!(saved.status, Status::Failed);
        assert_eq!(saved.note.as_deref(), Some("Exited"));
    }
}
