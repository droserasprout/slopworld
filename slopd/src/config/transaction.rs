//! Test-only bridge for historical config/catalog fixtures to permanent transactions.
//! No retired journal format is read or written here.

use std::collections::BTreeMap;
use std::path::{Path, PathBuf};

use crate::storage::{
    target::{StorageBinding, Target},
    transaction::{self, Change, Mutation},
};
use anyhow::Result;

fn binding(path: &Path) -> Result<StorageBinding> {
    let root = path
        .parent()
        .filter(|p| !p.as_os_str().is_empty())
        .unwrap_or(Path::new("."));
    StorageBinding::new(root, &root.join("data"), path)
}

pub(super) async fn recover(path: &Path) -> Result<()> {
    let gate = std::sync::Arc::new(tokio::sync::Mutex::new(()))
        .lock_owned()
        .await;
    transaction::recover(&binding(path)?, &gate).await
}

pub(super) async fn save(path: &Path, changes: BTreeMap<PathBuf, Option<String>>) -> Result<()> {
    let binding = binding(path)?;
    let changes = changes
        .into_iter()
        .map(|(path, text)| {
            let path = crate::paths::normalize(&path)?;
            let target = if path == binding.settings {
                Target::Settings
            } else {
                Target::Config(path.strip_prefix(&binding.config)?.to_owned())
            };
            Ok(Change {
                target,
                mutation: text.map_or(Mutation::Retire, Mutation::Replace),
            })
        })
        .collect::<Result<Vec<_>>>()?;
    let gate = std::sync::Arc::new(tokio::sync::Mutex::new(()))
        .lock_owned()
        .await;
    transaction::commit_in_operation(&binding, changes, &gate).await
}
