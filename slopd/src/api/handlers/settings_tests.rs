use super::*;
use crate::config::{NetworkMode, ProjectCfg, SessionCfg};
use crate::presets::CommandPreset;

#[tokio::test]
async fn preview_uses_saved_snapshots_and_unsaved_overrides_without_persisting() {
    let original = SessionCfg {
        name: "agent".into(),
        project: "repo".into(),
        command: "saved".into(),
        command_snapshot: Some(CommandPreset {
            name: "saved".into(),
            cmd: "original-command".into(),
            ..Default::default()
        }),
        ..Default::default()
    };
    let cfg = crate::config::Config {
        sessions: vec![original.clone()],
        projects: vec![ProjectCfg {
            name: "repo".into(),
            ..Default::default()
        }],
        ..Default::default()
    };
    let manager = crate::session::test_manager(cfg);
    let request: wire::SettingsPreviewRequest = serde_json::from_value(json!({
            "existing":"agent", "session":{"name":"agent","project":"repo","command":"saved","network":"none","limits":{"memory_mb":2048}}
        })).unwrap();
    let Proto(result) = settings_preview(State(manager.clone()), Proto(request))
        .await
        .unwrap();
    let result = serde_json::to_value(result).unwrap();
    assert!(result.to_string().contains("original-command"));
    assert_eq!(preview_values(&result, "Network"), ["none — agent setting"]);
    assert!(
        preview_values(&result, "Resource limits").contains(&"Memory (MiB): 2048 — agent setting")
    );
    assert_eq!(
        result["definitions"]["defaults"]["command"]["cmd"],
        "original-command"
    );
    let saved = manager.config().await.session("agent").unwrap().clone();
    assert_eq!(saved.network, NetworkMode::Private);
    assert_eq!(saved.network, original.network);
    assert_eq!(saved.limits, original.limits);
}

#[tokio::test]
async fn recipe_preview_uses_explicit_agent_defaults() {
    let manager = crate::session::test_manager(crate::config::Config::default());
    let request: wire::SettingsPreviewRequest = serde_json::from_value(json!({
        "recipe":true,"template":{"name":"recipe","defaults":{}}
    }))
    .unwrap();
    let Proto(result) = settings_preview(State(manager.clone()), Proto(request))
        .await
        .unwrap();
    let result = serde_json::to_value(result).unwrap();
    assert_eq!(
        preview_values(&result, "Network"),
        ["private — agent setting"]
    );
    assert!(manager.config().await.sessions.is_empty());
    assert!(manager.agent_templates().await.is_empty());
}

fn preview_values<'a>(result: &'a serde_json::Value, label: &str) -> Vec<&'a str> {
    result["fields"]
        .as_array()
        .unwrap()
        .iter()
        .find(|field| field["label"] == label)
        .unwrap()["values"]
        .as_array()
        .unwrap()
        .iter()
        .map(|value| value.as_str().unwrap())
        .collect()
}
