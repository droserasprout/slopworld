//! Shared configuration resolution for editor previews. Launch uses the same scalar,
//! dependency and prompt resolvers; previews never prepare private state or start a process.
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
        self.breadcrumb_snapshots = previous
            .breadcrumb_snapshots
            .iter()
            .filter(|p| self.breadcrumbs.contains(&p.name))
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
        let project_source = if recipe {
            "destination project".to_string()
        } else {
            format!("project {}", p.name)
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
            vec![if recipe && s.network.is_none() {
                "Use destination project".into()
            } else {
                format!(
                    "{} — {}",
                    serde_json::to_value(network).unwrap().as_str().unwrap(),
                    if s.network.is_some() {
                        "agent override"
                    } else {
                        &project_source
                    }
                )
            }],
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
        field(
            "DNS",
            vec![if recipe && s.dns.is_none() {
                "Use destination project / daemon resolver".into()
            } else {
                format!(
                    "{dns_label} — {}",
                    if s.dns.is_some() {
                        "agent override"
                    } else if p.dns.is_some() {
                        &project_source
                    } else {
                        "daemon resolver"
                    }
                )
            }],
        );
        let limits = self.limits_of(s, p);
        let mut caps = Vec::new();
        for (label, own, inherited, effective) in [
            (
                "Memory (MiB)",
                s.limits.memory_mb,
                p.limits.memory_mb,
                limits.memory_mb,
            ),
            ("Processes", s.limits.pids, p.limits.pids, limits.pids),
            (
                "Open files",
                s.limits.nofile,
                p.limits.nofile,
                limits.nofile,
            ),
            (
                "CPU (%)",
                s.limits.cpu_pct,
                p.limits.cpu_pct,
                limits.cpu_pct,
            ),
        ] {
            caps.push(if recipe && own.is_none() {
                format!("{label}: use destination project")
            } else {
                format!(
                    "{label}: {} — {}",
                    effective
                        .map(|v| v.to_string())
                        .unwrap_or("no configured cap".into()),
                    if own.is_some() {
                        "agent override"
                    } else if inherited.is_some() {
                        &project_source
                    } else {
                        "default"
                    }
                )
            });
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
            if p.sandbox.contains(&name) {
                owners.push("project");
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
        let mut names = p.breadcrumbs.clone();
        for name in &s.breadcrumbs {
            if !names.contains(name) {
                names.push(name.clone());
            }
        }
        let mut prompts = Vec::new();
        for name in names {
            let owner = if p.breadcrumbs.contains(&name) {
                "project"
            } else {
                "agent"
            };
            let snapshot = s.breadcrumb_snapshots.iter().find(|v| v.name == name);
            let text = snapshot.map(|v| v.text.clone()).or_else(|| {
                self.library_item(&name)
                    .filter(|v| v.kind == super::LibraryItemKind::Breadcrumb)
                    .map(|v| v.text)
            });
            prompts.push(format!(
                "{name} — {owner} — {}\n{}",
                if snapshot.is_some() {
                    "captured copy"
                } else {
                    "live definition"
                },
                text.unwrap_or("Missing breadcrumb".into())
            ));
        }
        field("Breadcrumb contributions (delivery order)", prompts);
        if self.daemon.experimental_breadcrumbs
            && self.daemon.experimental_instructions
            && s.slopworld_md
            && s.instructions_breadcrumb
            && self.daemon.instructions.breadcrumb_enabled
        {
            field(
                "Instructions discovery",
                vec![crate::manifest::render_breadcrumb(
                    &self.daemon.instructions.breadcrumb,
                    &p.name,
                    &self.daemon.instructions.mount_path,
                )],
            );
        }
        field(
            "Startup and session behavior",
            vec![
                format!("Start with daemon: {}", s.autostart),
                format!("Auto-resume: {}", s.auto_resume),
                format!("Persistent /tmp: {}", s.persistent_tmp),
                format!(
                    "Mount SLOPWORLD.md: {} (requires daemon instructions enabled)",
                    s.slopworld_md
                ),
                format!("Instructions discovery: {}", s.instructions_breadcrumb),
                format!(
                    "Paste breadcrumbs on first Enter: {} (requires daemon breadcrumbs enabled)",
                    s.breadcrumb_yolo
                ),
            ],
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
            "subtitle": if recipe { "Unspecified values are resolved in the destination project".to_string() } else { format!("Project: {} — {}", p.name, p.dir) },
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
    use crate::config::{BreadcrumbSnapshot, LibraryItemCfg, LibraryItemKind, Limits, NetworkMode};
    use crate::presets::{CommandPreset, SandboxPreset};
    use crate::session::AgentTemplate;

    #[test]
    fn sparse_recipe_uses_destination_defaults_and_legacy_explicit_values_stay_pinned() {
        let sparse: AgentTemplate = toml::from_str("name = 'sparse'\n[defaults]\n").unwrap();
        crate::session::validate_template_definition(&sparse).unwrap();
        let cfg = Config::default();
        let mut project = ProjectCfg {
            network: NetworkMode::Host,
            limits: Limits {
                memory_mb: Some(1024),
                ..Default::default()
            },
            ..Default::default()
        };
        let session = sparse.instantiate("agent".into(), "repo".into());
        assert_eq!(session.network, None);
        assert_eq!(session.dns, None);
        assert_eq!(cfg.network_of(&session, &project), NetworkMode::Host);
        assert_eq!(cfg.limits_of(&session, &project).memory_mb, Some(1024));
        assert!(session.instructions_breadcrumb);
        let legacy: AgentTemplate = toml::from_str("name = 'legacy'\n[defaults]\nnetwork = 'private'\nautostart = false\n[defaults.dns]\nmode = 'resolved'\n[defaults.limits]\nmemory_mb = 512\n").unwrap();
        let pinned = legacy.instantiate("legacy-agent".into(), "repo".into());
        project.network = NetworkMode::None;
        project.limits.memory_mb = Some(2048);
        assert_eq!(cfg.network_of(&session, &project), NetworkMode::None);
        assert_eq!(cfg.network_of(&pinned, &project), NetworkMode::Private);
        assert_eq!(cfg.limits_of(&pinned, &project).memory_mb, Some(512));
        let back: AgentTemplate = toml::from_str(&toml::to_string(&legacy).unwrap()).unwrap();
        assert_eq!(back.defaults.network, Some(NetworkMode::Private));
        assert_eq!(back.defaults.autostart, Some(false));
    }

    #[test]
    fn capture_defaults_to_explicit_choices_and_full_capture_is_opt_in() {
        let project = ProjectCfg {
            name: "source".into(),
            network: NetworkMode::Host,
            sandbox: vec!["git".into()],
            breadcrumbs: vec!["project-rules".into()],
            limits: Limits {
                memory_mb: Some(512),
                ..Default::default()
            },
            ..Default::default()
        };
        let cfg = Config {
            library: vec![LibraryItemCfg {
                name: "project-rules".into(),
                kind: LibraryItemKind::Breadcrumb,
                text: "Project-only rules".into(),
                ..Default::default()
            }],
            ..Default::default()
        };
        let source = SessionCfg {
            name: "source".into(),
            ..Default::default()
        };
        let sparse =
            AgentTemplate::capture("sparse".into(), "".into(), &source, &project, &cfg, false)
                .unwrap();
        assert_eq!(sparse.defaults.network, None);
        assert_eq!(sparse.defaults.dns, None);
        assert!(sparse.defaults.command.is_none());
        assert!(sparse.defaults.sandbox.is_empty());
        assert!(sparse.defaults.prompts.is_empty());
        assert!(sparse.defaults.limits.is_empty());
        let full = AgentTemplate::capture("full".into(), "".into(), &source, &project, &cfg, true)
            .unwrap();
        assert_eq!(full.defaults.network, Some(NetworkMode::Host));
        assert_eq!(full.defaults.limits.memory_mb, Some(512));
        assert!(full.defaults.sandbox.contains(&"global".into()));
        assert_eq!(full.defaults.prompts[0].text, "Project-only rules");
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
            breadcrumbs: vec!["rules".into()],
            breadcrumb_snapshots: vec![BreadcrumbSnapshot {
                name: "rules".into(),
                text: "Captured text".into(),
            }],
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
            limits: Limits {
                memory_mb: Some(1024),
                ..Default::default()
            },
            ..Default::default()
        };
        let result = cfg.settings_preview(&edit, &project, false).to_string();
        assert!(result.contains("1024"));
        assert!(result.contains("project repo"));
        assert!(result.contains("Captured text"));
        assert!(result.contains("captured copy"));
        assert!(result.contains("custom-command-line"));
        edit.command.clear();
        edit.breadcrumbs.clear();
        edit.preserve_selected_snapshots(&source);
        assert!(edit.command_snapshot.is_none());
        assert!(edit.sandbox_snapshots.is_empty());
        assert!(edit.breadcrumb_snapshots.is_empty());
    }
}
