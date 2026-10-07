//! Agent and host-shell record policy. The shared workspace store owns indexes
//! and mutation preparation; session_document owns nested extension preservation.

use super::{
    session_document,
    target::{StorageBinding, Target},
    workspace_document::Record,
};
use crate::config::{HostTerminalCfg, SessionCfg};
use anyhow::{Result, ensure};
use std::{collections::HashSet, path::PathBuf};

impl Record for SessionCfg {
    fn id(&self) -> &str {
        &self.state_id
    }
    fn directory(binding: &StorageBinding) -> PathBuf {
        binding.data.join("agents")
    }
    fn target(id: &str) -> Target {
        Target::Data(format!("agents/{id}.toml").into())
    }
    fn fields() -> &'static [&'static str] {
        session_document::AGENT
    }
    fn validate(&self) -> Result<()> {
        validate_identity(&self.state_id, &self.name)?;
        ensure!(
            self.intent.is_empty() && self.reader_key.is_empty(),
            "ephemeral reader cannot be persisted"
        );
        self.limits.validate()?;
        self.dns.validate(&self.name)?;
        let mut snapshots = HashSet::new();
        for snapshot in &self.sandbox_snapshots {
            ensure!(
                snapshots.insert(&snapshot.name),
                "duplicate captured sandbox name"
            );
        }
        Ok(())
    }
    fn validate_collection(values: &[Self]) -> Result<()> {
        validate_names(values.iter().map(|row| row.name.as_str()))
    }
    fn decode(raw: &toml::Value) -> Result<Self> {
        session_document::decode(raw)
    }
    fn document(&self, order: i64, old: Option<&toml::Value>) -> Result<toml::Value> {
        session_document::prepare(self, order, old)
    }
}

impl Record for HostTerminalCfg {
    fn id(&self) -> &str {
        &self.id
    }
    fn directory(binding: &StorageBinding) -> PathBuf {
        binding.data.join("host_shells")
    }
    fn target(id: &str) -> Target {
        Target::Data(format!("host_shells/{id}.toml").into())
    }
    fn fields() -> &'static [&'static str] {
        session_document::HOST
    }
    fn validate(&self) -> Result<()> {
        validate_identity(&self.id, &self.name)
    }
    fn validate_collection(values: &[Self]) -> Result<()> {
        validate_names(values.iter().map(|row| row.name.as_str()))
    }
}

fn validate_identity(id: &str, name: &str) -> Result<()> {
    ensure!(
        crate::storage_id::valid(id),
        "invalid session record identity"
    );
    ensure!(
        !name.trim().is_empty() && !name.chars().any(char::is_control),
        "invalid session record name"
    );
    Ok(())
}

fn validate_names<'a>(names: impl Iterator<Item = &'a str>) -> Result<()> {
    let mut seen = HashSet::new();
    for name in names {
        ensure!(seen.insert(name), "session name already exists");
    }
    Ok(())
}

#[cfg(test)]
#[path = "sessions_tests.rs"]
mod tests;
