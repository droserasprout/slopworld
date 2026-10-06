//! Agent/host-shell document schema and extension preservation. Runtime-only
//! fields are discarded on write. Extensions follow their enclosing definition:
//! removing an optional snapshot also removes extensions inside that snapshot.

use super::sessions::{Definition, Kind};
use crate::config::{HostTerminalCfg, SessionCfg};
use anyhow::{Context, Result};

#[derive(Clone)]
pub(super) struct Document(toml::Value);

const HOST: &[&str] = &[
    "storage_order",
    "id",
    "name",
    "label",
    "project",
    "path",
    "autostart",
];
const AGENT: &[&str] = &[
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
];
const COMMAND: &[&str] = &["name", "kind", "description", "cmd", "sandbox"];
const SANDBOX: &[&str] = &[
    "name",
    "description",
    "requires",
    "ro",
    "rw",
    "dev",
    "private",
    "seed",
    "skip",
    "shared",
    "escapes",
    "env",
    "setenv",
    "tmux",
    "daemon_config",
];
const LIMITS: &[&str] = &["memory_mb", "pids", "nofile", "cpu_pct"];
const DNS: &[&str] = &["mode", "servers"];

impl Document {
    pub(super) fn decode(kind: Kind, text: &str) -> Result<(Definition, i64, Self)> {
        let raw: toml::Value = toml::from_str(text).context("parsing session record")?;
        let order = raw
            .get("storage_order")
            .and_then(toml::Value::as_integer)
            .context("session record requires integer storage_order")?;
        anyhow::ensure!(order >= 0, "negative session storage_order");
        let mut modeled = raw.clone();
        if kind == Kind::Agent {
            // API schemas reject unknown nested policy fields. Disk records retain
            // them as extensions without interpreting them as current policy.
            for (field, keys) in [
                ("limits", LIMITS),
                ("dns", DNS),
                ("command_snapshot", COMMAND),
            ] {
                if let Some(value) = modeled.get_mut(field) {
                    retain_known(value, keys);
                }
            }
            if let Some(rows) = modeled
                .get_mut("sandbox_snapshots")
                .and_then(toml::Value::as_array_mut)
            {
                for row in rows {
                    retain_known(row, SANDBOX);
                }
            }
        }
        let value = match kind {
            Kind::Agent => Definition::Agent(Box::new(modeled.try_into::<SessionCfg>()?)),
            Kind::HostShell => Definition::HostShell(modeled.try_into::<HostTerminalCfg>()?),
        };
        Ok((value, order, Self(raw)))
    }

    pub(super) fn create(value: &Definition, order: i64) -> Result<(String, Self)> {
        let raw = modeled(value, order)?;
        Ok((toml::to_string_pretty(&raw)?, Self(raw)))
    }

    pub(super) fn prepare(&self, value: &Definition, order: i64) -> Result<(String, Self)> {
        let mut new = modeled(value, order)?;
        if value.kind() == Kind::Agent {
            for (field, keys) in [
                ("limits", LIMITS),
                ("dns", DNS),
                ("command_snapshot", COMMAND),
            ] {
                if let (Some(next), Some(previous)) = (new.get_mut(field), self.0.get(field)) {
                    preserve(next, previous, keys);
                }
            }
            if let (Some(next), Some(old)) = (
                new.get_mut("sandbox_snapshots")
                    .and_then(toml::Value::as_array_mut),
                self.0
                    .get("sandbox_snapshots")
                    .and_then(toml::Value::as_array),
            ) {
                for row in next {
                    if let Some(previous) =
                        old.iter().find(|old| old.get("name") == row.get("name"))
                    {
                        preserve(row, previous, SANDBOX);
                    }
                }
            }
        }
        // Limits may serialize to nothing after the last cap is cleared. Keep
        // extensions in that optional table without reviving any known cap.
        if new.get("limits").is_none()
            && value.kind() == Kind::Agent
            && let Some(old) = self.0.get("limits")
        {
            let mut extensions = toml::Value::Table(Default::default());
            preserve(&mut extensions, old, LIMITS);
            if extensions.as_table().is_some_and(|table| !table.is_empty()) {
                new.as_table_mut()
                    .context("serialized definition is not a table")?
                    .insert("limits".into(), extensions);
            }
        }
        preserve(
            &mut new,
            &self.0,
            match value.kind() {
                Kind::Agent => AGENT,
                Kind::HostShell => HOST,
            },
        );
        Ok((toml::to_string_pretty(&new)?, Self(new)))
    }
}

fn modeled(value: &Definition, order: i64) -> Result<toml::Value> {
    let mut raw = match value {
        Definition::Agent(agent) => toml::Value::try_from(agent)?,
        Definition::HostShell(shell) => toml::Value::try_from(shell)?,
    };
    raw.as_table_mut()
        .context("session definition must be a table")?
        .insert("storage_order".into(), toml::Value::Integer(order));
    Ok(raw)
}

fn retain_known(value: &mut toml::Value, keys: &[&str]) {
    if let Some(table) = value.as_table_mut() {
        table.retain(|key, _| keys.contains(&key));
    }
}

fn preserve(new: &mut toml::Value, old: &toml::Value, keys: &[&str]) {
    if let (Some(new), Some(old)) = (new.as_table_mut(), old.as_table()) {
        for (key, value) in old {
            if !keys.contains(&key.as_str()) {
                new.insert(key.clone(), value.clone());
            }
        }
    }
}
