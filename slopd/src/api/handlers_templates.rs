//! Personal agent-template catalog, snapshot, and instantiation boundaries.

use axum::extract::{Path, Query, State};
use axum::http::StatusCode;
use axum::{Extension, Json};
use serde_json::json;

use crate::grant::Cap;

use super::super::types::{CreateAgentTemplateReq, SaveAgentTemplateReq, TemplateVersionQuery};
use super::{err, ApiResult, Mgr};

fn template_error(error: anyhow::Error) -> (StatusCode, Json<serde_json::Value>) {
    let status = match error.downcast_ref::<crate::session::AgentTemplateError>() {
        Some(crate::session::AgentTemplateError::Missing(_)) => StatusCode::NOT_FOUND,
        Some(crate::session::AgentTemplateError::Conflict { .. }) => StatusCode::CONFLICT,
        Some(crate::session::AgentTemplateError::Exists(_)) => StatusCode::CONFLICT,
        None => StatusCode::BAD_REQUEST,
    };
    err(status, error)
}

pub(crate) async fn list_templates(
    State(m): State<Mgr>,
    Extension(_cap): Extension<Cap>,
) -> ApiResult {
    Ok(Json(json!({ "templates": m.agent_templates().await })))
}

pub(crate) async fn save_template(
    State(m): State<Mgr>,
    Extension(_cap): Extension<Cap>,
    Json(value): Json<serde_json::Value>,
) -> ApiResult {
    // A complete definition is the management UI's creation/duplication wire form. The
    // session-capture form below remains intentionally small for the existing agent editor.
    if value.get("defaults").is_some() {
        let mut template = crate::api::parse_template(value)?;
        if template.version != 0 {
            return Err(err(
                StatusCode::BAD_REQUEST,
                "new agent templates must omit version",
            ));
        }
        template.origin.source = "personal".into();
        let saved = m
            .create_agent_template_definition(template)
            .await
            .map_err(template_error)?;
        return Ok(Json(json!({ "ok": true, "template": saved })));
    }

    let req: SaveAgentTemplateReq =
        serde_json::from_value(value).map_err(|error| err(StatusCode::BAD_REQUEST, error))?;
    let saved = if !req.duplicate.trim().is_empty() {
        m.duplicate_agent_template(&req.duplicate, req.name, req.description)
            .await
            .map_err(template_error)?
    } else {
        m.capture_agent_template(&req.source, req.name, req.description)
            .await
            .map_err(template_error)?
    };
    Ok(Json(json!({ "ok": true, "template": saved })))
}

pub(crate) async fn replace_template(
    State(m): State<Mgr>,
    Extension(_cap): Extension<Cap>,
    Path(name): Path<String>,
    Json(value): Json<serde_json::Value>,
) -> ApiResult {
    let mut template = crate::api::parse_template(value)?;
    if template.version == 0 {
        return Err(err(
            StatusCode::BAD_REQUEST,
            "edit requires template version",
        ));
    }
    template.origin.source = "personal".into();
    let saved = m
        .replace_agent_template_definition(&name, template.version, template)
        .await
        .map_err(template_error)?;
    Ok(Json(json!({ "ok": true, "template": saved })))
}

pub(crate) async fn destroy_template(
    State(m): State<Mgr>,
    Extension(_cap): Extension<Cap>,
    Path(name): Path<String>,
    Query(query): Query<TemplateVersionQuery>,
) -> ApiResult {
    let version = query
        .version
        .ok_or_else(|| err(StatusCode::BAD_REQUEST, "delete requires template version"))?;
    m.remove_agent_template(&name, version)
        .await
        .map_err(template_error)?;
    Ok(Json(json!({ "ok": true })))
}

pub(crate) async fn create_from_template(
    State(m): State<Mgr>,
    Extension(_cap): Extension<Cap>,
    Path(template): Path<String>,
    Json(req): Json<CreateAgentTemplateReq>,
) -> ApiResult {
    let overrides = req.overrides.map(crate::api::parse_session).transpose()?;
    let session = m
        .create_from_agent_template(&template, req.name, req.project, overrides)
        .await
        .map_err(|error| err(StatusCode::BAD_REQUEST, error))?;
    Ok(Json(json!({ "ok": true, "session": session })))
}
