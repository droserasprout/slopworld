//! Explicit offline migration; preparation never rewrites legacy inputs.
//! TODO(remove after user tests and approves workspace store migration):
//! priv/notes/plan-storage-main.md. Keep the permanent transaction owner.
mod tasks;
use super::{
    session_document::Document,
    sessions::{Definition, Kind},
    target::{StorageBinding, Target},
    transaction::{self, Change, Mutation},
    workspace_document::Record,
};
use anyhow::{Context, Result, ensure};
use std::{collections::HashSet, sync::Arc};

pub(crate) async fn run(binding: &StorageBinding) -> Result<()> {
    let gate = Arc::new(tokio::sync::Mutex::new(())).lock_owned().await;
    // A new-format journal binds all roots. Check it before old recovery can write.
    transaction::recover(binding, &gate).await?;
    crate::config::legacy::recover(&binding.settings).await?;
    reject_displaced_catalogs(binding).await?;
    let changes = prepare(binding).await?;
    let count = changes.len();
    transaction::commit_in_operation(binding, changes, &gate).await?;
    tracing::info!(
        changed_files = count,
        "storage migration validated and committed"
    );
    Ok(())
}

// A settings filename override used to move its adjacent catalogs implicitly.
// They are independently owned directories, not part of workspace extraction.
// Refuse to hide them when the new configuration root is elsewhere.
async fn reject_displaced_catalogs(binding: &StorageBinding) -> Result<()> {
    let legacy = binding
        .settings
        .parent()
        .context("settings has no parent")?;
    if legacy == binding.config {
        return Ok(());
    }
    for directory in [
        "prompts",
        "breadcrumbs",
        "file_actions",
        "shell_scripts",
        "agent_templates",
    ] {
        let path = legacy.join(directory);
        ensure!(
            !tokio::fs::try_exists(&path).await?,
            "legacy catalog {} is outside SLOPD_CONFIG_ROOT; move the complete directory to {} while stopped before retrying migration (do not merge conflicting catalogs)",
            path.display(),
            binding.config.join(directory).display()
        );
    }
    Ok(())
}

async fn read(binding: &StorageBinding, target: &Target) -> Result<Option<String>> {
    let path = target.resolve(binding)?;
    match tokio::fs::read_to_string(&path).await {
        Ok(text) => Ok(Some(text)),
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => Ok(None),
        Err(e) => Err(e).with_context(|| format!("reading migration input {}", path.display())),
    }
}

async fn prepare(binding: &StorageBinding) -> Result<Vec<Change>> {
    let root = read(binding, &Target::Settings).await?.unwrap_or_default();
    let mut document: toml_edit::DocumentMut = root.parse()?;
    let mut raw: toml::Value = toml::from_str(&root)?;
    let mut changes = Vec::new();
    let mut cfg = crate::config::Config::default();
    let mut allocated = HashSet::new();
    for (key, directory, kind) in [
        ("project", "projects", None),
        ("session", "agents", Some(Kind::Agent)),
        ("host_terminal", "host_shells", Some(Kind::HostShell)),
    ] {
        let rows = raw
            .as_table_mut()
            .context("root must be a table")?
            .remove(key);
        if let Some(rows) = rows {
            let rows = rows
                .as_array()
                .context("inline workspace must be an array")?;
            ensure_empty(binding, directory, key == "project").await?;
            for (order, row) in rows.iter().enumerate() {
                let mut row = row.clone();
                ensure!(
                    row.get(if key == "project" {
                        "workspace_root"
                    } else {
                        "workspace"
                    })
                    .is_none(),
                    "retired workspace field in legacy {key}"
                );
                let id_key = if key == "session" { "state_id" } else { "id" };
                let id = identity(
                    binding,
                    directory,
                    key == "project",
                    &mut row,
                    id_key,
                    key != "session",
                    &mut allocated,
                )?;
                row.as_table_mut()
                    .context("record must be a table")?
                    .insert("storage_order".into(), i64::try_from(order)?.into());
                let text = toml::to_string_pretty(&row)?;
                match kind {
                    Some(kind) => {
                        let (value, _, _) = Document::decode(kind, &text)?;
                        value.validate()?;
                        match value {
                            Definition::Agent(row) => cfg.sessions.push(*row),
                            Definition::HostShell(row) => cfg.host_terminals.push(row),
                        }
                    }
                    None => {
                        let project = crate::config::ProjectCfg::decode(&row)?;
                        project.validate()?;
                        cfg.projects.push(project);
                    }
                }
                changes.push(Change {
                    target: record_target(directory, &id, key == "project"),
                    mutation: Mutation::Create(text),
                });
            }
            document.remove(key);
        }
    }
    cfg.settings = crate::config::settings::document::replace(&cfg, &document.to_string())?
        .candidate
        .settings;
    let worktrees = prepare_worktrees(binding, &mut changes).await?;
    prepare_tasks(binding, &mut changes).await?;
    prepare_grants(binding, &mut changes).await?;
    load_existing(binding, &mut cfg).await?;
    cfg.library =
        crate::config::Config::load_library_for(&binding.config.join("config.toml")).await?;
    super::layout::validate(&cfg, &worktrees)?;
    if document.to_string() != root {
        changes.push(Change {
            target: Target::Settings,
            mutation: Mutation::Replace(document.to_string()),
        });
    }
    Ok(changes)
}

async fn load_existing(binding: &StorageBinding, cfg: &mut crate::config::Config) -> Result<()> {
    // Reruns and non-conflicting already split stores are validated, never overwritten.
    if cfg.projects.is_empty() {
        cfg.projects = super::workspace::Store::<crate::config::ProjectCfg>::load(binding)
            .await?
            .ordered();
    }
    if cfg.sessions.is_empty() {
        cfg.sessions = super::sessions::Store::load(binding, Kind::Agent)
            .await?
            .ordered()
            .into_iter()
            .filter_map(|v| match v {
                Definition::Agent(v) => Some(*v),
                _ => None,
            })
            .collect();
    }
    if cfg.host_terminals.is_empty() {
        cfg.host_terminals = super::sessions::Store::load(binding, Kind::HostShell)
            .await?
            .ordered()
            .into_iter()
            .filter_map(|v| match v {
                Definition::HostShell(v) => Some(v),
                _ => None,
            })
            .collect();
    }
    Ok(())
}

fn record_target(directory: &str, id: &str, config: bool) -> Target {
    let relative = std::path::Path::new(directory).join(format!("{id}.toml"));
    if config {
        Target::Config(relative)
    } else {
        Target::Data(relative)
    }
}
fn identity(
    binding: &StorageBinding,
    directory: &str,
    config: bool,
    row: &mut toml::Value,
    key: &str,
    assign: bool,
    allocated: &mut HashSet<String>,
) -> Result<String> {
    let table = row.as_table_mut().context("record must be a table")?;
    let existing = table
        .get(key)
        .map(|v| v.as_str().context("record identity must be a string"))
        .transpose()?
        .unwrap_or("");
    let id = if existing.is_empty() && assign {
        crate::storage_id::allocate(|id| {
            Ok(allocated.contains(id)
                || match record_target(directory, id, config)
                    .resolve(binding)?
                    .symlink_metadata()
                {
                    Ok(_) => true,
                    Err(e) if e.kind() == std::io::ErrorKind::NotFound => false,
                    Err(e) => return Err(e.into()),
                })
        })?
    } else {
        existing.to_owned()
    };
    ensure!(
        crate::storage_id::valid_persistent(&id),
        "invalid or missing {key}"
    );
    ensure!(
        allocated.insert(id.clone()),
        "duplicate workspace identity {id}"
    );
    table.insert(key.into(), id.clone().into());
    Ok(id)
}
async fn ensure_empty(binding: &StorageBinding, directory: &str, config: bool) -> Result<()> {
    let root = if config {
        &binding.config
    } else {
        &binding.data
    };
    let path = root.join(directory);
    ensure!(
        crate::paths::normalize(&path)? == path,
        "aliased destination store"
    );
    match tokio::fs::read_dir(path).await {
        Ok(mut entries) => ensure!(
            entries.next_entry().await?.is_none(),
            "mixed legacy/new {directory} stores; resolve conflict before migration"
        ),
        Err(e) if e.kind() == std::io::ErrorKind::NotFound => {}
        Err(e) => return Err(e.into()),
    }
    Ok(())
}
async fn prepare_worktrees(
    binding: &StorageBinding,
    changes: &mut Vec<Change>,
) -> Result<Vec<crate::worktrees::Worktree>> {
    let target = Target::Legacy("worktrees.toml".into());
    let Some(text) = read(binding, &target).await? else {
        return Ok(
            super::workspace::Store::<crate::worktrees::Worktree>::load(binding)
                .await?
                .ordered(),
        );
    };
    ensure_empty(binding, "worktrees", false).await?;
    let raw: toml::Value = toml::from_str(&text)?;
    let rows = raw
        .get("worktrees")
        .map(|v| v.as_array().context("worktrees must be an array"))
        .transpose()?
        .cloned()
        .unwrap_or_default();
    let mut values = Vec::new();
    for (order, mut row) in rows.into_iter().enumerate() {
        let value = crate::worktrees::Worktree::decode(&row)?;
        value.validate()?;
        row.as_table_mut()
            .context("worktree must be a table")?
            .insert("storage_order".into(), i64::try_from(order)?.into());
        changes.push(Change {
            target: record_target("worktrees", &value.id, false),
            mutation: Mutation::Create(toml::to_string_pretty(&row)?),
        });
        values.push(value);
    }
    crate::worktrees::Worktree::validate_collection(&values)?;
    changes.push(Change {
        target,
        mutation: Mutation::Retire,
    });
    Ok(values)
}
async fn prepare_tasks(binding: &StorageBinding, changes: &mut Vec<Change>) -> Result<()> {
    let snapshot = Target::Legacy("tasks.toml".into());
    let journal = Target::Legacy("tasks.journal".into());
    let text = read(binding, &snapshot).await?;
    let log = read(binding, &journal).await?;
    if text.is_none() && log.is_none() {
        let data = binding.data.clone();
        tokio::task::spawn_blocking(move || crate::tasks::Tasks::load_records(&data)).await??;
        return Ok(());
    }
    ensure_empty(binding, "tasks", false).await?;
    for (order, mut row) in tasks::decode(text.as_deref(), log.as_deref())?
        .into_iter()
        .enumerate()
    {
        let task = tasks::validate(&row)?;
        row.as_table_mut()
            .context("task must be a table")?
            .insert("storage_order".into(), i64::try_from(order)?.into());
        changes.push(Change {
            target: record_target("tasks", &task.id, false),
            mutation: Mutation::Create(toml::to_string_pretty(&row)?),
        });
    }
    if text.is_some() {
        changes.push(Change {
            target: snapshot,
            mutation: Mutation::Retire,
        });
    }
    if log.is_some() {
        changes.push(Change {
            target: journal,
            mutation: Mutation::Retire,
        });
    }
    Ok(())
}
async fn prepare_grants(binding: &StorageBinding, changes: &mut Vec<Change>) -> Result<()> {
    let old = Target::Legacy("grants.toml".into());
    let new = Target::Data("grants.toml".into());
    let previous = read(binding, &old).await?;
    let destination = read(binding, &new).await?;
    if let Some(text) = &previous {
        crate::grant::Grants::validate_document(text)?;
    }
    if let Some(text) = &destination {
        crate::grant::Grants::validate_document(text)?;
    }
    if old.resolve(binding)? == new.resolve(binding)? {
        return Ok(());
    }
    if let Some(text) = previous {
        ensure!(
            destination.is_none(),
            "conflicting legacy and data grant authority stores"
        );
        changes.push(Change {
            target: new,
            mutation: Mutation::Create(text),
        });
        changes.push(Change {
            target: old,
            mutation: Mutation::Retire,
        });
    }
    Ok(())
}

#[cfg(test)]
mod tests;
