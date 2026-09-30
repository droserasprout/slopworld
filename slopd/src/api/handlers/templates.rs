//! Personal agent-template catalog, snapshot, and instantiation boundaries.
use crate::api::protobuf::{Proto, domain, reply};
use crate::shared::wire;

use axum::Extension;
use axum::extract::{Path, Query, State};
use axum::http::StatusCode;
use serde_json::json;

use crate::grant::Cap;

use super::super::types::{
    CreateAgentTemplateReq, SaveAgentTemplateReq, SpawnableTemplatesQuery, TemplateVersionQuery,
};
use super::{ApiResult, Mgr, err};

fn template_error(error: anyhow::Error) -> crate::api::protobuf::ApiError {
    let status = match error.downcast_ref::<crate::session::AgentTemplateError>() {
        Some(crate::session::AgentTemplateError::Missing(_)) => StatusCode::NOT_FOUND,
        Some(
            crate::session::AgentTemplateError::Conflict { .. }
            | crate::session::AgentTemplateError::Exists(_),
        ) => StatusCode::CONFLICT,
        None if error
            .downcast_ref::<crate::session::TemplatePersistence>()
            .is_some() =>
        {
            StatusCode::INTERNAL_SERVER_ERROR
        }
        None => StatusCode::BAD_REQUEST,
    };
    err(status, error)
}

pub(crate) async fn list_templates(
    State(m): State<Mgr>,
    Extension(_cap): Extension<Cap>,
    Query(query): Query<SpawnableTemplatesQuery>,
) -> ApiResult<wire::TemplatesReply> {
    let templates = m.agent_templates().await;
    if query.project.trim().is_empty() {
        return reply(json!({ "templates": templates }));
    }
    let project = m
        .worker_project(crate::tasks::HOST, &query.project)
        .await
        .map_err(|error| err(StatusCode::BAD_REQUEST, error))?;
    reply(json!({ "templates": templates, "project": project }))
}

/// Scoped discovery exposes only definitions explicitly enabled by the root worker policy. A
/// caller's project is the default context. Root callers may provide one or omit it to inspect
/// the complete enabled catalog. The response uses the same template shape as the root catalog
/// so agents and the CLI cannot grow a second definition parser.
pub(crate) async fn list_spawnable_templates(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    headers: axum::http::HeaderMap,
    Query(query): Query<SpawnableTemplatesQuery>,
) -> ApiResult<wire::TemplatesReply> {
    let caller = super::task_principal(&cap, &headers)?;
    let project = query.project.trim();
    if caller != crate::tasks::HOST || !project.is_empty() {
        let project = m
            .worker_project(&caller, project)
            .await
            .map_err(|error| err(StatusCode::BAD_REQUEST, error))?;
        let enabled = m.worker_template_names().await;
        return reply(json!({
            "templates": m
                .agent_templates()
                .await
                .into_iter()
                .filter(|template| enabled.contains(&template.name))
                .collect::<Vec<_>>(),
            "project": project,
        }));
    }

    let enabled = m.worker_template_names().await;
    reply(json!({
        "templates": m
            .agent_templates()
            .await
            .into_iter()
            .filter(|template| enabled.contains(&template.name))
            .collect::<Vec<_>>(),
    }))
}

pub(crate) async fn save_template(
    State(m): State<Mgr>,
    Extension(_cap): Extension<Cap>,
    Proto(value): Proto<wire::SaveTemplateRequest>,
) -> ApiResult<wire::TemplateResult> {
    // A complete definition is the management UI's creation/duplication wire form. The
    // session-capture form below remains intentionally small for the existing agent editor.
    let value: serde_json::Value = domain(value)?;
    if value.get("defaults").is_some() {
        let template = crate::api::parse_template(domain(value)?)?;
        if template.version != 0 {
            return Err(err(
                StatusCode::BAD_REQUEST,
                "Use version 0 for a new agent template.",
            ));
        }
        let saved = m
            .create_agent_template_definition(template)
            .await
            .map_err(template_error)?;
        return reply(json!({ "ok": true, "template": saved }));
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
    reply(json!({ "ok": true, "template": saved }))
}

pub(crate) async fn replace_template(
    State(m): State<Mgr>,
    Extension(_cap): Extension<Cap>,
    Path(name): Path<String>,
    Proto(value): Proto<wire::AgentTemplate>,
) -> ApiResult<wire::TemplateResult> {
    let template = crate::api::parse_template(domain(value)?)?;
    if template.version == 0 {
        return Err(err(
            StatusCode::BAD_REQUEST,
            "edit requires template version",
        ));
    }
    let saved = m
        .replace_agent_template_definition(&name, template.version, template)
        .await
        .map_err(template_error)?;
    reply(json!({ "ok": true, "template": saved }))
}

pub(crate) async fn destroy_template(
    State(m): State<Mgr>,
    Extension(_cap): Extension<Cap>,
    Path(name): Path<String>,
    Query(query): Query<TemplateVersionQuery>,
) -> ApiResult<wire::Ack> {
    let version = query
        .version
        .ok_or_else(|| err(StatusCode::BAD_REQUEST, "delete requires template version"))?;
    m.remove_agent_template(&name, version)
        .await
        .map_err(template_error)?;
    reply(json!({ "ok": true }))
}

pub(crate) async fn create_from_template(
    State(m): State<Mgr>,
    Extension(_cap): Extension<Cap>,
    Path(template): Path<String>,
    Proto(req): Proto<wire::CreateAgentTemplateReq>,
) -> ApiResult<wire::SessionResult> {
    let req: CreateAgentTemplateReq = domain(req)?;
    let overrides = req.overrides.map(crate::api::parse_session).transpose()?;
    let session = m
        .create_from_agent_template(&template, req.name, req.project, overrides, req.start)
        .await
        .map_err(|error| err(StatusCode::BAD_REQUEST, error))?;
    reply(json!({ "ok": true, "session": session }))
}

#[cfg(test)]
#[path = "templates_tests.rs"]
mod tests;
