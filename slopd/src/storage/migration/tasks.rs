//! Read-only legacy replay. Invalid snapshots and any incomplete/invalid journal tail abort.
//! TODO(remove after user tests and approves workspace store migration):
//! priv/notes/plan-storage-main.md.
use anyhow::{Context, Result, ensure};
use std::collections::HashMap;

pub(super) fn decode(snapshot: Option<&str>, journal: Option<&str>) -> Result<Vec<toml::Value>> {
    let raw: toml::Value = toml::from_str(snapshot.unwrap_or(""))?;
    let generation = raw
        .get("generation")
        .map(|v| v.as_integer().context("invalid task generation"))
        .transpose()?
        .unwrap_or(0);
    ensure!(generation >= 0, "invalid task generation");
    let mut tasks = match raw.get("tasks") {
        Some(value) => value.as_array().context("tasks must be an array")?.clone(),
        None => Vec::new(),
    };
    let mut positions = HashMap::new();
    for (index, raw) in tasks.iter().enumerate() {
        let task = validate(raw)?;
        ensure!(
            positions.insert(task.id, index).is_none(),
            "duplicate legacy task identity"
        );
    }
    for (index, line) in journal.unwrap_or("").split_inclusive('\n').enumerate() {
        ensure!(
            line.ends_with('\n'),
            "incomplete task journal tail at line {}; repair from backup before migration",
            index + 1
        );
        let entry: serde_json::Value = serde_json::from_str(line)
            .with_context(|| format!("invalid task journal line {}", index + 1))?;
        let table = entry
            .as_object()
            .context("task journal entry must be an object")?;
        ensure!(
            table
                .keys()
                .all(|k| ["generation", "create", "update"].contains(&k.as_str())),
            "unsupported task journal entry"
        );
        let entry_generation = entry
            .get("generation")
            .and_then(serde_json::Value::as_u64)
            .context("missing task journal generation")?;
        let create = entry.get("create").filter(|v| !v.is_null());
        let update = entry.get("update").filter(|v| !v.is_null());
        ensure!(
            create.is_some() != update.is_some(),
            "unsupported task journal operation"
        );
        // Decode even stale entries: malformed history must be reported, never repaired.
        if let Some(create) = create {
            let raw = json_toml(create)?;
            let task = validate(&raw)?;
            if entry_generation == u64::try_from(generation)? && !positions.contains_key(&task.id) {
                positions.insert(task.id, tasks.len());
                tasks.push(raw);
            }
        }
        if let Some(update) = update {
            let change: Update = serde_json::from_value(update.clone())?;
            if entry_generation == u64::try_from(generation)?
                && let Some(&at) = positions.get(&change.id)
            {
                let row = tasks
                    .get_mut(at)
                    .context("invalid task position")?
                    .as_table_mut()
                    .context("task must be a table")?;
                row.insert("status".into(), toml::Value::try_from(change.status)?);
                row.insert(
                    "updated_ms".into(),
                    i64::try_from(change.updated_ms)?.into(),
                );
                for (key, value) in [("note", change.note), ("summary", change.summary)] {
                    row.remove(key);
                    if let Some(value) = value {
                        row.insert(key.into(), value.into());
                    }
                }
            }
        }
    }
    Ok(tasks)
}
#[derive(serde::Deserialize)]
#[serde(deny_unknown_fields)]
struct Update {
    id: String,
    status: crate::tasks::Status,
    note: Option<String>,
    summary: Option<String>,
    updated_ms: u64,
}
fn json_toml(value: &serde_json::Value) -> Result<toml::Value> {
    let mut value = value.clone();
    fn prune(value: &mut serde_json::Value) {
        match value {
            serde_json::Value::Object(table) => {
                table.retain(|_, v| !v.is_null());
                for v in table.values_mut() {
                    prune(v);
                }
            }
            serde_json::Value::Array(values) => {
                for v in values {
                    prune(v);
                }
            }
            _ => {}
        }
    }
    prune(&mut value);
    Ok(toml::Value::try_from(value)?)
}
pub(super) fn validate(raw: &toml::Value) -> Result<crate::tasks::Task> {
    let task: crate::tasks::Task = raw.clone().try_into()?;
    ensure!(
        crate::storage_id::valid_task(&task.id),
        "invalid task identity"
    );
    ensure!(
        !task.from.trim().is_empty() && !task.to.trim().is_empty() && !task.body.trim().is_empty(),
        "invalid task participants/body"
    );
    Ok(task)
}
