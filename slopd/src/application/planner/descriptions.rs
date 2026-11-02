//! Markdown sidecars keyed by saved todo IDs. The backlog store's gate serializes
//! membership checks and writes; removing a task never deletes its description.
use super::{Store, items, revision};
use anyhow::{Context, Result, ensure};
use serde::Serialize;
use std::path::PathBuf;
use tokio::io::AsyncReadExt;

const MAX_DESCRIPTION_BYTES: usize = 1024 * 1024;

#[derive(Debug, Serialize)]
pub(crate) struct Description {
    pub text: String,
    pub revision: String,
}

impl Store {
    async fn description_path(&self, id: &str) -> Result<PathBuf> {
        uuid::Uuid::parse_str(id).context("Invalid Planner task ID")?;
        ensure!(
            items(&self.read_text().await?)?
                .iter()
                .any(|item| item.id == id),
            "Planner task no longer exists. Refresh the backlog."
        );
        Ok(self
            .path
            .parent()
            .context("Planner data root is missing")?
            .join("planner/descriptions")
            .join(format!("{id}.md")))
    }

    pub(crate) async fn read_description(&self, id: &str) -> Result<Description> {
        let _gate = self.gate.lock().await;
        let path = self.description_path(id).await?;
        description(read_text(&path).await?)
    }

    pub(crate) async fn save_description(
        &self,
        id: &str,
        text: String,
        expected: String,
    ) -> Result<Description> {
        let _gate = self.gate.lock().await;
        let path = self.description_path(id).await?;
        ensure!(
            revision(&read_text(&path).await?) == expected,
            "Description changed. Copy your draft, then reload before saving."
        );
        let result = description(text)?;
        crate::paths::write_atomic_async(&path, &result.text, Some(0o600)).await?;
        Ok(result)
    }
}

fn description(text: String) -> Result<Description> {
    ensure!(
        text.len() <= MAX_DESCRIPTION_BYTES,
        "Planner descriptions are limited to 1 MiB."
    );
    ensure!(
        !text.contains('\0'),
        "Planner descriptions cannot contain NUL bytes."
    );
    Ok(Description {
        revision: revision(&text),
        text,
    })
}

async fn read_text(path: &std::path::Path) -> Result<String> {
    let file = match tokio::fs::File::open(path).await {
        Ok(file) => file,
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => return Ok(String::new()),
        Err(error) => return Err(error.into()),
    };
    let mut text = String::new();
    file.take((MAX_DESCRIPTION_BYTES + 1) as u64)
        .read_to_string(&mut text)
        .await?;
    Ok(description(text)?.text)
}

#[cfg(test)]
#[path = "descriptions_tests.rs"]
mod tests;
