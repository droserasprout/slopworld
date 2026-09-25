//! Shared configuration resolution for editor previews.
//! Launch uses the same scalar and dependency resolvers.
//! Previews do not prepare private state or start a process.
use super::{Config, DnsConfig, ProjectCfg, SessionCfg};
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
    pub(crate) fn settings_preview(
        &self,
        s: &SessionCfg,
        p: &ProjectCfg,
        recipe: bool,
    ) -> anyhow::Result<Value> {
        let mut fields = Vec::new();
        let mut field = |label: &str, values: Vec<String>| {
            fields.push(json!({"label": label, "values": values}))
        };
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
        field(
            "Command",
            vec![if recipe && s.command.is_empty() && s.cmd.is_none() {
                "Use destination daemon's default command".into()
            } else {
                format!("{command} — {command_source}")
            }],
        );
        let network = self.network_of(s, p);
        field(
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
        field("DNS", vec![format!("{dns_label} — agent setting")]);
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
        field("Resource limits", caps);
        let table = s.preset_table();
        let effective = crate::sandbox::presets_for(self, s, p, &table)?;
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
        field("Sandbox contributions", selections);
        field(
            "Startup and session behavior",
            vec![
                format!("Start with daemon: {}", s.autostart),
                format!("Auto-resume: {}", s.auto_resume),
                format!("Persistent /tmp: {}", s.persistent_tmp),
                "Library breadcrumbs are inserted manually from the terminal context menu".into(),
            ],
        );
        field(
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
            for preset in &effective {
                for value in pick(preset) {
                    if !values.contains(value) {
                        values.push(value.clone());
                    }
                }
            }
            field(label, values);
        }
        let mut environment = std::collections::BTreeMap::new();
        for preset in &effective {
            environment.extend(preset.setenv.clone());
        }
        field(
            "Set environment (final)",
            environment
                .into_iter()
                .map(|(key, value)| format!("{key}={value}"))
                .collect(),
        );
        Ok(
            json!({"title": if recipe { "Template recipe" } else { "Effective settings for next start" },
            "subtitle": if recipe { "Portable agent settings. The destination project applies project mounts.".to_string() } else { format!("Project: {} — {}", p.name, p.dir) },
            "notes": ["This is saved or draft configuration. It does not describe a running process. The daemon checks mount existence and protected paths at launch."],
            "fields": fields}),
        )
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
