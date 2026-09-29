//! Effective launch settings, captured presets, and editor previews.
//! Resolution does not prepare private state or start processes.
use super::{default_agent, Config, DnsConfig, Limits, NetworkMode, ProjectCfg, SessionCfg};
use serde_json::{json, Value};

impl SessionCfg {
    /// Keep captured definitions only for selected presets and their transitive dependencies.
    /// A command-line override changes execution but preserves the selected preset's configuration.
    pub(crate) fn preserve_selected_snapshots(&mut self, previous: &Self) {
        self.command_snapshot = if self.command == previous.command {
            previous.command_snapshot.clone()
        } else {
            None
        };
        let mut selected = self.sandbox.clone();
        if let Some(command) = &self.command_snapshot {
            selected.extend(command.sandbox.clone());
        }
        let mut index = 0;
        while index < selected.len() {
            if let Some(preset) = previous
                .sandbox_snapshots
                .iter()
                .find(|p| p.name == selected[index])
            {
                for required in &preset.requires {
                    if !selected.contains(required) {
                        selected.push(required.clone());
                    }
                }
            }
            index += 1;
        }
        self.sandbox_snapshots = previous
            .sandbox_snapshots
            .iter()
            .filter(|p| selected.contains(&p.name))
            .cloned()
            .collect();
    }
}

impl Config {
    // Shared launch and preview resolvers.

    /// Agent-owned policy; the project argument keeps resolver signatures consistent.
    pub fn network_of(&self, s: &SessionCfg, _p: &ProjectCfg) -> NetworkMode {
        s.network
    }

    pub fn dns_of(&self, s: &SessionCfg, _p: &ProjectCfg) -> DnsConfig {
        s.dns.clone()
    }

    /// Resource limits are final agent settings. They do not provide an isolation boundary.
    pub fn limits_of(&self, s: &SessionCfg, _p: &ProjectCfg) -> Limits {
        s.limits
    }

    /// Resolve the command preset; a standalone command line inherits no preset mounts.
    pub fn command_name(&self, s: &SessionCfg) -> String {
        if let Some(snapshot) = &s.command_snapshot {
            return snapshot.name.clone();
        }
        let own = s.command.trim();
        if !own.is_empty() {
            return own.to_string();
        }
        if s.cmd
            .as_deref()
            .map(str::trim)
            .is_some_and(|c| !c.is_empty())
        {
            return String::new();
        }
        match self.defaults.agent.trim() {
            "" => default_agent(),
            a => a.to_string(),
        }
    }

    /// Return the command to execute: the explicit command line, the snapshot, or the command preset.
    /// A missing preset gives an empty command, which `start` rejects.
    pub fn command_of(&self, s: &SessionCfg) -> String {
        if let Some(c) = s.cmd.as_deref().map(str::trim).filter(|c| !c.is_empty()) {
            return c.to_string();
        }
        if let Some(snapshot) = &s.command_snapshot {
            return snapshot.cmd.clone();
        }
        crate::presets::table()
            .command(&self.command_name(s))
            .map(|c| c.cmd.clone())
            .unwrap_or_default()
    }

    /// Resolve global, command, then agent presets, deduplicated with dependencies first.
    pub fn sandbox_of(&self, s: &SessionCfg, _p: &ProjectCfg) -> Vec<String> {
        let t = crate::presets::table();
        let command_sandbox = s
            .command_snapshot
            .as_ref()
            .map(|c| c.sandbox.clone())
            .or_else(|| t.command(&self.command_name(s)).map(|c| c.sandbox.clone()))
            .unwrap_or_default();
        let asked: Vec<String> = std::iter::once("global".to_string())
            .chain(command_sandbox)
            .chain(s.sandbox.iter().cloned())
            .collect();

        fn add(
            name: &str,
            table: &crate::presets::Table,
            snapshots: &[crate::presets::SandboxPreset],
            out: &mut Vec<String>,
            visiting: &mut Vec<String>,
        ) {
            if out.iter().any(|seen| seen == name) {
                return;
            }
            if visiting.iter().any(|seen| seen == name) {
                tracing::warn!(
                    "sandbox preset dependency cycle at {name:?}, ignoring its back-edge"
                );
                return;
            }
            visiting.push(name.to_string());
            // Resolve the graph from the same definitions used to build the launch plan.
            if let Some(preset) = snapshots
                .iter()
                .find(|preset| preset.name == name)
                .or_else(|| table.sandbox(name))
            {
                for required in &preset.requires {
                    add(required, table, snapshots, out, visiting);
                }
            }
            visiting.pop();
            if !out.iter().any(|seen| seen == name) {
                out.push(name.to_string());
            }
        }

        let mut names = Vec::new();
        for name in asked {
            add(&name, &t, &s.sandbox_snapshots, &mut names, &mut Vec::new());
        }
        names
    }

    // Editor projection of the resolved settings.

    pub(crate) fn settings_preview(
        &self,
        s: &SessionCfg,
        p: &ProjectCfg,
        recipe: bool,
    ) -> anyhow::Result<Value> {
        let mut fields = Vec::new();
        self.append_basic_preview_fields(s, p, recipe, &mut fields);
        let table = s.preset_table();
        let effective = crate::sandbox::presets_for(self, s, p, &table)?;
        self.append_sandbox_contribution_field(s, p, &table, &mut fields);
        Self::append_startup_and_mount_fields(s, p, &mut fields);
        Self::append_preset_detail_fields(&effective, &mut fields);
        Ok(json!({
            "title": if recipe { "Template recipe" } else { "Effective settings for next start" },
            "subtitle": if recipe { "Portable agent settings. The destination project applies project mounts.".to_string() } else { format!("Project: {} — {}", p.name, p.dir) },
            "notes": ["This is saved or draft configuration. It does not describe a running process. The daemon checks mount existence and protected paths at launch."],
            "fields": fields
        }))
    }

    fn append_basic_preview_fields(
        &self,
        s: &SessionCfg,
        p: &ProjectCfg,
        recipe: bool,
        fields: &mut Vec<Value>,
    ) {
        let command = self.command_of(s);
        let command = if command.is_empty() {
            "Unavailable command preset. Choose an installed command.".into()
        } else {
            command
        };
        let command_source = if s.cmd.as_ref().is_some_and(|cmd| !cmd.trim().is_empty()) {
            "agent command line"
        } else if s.command_snapshot.is_some() {
            "captured command"
        } else if !s.command.is_empty() {
            "selected command preset"
        } else {
            "daemon default"
        };
        Self::preview_field(
            fields,
            "Command",
            vec![if recipe && s.command.is_empty() && s.cmd.is_none() {
                "Use destination daemon's default command".into()
            } else {
                format!("{command} — {command_source}")
            }],
        );
        let network = self.network_of(s, p);
        Self::preview_field(
            fields,
            "Network",
            vec![format!(
                "{} — agent setting",
                serde_json::to_value(network).unwrap().as_str().unwrap()
            )],
        );
        let dns = self.dns_of(s, p);
        let dns_label = match &dns {
            DnsConfig::Resolved => "System resolver".into(),
            DnsConfig::Servers { servers } => servers
                .iter()
                .map(ToString::to_string)
                .collect::<Vec<_>>()
                .join(", "),
        };
        Self::preview_field(fields, "DNS", vec![format!("{dns_label} — agent setting")]);
        let limits = self.limits_of(s, p);
        let mut caps = Vec::new();
        for (label, effective) in [
            ("Memory (MiB)", limits.memory_mb),
            ("Processes", limits.pids),
            ("Open files", limits.nofile),
            ("CPU (%)", limits.cpu_pct),
        ] {
            caps.push(format!(
                "{label}: {} — agent setting",
                effective
                    .map(|v| v.to_string())
                    .unwrap_or("no configured cap".into())
            ));
        }
        Self::preview_field(fields, "Resource limits", caps);
    }

    fn append_sandbox_contribution_field(
        &self,
        s: &SessionCfg,
        p: &ProjectCfg,
        table: &crate::presets::Table,
        fields: &mut Vec<Value>,
    ) {
        let command_presets = s
            .command_snapshot
            .as_ref()
            .map(|c| c.sandbox.clone())
            .or_else(|| {
                table
                    .command(&self.command_name(s))
                    .map(|c| c.sandbox.clone())
            })
            .unwrap_or_default();
        let mut selections = Vec::new();
        for name in self.sandbox_of(s, p) {
            let mut owners = Vec::new();
            if name == "global" {
                owners.push("global");
            }
            if command_presets.contains(&name) {
                owners.push("command");
            }
            if s.sandbox.contains(&name) {
                owners.push("agent");
            }
            if owners.is_empty() {
                owners.push("dependency");
            }
            let definition = if s.sandbox_snapshots.iter().any(|v| v.name == name) {
                "captured copy"
            } else {
                "live definition"
            };
            selections.push(format!("{name} — {} — {definition}", owners.join(" + ")));
        }
        Self::preview_field(fields, "Sandbox contributions", selections);
    }

    fn append_startup_and_mount_fields(s: &SessionCfg, p: &ProjectCfg, fields: &mut Vec<Value>) {
        Self::preview_field(
            fields,
            "Startup and session behavior",
            vec![
                format!("Start with daemon: {}", s.autostart),
                format!("Auto-resume: {}", s.auto_resume),
                format!("Persistent /tmp: {}", s.persistent_tmp),
                "Library breadcrumbs are inserted manually from the terminal context menu".into(),
            ],
        );
        Self::preview_field(
            fields,
            "Project mounts (next start)",
            if p.mounts.is_empty() {
                vec!["none".into()]
            } else {
                p.mounts
                    .iter()
                    .map(|mount| {
                        format!(
                            "{} → {} — {}",
                            mount.from,
                            mount.to,
                            match mount.mode {
                                super::MountMode::Ro => "read-only",
                                super::MountMode::Rw => "read-write",
                                super::MountMode::Cache => "shared cache",
                            }
                        )
                    })
                    .collect()
            },
        );
    }

    fn append_preset_detail_fields(
        effective: &[&crate::presets::SandboxPreset],
        fields: &mut Vec<Value>,
    ) {
        for (label, pick) in [
            (
                "Requested read-only binds",
                (|v: &crate::presets::SandboxPreset| &v.ro)
                    as fn(&crate::presets::SandboxPreset) -> &Vec<String>,
            ),
            (
                "Requested read-write binds",
                |v: &crate::presets::SandboxPreset| &v.rw,
            ),
            (
                "Requested device binds",
                |v: &crate::presets::SandboxPreset| &v.dev,
            ),
            ("Seed paths", |v: &crate::presets::SandboxPreset| &v.seed),
            ("Excluded paths", |v: &crate::presets::SandboxPreset| {
                &v.skip
            }),
            ("Shared files", |v: &crate::presets::SandboxPreset| {
                &v.shared
            }),
            ("Private paths", |v: &crate::presets::SandboxPreset| {
                &v.private
            }),
            (
                "Forwarded environment",
                |v: &crate::presets::SandboxPreset| &v.env,
            ),
        ] {
            let mut values = Vec::new();
            for preset in effective {
                for value in pick(preset) {
                    if !values.contains(value) {
                        values.push(value.clone());
                    }
                }
            }
            Self::preview_field(fields, label, values);
        }
        let mut environment = std::collections::BTreeMap::new();
        for preset in effective {
            environment.extend(preset.setenv.clone());
        }
        Self::preview_field(
            fields,
            "Set environment (final)",
            environment
                .into_iter()
                .map(|(key, value)| format!("{key}={value}"))
                .collect(),
        );
    }

    fn preview_field(fields: &mut Vec<Value>, label: &str, values: Vec<String>) {
        fields.push(json!({"label": label, "values": values}));
    }
}

impl SessionCfg {
    pub(crate) fn preset_table(&self) -> crate::presets::Table {
        let mut table = (*crate::presets::table()).clone();
        for snapshot in &self.sandbox_snapshots {
            table.sandbox.retain(|p| p.name != snapshot.name);
            table.sandbox.push(snapshot.clone());
        }
        table
    }
}

#[cfg(test)]
#[path = "resolution_tests.rs"]
mod tests;
