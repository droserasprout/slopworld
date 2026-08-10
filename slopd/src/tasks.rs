use std::fs;
use std::path::{Path, PathBuf};
use std::time::{SystemTime, UNIX_EPOCH};

use anyhow::{bail, Context, Result};
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum Status {
    Queued,
    Accepted,
    Working,
    Done,
    Failed,
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
            Ok(s) => {
                serde_json::from_str(&s).with_context(|| format!("parsing {}", path.display()))?
            }
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => File::default(),
            Err(e) => return Err(e).with_context(|| format!("reading {}", path.display())),
        };
        Ok(Self {
            path,
            file,
            sequence: 0,
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
}
