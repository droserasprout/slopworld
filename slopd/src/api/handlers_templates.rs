//! Personal agent-template catalog, snapshot, and instantiation boundaries.

use axum::extract::{Path, State};
use axum::http::StatusCode;
use axum::{Extension, Json};
use serde_json::json;

use crate::grant::Cap;

use super::super::types::{CreateAgentTemplateReq, SaveAgentTemplateReq};
use super::{err, ApiResult, Mgr};

pub(crate) async fn list_templates(
    State(m): State<Mgr>,
    Extension(_cap): Extension<Cap>,
) -> ApiResult {
    Ok(Json(json!({ "templates": m.agent_templates().await })))
}

pub(crate) async fn save_template(
    State(m): State<Mgr>,
    Extension(_cap): Extension<Cap>,
    Json(req): Json<SaveAgentTemplateReq>,
) -> ApiResult {
    m.save_agent_template(&req.source, req.name, req.description)
        .await
        .map_err(|error| err(StatusCode::BAD_REQUEST, error))?;
    Ok(Json(json!({ "ok": true })))
}

pub(crate) async fn replace_template(
    State(m): State<Mgr>,
    Extension(_cap): Extension<Cap>,
    Path(name): Path<String>,
    Json(mut template): Json<crate::session::AgentTemplate>,
) -> ApiResult {
    if template.name != name {
        return Err(err(
            StatusCode::BAD_REQUEST,
            "template name in the path and body must match",
        ));
    }
    template.origin.source = "personal".into();
    m.save_agent_template_definition(template)
        .await
        .map_err(|error| err(StatusCode::BAD_REQUEST, error))?;
    Ok(Json(json!({ "ok": true })))
}

pub(crate) async fn destroy_template(
    State(m): State<Mgr>,
    Extension(_cap): Extension<Cap>,
    Path(name): Path<String>,
) -> ApiResult {
    m.remove_agent_template(&name)
        .await
        .map_err(|error| err(StatusCode::NOT_FOUND, error))?;
    Ok(Json(json!({ "ok": true })))
}

pub(crate) async fn create_from_template(
    State(m): State<Mgr>,
    Extension(_cap): Extension<Cap>,
    Path(template): Path<String>,
    Json(req): Json<CreateAgentTemplateReq>,
) -> ApiResult {
    let session = m
        .create_from_agent_template(&template, req.name, req.project, req.overrides)
        .await
        .map_err(|error| err(StatusCode::BAD_REQUEST, error))?;
    Ok(Json(json!({ "ok": true, "session": session })))
}
