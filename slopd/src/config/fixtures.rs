//! Test fixture assembly using production record documents and transactions.
//! Manager edits always use their accepted stores, never this offline seed helper.

use super::Config;
use crate::storage::{
    target::{StorageBinding, Target},
    transaction::{self, Change, Mutation},
    workspace,
};
use anyhow::{Context, Result};
use std::path::Path;

pub(crate) fn binding(path: &Path) -> Result<StorageBinding> {
    let root = path.parent().context("fixture root")?;
    StorageBinding::new(root, &root.join("data"), path)
}

pub(crate) async fn save(cfg: &Config, path: &Path) -> Result<()> {
    let binding = binding(path)?;
    let gate = std::sync::Arc::new(tokio::sync::Mutex::new(()))
        .lock_owned()
        .await;
    transaction::recover(&binding, &gate).await?;
    let current = match tokio::fs::read_to_string(path).await {
        Ok(text) => text,
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => String::new(),
        Err(error) => return Err(error.into()),
    };
    let settings =
        super::settings::document::patch(cfg, &current, toml::Value::try_from(&cfg.settings)?)?;
    let mut changes = vec![Change {
        target: Target::Settings,
        mutation: Mutation::Replace(settings.text),
    }];
    let mut projects = cfg.projects.clone();
    for project in &mut projects {
        if project.id.is_empty() {
            project.id = crate::storage_id::draft_identity();
        }
    }
    changes.extend(
        workspace::Store::load(&binding)
            .await?
            .prepare(projects)?
            .changes,
    );
    changes.extend(
        workspace::Store::load(&binding)
            .await?
            .prepare(cfg.sessions.clone())?
            .changes,
    );
    changes.extend(
        workspace::Store::load(&binding)
            .await?
            .prepare(cfg.host_terminals.clone())?
            .changes,
    );

    let dirs = Config::library_dirs_for(path);
    for (path, text) in super::catalog::replacement_changes(
        &dirs,
        super::catalog::prepare_library(&dirs, &cfg.library)?,
    )
    .await?
    {
        changes.push(Change {
            target: Target::Config(path.strip_prefix(&binding.config)?.into()),
            mutation: text.map_or(Mutation::Retire, Mutation::Replace),
        });
    }
    transaction::commit_in_operation(&binding, changes, &gate).await
}

/// Build an initial accepted index using production record preparation. This is
/// offline fixture seeding, before a manager or recovery journal can exist.
pub(crate) fn seed<T: workspace::Record>(
    binding: &StorageBinding,
    values: Vec<T>,
) -> Result<workspace::Store<T>> {
    let mut store = workspace::Store::empty();
    let plan = store.prepare(values)?;
    for change in &plan.changes {
        let Mutation::Create(text) = &change.mutation else {
            anyhow::bail!("initial fixture must create records");
        };
        crate::paths::write_private_toml(&change.target.resolve(binding)?, text)?;
    }
    store.publish(plan);
    Ok(store)
}
