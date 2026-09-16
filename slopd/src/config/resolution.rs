//! Shared configuration resolution for editor previews. Launch uses the same scalar and
//! dependency resolvers; previews never prepare private state or start a process.
use super::{Config, DnsConfig, ProjectCfg, SessionCfg};
use serde_json::{json, Value};

impl SessionCfg {
    /// Preserve only still-selected captured definitions, including transitive dependencies.
    /// A command-line override changes execution, not the selected preset's wiring.
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
    pub(crate) fn settings_preview(&self, s: &SessionCfg, p: &ProjectCfg, recipe: bool) -> Value {
        let mut fields = Vec::new();
        let mut field = |label: &str, values: Vec<String>| {
            fields.push(json!({"label": label, "values": values}))
        };
        let command = self.command_of(s);
        let command = if command.is_empty() {
            "Unavailable command preset; choose an installed command".into()
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
        let effective = crate::sandbox::presets_for(self, s, p, &table);
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
            let status = if effective.iter().any(|v| v.name == name) {
                definition
            } else {
                "missing or invalid; ignored at launch"
            };
            selections.push(format!("{name} — {} — {status}", owners.join(" + ")));
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
        json!({"title": if recipe { "Template recipe" } else { "Effective settings for next start" },
            "subtitle": if recipe { "Portable agent settings; project mounts are applied by the destination project".to_string() } else { format!("Project: {} — {}", p.name, p.dir) },
            "notes": ["This is saved/draft configuration, not the sandbox of an already running process. Mount existence and protected-path checks are applied at launch."],
            "fields": fields})
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
mod tests {
    use super::*;
    use crate::config::{Limits, Mount, MountMode, NetworkMode};
    use crate::presets::CommandPreset;
    use crate::presets::SandboxPreset;
    use crate::session::AgentTemplate;

    #[test]
    fn sparse_recipe_uses_documented_agent_defaults() {
        let sparse: AgentTemplate = toml::from_str("name = 'sparse'\n[defaults]\n").unwrap();
        crate::session::validate_template_definition(&sparse).unwrap();
        let cfg = Config::default();
        let project = ProjectCfg::default();
        let session = sparse.instantiate("agent".into(), "repo".into());
        assert_eq!(cfg.network_of(&session, &project), NetworkMode::Private);
        assert_eq!(cfg.limits_of(&session, &project), Limits::default());
    }

    #[test]
    fn capture_copies_agent_settings_but_not_project_settings() {
        let project = ProjectCfg {
            name: "source".into(),
            ..Default::default()
        };
        let cfg = Config::default();
        let source = SessionCfg {
            name: "source".into(),
            network: NetworkMode::Host,
            limits: Limits {
                memory_mb: Some(512),
                ..Default::default()
            },
            ..Default::default()
        };
        let sparse =
            AgentTemplate::capture("sparse".into(), "".into(), &source, &project, &cfg).unwrap();
        assert_eq!(sparse.defaults.network, Some(NetworkMode::Host));
        assert_eq!(
            sparse.defaults.dns,
            Some(crate::config::DnsConfig::Resolved)
        );
        assert!(sparse.defaults.command.is_none());
        assert!(sparse.defaults.sandbox.is_empty());
        assert_eq!(sparse.defaults.limits.memory_mb, Some(512));
        assert!(source.sandbox.is_empty());
    }

    #[test]
    fn editing_and_preview_keep_command_wiring_and_transitive_snapshots() {
        let source = SessionCfg {
            command: "captured-agent".into(),
            command_snapshot: Some(CommandPreset {
                name: "captured-agent".into(),
                cmd: "captured-command".into(),
                sandbox: vec!["parent".into()],
                ..Default::default()
            }),
            sandbox_snapshots: vec![
                SandboxPreset {
                    name: "parent".into(),
                    requires: vec!["dependency".into()],
                    ..Default::default()
                },
                SandboxPreset {
                    name: "dependency".into(),
                    ..Default::default()
                },
            ],
            ..Default::default()
        };
        let mut edit = source.clone();
        edit.cmd = Some("custom-command-line".into());
        edit.preserve_selected_snapshots(&source);
        assert!(edit.command_snapshot.is_some());
        assert_eq!(edit.sandbox_snapshots.len(), 2);
        let cfg = Config::default();
        let project = ProjectCfg {
            name: "repo".into(),
            mounts: vec![Mount {
                from: "/tmp".into(),
                to: "/mnt/other".into(),
                mode: MountMode::Ro,
            }],
            ..Default::default()
        };
        let result = cfg.settings_preview(&edit, &project, false).to_string();
        assert!(result.contains("Project mounts"));
        assert!(result.contains("Project: repo"));
        assert!(result.contains("captured copy"));
        assert!(result.contains("custom-command-line"));
        edit.command.clear();
        edit.preserve_selected_snapshots(&source);
        assert!(edit.command_snapshot.is_none());
        assert!(edit.sandbox_snapshots.is_empty());
    }
}
