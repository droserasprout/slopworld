//! Durable task records and commit boundaries. Session names are labels; participant IDs own authority.

use crate::clock::unix_ms;
use std::collections::{HashMap, HashSet};
use std::fs;
use std::io::Write;
use std::os::unix::fs::OpenOptionsExt;
use std::path::{Path, PathBuf};

use anyhow::{bail, Context, Result};
use serde::{Deserialize, Serialize};

/// The user at the keyboard, never a session.
/// Host `slopctl` uses this identity. The daemon accepts it only with the root token.
/// A grant cannot use this identity, regardless of its grantor name.
pub const HOST: &str = crate::shared::protocol::HOST_IDENTITY;

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
    Status::Queued => crate::shared::protocol::enums::task_status::QUEUED,
    Status::Accepted => crate::shared::protocol::enums::task_status::ACCEPTED,
    Status::Working => crate::shared::protocol::enums::task_status::WORKING,
    Status::Done => crate::shared::protocol::enums::task_status::DONE,
    Status::Failed => crate::shared::protocol::enums::task_status::FAILED,
    Status::Canceled => crate::shared::protocol::enums::task_status::CANCELED,
});

impl Status {
    /// Whether the task reached a final state.
    /// `prune` may remove these tasks. An inbox excludes them unless requested.
    pub fn is_terminal(self) -> bool {
        matches!(self, Status::Done | Status::Failed | Status::Canceled)
    }
}

/// Resolved by the manager while the session boundary holds identity stable.
pub(crate) struct Participant {
    pub(crate) name: String,
    pub(crate) identity: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Task {
    pub id: String,
    pub from: String,
    pub to: String,
    /// Absent in legacy records. Never infer authority from a reused display name.
    #[serde(default)]
    pub(crate) from_id: String,
    #[serde(default)]
    pub(crate) to_id: String,
    pub body: String,
    pub status: Status,
    pub note: Option<String>,
    /// Optional OpenRouter summary for the compact sidebar.
    /// The task body remains the source of truth.
    /// Older task files can omit this field.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub summary: Option<String>,
    pub created_ms: u64,
    pub updated_ms: u64,
    /// Present only when a task owns a worker that the daemon started.
    /// The task body remains the source of truth.
    /// The daemon gives the worker this task ID at startup.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub worker: Option<WorkerTask>,
}

impl Task {
    fn visible_to(&self, identity: &str) -> bool {
        Self::owns(&self.from_id, &self.from, identity) || self.recipient_is(identity)
    }

    fn recipient_is(&self, identity: &str) -> bool {
        Self::owns(&self.to_id, &self.to, identity)
    }

    fn owns(recorded: &str, name: &str, identity: &str) -> bool {
        (!recorded.is_empty() && recorded == identity)
            || (recorded.is_empty() && name == HOST && identity == HOST)
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct WorkerTask {
    pub session: String,
    pub parent: String,
    /// Persistent workers remain in config.toml after their process exits. One-shot workers do not.
    #[serde(default)]
    pub durable: bool,
}

#[derive(Default, Serialize, Deserialize)]
struct File {
    #[serde(default)]
    generation: u64,
    #[serde(default)]
    tasks: Vec<Task>,
}

#[derive(Serialize, Deserialize)]
struct JournalEntry {
    generation: u64,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    create: Option<Task>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    update: Option<TaskUpdate>,
}

#[derive(Serialize, Deserialize)]
struct TaskUpdate {
    id: String,
    status: Status,
    note: Option<String>,
    summary: Option<String>,
    updated_ms: u64,
}

// Bound replay work without putting task bodies in every progress entry. Snapshot replacement
// advances generation before cleanup, so an interrupted compaction cannot replay stale entries.
const MAX_JOURNAL_BYTES: u64 = 1024 * 1024;

pub struct Tasks {
    path: PathBuf,
    journal: PathBuf,
    file: File,
    sequence: u64,
    journal_poisoned: bool,
}

impl Tasks {
    pub fn load(config: &Path) -> Result<Self> {
        let path = config.with_file_name("tasks.toml");
        let mut file: File = match fs::read_to_string(&path) {
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
        let journal = config.with_file_name("tasks.journal");
        let mut positions: HashMap<String, usize> = file
            .tasks
            .iter()
            .enumerate()
            .map(|(index, task)| (task.id.clone(), index))
            .collect();
        // An incomplete final line can follow an interrupted write. A new snapshot
        // changes generation before journal cleanup, so old updates cannot resurrect
        // removed tasks after a crash between those two operations.
        let journal_text = match fs::read_to_string(&journal) {
            Ok(text) => Some(text),
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => None,
            Err(error) => {
                return Err(error)
                    .with_context(|| format!("reading task journal {}", journal.display()))
            }
        };
        if let Some(text) = journal_text {
            let mut valid_len = 0usize;
            for line in text.split_inclusive('\n') {
                if !line.ends_with('\n') {
                    break;
                }
                let Ok(entry) = serde_json::from_str::<JournalEntry>(line) else {
                    tracing::warn!(
                        "stopping at invalid task journal entry in {}",
                        journal.display()
                    );
                    break;
                };
                if usize::from(entry.create.is_some()) + usize::from(entry.update.is_some()) != 1 {
                    tracing::warn!(
                        "stopping at unsupported task journal entry in {}",
                        journal.display()
                    );
                    break;
                }
                valid_len += line.len();
                if entry.generation != file.generation {
                    continue;
                }
                if let Some(task) = entry.create {
                    if !positions.contains_key(&task.id) {
                        positions.insert(task.id.clone(), file.tasks.len());
                        file.tasks.push(task);
                    }
                }
                if let Some(update) = entry.update {
                    if let Some(&index) = positions.get(&update.id) {
                        let task = &mut file.tasks[index];
                        task.status = update.status;
                        task.note = update.note;
                        task.summary = update.summary;
                        task.updated_ms = update.updated_ms;
                    }
                }
            }
            if valid_len < text.len() {
                fs::OpenOptions::new()
                    .write(true)
                    .open(&journal)
                    .and_then(|file| file.set_len(valid_len as u64))
                    .with_context(|| format!("repairing task journal {}", journal.display()))?;
            }
        }
        let sequence = file
            .tasks
            .iter()
            .filter_map(|task| task.id.rsplit_once('-'))
            .filter_map(|(_, suffix)| u64::from_str_radix(suffix, 16).ok())
            .max()
            .unwrap_or(0);
        let tasks = Self {
            path,
            journal,
            file,
            sequence,
            journal_poisoned: false,
        };
        Ok(tasks)
    }

    #[cfg(test)]
    pub fn create(&mut self, from: String, to: String, body: String) -> Result<Task> {
        self.create_owned(
            Participant {
                identity: from.clone(),
                name: from,
            },
            Participant {
                identity: to.clone(),
                name: to,
            },
            body,
            None,
        )
    }

    #[cfg(test)]
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
        self.create_owned(
            Participant {
                identity: from.clone(),
                name: from,
            },
            Participant {
                identity: to.clone(),
                name: to,
            },
            body,
            worker,
        )
    }

    pub(crate) fn create_owned(
        &mut self,
        from: Participant,
        to: Participant,
        body: String,
        worker: Option<WorkerTask>,
    ) -> Result<Task> {
        if body.trim().is_empty() {
            bail!("Provide a task body.");
        }
        let now = unix_ms();
        self.sequence += 1;
        let task = Task {
            id: format!("{now:013x}-{:04x}", self.sequence),
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
        };
        self.file.tasks.push(task.clone());
        if let Err(error) = self.append_entry(JournalEntry {
            generation: self.file.generation,
            create: Some(task.clone()),
            update: None,
        }) {
            self.file.tasks.pop();
            return Err(error);
        }
        Ok(task)
    }

    /// The daemon owns worker-exit bookkeeping.
    /// Do not overwrite a worker's explicit `finish` or `fail` result.
    /// Keep a failed task after one-shot cleanup so it remains available after a daemon restart.
    pub fn fail_worker(&mut self, id: &str, note: String) -> Result<Option<Task>> {
        let Some(index) = self
            .file
            .tasks
            .iter()
            .position(|task| task.id == id && task.worker.is_some())
        else {
            return Ok(None);
        };
        let mut task = self.file.tasks[index].clone();
        if task.status.is_terminal() {
            return Ok(Some(task.clone()));
        }
        task.status = Status::Failed;
        task.note = Some(note);
        task.updated_ms = unix_ms();
        let result = task.clone();
        self.commit_update(index, task)?;
        Ok(Some(result))
    }

    pub fn visible(&self, who: &str) -> Vec<Task> {
        self.file
            .tasks
            .iter()
            .filter(|t| t.visible_to(who))
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
            .find(|t| t.id == id && (t.visible_to(who)))
            .cloned()
    }

    pub fn update(
        &mut self,
        who: &str,
        id: &str,
        status: Status,
        note: Option<String>,
    ) -> Result<Task> {
        let index = self
            .file
            .tasks
            .iter()
            .position(|t| t.id == id)
            .with_context(|| format!("Task {id} does not exist."))?;
        let mut task = self.file.tasks[index].clone();
        if !task.recipient_is(who) {
            bail!("Only the recipient can update this task.");
        }
        if task.status == Status::Canceled {
            bail!("Task {id} is canceled. You cannot update it.");
        }
        task.status = status;
        task.note = note;
        task.updated_ms = unix_ms();
        let result = task.clone();
        self.commit_update(index, task)?;
        Ok(result)
    }

    pub fn set_summary(&mut self, id: &str, summary: String) -> Result<Option<Task>> {
        let Some(index) = self.file.tasks.iter().position(|task| task.id == id) else {
            return Ok(None);
        };
        let mut task = self.file.tasks[index].clone();
        let summary = summary.trim().to_string();
        if summary.is_empty() {
            return Ok(Some(task.clone()));
        }
        task.summary = Some(summary);
        let result = task.clone();
        self.commit_update(index, task)?;
        Ok(Some(result))
    }

    /// Cancel queued or accepted work. The recipient may cancel its own task.
    /// The root token can cancel any queued or accepted task.
    /// This lets the host task board cancel work before the recipient starts it.
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
                .with_context(|| format!("Task {id} does not exist."))?;
            if !force && !task.recipient_is(who) {
                bail!("Only the recipient can cancel task {id}.");
            }
            if !matches!(task.status, Status::Queued | Status::Accepted) {
                bail!("Cancel task {id} only when it is queued or accepted.");
            }
        }

        let now = unix_ms();
        let mut canceled = Vec::new();
        let mut candidate = self.file.tasks.clone();
        for task in &mut candidate {
            if wanted.contains(&task.id) {
                task.status = Status::Canceled;
                task.updated_ms = now;
                canceled.push(task.clone());
            }
        }
        self.save_tasks(candidate)?;
        Ok(canceled)
    }

    /// Remove one task. The store keeps one copy for both participants.
    /// Participants can remove only terminal tasks.
    /// The root token can also remove an unfinished task.
    pub fn remove(&mut self, who: &str, id: &str, force: bool) -> Result<Task> {
        let at = self
            .file
            .tasks
            .iter()
            .position(|t| t.id == id && (force || t.visible_to(who)))
            .with_context(|| format!("Task {id} does not exist."))?;
        if !force && !self.file.tasks[at].status.is_terminal() {
            bail!("Task {id} is unfinished. Finish, fail, or cancel it before you remove it.");
        }
        let mut candidate = self.file.tasks.clone();
        let task = candidate.remove(at);
        self.save_tasks(candidate)?;
        Ok(task)
    }

    /// Remove several tasks in one saved change.
    /// Check every ID before you remove any task.
    /// This prevents a stale selection from causing a partial removal.
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
                .find(|t| t.id == *id && (force || t.visible_to(who)))
                .with_context(|| format!("Task {id} does not exist."))?;
            if !force && !task.status.is_terminal() {
                bail!("Task {id} is unfinished. Finish, fail, or cancel it before you remove it.");
            }
        }

        let before = self.file.tasks.len();
        let candidate: Vec<_> = self
            .file
            .tasks
            .iter()
            .filter(|task| !wanted.contains(&task.id))
            .cloned()
            .collect();
        let removed = before - candidate.len();
        if removed > 0 {
            self.save_tasks(candidate)?;
        }
        Ok(removed)
    }

    /// Remove every finished task visible to the caller.
    /// The root token can set `all` to include tasks from every participant.
    /// This keeps the store and inbox from growing indefinitely.
    pub fn prune(&mut self, who: &str, all: bool) -> Result<usize> {
        let before = self.file.tasks.len();
        let candidate: Vec<_> = self
            .file
            .tasks
            .iter()
            .filter(|t| !(t.status.is_terminal() && (all || t.visible_to(who))))
            .cloned()
            .collect();
        let removed = before - candidate.len();
        if removed > 0 {
            self.save_tasks(candidate)?;
        }
        Ok(removed)
    }

    fn commit_update(&mut self, index: usize, candidate: Task) -> Result<()> {
        let previous = std::mem::replace(&mut self.file.tasks[index], candidate.clone());
        if let Err(error) = self.append_update(&candidate) {
            self.file.tasks[index] = previous;
            return Err(error);
        }
        Ok(())
    }

    fn append_update(&mut self, task: &Task) -> Result<()> {
        self.append_entry(JournalEntry {
            generation: self.file.generation,
            create: None,
            update: Some(TaskUpdate {
                id: task.id.clone(),
                status: task.status,
                note: task.note.clone(),
                summary: task.summary.clone(),
                updated_ms: task.updated_ms,
            }),
        })
    }

    fn append_entry(&mut self, entry: JournalEntry) -> Result<()> {
        if self.journal_poisoned {
            bail!("task journal needs repair before further appends");
        }
        let mut line = serde_json::to_vec(&entry)?;
        line.push(b'\n');
        let open = || {
            fs::OpenOptions::new()
                .create(true)
                .append(true)
                .mode(0o600)
                .open(&self.journal)
        };
        let mut file = match open() {
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => {
                if let Some(parent) = self.journal.parent() {
                    fs::create_dir_all(parent)?;
                }
                open()?
            }
            result => result?,
        };
        let before = file.metadata()?.len();
        if let Err(error) = file.write_all(&line) {
            if let Err(rollback) = file.set_len(before) {
                self.journal_poisoned = true;
                return Err(error)
                    .context(format!("task journal rollback also failed: {rollback}"));
            }
            return Err(error.into());
        }
        if before + line.len() as u64 >= MAX_JOURNAL_BYTES {
            // The append already succeeded. Failure to compact must not turn a committed
            // mutation into an API failure. Retry compaction after the next append.
            if let Err(error) = self.save() {
                tracing::warn!(%error, "could not compact task journal");
            }
        }
        Ok(())
    }

    fn save(&mut self) -> Result<()> {
        self.save_tasks(self.file.tasks.clone())
    }

    /// Atomic replacement is the snapshot commit point. Failed writes leave memory unchanged.
    fn save_tasks(&mut self, tasks: Vec<Task>) -> Result<()> {
        let candidate = File {
            generation: self.file.generation.wrapping_add(1),
            tasks,
        };
        let text = toml::to_string_pretty(&candidate)?;
        crate::paths::write_private_toml(&self.path, &text)?;
        self.file = candidate;
        match fs::remove_file(&self.journal) {
            Ok(()) => self.journal_poisoned = false,
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => {
                self.journal_poisoned = false;
            }
            Err(error) => {
                // A committed snapshot does not repair a damaged journal suffix.
                // Preserve poisoning until cleanup succeeds; snapshot commits remain valid.
                tracing::warn!(
                    "could not clear task journal {}: {error}",
                    self.journal.display()
                );
            }
        }
        Ok(())
    }
}

#[cfg(test)]
#[path = "tasks_tests.rs"]
mod tests;
