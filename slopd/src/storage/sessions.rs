//! Accepted agent and host-shell records, deterministic startup discovery and
//! targeted mutation preparation. The manager owns cross-store validation and
//! holds the persistence gate from preparation through commit and publication.
//! This owner never polls or rereads records during ordinary mutations.

use super::{
    session_document::Document,
    target::{StorageBinding, Target},
    transaction::{Change, Mutation},
};
use crate::config::{HostTerminalCfg, SessionCfg};
use anyhow::{Context, Result, ensure};
use std::collections::{BTreeMap, HashSet};

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub(crate) enum Kind {
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

#[derive(Clone)]
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
            crate::storage_id::valid_persistent(self.id()),
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

#[derive(Clone)]
struct Entry {
    value: Definition,
    order: i64,
    document: Document,
}

#[derive(Clone)]
pub(crate) struct Store {
    kind: Kind,
    records: BTreeMap<String, Entry>,
    names: BTreeMap<String, String>,
    order: BTreeMap<i64, String>,
    next_order: i64,
}

/// Contains one explicit file mutation and its already validated memory change.
/// Publish only after the shared transaction succeeds, under the same gate.
#[derive(Clone)]
pub(crate) struct Prepared {
    pub(crate) change: Change,
    id: String,
    next: Option<Entry>,
}

impl Store {
    pub(crate) fn empty(kind: Kind) -> Self {
        Self {
            kind,
            records: BTreeMap::new(),
            names: BTreeMap::new(),
            order: BTreeMap::new(),
            next_order: 0,
        }
    }

    /// Recover the shared journal before calling this at startup. Missing stores
    /// are empty; malformed entries, aliases and duplicate orders are errors.
    pub(crate) async fn load(binding: &StorageBinding, kind: Kind) -> Result<Self> {
        let mut store = Self::empty(kind);
        let directory = binding.data.join(kind.directory());
        ensure!(
            crate::paths::normalize(&directory)? == directory,
            "session store aliases another path"
        );
        let mut entries = match tokio::fs::read_dir(&directory).await {
            Ok(entries) => entries,
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => return Ok(store),
            Err(error) => {
                return Err(error).with_context(|| format!("reading {}", directory.display()));
            }
        };
        let mut paths = Vec::new();
        while let Some(entry) = entries.next_entry().await? {
            let path = entry.path();
            if path
                .extension()
                .is_some_and(|extension| extension == "toml")
            {
                paths.push(path);
            }
        }
        paths.sort();
        for path in paths {
            let id = path
                .file_stem()
                .and_then(|stem| stem.to_str())
                .context("invalid session record filename")?;
            ensure!(
                kind.target(id).resolve(binding)? == path,
                "invalid session record target"
            );
            let text = tokio::fs::read_to_string(&path)
                .await
                .with_context(|| format!("reading {}", path.display()))?;
            let (value, order, document) = Document::decode(kind, &text)
                .with_context(|| format!("decoding {}", path.display()))?;
            value.validate()?;
            ensure!(
                id == value.id(),
                "session filename/content identity mismatch: {}",
                path.display()
            );
            store.check_new(&value)?;
            ensure!(
                !store.order.contains_key(&order),
                "duplicate session storage_order {order}"
            );
            ensure!(order < i64::MAX, "session storage_order exhausted");
            store.insert(Entry {
                value,
                order,
                document,
            });
        }
        Ok(store)
    }

    #[expect(
        clippy::expect_used,
        reason = "record and order indexes publish together"
    )]
    pub(crate) fn ordered(&self) -> Vec<Definition> {
        self.order
            .values()
            .map(|id| {
                self.records
                    .get(id)
                    .expect("ordered identity belongs to accepted records")
                    .value
                    .clone()
            })
            .collect()
    }

    pub(crate) fn get(&self, id: &str) -> Option<&Definition> {
        self.records.get(id).map(|entry| &entry.value)
    }

    fn check_new(&self, value: &Definition) -> Result<()> {
        value.validate()?;
        ensure!(value.kind() == self.kind, "wrong session record kind");
        ensure!(
            !self.records.contains_key(value.id()),
            "session identity already exists"
        );
        ensure!(
            !self.names.contains_key(value.name()),
            "session name already exists"
        );
        Ok(())
    }

    /// The allocator checks the full identity namespace under the manager's gate.
    /// Create still uses exclusive publication to reject an occupied file.
    pub(crate) fn create(&self, value: Definition) -> Result<Prepared> {
        self.check_new(&value)?;
        ensure!(
            self.next_order < i64::MAX,
            "session storage_order exhausted"
        );
        let order = self.next_order;
        let (text, document) = Document::create(&value, order)?;
        Ok(Prepared {
            change: Change {
                target: self.kind.target(value.id()),
                mutation: Mutation::Create(text),
            },
            id: value.id().into(),
            next: Some(Entry {
                value,
                order,
                document,
            }),
        })
    }

    /// ID, not name, selects accepted ownership. A delayed edit of a retired ID
    /// fails rather than creating a new file or touching a reused display name.
    pub(crate) fn update(&self, id: &str, value: Definition) -> Result<Prepared> {
        let old = self.records.get(id).context("session record was removed")?;
        value.validate()?;
        ensure!(
            value.kind() == self.kind && value.id() == id,
            "session identity cannot change"
        );
        ensure!(
            self.names
                .get(value.name())
                .is_none_or(|existing| existing == id),
            "session name already exists"
        );
        let (text, document) = old.document.prepare(&value, old.order)?;
        Ok(Prepared {
            change: Change {
                target: self.kind.target(id),
                mutation: Mutation::Replace(text),
            },
            id: id.into(),
            next: Some(Entry {
                value,
                order: old.order,
                document,
            }),
        })
    }

    pub(crate) fn retire(&self, id: &str) -> Option<Prepared> {
        self.records.contains_key(id).then(|| Prepared {
            change: Change {
                target: self.kind.target(id),
                mutation: Mutation::Retire,
            },
            id: id.into(),
            next: None,
        })
    }

    /// Infallible publication of a prepared change. The caller retains the same
    /// mutation gate across preparation, disk commit and this index update.
    pub(crate) fn publish(&mut self, prepared: Prepared) {
        if let Some(old) = self.records.remove(&prepared.id) {
            self.names.remove(old.value.name());
            self.order.remove(&old.order);
        }
        if let Some(next) = prepared.next {
            self.insert(next);
        }
    }

    fn insert(&mut self, entry: Entry) {
        self.next_order = self.next_order.max(entry.order + 1);
        self.names
            .insert(entry.value.name().into(), entry.value.id().into());
        self.order.insert(entry.order, entry.value.id().into());
        self.records.insert(entry.value.id().into(), entry);
    }
}

#[cfg(test)]
#[path = "sessions_tests.rs"]
mod tests;
