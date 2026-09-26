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
        sessions: vec![original],
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
    assert!(result.to_string().contains("2048"));
    assert!(result.to_string().contains("agent setting"));
    assert_eq!(
        result["definitions"]["defaults"]["command"]["cmd"],
        "original-command"
    );
    assert_eq!(
        manager.config().await.session("agent").unwrap().network,
        NetworkMode::Private
    );
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
    assert!(result.to_string().contains("agent setting"));
    assert!(manager.config().await.sessions.is_empty());
    assert!(manager.agent_templates().await.is_empty());
}
