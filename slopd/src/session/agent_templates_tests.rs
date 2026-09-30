use super::*;
use crate::config::{Config, ProjectCfg};

fn command() -> CommandPreset {
    CommandPreset {
        name: "agent".into(),
        cmd: "agent --safe".into(),
        ..Default::default()
    }
}

fn sandbox() -> SandboxPreset {
    SandboxPreset {
        name: "captured".into(),
        description: "Captured sandbox".into(),
        ..Default::default()
    }
}

fn template() -> AgentTemplate {
    AgentTemplate {
        name: "reviewer".into(),
        version: 0,
        description: "Review changes".into(),
        defaults: AgentTemplateDefaults {
            command: Some(command()),
            cmd: None,
            sandbox: vec![],
            sandbox_presets: vec![],
            persistent_tmp: Some(false),
            network: Some(NetworkMode::Private),
            dns: Some(DnsConfig::Resolved),
            limits: Limits::default(),
            autostart: Some(false),
            auto_resume: Some(false),
        },
    }
}

#[test]
fn template_instantiation_mints_fresh_session_identity() {
    let template = template();
    let first = template.instantiate("one".into(), "repo".into());
    let second = template.instantiate("two".into(), "repo".into());
    assert_ne!(first.state_id, second.state_id);
    assert_eq!(first.name, "one");
    assert_eq!(first.project, "repo");
    assert!(first.worker_token.is_none());
}

#[test]
fn sandbox_dependencies_follow_snapshots_instead_of_live_definitions() {
    let cfg = Config::default();
    let project = ProjectCfg::default();
    let mut instance = template().instantiate("agent".into(), "repo".into());
    instance.sandbox = vec!["codex".into()];
    // The live codex preset requires display presets. This captured version instead
    // requires a dependency which exists only in the snapshot, including a nested edge.
    instance.sandbox_snapshots = vec![
        SandboxPreset {
            name: "codex".into(),
            requires: vec!["captured".into()],
            ..Default::default()
        },
        SandboxPreset {
            name: "captured".into(),
            requires: vec!["nested".into()],
            ..Default::default()
        },
        SandboxPreset {
            name: "nested".into(),
            ..Default::default()
        },
    ];
    assert_eq!(
        cfg.sandbox_of(&instance, &project),
        ["global", "nested", "captured", "codex"]
    );
}

#[test]
fn template_store_rejects_duplicate_names() {
    let mut store = AgentTemplateStore::default();
    store.create(template()).unwrap();
    store.create(template()).unwrap_err();
}

#[tokio::test]
async fn template_store_round_trips_its_snapshots() {
    let root = std::env::temp_dir().join(format!("slopd-template-store-{}", uuid::Uuid::new_v4()));
    std::fs::create_dir_all(&root).unwrap();
    let path = root.join("agent_templates");
    let mut saved = template();
    saved.defaults.sandbox = vec!["captured".into()];
    saved.defaults.sandbox_presets = vec![sandbox()];

    let mut store = AgentTemplateStore::default();
    store.create(saved).unwrap();
    store.save(&path).await.unwrap();
    let loaded = AgentTemplateStore::load(&path).await.unwrap();
    let loaded = loaded.get("reviewer").unwrap();
    assert!(loaded.version > 0);
    assert_eq!(loaded.defaults.sandbox[0], "captured");
    assert_eq!(
        loaded.defaults.sandbox_presets[0].description,
        "Captured sandbox"
    );
    drop(std::fs::remove_dir_all(root));
}

#[test]
fn legacy_origin_is_rejected() {
    toml::from_str::<AgentTemplate>(
        r#"
name = "legacy"
version = 4

[origin]
source = "personal"
project = "old-project"
agent = "old-agent"

[defaults]
"#,
    )
    .unwrap_err();
}

#[tokio::test]
async fn version_cursor_survives_reload_and_delete_recreate() {
    let root = std::env::temp_dir().join(format!(
        "slopd-template-version-store-{}",
        uuid::Uuid::new_v4()
    ));
    std::fs::create_dir_all(&root).unwrap();
    let path = root.join("agent_templates");
    let mut store = AgentTemplateStore::default();
    let first = store.create(template()).unwrap();
    store.save(&path).await.unwrap();

    let mut restarted = AgentTemplateStore::load(&path).await.unwrap();
    restarted.remove(&first.name, first.version).unwrap();
    restarted.save(&path).await.unwrap();

    let mut recreated_store = AgentTemplateStore::load(&path).await.unwrap();
    let recreated = recreated_store.create(template()).unwrap();
    assert!(recreated.version > first.version);
    drop(std::fs::remove_dir_all(root));
}

#[test]
fn version_tokens_stay_exact_in_json_clients() {
    let mut store = AgentTemplateStore {
        next_version: MAX_VERSION,
        ..Default::default()
    };
    let last = store.create(template()).unwrap();
    assert_eq!(last.version, MAX_VERSION);
    store
        .replace(&last.name, last.version, last.clone())
        .unwrap_err();
    assert_eq!(store.get(&last.name).unwrap().version, last.version);
}

#[tokio::test]
async fn edits_survive_restart_and_rejected_renames_preserve_both_definitions() {
    let root = std::env::temp_dir().join(format!("slopd-template-edit-{}", uuid::Uuid::new_v4()));
    std::fs::create_dir_all(&root).unwrap();
    let path = root.join("agent_templates");
    let mut store = AgentTemplateStore::default();
    let original = store.create(template()).unwrap();
    let mut other = template();
    other.name = "other".into();
    store.create(other).unwrap();
    store.save(&path).await.unwrap();
    let mut store = AgentTemplateStore::load(&path).await.unwrap();
    let mut draft = original.clone();
    draft.name = "other".into();
    store
        .replace(&original.name, original.version, draft)
        .unwrap_err();
    assert_eq!(store.templates.len(), 2);
    assert_eq!(store.get(&original.name).unwrap().version, original.version);
    let mut draft = original.clone();
    draft.name = "renamed".into();
    let renamed = store
        .replace(&original.name, original.version, draft)
        .unwrap();
    assert!(store.get(&original.name).is_none());
    assert!(store.remove("renamed", original.version).is_err());
    store.save(&path).await.unwrap();
    let store = AgentTemplateStore::load(&path).await.unwrap();
    assert_eq!(store.get("renamed").unwrap().version, renamed.version);
    std::fs::remove_dir_all(root).unwrap();
}

#[test]
fn an_instance_keeps_captured_behavior_when_live_entries_change() {
    let mut saved = template();
    saved.defaults.sandbox = vec!["captured".into()];
    saved.defaults.sandbox_presets = vec![sandbox()];
    let instance = saved.instantiate("new-agent".into(), "repo".into());

    let cfg = Config::default();
    let project = ProjectCfg {
        name: "repo".into(),
        dir: "/tmp".into(),
        ..Default::default()
    };

    assert_eq!(cfg.command_of(&instance), "agent --safe");
    assert_eq!(cfg.sandbox_of(&instance, &project), ["global", "captured"]);

    saved.defaults.command.as_mut().unwrap().cmd = "edited command".into();
    assert_eq!(cfg.command_of(&instance), "agent --safe");
}

#[test]
fn capture_rejects_stale_sandbox_references() {
    let mut command = command();
    command.sandbox = vec!["missing-command-preset".into(), "captured".into()];
    let source = SessionCfg {
        name: "slop-lxh".into(),
        project: "slopworld".into(),
        command_snapshot: Some(command),
        sandbox: vec!["missing-agent-preset".into(), "invalid".into()],
        sandbox_snapshots: vec![
            sandbox(),
            SandboxPreset {
                name: "invalid".into(),
                requires: vec!["missing-dependency".into()],
                ..Default::default()
            },
        ],
        ..Default::default()
    };
    let project = ProjectCfg {
        name: "slopworld".into(),
        ..Default::default()
    };
    let result = AgentTemplate::from_session(
        "copy".into(),
        String::new(),
        &source,
        &project,
        &Config::default(),
    );
    result.unwrap_err();
    // A rejected capture leaves the source settings intact.
    assert!(source.sandbox.contains(&"missing-agent-preset".into()));
}
