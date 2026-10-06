//! Ordered project and registered-checkout records. Schema policy belongs to
//! workspace_document; the manager owns cross-record references and Git effects.
//! Accepted documents are loaded once, then explicit plans publish under the gate.

use super::{
    target::{StorageBinding, Target},
    transaction::{Change, Mutation},
    workspace_document::Record,
};
use anyhow::{Context, Result, ensure};
use std::collections::{BTreeMap, HashSet};

#[derive(Clone)]
struct Entry<T> {
    value: T,
    order: i64,
    raw: toml::Value,
}

#[derive(Clone)]
pub(crate) struct Store<T> {
    entries: BTreeMap<String, Entry<T>>,
    order: BTreeMap<i64, String>,
    next_order: i64,
}

pub(crate) struct Prepared<T> {
    pub(crate) changes: Vec<Change>,
    next: Store<T>,
}

impl<T: Record> Store<T> {
    pub(crate) fn empty() -> Self {
        Self {
            entries: BTreeMap::new(),
            order: BTreeMap::new(),
            next_order: 0,
        }
    }
    pub(crate) async fn load(binding: &StorageBinding) -> Result<Self> {
        let directory = T::directory(binding);
        ensure!(
            crate::paths::normalize(&directory)? == directory,
            "workspace store aliases another path"
        );
        let mut result = Self::empty();
        let mut entries = match tokio::fs::read_dir(&directory).await {
            Ok(entries) => entries,
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => return Ok(result),
            Err(error) => return Err(error).context("reading workspace store"),
        };
        let mut paths = Vec::new();
        while let Some(entry) = entries.next_entry().await? {
            if entry.path().extension().is_some_and(|ext| ext == "toml") {
                paths.push(entry.path());
            }
        }
        paths.sort();
        for path in paths {
            let id = path
                .file_stem()
                .and_then(|s| s.to_str())
                .context("invalid record filename")?;
            ensure!(
                T::target(id).resolve(binding)? == path,
                "invalid record target"
            );
            let raw: toml::Value = toml::from_str(&tokio::fs::read_to_string(&path).await?)
                .with_context(|| format!("decoding {}", path.display()))?;
            let value = T::decode(&raw)?;
            value.validate()?;
            ensure!(
                value.id() == id,
                "workspace filename/content identity mismatch"
            );
            let order = raw
                .get("storage_order")
                .and_then(toml::Value::as_integer)
                .context("missing integer storage_order")?;
            ensure!((0..i64::MAX).contains(&order), "invalid storage_order");
            ensure!(
                !result.order.contains_key(&order),
                "duplicate storage_order"
            );
            result.insert(Entry { value, order, raw });
        }
        T::validate_collection(&result.ordered())?;
        Ok(result)
    }
    pub(crate) fn ordered(&self) -> Vec<T> {
        self.order
            .values()
            .map(|id| self.entries[id].value.clone())
            .collect()
    }
    /// Compare only the owning collection. Retained IDs keep their ordinal and
    /// extensions; absent IDs retire, new IDs require exclusive file creation.
    pub(crate) fn prepare(&self, values: Vec<T>) -> Result<Prepared<T>> {
        T::validate_collection(&values)?;
        let ids: HashSet<_> = values.iter().map(Record::id).collect();
        ensure!(ids.len() == values.len(), "duplicate workspace identity");
        let mut next = self.clone();
        let mut changes = Vec::new();
        for (id, old) in &self.entries {
            if !ids.contains(id.as_str()) {
                changes.push(Change {
                    target: T::target(id),
                    mutation: Mutation::Retire,
                });
                next.entries.remove(id);
                next.order.remove(&old.order);
            }
        }
        for value in &values {
            value.validate()?;
            let old = self.entries.get(value.id());
            if let Some(old) = old
                && toml::Value::try_from(&old.value)? == toml::Value::try_from(value)?
            {
                continue;
            }
            let order = old.map_or(next.next_order, |old| old.order);
            ensure!(order < i64::MAX, "storage_order exhausted");
            let raw = value.document(order, old.map(|old| &old.raw))?;
            let text = toml::to_string_pretty(&raw)?;
            changes.push(Change {
                target: T::target(value.id()),
                mutation: if old.is_some() {
                    Mutation::Replace(text)
                } else {
                    Mutation::Create(text)
                },
            });
            next.insert(Entry {
                value: value.clone(),
                order,
                raw,
            });
        }
        ensure!(
            next.order
                .values()
                .map(String::as_str)
                .eq(values.iter().map(Record::id)),
            "cannot reorder retained workspace records"
        );
        Ok(Prepared { changes, next })
    }
    pub(crate) fn publish(&mut self, prepared: Prepared<T>) {
        *self = prepared.next;
    }
    fn insert(&mut self, entry: Entry<T>) {
        self.next_order = self.next_order.max(entry.order + 1);
        self.order.insert(entry.order, entry.value.id().into());
        self.entries.insert(entry.value.id().into(), entry);
    }
}

pub(crate) fn project_target(id: &str) -> Target {
    Target::Config(format!("projects/{id}.toml").into())
}
pub(crate) fn worktree_target(id: &str) -> Target {
    Target::Data(format!("worktrees/{id}.toml").into())
}

#[cfg(test)]
#[path = "workspace_tests.rs"]
mod tests;
