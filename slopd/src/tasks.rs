use std::collections::HashSet;
use std::fs;
use std::path::{Path, PathBuf};
use std::time::{SystemTime, UNIX_EPOCH};

use anyhow::{bail, Context, Result};
use serde::{Deserialize, Serialize};

/// The user at the keyboard. Not a session and never one: `slopctl` run from the host states it
/// as its identity, and the daemon accepts it only from the root token, so a grant cannot wear it
/// however its grantor happens to be named.
pub const HOST: &str = crate::wire::HOST_IDENTITY;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Status {
    Queued,
    Accepted,
    Working,
    Done,
    Failed,
    Canceled,
}

crate::wire_enum!(Status, {
    Status::Queued => crate::wire::enums::task_status::QUEUED,
    Status::Accepted => crate::wire::enums::task_status::ACCEPTED,
    Status::Working => crate::wire::enums::task_status::WORKING,
    Status::Done => crate::wire::enums::task_status::DONE,
    Status::Failed => crate::wire::enums::task_status::FAILED,
    Status::Canceled => crate::wire::enums::task_status::CANCELED,
});

impl Status {
    /// Where a task stops moving. What `prune` may drop, and what an inbox leaves out until asked.
    pub fn is_terminal(self) -> bool {
        matches!(self, Status::Done | Status::Failed | Status::Canceled)
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Task {
    pub id: String,
    pub from: String,
    pub to: String,
    pub body: String,
    pub status: Status,
    pub note: Option<String>,
    /// Optional OpenRouter preview for the compact sidebar. The task body remains the source
    /// of truth and older task files simply deserialize this as absent.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub summary: Option<String>,
    pub created_ms: u64,
    pub updated_ms: u64,
    /// Present only for a task that owns a daemon-spawned worker. The task body remains the
    /// single source of truth; the worker receives only this task's id at startup.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub worker: Option<WorkerTask>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct WorkerTask {
    pub session: String,
    pub parent: String,
    /// Durable workers remain in config.toml after their process exits; one-shot workers do not.
    #[serde(default)]
    pub durable: bool,
}

#[derive(Default, Serialize, Deserialize)]
struct File {
    #[serde(default)]
    tasks: Vec<Task>,
}

pub struct Tasks {
    path: PathBuf,
    file: File,
    sequence: u64,
}

impl Tasks {
    pub fn load(config: &Path) -> Result<Self> {
        let path = config.with_file_name("tasks.toml");
        let file = match fs::read_to_string(&path) {
            Ok(s) => match toml::from_str(&s) {
                Ok(file) => file,
                Err(e) => {
                    tracing::warn!("ignoring invalid task store {}: {e}", path.display());
                    File::default()
                }
            },
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => File::default(),
            Err(e) => {
                tracing::warn!("ignoring unreadable task store {}: {e}", path.display());
                File::default()
            }
        };
        let sequence = file
            .tasks
            .iter()
            .filter_map(|task| task.id.rsplit_once('-'))
            .filter_map(|(_, suffix)| u64::from_str_radix(suffix, 16).ok())
            .max()
            .unwrap_or(0);
        let tasks = Self {
            path,
            file,
            sequence,
        };
        Ok(tasks)
    }

    pub fn create(&mut self, from: String, to: String, body: String) -> Result<Task> {
        self.create_inner(from, to, body, None)
    }

    pub fn create_worker(
        &mut self,
        from: String,
        to: String,
        body: String,
        parent: String,
        durable: bool,
    ) -> Result<Task> {
        let worker = Some(WorkerTask {
            session: to.clone(),
            parent,
            durable,
        });
        self.create_inner(from, to, body, worker)
    }

    fn create_inner(
        &mut self,
        from: String,
        to: String,
        body: String,
        worker: Option<WorkerTask>,
    ) -> Result<Task> {
        if body.trim().is_empty() {
            bail!("a task body is required");
        }
        let now = now_ms();
        self.sequence += 1;
        let task = Task {
            id: format!("{now:013x}-{:04x}", self.sequence),
            from,
            to,
            body,
            status: Status::Queued,
            note: None,
            summary: None,
            created_ms: now,
            updated_ms: now,
            worker,
        };
        self.file.tasks.push(task.clone());
        self.save()?;
        Ok(task)
    }

    /// A worker exit is daemon-owned lifecycle bookkeeping, not a recipient update. Do not
    /// overwrite a worker's explicit `finish`/`fail`, and keep a failed task after one-shot worker
    /// cleanup so a lost process is still inspectable after a daemon restart.
    pub fn fail_worker(&mut self, id: &str, note: String) -> Result<Option<Task>> {
        let Some(task) = self
            .file
            .tasks
            .iter_mut()
            .find(|task| task.id == id && task.worker.is_some())
        else {
            return Ok(None);
        };
        if task.status.is_terminal() {
            return Ok(Some(task.clone()));
        }
        task.status = Status::Failed;
        task.note = Some(note);
        task.updated_ms = now_ms();
        let result = task.clone();
        self.save()?;
        Ok(Some(result))
    }

    pub fn visible(&self, who: &str) -> Vec<Task> {
        self.file
            .tasks
            .iter()
            .filter(|t| t.from == who || t.to == who)
            .cloned()
            .collect()
    }

    pub fn all(&self) -> Vec<Task> {
        self.file.tasks.clone()
    }

    pub fn get(&self, who: &str, id: &str) -> Option<Task> {
        self.file
            .tasks
            .iter()
            .find(|t| t.id == id && (t.from == who || t.to == who))
            .cloned()
    }

    pub fn update(
        &mut self,
        who: &str,
        id: &str,
        status: Status,
        note: Option<String>,
    ) -> Result<Task> {
        let task = self
            .file
            .tasks
            .iter_mut()
            .find(|t| t.id == id)
            .with_context(|| format!("no such task: {id}"))?;
        if task.to != who {
            bail!("only the task recipient may update it");
        }
        if task.status == Status::Canceled {
            bail!("a canceled task cannot be updated: {id}");
        }
        task.status = status;
        task.note = note;
        task.updated_ms = now_ms();
        let result = task.clone();
        self.save()?;
        Ok(result)
    }

    pub fn set_summary(&mut self, id: &str, summary: String) -> Result<Option<Task>> {
        let Some(task) = self.file.tasks.iter_mut().find(|task| task.id == id) else {
            return Ok(None);
        };
        let summary = summary.trim().to_string();
        if summary.is_empty() {
            return Ok(Some(task.clone()));
        }
        task.summary = Some(summary);
        let result = task.clone();
        self.save()?;
        Ok(Some(result))
    }

    /// Cancel queued or accepted work. The recipient may cancel its own task; the root may
    /// cancel any task so the host task board can stop work it sent before it was picked up.
    pub fn cancel_many(&mut self, who: &str, ids: &[String], force: bool) -> Result<Vec<Task>> {
        let wanted: HashSet<String> = ids
            .iter()
            .filter(|id| !id.trim().is_empty())
            .cloned()
            .collect();
        if wanted.is_empty() {
            return Ok(Vec::new());
        }

        for id in &wanted {
            let task = self
                .file
                .tasks
                .iter()
                .find(|t| t.id == *id)
                .with_context(|| format!("no such task: {id}"))?;
            if !force && task.to != who {
                bail!("only the task recipient may cancel it: {id}");
            }
            if !matches!(task.status, Status::Queued | Status::Accepted) {
                bail!("only queued or accepted tasks can be canceled: {id}");
            }
        }

        let now = now_ms();
        let mut canceled = Vec::new();
        for task in &mut self.file.tasks {
            if wanted.contains(&task.id) {
                task.status = Status::Canceled;
                task.updated_ms = now;
                canceled.push(task.clone());
            }
        }
        self.save()?;
        Ok(canceled)
    }

    /// Drop one task. The store holds a single copy of a task rather than one per side, so a
    /// removal is not a participant hiding it from their own view - it is gone for both. A
    /// participant may therefore only drop one that has already stopped moving; `force` is the
    /// root's, and reaches a task still in flight.
    pub fn remove(&mut self, who: &str, id: &str, force: bool) -> Result<Task> {
        let at = self
            .file
            .tasks
            .iter()
            .position(|t| t.id == id && (force || t.from == who || t.to == who))
            .with_context(|| format!("no such task: {id}"))?;
        if !force && !self.file.tasks[at].status.is_terminal() {
            bail!("a task still in flight cannot be removed: {id}");
        }
        let task = self.file.tasks.remove(at);
        self.save()?;
        Ok(task)
    }

    /// Drop several tasks as one durable mutation. Validate the complete request before changing
    /// the store so a stale selection cannot remove only part of what the user confirmed.
    pub fn remove_many(&mut self, who: &str, ids: &[String], force: bool) -> Result<usize> {
        let wanted: HashSet<String> = ids
            .iter()
            .filter(|id| !id.trim().is_empty())
            .cloned()
            .collect();
        if wanted.is_empty() {
            return Ok(0);
        }

        for id in &wanted {
            let task = self
                .file
                .tasks
                .iter()
                .find(|t| t.id == *id && (force || t.from == who || t.to == who))
                .with_context(|| format!("no such task: {id}"))?;
            if !force && !task.status.is_terminal() {
                bail!("a task still in flight cannot be removed: {id}");
            }
        }

        let before = self.file.tasks.len();
        self.file.tasks.retain(|task| !wanted.contains(&task.id));
        let removed = before - self.file.tasks.len();
        if removed > 0 {
            self.save()?;
        }
        Ok(removed)
    }

    /// Drop every finished task the caller can see - `all` widens that to the whole store and is
    /// the root's. Without this the file is append-only and an inbox is a growing wall.
    pub fn prune(&mut self, who: &str, all: bool) -> Result<usize> {
        let before = self.file.tasks.len();
        self.file
            .tasks
            .retain(|t| !(t.status.is_terminal() && (all || t.from == who || t.to == who)));
        let removed = before - self.file.tasks.len();
        if removed > 0 {
            self.save()?;
        }
        Ok(removed)
    }

    fn save(&self) -> Result<()> {
        crate::paths::write_private_toml(&self.path, &toml::to_string_pretty(&self.file)?)
    }
}

fn now_ms() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_millis() as u64
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn visibility_updates_and_persistence() {
        let dir = std::env::temp_dir().join(format!("slopd-tasks-{}", std::process::id()));
        let _ = fs::remove_dir_all(&dir);
        fs::create_dir_all(&dir).unwrap();
        let mut s = Tasks::load(&dir.join("config.toml")).unwrap();
        let t = s
            .create("alice".into(), "bob".into(), "check".into())
            .unwrap();
        assert!(s.visible("eve").is_empty());
        assert!(s.update("alice", &t.id, Status::Done, None).is_err());
        s.update("bob", &t.id, Status::Done, Some("ok".into()))
            .unwrap();
        assert_eq!(
            Tasks::load(&dir.join("config.toml"))
                .unwrap()
                .get("alice", &t.id)
                .unwrap()
                .status,
            Status::Done
        );
        let _ = fs::remove_dir_all(dir);
    }

    #[test]
    fn summary_round_trips_without_changing_task_age() {
        let dir = std::env::temp_dir().join(format!("slopd-task-summary-{}", std::process::id()));
        let _ = fs::remove_dir_all(&dir);
        fs::create_dir_all(&dir).unwrap();
        let config = dir.join("config.toml");
        let mut tasks = Tasks::load(&config).unwrap();
        let task = tasks
            .create(
                "host".into(),
                "agent".into(),
                "inspect the sidebar layout".into(),
            )
            .unwrap();
        let updated = tasks
            .set_summary(&task.id, "Inspect sidebar layout".into())
            .unwrap()
            .unwrap();

        assert_eq!(updated.summary.as_deref(), Some("Inspect sidebar layout"));
        assert_eq!(updated.updated_ms, task.updated_ms);
        assert_eq!(
            Tasks::load(&config).unwrap().all()[0].summary.as_deref(),
            Some("Inspect sidebar layout")
        );
        let _ = fs::remove_dir_all(dir);
    }

    /// Removal is shared, so it waits for the task to stop moving - unless the root asks. Pruning
    /// takes the finished ones the caller can see, and `all` the rest.
    #[test]
    fn removing_and_pruning_finished_work() {
        let dir = std::env::temp_dir().join(format!("slopd-rm-tasks-{}", std::process::id()));
        let _ = fs::remove_dir_all(&dir);
        fs::create_dir_all(&dir).unwrap();
        let config = dir.join("config.toml");
        let mut s = Tasks::load(&config).unwrap();
        let live = s
            .create("alice".into(), "bob".into(), "live".into())
            .unwrap();
        let over = s
            .create("alice".into(), "bob".into(), "over".into())
            .unwrap();
        s.update("bob", &over.id, Status::Done, None).unwrap();

        let accepted = s
            .create("alice".into(), "bob".into(), "accepted".into())
            .unwrap();
        s.update("bob", &accepted.id, Status::Accepted, None)
            .unwrap();
        let canceled = s
            .cancel_many("bob", std::slice::from_ref(&accepted.id), false)
            .unwrap();
        assert_eq!(canceled[0].status, Status::Canceled);
        assert!(s.update("bob", &accepted.id, Status::Done, None).is_err());
        assert!(s.remove("alice", &accepted.id, false).is_ok());

        assert!(s.remove("eve", &over.id, false).is_err()); // not a participant
        assert!(s.remove("alice", &live.id, false).is_err()); // still in flight
        assert!(s.remove("alice", &live.id, true).is_ok()); // the root reaches it
        assert!(s.remove("alice", &over.id, false).is_ok()); // finished, so either side may

        let first = s
            .create("alice".into(), "bob".into(), "first batch item".into())
            .unwrap();
        let second = s
            .create("alice".into(), "bob".into(), "second batch item".into())
            .unwrap();
        s.update("bob", &first.id, Status::Done, None).unwrap();
        s.update("bob", &second.id, Status::Failed, None).unwrap();
        assert_eq!(
            s.remove_many(
                "alice",
                &[first.id.clone(), second.id.clone(), first.id],
                false
            )
            .unwrap(),
            2
        );

        let other = s
            .create("carol".into(), "dave".into(), "theirs".into())
            .unwrap();
        s.update("dave", &other.id, Status::Failed, None).unwrap();
        assert_eq!(s.prune("alice", false).unwrap(), 0); // not alice's to see
        assert_eq!(s.prune("carol", false).unwrap(), 1);

        let mut s = Tasks::load(&config).unwrap();
        let mine = s
            .create("alice".into(), "bob".into(), "mine".into())
            .unwrap();
        s.update("bob", &mine.id, Status::Done, None).unwrap();
        assert_eq!(s.prune("nobody", true).unwrap(), 1); // `all` ignores who is asking
        assert!(Tasks::load(&config).unwrap().visible("alice").is_empty());
        let _ = fs::remove_dir_all(dir);
    }

    #[test]
    fn invalid_store_is_ignored_and_loaded_ids_continue_the_sequence() {
        let dir = std::env::temp_dir().join(format!("slopd-bad-tasks-{}", std::process::id()));
        let _ = fs::remove_dir_all(&dir);
        fs::create_dir_all(&dir).unwrap();
        let config = dir.join("config.toml");
        fs::write(dir.join("tasks.toml"), "[").unwrap();
        assert!(Tasks::load(&config).unwrap().visible("anyone").is_empty());

        let mut tasks = Tasks::load(&config).unwrap();
        let first = tasks
            .create("alice".into(), "bob".into(), "first".into())
            .unwrap();
        let mut loaded = Tasks::load(&config).unwrap();
        let second = loaded
            .create("alice".into(), "bob".into(), "second".into())
            .unwrap();
        assert_eq!(first.id.rsplit_once('-').unwrap().1, "0001");
        assert_eq!(second.id.rsplit_once('-').unwrap().1, "0002");
        let _ = fs::remove_dir_all(dir);
    }

    #[test]
    fn worker_task_metadata_and_daemon_failure_survive_reload() {
        let dir = std::env::temp_dir().join(format!("slopd-worker-tasks-{}", std::process::id()));
        let _ = fs::remove_dir_all(&dir);
        fs::create_dir_all(&dir).unwrap();
        let config = dir.join("config.toml");
        let mut tasks = Tasks::load(&config).unwrap();
        let task = tasks
            .create_worker(
                "parent".into(),
                "parent-worker".into(),
                "inspect the build".into(),
                "parent".into(),
                true,
            )
            .unwrap();
        let worker = task.worker.as_ref().unwrap();
        assert_eq!(worker.session, "parent-worker");
        assert_eq!(worker.parent, "parent");
        assert!(worker.durable);

        let failed = tasks
            .fail_worker(&task.id, "worker exited".into())
            .unwrap()
            .unwrap();
        assert_eq!(failed.status, Status::Failed);
        assert_eq!(failed.note.as_deref(), Some("worker exited"));

        let reloaded = Tasks::load(&config).unwrap();
        let persisted = reloaded.get("parent", &task.id).unwrap();
        assert_eq!(persisted.status, Status::Failed);
        assert_eq!(persisted.worker.unwrap().session, "parent-worker");
        let unchanged = tasks
            .fail_worker(&task.id, "a later exit".into())
            .unwrap()
            .unwrap();
        assert_eq!(unchanged.note.as_deref(), Some("worker exited"));
        let _ = fs::remove_dir_all(dir);
    }
}
