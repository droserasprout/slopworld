//! Project and checkout schemas and retained extensions. Mount extensions follow
//! their destination, which is the unique ownership key of a project mount.
use super::{
    target::{StorageBinding, Target},
    workspace::{project_target, worktree_target},
};
use crate::{config::ProjectCfg, worktrees::Worktree};
use anyhow::{Context, Result, ensure};
use serde::{Serialize, de::DeserializeOwned};
use std::{collections::HashSet, path::PathBuf};

pub(crate) trait Record: Clone + Serialize + DeserializeOwned {
    fn id(&self) -> &str;
    fn directory(binding: &StorageBinding) -> PathBuf;
    fn target(id: &str) -> Target;
    fn fields() -> &'static [&'static str];
    fn validate(&self) -> Result<()>;
    fn validate_collection(values: &[Self]) -> Result<()>;
    fn decode(raw: &toml::Value) -> Result<Self> {
        Ok(raw.clone().try_into()?)
    }
    fn document(&self, order: i64, old: Option<&toml::Value>) -> Result<toml::Value> {
        let mut next = toml::Value::try_from(self)?;
        next.as_table_mut()
            .context("record must be a table")?
            .insert("storage_order".into(), order.into());
        preserve(&mut next, old, Self::fields());
        Ok(next)
    }
}

fn preserve(next: &mut toml::Value, old: Option<&toml::Value>, fields: &[&str]) {
    if let (Some(next), Some(old)) = (next.as_table_mut(), old.and_then(toml::Value::as_table)) {
        for (key, value) in old {
            if key != "storage_order" && !fields.contains(&key.as_str()) {
                next.insert(key.clone(), value.clone());
            }
        }
    }
}

impl Record for ProjectCfg {
    fn id(&self) -> &str {
        &self.id
    }
    fn directory(binding: &StorageBinding) -> PathBuf {
        binding.config.join("projects")
    }
    fn target(id: &str) -> Target {
        project_target(id)
    }
    fn fields() -> &'static [&'static str] {
        &["id", "name", "dir", "temp", "worktree_root", "mounts"]
    }
    fn validate(&self) -> Result<()> {
        ensure!(
            crate::storage_id::valid(&self.id),
            "invalid project identity"
        );
        crate::config::validate_project_names(std::slice::from_ref(self))?;
        crate::config::validate_mount_paths(self)
    }
    fn validate_collection(values: &[Self]) -> Result<()> {
        crate::config::validate_project_names(values)
    }
    fn decode(raw: &toml::Value) -> Result<Self> {
        let mut modeled = raw.clone();
        if let Some(mounts) = modeled
            .get_mut("mounts")
            .and_then(toml::Value::as_array_mut)
        {
            for mount in mounts {
                if let Some(table) = mount.as_table_mut() {
                    table.retain(|key, _| ["from", "to", "mode"].contains(&key));
                }
            }
        }
        Ok(modeled.try_into()?)
    }
    fn document(&self, order: i64, old: Option<&toml::Value>) -> Result<toml::Value> {
        let mut next = toml::Value::try_from(self)?;
        next.as_table_mut()
            .context("record must be a table")?
            .insert("storage_order".into(), order.into());
        if let Some(mounts) = next.get_mut("mounts").and_then(toml::Value::as_array_mut) {
            for mount in mounts {
                let previous = old
                    .and_then(|old| old.get("mounts"))
                    .and_then(toml::Value::as_array)
                    .and_then(|rows| rows.iter().find(|row| row.get("to") == mount.get("to")));
                preserve(mount, previous, &["from", "to", "mode"]);
            }
        }
        preserve(&mut next, old, Self::fields());
        Ok(next)
    }
}

impl Record for Worktree {
    fn id(&self) -> &str {
        &self.id
    }
    fn directory(binding: &StorageBinding) -> PathBuf {
        binding.data.join("worktrees")
    }
    fn target(id: &str) -> Target {
        worktree_target(id)
    }
    fn fields() -> &'static [&'static str] {
        &[
            "id",
            "project_id",
            "name",
            "path",
            "repository",
            "managed",
            "initial_branch",
            "base",
            "phase",
            "error",
        ]
    }
    fn validate(&self) -> Result<()> {
        ensure!(
            crate::storage_id::valid(&self.id),
            "invalid worktree identity (Main is derived)"
        );
        ensure!(
            crate::storage_id::valid(&self.project_id),
            "invalid worktree project identity"
        );
        crate::config::project_name_component(&self.name)?;
        ensure!(
            std::path::Path::new(&self.path).is_absolute()
                && std::path::Path::new(&self.repository).is_absolute(),
            "worktree paths must be absolute"
        );
        ensure!(
            ["allocating", "ready", "removing", "relocating", "error"]
                .contains(&self.phase.as_str()),
            "invalid worktree phase"
        );
        Ok(())
    }
    fn validate_collection(values: &[Self]) -> Result<()> {
        let mut paths = HashSet::new();
        for value in values {
            ensure!(paths.insert(&value.path), "duplicate checkout path");
        }
        Ok(())
    }
}
