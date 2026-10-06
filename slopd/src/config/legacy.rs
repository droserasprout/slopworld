//! Temporary persistence adapter for the selected inline workspace layout.
//! Remove at storage cutover (priv/notes/plan-storage-main.md). Runtime Config is
//! an assembled view; only this adapter may persist its legacy representation.

use super::{
    Config,
    catalog::prepare_library,
    persistence::{preserve_unknown_fields, reject_removed_worktree_fields},
};
use anyhow::{Context, Result};
use std::path::Path;

// Only initial creation and test fixtures select both stores. Mutation callers
// choose destinations before preparation; committing never discovers more files.
pub(crate) async fn save(cfg: &Config, path: &Path) -> Result<()> {
    prepare(cfg, path, true, true).await?.commit(path).await
}

pub(crate) struct PreparedWrite {
    changes: std::collections::BTreeMap<std::path::PathBuf, Option<String>>,
}

impl PreparedWrite {
    pub(crate) fn root_text(&self, path: &Path) -> Option<&str> {
        self.changes.get(path).and_then(|text| text.as_deref())
    }

    pub(crate) async fn commit(&self, path: &Path) -> Result<()> {
        super::transaction::save(path, self.changes.clone()).await
    }
}

pub(crate) async fn recover(path: &Path) -> Result<()> {
    super::transaction::recover(path).await
}

pub(crate) async fn prepare(
    cfg: &Config,
    path: &Path,
    root: bool,
    library: bool,
) -> Result<PreparedWrite> {
    super::validation::validate_loaded(cfg)?;
    recover(path).await?;
    let mut changes = std::collections::BTreeMap::new();
    if library {
        let dirs = Config::library_dirs_for(path);
        let catalog = prepare_library(&dirs, &cfg.library)?;
        changes = super::catalog::replacement_changes(&dirs, catalog).await?;
    }
    if root {
        let mut document = toml::Value::try_from(cfg)?;
        if tokio::fs::try_exists(path).await? {
            let text = tokio::fs::read_to_string(path)
                .await
                .with_context(|| format!("reading existing configuration {}", path.display()))?;
            let previous: toml::Value = toml::from_str(&text)
                .with_context(|| format!("parsing existing configuration {}", path.display()))?;
            reject_removed_worktree_fields(&previous)?;
            let previous_config: Config = previous
                .clone()
                .try_into()
                .context("reading modeled fields before preserving unknown configuration")?;
            let previous_modeled = toml::Value::try_from(previous_config)?;
            preserve_unknown_fields(&mut document, &previous, Some(&previous_modeled));
        }
        if let Some(table) = document.as_table_mut() {
            // Library entries have a separate catalog.
            // Do not serialize the in-memory catalog into the main configuration document.
            table.remove("library");
        }
        let text = toml::to_string_pretty(&document)?;
        changes.insert(path.to_owned(), Some(text));
    }
    Ok(PreparedWrite { changes })
}
