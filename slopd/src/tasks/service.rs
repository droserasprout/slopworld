//! Task policy, authority and accepted indexes. Persistence owns file formats.
//! One blocking owner serializes validation, disk commit and publication. Batches
//! prevalidate completely, then stop at the first failed independent record write.
use super::*;
use crate::clock::unix_ms;
use anyhow::{Context, Result, ensure};
use std::{
    collections::{BTreeMap, HashMap, HashSet},
    path::Path,
};

#[derive(Debug, Default, Serialize)]
pub(crate) struct BatchResult {
    pub committed: Vec<String>,
    pub unchanged: Vec<String>,
    pub absent: Vec<String>,
    pub failed: Vec<BatchFailure>,
    pub unattempted: Vec<String>,
    pub tasks: Vec<Task>,
}
#[derive(Debug, Serialize)]
pub(crate) struct BatchFailure {
    pub id: String,
    pub error: String,
}

#[derive(Clone)]
pub(crate) struct TaskStamp {
    id: String,
    incarnation: u64,
}

struct Entry {
    task: Task,
    order: u64,
    incarnation: u64,
}

pub(crate) struct Tasks {
    disk: super::records::Records,
    by_id: HashMap<String, Entry>,
    order: BTreeMap<u64, String>,
    next_order: u64,
    incarnation: u64,
    reserved: HashSet<String>,
}
impl Tasks {
    #[cfg(test)]
    pub(crate) fn load(config: &Path) -> Result<Self> {
        Self::load_records(&config.parent().unwrap_or(Path::new(".")).join("data"))
    }
    pub(crate) fn load_records(data: &Path) -> Result<Self> {
        let (disk, values) = super::records::Records::load(data)?;
        Self::loaded(disk, values)
    }
    fn loaded(disk: super::records::Records, values: Vec<(u64, Task)>) -> Result<Self> {
        let mut store = Self {
            disk,
            by_id: HashMap::new(),
            order: BTreeMap::new(),
            next_order: 0,
            incarnation: 0,
            reserved: HashSet::new(),
        };
        for (order, task) in values {
            ensure!(
                !store.by_id.contains_key(&task.id),
                "duplicate task identity"
            );
            ensure!(
                !store.order.contains_key(&order),
                "duplicate task storage_order"
            );
            store.publish(task, order)?;
        }
        Ok(store)
    }
    fn publish(&mut self, task: Task, order: u64) -> Result<()> {
        self.next_order = self
            .next_order
            .max(order.checked_add(1).context("task order exhausted")?);
        let incarnation = match self.by_id.get(&task.id) {
            Some(entry) => entry.incarnation,
            None => {
                self.incarnation = self
                    .incarnation
                    .checked_add(1)
                    .context("task incarnation exhausted")?;
                self.incarnation
            }
        };
        self.reserved.insert(task.id.clone());
        self.order.insert(order, task.id.clone());
        self.by_id.insert(
            task.id.clone(),
            Entry {
                task,
                order,
                incarnation,
            },
        );
        Ok(())
    }
    fn persist(&mut self, task: Task, order: u64) -> Result<Task> {
        // Reserve infallible publication before the disk commit.
        ensure!(
            order < i64::MAX as u64 && self.incarnation < u64::MAX,
            "task metadata exhausted"
        );
        self.disk
            .put(&task, order, self.by_id.contains_key(&task.id))?;
        self.publish(task.clone(), order)?;
        Ok(task)
    }
    fn retire(&mut self, id: &str) -> Result<()> {
        self.disk.retire(id)?;
        if let Some(old) = self.by_id.remove(id) {
            self.order.remove(&old.order);
        }
        Ok(())
    }
    pub(crate) fn reserve_references(&mut self, ids: impl IntoIterator<Item = String>) {
        self.reserved
            .extend(ids.into_iter().filter(|id| !id.is_empty()));
    }

    pub(crate) fn create_owned(
        &mut self,
        from: Participant,
        to: Participant,
        body: String,
        worker: Option<WorkerTask>,
    ) -> Result<Task> {
        ensure!(!body.trim().is_empty(), "Provide a task body.");
        ensure!(
            !from.identity.is_empty() && !to.identity.is_empty(),
            "Task participants require explicit identities."
        );
        let id = crate::storage_id::allocate(|id| {
            if self.reserved.contains(id) {
                return Ok(true);
            }
            self.disk.occupied(id)
        })?;
        let now = unix_ms();
        self.persist(
            Task {
                id,
                from: from.name,
                to: to.name,
                from_id: from.identity,
                to_id: to.identity,
                body,
                status: Status::Queued,
                note: None,
                summary: None,
                created_ms: now,
                updated_ms: now,
                worker,
            },
            self.next_order,
        )
    }
    #[cfg(test)]
    pub(crate) fn create(&mut self, from: String, to: String, body: String) -> Result<Task> {
        self.create_owned(
            Participant {
                name: from.clone(),
                identity: from,
            },
            Participant {
                name: to.clone(),
                identity: to,
            },
            body,
            None,
        )
    }
    #[cfg(test)]
    pub(crate) fn create_worker(
        &mut self,
        from: String,
        to: String,
        body: String,
        parent: String,
        durable: bool,
    ) -> Result<Task> {
        let worker = WorkerTask {
            session: to.clone(),
            parent,
            durable,
        };
        self.create_owned(
            Participant {
                name: from.clone(),
                identity: from,
            },
            Participant {
                name: to.clone(),
                identity: to,
            },
            body,
            Some(worker),
        )
    }
    #[expect(
        clippy::expect_used,
        reason = "the ordinal and ID indexes publish together; a missing entry is an internal invariant failure"
    )]
    fn indexed(&self, id: &str) -> &Entry {
        self.by_id
            .get(id)
            .expect("task order index refers to a missing identity")
    }

    pub(crate) fn all(&self) -> Vec<Task> {
        self.order
            .values()
            .map(|id| self.indexed(id).task.clone())
            .collect()
    }
    pub(crate) fn visible(&self, who: &str) -> Vec<Task> {
        self.order
            .values()
            .filter_map(|id| {
                let task = &self.indexed(id).task;
                task.visible_to(who).then(|| task.clone())
            })
            .collect()
    }
    pub(crate) fn get(&self, who: &str, id: &str) -> Option<Task> {
        self.by_id
            .get(id)
            .map(|e| &e.task)
            .filter(|task| task.visible_to(who))
            .cloned()
    }
    pub(crate) fn participant_identities(&self) -> HashSet<String> {
        self.by_id
            .values()
            .flat_map(|e| [&e.task.from_id, &e.task.to_id])
            .filter(|s| !s.is_empty())
            .cloned()
            .collect()
    }
    pub(crate) fn update(
        &mut self,
        who: &str,
        id: &str,
        status: Status,
        note: Option<String>,
    ) -> Result<Task> {
        let entry = self.by_id.get(id).context("Task does not exist")?;
        ensure!(
            entry.task.recipient_is(who),
            "Only the recipient can update this task."
        );
        ensure!(
            entry.task.status != Status::Canceled,
            "Task is canceled. You cannot update it."
        );
        let mut task = entry.task.clone();
        let order = entry.order;
        task.status = status;
        task.note = note;
        task.updated_ms = unix_ms();
        self.persist(task, order)
    }
    pub(crate) fn stamp(&self, task: &Task) -> Option<TaskStamp> {
        self.by_id
            .get(&task.id)
            .filter(|e| {
                e.task.created_ms == task.created_ms
                    && e.task.body == task.body
                    && e.task.from_id == task.from_id
                    && e.task.to_id == task.to_id
            })
            .map(|e| TaskStamp {
                id: task.id.clone(),
                incarnation: e.incarnation,
            })
    }
    pub(crate) fn set_summary_checked(
        &mut self,
        stamp: &TaskStamp,
        summary: String,
    ) -> Result<Option<Task>> {
        let Some(entry) = self
            .by_id
            .get(&stamp.id)
            .filter(|e| e.incarnation == stamp.incarnation && e.task.summary.is_none())
        else {
            return Ok(None);
        };
        let mut task = entry.task.clone();
        let order = entry.order;
        if summary.trim().is_empty() {
            return Ok(Some(task));
        }
        task.summary = Some(summary.trim().into());
        self.persist(task, order).map(Some)
    }
    #[cfg(test)]
    pub(crate) fn set_summary(&mut self, id: &str, summary: String) -> Result<Option<Task>> {
        let Some(entry) = self.by_id.get(id) else {
            return Ok(None);
        };
        let stamp = TaskStamp {
            id: id.into(),
            incarnation: entry.incarnation,
        };
        self.set_summary_checked(&stamp, summary)
    }
    pub(crate) fn fail_worker(
        &mut self,
        id: &str,
        expected: &str,
        note: String,
    ) -> Result<Option<Task>> {
        let Some(entry) = self
            .by_id
            .get(id)
            .filter(|e| e.task.worker.is_some() && e.task.to_id == expected)
        else {
            return Ok(None);
        };
        let mut task = entry.task.clone();
        let order = entry.order;
        if task.status.is_terminal() {
            return Ok(Some(task));
        }
        task.status = Status::Failed;
        task.note = Some(note);
        task.updated_ms = unix_ms();
        self.persist(task, order).map(Some)
    }
    pub(crate) fn remove(&mut self, who: &str, id: &str, force: bool) -> Result<Task> {
        let task = self
            .by_id
            .get(id)
            .map(|e| e.task.clone())
            .filter(|t| force || t.visible_to(who))
            .context("Task does not exist")?;
        ensure!(
            force || task.status.is_terminal(),
            "Task is unfinished. Finish, fail, or cancel it before you remove it."
        );
        self.retire(id)?;
        Ok(task)
    }
    pub(crate) fn cancel_many(
        &mut self,
        who: &str,
        ids: &[String],
        force: bool,
    ) -> Result<BatchResult> {
        self.batch(who, ids, force, true)
    }
    pub(crate) fn remove_many(
        &mut self,
        who: &str,
        ids: &[String],
        force: bool,
    ) -> Result<BatchResult> {
        self.batch(who, ids, force, false)
    }
    pub(crate) fn prune(&mut self, who: &str, all: bool) -> Result<BatchResult> {
        let ids = self
            .order
            .values()
            .filter(|id| {
                let t = &self.indexed(id).task;
                t.status.is_terminal() && (all || t.visible_to(who))
            })
            .cloned()
            .collect::<Vec<_>>();
        self.batch(who, &ids, all, false)
    }
    fn batch(
        &mut self,
        who: &str,
        ids: &[String],
        force: bool,
        cancel: bool,
    ) -> Result<BatchResult> {
        let mut selected = ids
            .iter()
            .filter(|id| !id.trim().is_empty())
            .cloned()
            .collect::<Vec<_>>();
        selected.sort();
        selected.dedup();
        // Stable persisted listing order first; missing IDs sort lexically last.
        selected.sort_by_key(|id| self.by_id.get(id).map_or(u64::MAX, |e| e.order));
        for id in &selected {
            let Some(entry) = self.by_id.get(id) else {
                continue;
            };
            let task = &entry.task;
            if cancel {
                ensure!(
                    force || task.recipient_is(who),
                    "Only the recipient can cancel task {id}."
                );
                ensure!(
                    matches!(
                        task.status,
                        Status::Queued | Status::Accepted | Status::Canceled
                    ),
                    "Cancel task {id} only when it is queued or accepted."
                );
            } else {
                ensure!(
                    force || task.visible_to(who),
                    "Task {id} is not available to this caller."
                );
                ensure!(
                    force || task.status.is_terminal(),
                    "Task {id} is unfinished. Finish, fail, or cancel it before you remove it."
                );
            }
        }
        let mut result = BatchResult::default();
        for (at, id) in selected.iter().enumerate() {
            let Some(entry) = self.by_id.get(id) else {
                result.absent.push(id.clone());
                continue;
            };
            if cancel && entry.task.status == Status::Canceled {
                result.unchanged.push(id.clone());
                result.tasks.push(entry.task.clone());
                continue;
            }
            let change = if cancel {
                let mut task = entry.task.clone();
                let order = entry.order;
                task.status = Status::Canceled;
                task.updated_ms = unix_ms();
                self.persist(task, order).map(|task| {
                    result.tasks.push(task);
                })
            } else {
                self.retire(id)
            };
            if let Err(error) = change {
                result.failed.push(BatchFailure {
                    id: id.clone(),
                    error: format!("{error:#}"),
                });
                result
                    .unattempted
                    .extend(selected.iter().skip(at + 1).cloned());
                break;
            }
            result.committed.push(id.clone());
        }
        Ok(result)
    }
}

#[cfg(test)]
#[path = "service_tests.rs"]
mod tests;
