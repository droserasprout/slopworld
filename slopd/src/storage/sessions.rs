//! Accepted agent and host-shell records, deterministic startup discovery and
//! targeted mutation preparation. The manager owns cross-store validation and
//! holds the persistence gate from preparation through commit and publication.
//! This owner never polls or rereads records during ordinary mutations.

use super::{
    session_document,
    target::{StorageBinding, Target},
    workspace,
    workspace_document::Record,
};
use crate::config::{HostTerminalCfg, SessionCfg};
#[cfg(test)]
use anyhow::Context;
use anyhow::{Result, ensure};
use std::collections::HashSet;

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub(crate) enum Kind {
    #[default]
    Agent,
    HostShell,
}

impl Kind {
    fn directory(self) -> &'static str {
        match self {
            Self::Agent => "agents",
            Self::HostShell => "host_shells",
        }
    }
    fn target(self, id: &str) -> Target {
        Target::Data(std::path::Path::new(self.directory()).join(format!("{id}.toml")))
    }
}

#[derive(Clone, serde::Serialize)]
#[serde(untagged)]
pub(crate) enum Definition {
    Agent(Box<SessionCfg>),
    HostShell(HostTerminalCfg),
}

impl Definition {
    pub(crate) fn kind(&self) -> Kind {
        match self {
            Self::Agent(_) => Kind::Agent,
            Self::HostShell(_) => Kind::HostShell,
        }
    }
    pub(crate) fn id(&self) -> &str {
        match self {
            Self::Agent(row) => &row.state_id,
            Self::HostShell(row) => &row.id,
        }
    }
    pub(crate) fn name(&self) -> &str {
        match self {
            Self::Agent(row) => &row.name,
            Self::HostShell(row) => &row.name,
        }
    }
    pub(super) fn validate(&self) -> Result<()> {
        ensure!(
            crate::storage_id::valid(self.id()),
            "invalid session record identity"
        );
        ensure!(
            !self.name().trim().is_empty() && !self.name().chars().any(char::is_control),
            "invalid session record name"
        );
        if let Self::Agent(row) = self {
            ensure!(
                row.intent.is_empty() && row.reader_key.is_empty(),
                "ephemeral reader cannot be persisted"
            );
            row.limits.validate()?;
            row.dns.validate(&row.name)?;
            let mut snapshots = HashSet::new();
            for snapshot in &row.sandbox_snapshots {
                ensure!(
                    snapshots.insert(&snapshot.name),
                    "duplicate captured sandbox name"
                );
            }
        }
        Ok(())
    }
}

impl Record for Definition {
    type Scope = Kind;
    fn id(&self) -> &str {
        self.id()
    }
    fn directory(binding: &StorageBinding, kind: Kind) -> std::path::PathBuf {
        binding.data.join(kind.directory())
    }
    fn target(id: &str, kind: Kind) -> Target {
        kind.target(id)
    }
    fn fields() -> &'static [&'static str] {
        &[]
    }
    fn validate(&self) -> Result<()> {
        self.validate()
    }
    fn validate_collection(values: &[Self]) -> Result<()> {
        let mut names = HashSet::new();
        for value in values {
            ensure!(names.insert(value.name()), "session name already exists");
        }
        Ok(())
    }
    fn decode(raw: &toml::Value, kind: Kind) -> Result<Self> {
        session_document::decode(kind, raw)
    }
    fn document(&self, order: i64, old: Option<&toml::Value>) -> Result<toml::Value> {
        session_document::prepare(self, order, old)
    }
}

/// Session policy around the shared ordered-record index. A prepared mutation
/// owns its next index; publish it only after the transaction succeeds.
#[derive(Clone)]
pub(crate) struct Store {
    kind: Kind,
    records: workspace::Store<Definition>,
}

#[cfg(test)]
#[derive(Clone)]
pub(crate) struct Prepared {
    pub(crate) change: super::transaction::Change,
    next: workspace::Prepared<Definition>,
}

impl Store {
    #[cfg(test)]
    pub(crate) fn empty(kind: Kind) -> Self {
        Self {
            kind,
            records: workspace::Store::empty_scoped(kind),
        }
    }
    pub(crate) async fn load(binding: &StorageBinding, kind: Kind) -> Result<Self> {
        Ok(Self {
            kind,
            records: workspace::Store::load_scoped(binding, kind).await?,
        })
    }
    pub(crate) fn ordered(&self) -> Vec<Definition> {
        self.records.ordered()
    }
    #[cfg(test)]
    pub(crate) fn get(&self, id: &str) -> Option<&Definition> {
        self.records.get(id)
    }
    pub(crate) fn prepare(
        &self,
        values: Vec<Definition>,
    ) -> Result<workspace::Prepared<Definition>> {
        ensure!(
            values.iter().all(|value| value.kind() == self.kind),
            "wrong session record kind"
        );
        self.records.prepare(values)
    }
    pub(crate) fn publish_all(&mut self, plan: workspace::Prepared<Definition>) {
        self.records.publish(plan);
    }
    #[cfg(test)]
    pub(crate) fn create(&self, value: Definition) -> Result<Prepared> {
        ensure!(
            self.get(value.id()).is_none(),
            "session identity already exists"
        );
        let mut values = self.ordered();
        values.push(value);
        self.prepare_one(values)
    }
    #[cfg(test)]
    pub(crate) fn update(&self, id: &str, value: Definition) -> Result<Prepared> {
        ensure!(value.id() == id, "session identity cannot change");
        let mut values = self.ordered();
        *values
            .iter_mut()
            .find(|old| old.id() == id)
            .context("session record was removed")? = value;
        self.prepare_one(values)
    }
    #[cfg(test)]
    pub(crate) fn retire(&self, id: &str) -> Option<Prepared> {
        self.get(id)?;
        let mut values = self.ordered();
        values.retain(|value| value.id() != id);
        // Retiring an accepted record cannot invalidate its remaining collection.
        Some(
            self.prepare_one(values)
                .expect("retire accepted session record"),
        )
    }
    #[cfg(test)]
    fn prepare_one(&self, values: Vec<Definition>) -> Result<Prepared> {
        let next = self.prepare(values)?;
        let change = next
            .changes
            .first()
            .context("session edit has no change")?
            .clone();
        Ok(Prepared { change, next })
    }
    #[cfg(test)]
    pub(crate) fn publish(&mut self, prepared: Prepared) {
        self.publish_all(prepared.next);
    }
}

#[cfg(test)]
#[path = "sessions_tests.rs"]
mod tests;
