//! Explicit Planner associations. An existing file, including an empty list, overrides
//! legacy task markers. Worker links use stable identities, never session names.
use super::{Document, Item, Store, items, marker, revision};
use crate::tasks::Task;
use anyhow::{Context, Result, bail, ensure};
use serde::{Deserialize, Serialize};
use std::path::PathBuf;
use tokio::io::AsyncReadExt;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub(crate) enum LinkKind {
    Task,
    Worker,
}

impl LinkKind {
    pub(crate) fn parse(value: &str) -> Result<Self> {
        match value {
            "task" => Ok(Self::Task),
            "worker" => Ok(Self::Worker),
            _ => bail!("Unknown Planner link kind."),
        }
    }
}

/// Persisted association and its fallback label; live status belongs to projection.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub(crate) struct LinkReference {
    pub kind: LinkKind,
    pub id: String,
    #[serde(default)]
    pub label: String,
}

#[derive(Debug, Clone, Serialize)]
pub(crate) struct Link {
    #[serde(flatten)]
    pub reference: LinkReference,
    pub status: String,
}

impl Store {
    fn links_path(&self, id: &str) -> Result<PathBuf> {
        uuid::Uuid::parse_str(id).context("Invalid Planner task ID")?;
        Ok(self
            .path
            .parent()
            .context("Planner data root is missing")?
            .join("planner/links")
            .join(format!("{id}.json")))
    }

    pub(crate) async fn require_item(&self, id: &str) -> Result<()> {
        let _gate = self.gate.lock().await;
        self.item(id).await.map(|_| ())
    }

    async fn item(&self, id: &str) -> Result<Item> {
        self.links_path(id)?;
        items(&self.read_text().await?)?
            .into_iter()
            .find(|item| item.id == id)
            .context("Planner task no longer exists. Refresh the backlog.")
    }

    async fn load_links(
        &self,
        item: &Item,
        tasks: &[Task],
    ) -> Result<(Vec<LinkReference>, String)> {
        let path = self.links_path(&item.id)?;
        let text = match tokio::fs::File::open(path).await {
            Ok(file) => {
                let mut text = String::new();
                file.take(65537).read_to_string(&mut text).await?;
                ensure!(text.len() <= 65536, "Planner links exceed 64 KiB.");
                Some(text)
            }
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => None,
            Err(error) => return Err(error.into()),
        };
        match text {
            Some(text) => Ok((serde_json::from_str(&text)?, revision(&text))),
            None => {
                let mut links = Vec::new();
                let prefix = format!("{}\n", marker(&item.id));
                for task in tasks.iter().filter(|task| {
                    task.id == item.task_id
                        || (task.from == crate::tasks::HOST
                            && task.worker.is_some()
                            && task.body.starts_with(&prefix))
                }) {
                    append_worker(&mut links, task);
                }
                // Retain a missing legacy reference so the operator can remove it.
                if links.is_empty() && !item.task_id.is_empty() {
                    links.push(LinkReference {
                        kind: LinkKind::Task,
                        id: item.task_id.clone(),
                        label: item.task_id.clone(),
                    });
                }
                let revision = revision(&serde_json::to_string(&links)?);
                Ok((links, format!("implicit:{revision}")))
            }
        }
    }

    async fn write_links(&self, id: &str, links: &[LinkReference]) -> Result<()> {
        let text = serde_json::to_string(links)?;
        ensure!(text.len() <= 65536, "Planner links exceed 64 KiB.");
        crate::paths::write_atomic_async(&self.links_path(id)?, &text, Some(0o600)).await
    }

    pub(crate) async fn save_links(
        &self,
        id: &str,
        links: Vec<LinkReference>,
        expected: &str,
        tasks: &[Task],
        workers: &[String],
    ) -> Result<Document> {
        let _gate = self.gate.lock().await;
        let item = self.item(id).await?;
        let (previous, current) = self.load_links(&item, tasks).await?;
        ensure!(
            current == expected,
            "Planner links changed. Refresh before editing links."
        );
        let mut unique = std::collections::HashSet::new();
        for link in &links {
            ensure!(
                unique.insert((&link.kind, &link.id)),
                "Duplicate Planner link."
            );
            ensure!(
                previous
                    .iter()
                    .any(|old| old.kind == link.kind && old.id == link.id)
                    || (link.kind == LinkKind::Worker && workers.contains(&link.id))
                    || tasks.iter().any(|task| if link.kind == LinkKind::Task {
                        task.id == link.id
                    } else {
                        task.worker.is_some() && task.to_id == link.id
                    }),
                "Linked item no longer exists. Refresh Tasks and try again."
            );
        }
        self.write_links(id, &links).await?;
        self.document(self.read_text().await?)
    }

    pub(crate) async fn link_worker(&self, id: &str, task: &Task, tasks: &[Task]) -> Result<()> {
        let _gate = self.gate.lock().await;
        let item = self.item(id).await?;
        let (mut links, _) = self.load_links(&item, tasks).await?;
        append_worker(&mut links, task);
        self.write_links(id, &links).await
    }

    pub(crate) async fn project_links(
        &self,
        mut document: Document,
        tasks: &[Task],
    ) -> Result<Document> {
        let _gate = self.gate.lock().await;
        for item in &mut document.items {
            if item.id.is_empty() {
                continue;
            }
            let (links, revision) = self.load_links(item, tasks).await?;
            // Task outcomes belong to links, not the user's Planner completion state.
            item.task_id.clear();
            item.status = if item.text.starts_with("x ") {
                "complete"
            } else {
                ""
            }
            .into();
            item.links = links
                .into_iter()
                .map(|reference| {
                    let status = match reference.kind {
                        LinkKind::Task => tasks
                            .iter()
                            .find(|task| task.id == reference.id)
                            .map_or_else(
                                || "unavailable".into(),
                                |task| format!("{:?}", task.status).to_lowercase(),
                            ),
                        LinkKind::Worker => String::new(),
                    };
                    Link { reference, status }
                })
                .collect();
            item.links_revision = revision;
        }
        Ok(document)
    }
}

fn append_worker(links: &mut Vec<LinkReference>, task: &Task) {
    let mut add = |kind: LinkKind, id: &str, label: &str| {
        if !links.iter().any(|link| link.kind == kind && link.id == id) {
            links.push(LinkReference {
                kind,
                id: id.into(),
                label: label.into(),
            });
        }
    };
    add(LinkKind::Task, &task.id, &task.id);
    if let Some(worker) = &task.worker
        && !task.to_id.is_empty()
    {
        add(LinkKind::Worker, &task.to_id, &worker.session);
    }
}

#[cfg(test)]
#[path = "links_tests.rs"]
mod tests;
