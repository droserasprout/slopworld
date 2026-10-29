//! Serialized, revision-checked TODO.txt persistence. Unknown text survives round trips.
use anyhow::{Context, Result, bail};
use serde::Serialize;
use std::{collections::HashSet, path::PathBuf};
use tokio::{io::AsyncReadExt, sync::Mutex};

pub(crate) const MAX_BYTES: usize = 64 * 1024;
pub(crate) struct Store {
    path: PathBuf,
    pub(super) gate: Mutex<()>,
}
#[derive(Debug, Serialize)]
pub(crate) struct Document {
    pub text: String,
    pub revision: String,
    pub path: String,
    pub items: Vec<Item>,
}
#[derive(Debug, Serialize)]
pub(crate) struct Item {
    pub line: u32,
    pub id: String,
    pub text: String,
    pub task_id: String,
    pub status: String,
}
impl Store {
    pub(crate) fn new(path: PathBuf) -> Self {
        Self {
            path,
            gate: Mutex::new(()),
        }
    }
    pub(crate) async fn read(&self) -> Result<Document> {
        let _gate = self.gate.lock().await;
        self.document(self.read_text().await?)
    }
    pub(crate) async fn save(&self, text: String, expected: String) -> Result<Document> {
        let _gate = self.gate.lock().await;
        check_revision(&self.read_text().await?, &expected)?;
        let text = assign_ids(&text)?;
        self.write_text(&text).await?;
        self.document(text)
    }
    fn document(&self, text: String) -> Result<Document> {
        let items = items(&text)?;
        Ok(Document {
            revision: revision(&text),
            text,
            items,
            path: self.path.to_string_lossy().into(),
        })
    }
    pub(super) async fn read_text(&self) -> Result<String> {
        let file = match tokio::fs::File::open(&self.path).await {
            Ok(file) => file,
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => return Ok(String::new()),
            Err(error) => return Err(error.into()),
        };
        let mut text = String::new();
        file.take((MAX_BYTES + 1) as u64)
            .read_to_string(&mut text)
            .await?;
        validate_size(&text)?;
        Ok(text)
    }
    pub(super) async fn write_text(&self, text: &str) -> Result<()> {
        validate_size(text)?;
        crate::paths::write_atomic_async(&self.path, text, Some(0o600)).await
    }
}
fn validate_size(text: &str) -> Result<()> {
    if text.len() > MAX_BYTES {
        bail!("Planner documents are limited to 64 KiB.");
    }
    if text.contains('\0') {
        bail!("TODO.txt cannot contain NUL bytes.");
    }
    Ok(())
}
fn revision(text: &str) -> String {
    ring::digest::digest(&ring::digest::SHA256, text.as_bytes())
        .as_ref()
        .iter()
        .map(|byte| format!("{byte:02x}"))
        .collect()
}
pub(super) fn check_revision(text: &str, expected: &str) -> Result<()> {
    if revision(text) != expected {
        bail!("TODO.txt changed. Copy your draft, then reload before saving or executing.");
    }
    Ok(())
}
fn field<'a>(line: &'a str, key: &str) -> Result<&'a str> {
    let mut values = line
        .split_whitespace()
        .filter_map(|word| word.strip_prefix(key));
    let first = values.next();
    if first == Some("") {
        bail!("Empty {key} metadata.");
    }
    let value = first.unwrap_or_default();
    if values.next().is_some() || value.contains(':') {
        bail!("Invalid or duplicate {key} metadata.");
    }
    Ok(value)
}
fn items(text: &str) -> Result<Vec<Item>> {
    validate_size(text)?;
    let mut ids = HashSet::new();
    text.lines()
        .enumerate()
        .filter(|(_, line)| !line.trim().is_empty())
        .map(|(line, text)| {
            let id = field(text, "sw-id:")?;
            if !id.is_empty() && (uuid::Uuid::parse_str(id).is_err() || !ids.insert(id)) {
                bail!("Each sw-id must be a unique UUID. Remove copied IDs before saving.");
            }
            Ok(Item {
                line: u32::try_from(line).context("Too many task lines")?,
                id: id.into(),
                text: text.into(),
                task_id: field(text, "sw-task:")?.into(),
                status: if text.starts_with("x ") {
                    "complete"
                } else {
                    ""
                }
                .into(),
            })
        })
        .collect()
}
fn assign_ids(text: &str) -> Result<String> {
    items(text)?;
    let mut output = String::new();
    for segment in text.split_inclusive('\n') {
        let line = segment.trim_end_matches(['\r', '\n']);
        output.push_str(line);
        if !line.trim().is_empty() && field(line, "sw-id:")?.is_empty() {
            output.push_str(&format!(" sw-id:{}", uuid::Uuid::new_v4()));
        }
        output.push_str(segment.get(line.len()..).unwrap_or_default());
    }
    validate_size(&output)?;
    Ok(output)
}
pub(super) fn selected(text: &str, line: u32) -> Result<Item> {
    let item = items(text)?
        .into_iter()
        .find(|item| item.line == line)
        .context("Select a task line.")?;
    if item.text.starts_with("x ") {
        bail!("Completed TODO items cannot be executed.");
    }
    if item.id.is_empty() {
        bail!("Save the document before executing.");
    }
    Ok(item)
}
pub(super) fn link(text: &str, line: u32, task: &str) -> Result<String> {
    let mut output = String::new();
    for (index, segment) in text.split_inclusive('\n').enumerate() {
        if index != line as usize {
            output.push_str(segment);
            continue;
        }
        let body = segment.trim_end_matches(['\r', '\n']);
        let old = field(body, "sw-task:")?;
        if old.is_empty() {
            output.push_str(body);
            output.push_str(&format!(" sw-task:{task}"));
        } else {
            let token = format!("sw-task:{old}");
            let start = body
                .match_indices(&token)
                .find(|(start, _)| {
                    body.get(..*start).is_some_and(|prefix| {
                        prefix.is_empty() || prefix.ends_with(char::is_whitespace)
                    })
                })
                .map(|(start, _)| start)
                .context("Missing task metadata")?;
            output.push_str(body.get(..start).context("Invalid metadata offset")?);
            output.push_str(&format!("sw-task:{task}"));
            output.push_str(
                body.get(start + token.len()..)
                    .context("Invalid metadata end")?,
            );
        }
        output.push_str(segment.get(body.len()..).unwrap_or_default());
    }
    validate_size(&output)?;
    Ok(output)
}
pub(super) fn marker(id: &str) -> String {
    format!("SlopWorld TODO item: {id}")
}
impl Document {
    pub(crate) fn with_tasks(mut self, tasks: &[crate::tasks::Task]) -> Self {
        for item in &mut self.items {
            let marker = format!("{}\n", marker(&item.id));
            let task = tasks.iter().find(|task| {
                task.from == crate::tasks::HOST
                    && task.worker.is_some()
                    && (task.id == item.task_id
                        || (!item.id.is_empty() && task.body.starts_with(&marker)))
            });
            if let Some(task) = task {
                item.task_id = task.id.clone();
                item.status = format!("{:?}", task.status).to_lowercase();
            } else if !item.task_id.is_empty() {
                item.status = "dispatch reserved; inspect Tasks (no automatic retry)".into();
            }
        }
        self
    }
}
#[cfg(test)]
#[path = "store_tests.rs"]
mod tests;
