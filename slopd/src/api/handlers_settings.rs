use crate::api::protobuf::{domain, reply, Proto};
use crate::shared::wire;
use axum::extract::State;
use axum::http::StatusCode;

use serde::Deserialize;
use serde_json::json;
use serde_json::Value;

use super::{err, ApiResult, Mgr};

#[derive(Deserialize)]
pub(crate) struct SettingsPreviewReq {
    #[serde(default)]
    existing: String,
    session: Option<Value>,
    project: Option<Value>,
    template: Option<Value>,
    #[serde(default)]
    recipe: bool,
}

/// Root-only, read-only resolution of an unsaved project, recipe or agent form.
/// Saved snapshots are daemon-owned and retained using the same rule as an actual edit.
pub(crate) async fn settings_preview(
    State(m): State<Mgr>,
    Proto(req): Proto<wire::SettingsPreviewRequest>,
) -> ApiResult<wire::SettingsPreview> {
    let req: SettingsPreviewReq = domain(req)?;
    m.reload_if_changed().await;
    let cfg = m.config().await;
    let requested_session = req
        .session
        .clone()
        .map(crate::api::parse_session)
        .transpose()?;
    let requested_project = req
        .project
        .clone()
        .map(crate::api::parse_project)
        .transpose()?;
    let requested_template = req
        .template
        .clone()
        .map(crate::api::parse_template)
        .transpose()?;
    let previous = if req.existing.is_empty() {
        None
    } else {
        Some(
            cfg.session(&req.existing)
                .ok_or_else(|| err(StatusCode::NOT_FOUND, "Agent no longer exists"))?
                .clone(),
        )
    };
    let mut session = requested_session
        .clone()
        .or_else(|| previous.clone())
        .unwrap_or_default();
    session.command_snapshot = None;
    session.sandbox_snapshots.clear();
    if let Some(template) = requested_template {
        let mut base = template.instantiate(session.name.clone(), session.project.clone());
        if requested_session.is_some() {
            template.apply_overrides(&mut base, &session);
        }
        session = base;
    } else if let Some(previous) = previous {
        session.preserve_selected_snapshots(&previous);
    }
    let project = requested_project
        .unwrap_or_else(|| cfg.project(&session.project).cloned().unwrap_or_default());
    if !req.recipe && !session.project.is_empty() && project.name.is_empty() {
        return Err(err(StatusCode::BAD_REQUEST, "Choose an available project"));
    }
    let definitions = json!({"defaults": {
        "command": session.command_snapshot,
        "sandbox_presets": session.sandbox_snapshots,
    }});
    let mut result = cfg.settings_preview(&session, &project, req.recipe);
    result["definitions"] = definitions;
    reply(result)
}

#[cfg(test)]
mod tests {
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
}
