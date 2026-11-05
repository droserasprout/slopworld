//! Agent/host-shell document schema and extension preservation. Runtime-only
//! fields are discarded on write. One nested schema governs decode and retention.
//! Extensions follow their enclosing definition:
//! removing an optional snapshot also removes extensions inside that snapshot.

use super::document::{preserve, retain_known};
use crate::config::SessionCfg;
use anyhow::{Context, Result};

pub(super) const HOST: &[&str] = &[
    "storage_order",
    "id",
    "name",
    "label",
    "project",
    "path",
    "autostart",
];
pub(super) const AGENT: &[&str] = &[
    "storage_order",
    "worktree",
    "name",
    "label",
    "state_id",
    "project",
    "command",
    "cmd",
    "args",
    "command_snapshot",
    "sandbox",
    "sandbox_snapshots",
    "persistent_tmp",
    "network",
    "dns",
    "limits",
    "autostart",
    "auto_resume",
    "initial_prompt",
    "workers",
    "parent_id",
    "parent_access",
    "worker",
    "parent",
    "task_id",
    "intent",
    "reader_label",
    "reader_path",
    "reader_key",
    "reader_scope",
    "reader_pinned",
    "reader_line",
    "worker_token",
    "daemon_token",
];
const COMMAND: &[&str] = &["name", "kind", "description", "cmd", "sandbox"];
const SANDBOX: &[&str] = &[
    "name",
    "description",
    "requires",
    "ro",
    "rw",
    "cache",
    "dev",
    "private",
    "seed",
    "skip",
    "shared",
    "escapes",
    "warning_level",
    "env",
    "setenv",
    "tmux",
    "daemon_config",
];
const LIMITS: &[&str] = &["memory_mb", "pids", "nofile", "cpu_pct"];
const DNS: &[&str] = &["mode", "servers"];
const WORKERS: &[&str] = &[
    "enabled",
    "templates",
    "all_templates",
    "access",
    "recursive",
];

// Field metadata stays explicit because disk extension lifetimes are not part of
// the API model. Tests compare every known-key set to Serde's complete field list,
// including optional fields omitted from default serialization.
struct NestedField {
    name: &'static str,
    known: &'static [&'static str],
    shape: ExtensionShape,
}

#[derive(Clone, Copy)]
enum ExtensionShape {
    Table,
    // Clearing the last modeled limit keeps an extension-only table.
    RetainedTable,
    Rows { key: &'static str },
}

const NESTED_FIELDS: &[NestedField] = &[
    NestedField {
        name: "limits",
        known: LIMITS,
        shape: ExtensionShape::RetainedTable,
    },
    NestedField {
        name: "workers",
        known: WORKERS,
        shape: ExtensionShape::Table,
    },
    NestedField {
        name: "dns",
        known: DNS,
        shape: ExtensionShape::Table,
    },
    NestedField {
        name: "command_snapshot",
        known: COMMAND,
        shape: ExtensionShape::Table,
    },
    NestedField {
        name: "sandbox_snapshots",
        known: SANDBOX,
        shape: ExtensionShape::Rows { key: "name" },
    },
];

impl NestedField {
    fn retain_modeled(&self, document: &mut toml::Value) {
        let Some(value) = document.get_mut(self.name) else {
            return;
        };
        match self.shape {
            ExtensionShape::Rows { .. } => {
                if let Some(rows) = value.as_array_mut() {
                    for row in rows {
                        retain_known(row, self.known);
                    }
                }
            }
            ExtensionShape::Table | ExtensionShape::RetainedTable => {
                retain_known(value, self.known)
            }
        }
    }

    fn preserve_extensions(
        &self,
        document: &mut toml::Value,
        old: Option<&toml::Value>,
    ) -> Result<()> {
        if let ExtensionShape::Rows { key } = self.shape {
            super::document::preserve_rows(document, old, self.name, key, self.known);
            return Ok(());
        }
        let previous = old.and_then(|old| old.get(self.name));
        if let Some(next) = document.get_mut(self.name) {
            preserve(next, previous, self.known);
        } else if matches!(self.shape, ExtensionShape::RetainedTable) {
            let mut extensions = toml::Value::Table(Default::default());
            preserve(&mut extensions, previous, self.known);
            if extensions.as_table().is_some_and(|table| !table.is_empty()) {
                document
                    .as_table_mut()
                    .context("serialized definition is not a table")?
                    .insert(self.name.into(), extensions);
            }
        }
        Ok(())
    }
}

pub(super) fn decode(raw: &toml::Value) -> Result<SessionCfg> {
    let mut modeled = raw.clone();
    // API schemas reject unknown nested policy fields. Disk records retain
    // them as extensions without interpreting them as current policy.
    for field in NESTED_FIELDS {
        field.retain_modeled(&mut modeled);
    }
    Ok(modeled.try_into()?)
}

pub(super) fn prepare(
    value: &SessionCfg,
    order: i64,
    old: Option<&toml::Value>,
) -> Result<toml::Value> {
    let mut new = super::document::prepare(value, order, old, AGENT)?;
    for field in NESTED_FIELDS {
        field.preserve_extensions(&mut new, old)?;
    }
    Ok(new)
}

#[cfg(test)]
#[path = "session_document_tests.rs"]
mod tests;
