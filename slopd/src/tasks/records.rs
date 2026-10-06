//! Full task records with exclusive creation, atomic replacement and targeted
//! retirement. Runs only on the blocking owner. No journal, polling or disk index.
use super::Task;
use anyhow::{Context, Result, ensure};
use std::{
    collections::HashMap,
    fs,
    path::{Path, PathBuf},
};

pub(super) struct Records {
    directory: PathBuf,
    documents: HashMap<String, toml::Value>,
}
impl Records {
    pub(super) fn load(data: &Path) -> Result<(Self, Vec<(u64, Task)>)> {
        let directory = crate::paths::normalize(data)?.join("tasks");
        ensure!(
            crate::paths::normalize(&directory)? == directory,
            "task directory aliases another path"
        );
        let mut store = Self {
            directory,
            documents: HashMap::new(),
        };
        let entries = match fs::read_dir(&store.directory) {
            Ok(entries) => entries,
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => {
                return Ok((store, Vec::new()));
            }
            Err(error) => return Err(error).context("reading task records"),
        };
        let mut paths = entries
            .map(|entry| entry.map(|entry| entry.path()))
            .collect::<std::io::Result<Vec<_>>>()?;
        paths.retain(|path| path.extension().is_some_and(|ext| ext == "toml"));
        paths.sort();
        let mut values = Vec::new();
        for path in paths {
            let id = path
                .file_stem()
                .and_then(|s| s.to_str())
                .context("invalid task filename")?;
            ensure!(store.path(id)? == path, "invalid task target");
            let raw: toml::Value = toml::from_str(&fs::read_to_string(&path)?)
                .with_context(|| format!("decoding {}", path.display()))?;
            let task: Task = raw.clone().try_into()?;
            ensure!(task.id == id, "task filename/content identity mismatch");
            ensure!(
                !task.from.trim().is_empty()
                    && !task.to.trim().is_empty()
                    && !task.body.trim().is_empty(),
                "invalid task participants or body"
            );
            let order = raw
                .get("storage_order")
                .and_then(toml::Value::as_integer)
                .context("missing task storage_order")?;
            ensure!((0..i64::MAX).contains(&order), "invalid task storage_order");
            values.push((u64::try_from(order)?, task));
            store.documents.insert(id.into(), raw);
        }
        Ok((store, values))
    }
    fn path(&self, id: &str) -> Result<PathBuf> {
        ensure!(crate::storage_id::valid_task(id), "invalid task identity");
        let path = self.directory.join(format!("{id}.toml"));
        ensure!(
            crate::paths::normalize(&path)? == path,
            "task record aliases another path"
        );
        Ok(path)
    }
    pub(super) fn occupied(&self, id: &str) -> Result<bool> {
        ensure!(crate::storage_id::valid_task(id), "invalid task identity");
        ensure!(
            crate::paths::normalize(&self.directory)? == self.directory,
            "task directory aliases another path"
        );
        // Every occupied directory entry reserves the ID, including a dangling
        // symlink. Creation/replacement still validates its exact destination.
        match fs::symlink_metadata(self.directory.join(format!("{id}.toml"))) {
            Ok(_) => Ok(true),
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(false),
            Err(error) => Err(error.into()),
        }
    }
    pub(super) fn put(&mut self, task: &Task, order: u64, exists: bool) -> Result<()> {
        let mut raw = toml::Value::try_from(task)?;
        raw.as_table_mut()
            .context("task must be a table")?
            .insert("storage_order".into(), i64::try_from(order)?.into());
        if let Some(old) = self.documents.get(&task.id) {
            if let Some(worker) = raw.get_mut("worker") {
                preserve(worker, old.get("worker"), &["session", "parent", "durable"]);
            }
            preserve(
                &mut raw,
                Some(old),
                &[
                    "id",
                    "from",
                    "to",
                    "from_id",
                    "to_id",
                    "body",
                    "status",
                    "note",
                    "summary",
                    "created_ms",
                    "updated_ms",
                    "worker",
                    "storage_order",
                ],
            );
        }
        let text = toml::to_string_pretty(&raw)?;
        let path = self.path(&task.id)?;
        if exists {
            crate::paths::write_private_toml(&path, &text)?;
        } else {
            crate::paths::create_atomic(&path, &text, Some(0o600))?;
        }
        self.documents.insert(task.id.clone(), raw);
        Ok(())
    }
    pub(super) fn retire(&mut self, id: &str) -> Result<()> {
        match fs::remove_file(self.path(id)?) {
            Ok(()) => {}
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => {}
            Err(error) => return Err(error.into()),
        }
        self.documents.remove(id);
        Ok(())
    }
}
fn preserve(next: &mut toml::Value, old: Option<&toml::Value>, known: &[&str]) {
    if let (Some(next), Some(old)) = (next.as_table_mut(), old.and_then(toml::Value::as_table)) {
        for (key, value) in old {
            if !known.contains(&key.as_str()) {
                next.insert(key.clone(), value.clone());
            }
        }
    }
}
