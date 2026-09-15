//! Configuration and generated-instructions HTTP boundaries.

use axum::extract::State;
use axum::http::StatusCode;
use axum::Json;
use serde_json::{json, Value};

use crate::config::ProjectCfg;

use super::super::types::*;
use super::{err, ok_json, ApiResult, Mgr};

fn requested_breadcrumb<'a>(draft: Option<&'a str>, saved: &'a str) -> &'a str {
    draft.unwrap_or(saved)
}

pub(crate) async fn get_config(State(m): State<Mgr>) -> ApiResult {
    // Keep the raw text and parsed values from different snapshots when a user edits the file
    // outside the daemon.
    m.reload_if_changed().await;
    let text = std::fs::read_to_string(&m.cfg_path)
        .map_err(|e| err(StatusCode::INTERNAL_SERVER_ERROR, e))?;
    // The token never leaves the daemon as written: the raw text and the parsed values both
    // carry the sentinel, and a write that sends it back is read as "unchanged". The endpoint
    // is behind the token itself, so this guards the one case that is not - the raw editor,
    // and any future client that reaches the config without holding the secret first.
    let cfg = m.config().await;
    let factory = crate::config::Config::default();
    let caps = crate::runtime::capabilities();
    Ok(Json(json!({
        "path": m.cfg_path,
        "text": crate::config::redact_token_text(&text),
        "values": cfg.redacted(),
        "metadata": {
            // Factory defaults are deliberately a response-only read model. They are not
            // accepted as a client patch and contain no secrets.
            "defaults": factory,
            "usage_catalog": crate::usage::catalog(),
            "temporary_root": crate::config::TEMP_ROOT,
            "terminal": caps.terminal,
        },
    })))
}

pub(crate) async fn capabilities() -> ApiResult {
    Ok(Json(json!(crate::runtime::capabilities())))
}

pub(crate) async fn put_config(State(m): State<Mgr>, Json(req): Json<ConfigReq>) -> ApiResult {
    ok_json(m.replace_config(&req.text).await)
}

/// Apply only the fields named by the client, leaving unmentioned fields untouched.
pub(crate) async fn put_config_patch(State(m): State<Mgr>, Json(req): Json<Value>) -> ApiResult {
    ok_json(m.patch_config(req).await)
}

pub(crate) async fn instructions_preview(
    State(m): State<Mgr>,
    Json(req): Json<InstructionsPreviewReq>,
) -> ApiResult {
    let cfg = m.config().await;
    let project = if req.project.trim().is_empty() {
        cfg.projects.first().cloned().unwrap_or_else(|| ProjectCfg {
            name: "project".into(),
            dir: "/workspace/project".into(),
            ..Default::default()
        })
    } else {
        cfg.project(&req.project).cloned().ok_or_else(|| {
            err(
                StatusCode::BAD_REQUEST,
                format!("no such project: {}", req.project),
            )
        })?
    };
    let mount_path = if req.mount_path.trim().is_empty() {
        cfg.daemon.instructions.mount_path.clone()
    } else {
        req.mount_path
    };
    let mut instructions = cfg.daemon.instructions.clone();
    instructions.mount_path = mount_path.clone();
    instructions
        .validate()
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    let breadcrumb = requested_breadcrumb(req.breadcrumb.as_deref(), &instructions.breadcrumb);
    let text =
        crate::manifest::preview(&cfg, &project, &m.views().await, &req.template, &mount_path);
    Ok(Json(json!({
        "text": text,
        "breadcrumb": crate::manifest::render_breadcrumb(breadcrumb, &project.name, &mount_path),
        "project": project.name,
        "mount_path": mount_path,
    })))
}

#[cfg(test)]
mod tests {
    use super::requested_breadcrumb;

    #[test]
    fn empty_breadcrumb_draft_does_not_fall_back_to_saved_text() {
        assert_eq!(requested_breadcrumb(Some(""), "saved"), "");
        assert_eq!(requested_breadcrumb(None, "saved"), "saved");
    }
}
