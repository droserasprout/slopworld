use axum::extract::State;
use axum::http::StatusCode;
use axum::Json;
use serde::Deserialize;
use serde_json::json;

use super::{err, ApiResult, Mgr};
use crate::config::{ProjectCfg, SessionCfg};
use crate::session::AgentTemplate;

#[derive(Deserialize)]
pub(crate) struct SettingsPreviewReq {
    #[serde(default)]
    existing: String,
    session: Option<SessionCfg>,
    project: Option<ProjectCfg>,
    template: Option<AgentTemplate>,
    #[serde(default)]
    recipe: bool,
}

/// Root-only, read-only resolution of an unsaved project, recipe or agent form.
/// Saved snapshots are daemon-owned and retained using the same rule as an actual edit.
pub(crate) async fn settings_preview(
    State(m): State<Mgr>,
    Json(req): Json<SettingsPreviewReq>,
) -> ApiResult {
    m.reload_if_changed().await;
    let cfg = m.config().await;
    let previous = if req.existing.is_empty() {
        None
    } else {
        Some(
            cfg.session(&req.existing)
                .ok_or_else(|| err(StatusCode::NOT_FOUND, "Agent no longer exists"))?
                .clone(),
        )
    };
    let mut session = req
        .session
        .clone()
        .or_else(|| previous.clone())
        .unwrap_or_default();
    session.command_snapshot = None;
    session.sandbox_snapshots.clear();
    session.breadcrumb_snapshots.clear();
    if let Some(template) = req.template {
        let mut base = template.instantiate(session.name.clone(), session.project.clone());
        if req.session.is_some() {
            template.apply_overrides(&mut base, &session);
        }
        session = base;
    } else if let Some(previous) = previous {
        session.preserve_selected_snapshots(&previous);
    }
    let project = req
        .project
        .unwrap_or_else(|| cfg.project(&session.project).cloned().unwrap_or_default());
    if !req.recipe && !session.project.is_empty() && project.name.is_empty() {
        return Err(err(StatusCode::BAD_REQUEST, "Choose an available project"));
    }
    let definitions = json!({"defaults": {
        "command": session.command_snapshot,
        "sandbox_presets": session.sandbox_snapshots,
        "prompts": session.breadcrumb_snapshots,
    }});
    let mut result = cfg.settings_preview(&session, &project, req.recipe);
    result["definitions"] = definitions;
    Ok(Json(result))
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::config::{Limits, NetworkMode};
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
                network: NetworkMode::Private,
                limits: Limits {
                    memory_mb: Some(2048),
                    ..Default::default()
                },
                ..Default::default()
            }],
            ..Default::default()
        };
        let manager = crate::session::test_manager(cfg);
        let request: SettingsPreviewReq = serde_json::from_value(json!({
            "existing":"agent", "session":{"name":"agent","project":"repo","command":"saved","network":"none"}
        })).unwrap();
        let Json(result) = settings_preview(State(manager.clone()), Json(request))
            .await
            .unwrap();
        assert!(result.to_string().contains("original-command"));
        assert!(result.to_string().contains("2048"));
        assert!(result.to_string().contains("agent override"));
        assert_eq!(
            result["definitions"]["defaults"]["command"]["cmd"],
            "original-command"
        );
        assert_eq!(
            manager.config().await.session("agent").unwrap().network,
            None
        );
    }

    #[tokio::test]
    async fn recipe_preview_keeps_unset_values_unresolved_until_creation() {
        let manager = crate::session::test_manager(crate::config::Config::default());
        let request: SettingsPreviewReq = serde_json::from_value(json!({
            "recipe":true,"template":{"name":"recipe","defaults":{}}
        }))
        .unwrap();
        let Json(result) = settings_preview(State(manager.clone()), Json(request))
            .await
            .unwrap();
        assert!(result.to_string().contains("Use destination project"));
        assert!(manager.config().await.sessions.is_empty());
        assert!(manager.agent_templates().await.is_empty());
    }
}
