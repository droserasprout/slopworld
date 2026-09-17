//! Personal agent-template catalog, snapshot, and instantiation boundaries.

use axum::extract::{Path, Query, State};
use axum::http::StatusCode;
use axum::{Extension, Json};
use serde_json::json;

use crate::grant::Cap;

use super::super::types::{
    CreateAgentTemplateReq, SaveAgentTemplateReq, SpawnableTemplatesQuery, TemplateVersionQuery,
};
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
    Query(query): Query<SpawnableTemplatesQuery>,
) -> ApiResult {
    let templates = m.agent_templates().await;
    if query.project.trim().is_empty() {
        return Ok(Json(json!({ "templates": templates })));
    }
    let project = m
        .worker_project(crate::tasks::HOST, &query.project)
        .await
        .map_err(|error| err(StatusCode::BAD_REQUEST, error))?;
    Ok(Json(json!({ "templates": templates, "project": project })))
}

/// Scoped discovery exposes only definitions explicitly enabled by the root worker policy. A
/// caller's project is the default context; root callers may provide one or omit it to inspect
/// the complete enabled catalog. The response uses the same template shape as the root catalog
/// so agents and the CLI cannot grow a second definition parser.
pub(crate) async fn list_spawnable_templates(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    headers: axum::http::HeaderMap,
    Query(query): Query<SpawnableTemplatesQuery>,
) -> ApiResult {
    let caller = super::task_principal(&cap, &headers)?;
    let project = query.project.trim();
    if caller != crate::tasks::HOST || !project.is_empty() {
        let project = m
            .worker_project(&caller, project)
            .await
            .map_err(|error| err(StatusCode::BAD_REQUEST, error))?;
        let enabled = m.worker_template_names().await;
        return Ok(Json(json!({
            "templates": m
                .agent_templates()
                .await
                .into_iter()
                .filter(|template| enabled.contains(&template.name))
                .collect::<Vec<_>>(),
            "project": project,
        })));
    }

    let enabled = m.worker_template_names().await;
    Ok(Json(json!({
        "templates": m
            .agent_templates()
            .await
            .into_iter()
            .filter(|template| enabled.contains(&template.name))
            .collect::<Vec<_>>(),
    })))
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
        .create_from_agent_template(&template, req.name, req.project, overrides, req.start)
        .await
        .map_err(|error| err(StatusCode::BAD_REQUEST, error))?;
    Ok(Json(json!({ "ok": true, "session": session })))
}
