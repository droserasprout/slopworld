use std::fs;
use std::path::{Path, PathBuf};
use std::time::{SystemTime, UNIX_EPOCH};

use anyhow::{bail, Context, Result};
use serde::{Deserialize, Serialize};

/// The user at the keyboard. Not a session and never one: `slopctl` run from the host states it
/// as its identity, and the daemon accepts it only from the root token, so a grant cannot wear it
/// however its grantor happens to be named.
pub const HOST: &str = "host";

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum Status {
    Queued,
    Accepted,
    Working,
    Done,
    Failed,
}

impl Status {
    /// Where a task stops moving. What `prune` may drop, and what an inbox leaves out until asked.
    pub fn is_terminal(self) -> bool {
        matches!(self, Status::Done | Status::Failed)
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
    pub created_ms: u64,
    pub updated_ms: u64,
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
        let path = config.with_file_name("tasks.json");
        let file = match fs::read_to_string(&path) {
            Ok(s) => match serde_json::from_str(&s) {
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
        Ok(Self {
            path,
            file,
            sequence,
        })
    }

    pub fn create(&mut self, from: String, to: String, body: String) -> Result<Task> {
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
            created_ms: now,
            updated_ms: now,
        };
        self.file.tasks.push(task.clone());
        self.save()?;
        Ok(task)
    }

    pub fn visible(&self, who: &str) -> Vec<Task> {
        self.file
            .tasks
            .iter()
            .filter(|t| t.from == who || t.to == who)
            .cloned()
            .collect()
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
        task.status = status;
        task.note = note;
        task.updated_ms = now_ms();
        let result = task.clone();
        self.save()?;
        Ok(result)
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
        if let Some(parent) = self.path.parent() {
            fs::create_dir_all(parent)?;
        }
        let tmp = self.path.with_extension("json.tmp");
        fs::write(&tmp, serde_json::to_vec_pretty(&self.file)?)?;
        fs::rename(&tmp, &self.path).with_context(|| format!("installing {}", self.path.display()))
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

        assert!(s.remove("eve", &over.id, false).is_err()); // not a participant
        assert!(s.remove("alice", &live.id, false).is_err()); // still in flight
        assert!(s.remove("alice", &live.id, true).is_ok()); // the root reaches it
        assert!(s.remove("alice", &over.id, false).is_ok()); // finished, so either side may

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
        fs::write(dir.join("tasks.json"), "{").unwrap();
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
}
