use super::*;
use crate::config::{Limits, Mount, MountMode, NetworkMode};
use crate::presets::CommandPreset;
use crate::presets::SandboxPreset;
use crate::session::AgentTemplate;

#[test]
fn changing_command_keeps_only_explicit_sandbox_and_its_dependencies() {
    let source = SessionCfg {
        command: "old-command".into(),
        command_snapshot: Some(CommandPreset {
            name: "old-command".into(),
            sandbox: vec!["command-only".into()],
            ..Default::default()
        }),
        sandbox_snapshots: vec![
            SandboxPreset {
                name: "command-only".into(),
                ..Default::default()
            },
            SandboxPreset {
                name: "selected".into(),
                requires: vec!["dependency".into()],
                ..Default::default()
            },
            SandboxPreset {
                name: "dependency".into(),
                ..Default::default()
            },
            SandboxPreset {
                name: "unused".into(),
                ..Default::default()
            },
        ],
        ..Default::default()
    };
    let mut edit = source.clone();
    edit.command = "new-command".into();
    edit.sandbox = vec!["selected".into()];
    edit.preserve_selected_snapshots(&source);
    assert!(edit.command_snapshot.is_none());
    assert_eq!(
        edit.sandbox_snapshots
            .iter()
            .map(|p| p.name.as_str())
            .collect::<Vec<_>>(),
        ["selected", "dependency"]
    );
    edit.sandbox_snapshots[0].requires.clear();
    assert_eq!(source.sandbox_snapshots[1].requires, ["dependency"]);
    assert!(source.command_snapshot.is_some());
}

#[test]
fn snapshot_selection_handles_cycles_shared_dependencies_and_missing_definitions() {
    let source = SessionCfg {
        sandbox_snapshots: vec![
            SandboxPreset {
                name: "a".into(),
                requires: vec!["b".into(), "missing".into()],
                ..Default::default()
            },
            SandboxPreset {
                name: "b".into(),
                requires: vec!["a".into()],
                ..Default::default()
            },
            SandboxPreset {
                name: "unused".into(),
                ..Default::default()
            },
        ],
        ..Default::default()
    };
    let mut edit = SessionCfg {
        sandbox: vec!["a".into(), "b".into(), "missing".into()],
        ..Default::default()
    };
    edit.preserve_selected_snapshots(&source);
    assert_eq!(
        edit.sandbox_snapshots
            .iter()
            .map(|p| p.name.as_str())
            .collect::<Vec<_>>(),
        ["a", "b"]
    );
    assert_eq!(edit.sandbox, ["a", "b", "missing"]);
    edit.sandbox.clear();
    edit.preserve_selected_snapshots(&source);
    assert!(edit.sandbox_snapshots.is_empty());
}

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
    let result = cfg
        .settings_preview(&edit, &project, false)
        .unwrap()
        .to_string();
    assert!(result.contains("Project mounts"));
    assert!(result.contains("Project: repo"));
    assert!(result.contains("captured copy"));
    assert!(result.contains("custom-command-line"));
    edit.command.clear();
    edit.preserve_selected_snapshots(&source);
    assert!(edit.command_snapshot.is_none());
    assert!(edit.sandbox_snapshots.is_empty());
}
