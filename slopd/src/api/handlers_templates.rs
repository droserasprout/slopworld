//! Personal agent-template catalog, snapshot, and instantiation boundaries.
use crate::api::protobuf::{domain, reply, Proto};
use crate::shared::wire;

use axum::extract::{Path, Query, State};
use axum::http::StatusCode;
use axum::Extension;
use serde_json::json;

use crate::grant::Cap;

use super::super::types::{
    CreateAgentTemplateReq, SaveAgentTemplateReq, SpawnableTemplatesQuery, TemplateVersionQuery,
};
use super::{err, ApiResult, Mgr};

fn template_error(error: anyhow::Error) -> crate::api::protobuf::ApiError {
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
/// caller's project is the default context; root callers may provide one or omit it to inspect
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
                "new agent templates must omit version",
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
mod tests {
    use super::*;
    use crate::config::Config;

    fn proto<T: serde::de::DeserializeOwned>(value: serde_json::Value) -> Proto<T> {
        Proto(serde_json::from_value(value).unwrap())
    }

    #[tokio::test]
    async fn template_crud_rejects_stale_versions_and_preserves_winner() {
        let m = crate::session::test_manager(Config::default());
        let draft = json!({"name":"reviewer", "description":"original", "defaults":{}});
        let saved = save_template(State(m.clone()), Extension(Cap::Root), proto(draft.clone()))
            .await
            .unwrap()
            .0
            .template
            .unwrap();
        let version = saved.version;
        assert!(version > 0);
        let error = save_template(State(m.clone()), Extension(Cap::Root), proto(draft))
            .await
            .unwrap_err();
        assert_eq!(error.0, StatusCode::CONFLICT);
        let mut edited = saved.clone();
        edited.description = "winner".into();
        let winner = replace_template(
            State(m.clone()),
            Extension(Cap::Root),
            Path("reviewer".into()),
            Proto(edited),
        )
        .await
        .unwrap()
        .0
        .template
        .unwrap();
        assert!(winner.version > version);
        assert_eq!(
            replace_template(
                State(m.clone()),
                Extension(Cap::Root),
                Path("reviewer".into()),
                Proto(saved)
            )
            .await
            .unwrap_err()
            .0,
            StatusCode::CONFLICT
        );
        assert_eq!(
            destroy_template(
                State(m.clone()),
                Extension(Cap::Root),
                Path("reviewer".into()),
                Query(TemplateVersionQuery {
                    version: Some(version)
                })
            )
            .await
            .unwrap_err()
            .0,
            StatusCode::CONFLICT
        );
        let catalog = list_templates(
            State(m.clone()),
            Extension(Cap::Root),
            Query(SpawnableTemplatesQuery::default()),
        )
        .await
        .unwrap()
        .0;
        assert_eq!(catalog.templates.len(), 1);
        assert_eq!(catalog.templates[0].description, "winner");
        assert!(
            destroy_template(
                State(m.clone()),
                Extension(Cap::Root),
                Path("reviewer".into()),
                Query(TemplateVersionQuery {
                    version: Some(winner.version)
                })
            )
            .await
            .unwrap()
            .0
            .ok
        );
        assert_eq!(
            destroy_template(
                State(m.clone()),
                Extension(Cap::Root),
                Path("reviewer".into()),
                Query(TemplateVersionQuery {
                    version: Some(winner.version)
                })
            )
            .await
            .unwrap_err()
            .0,
            StatusCode::NOT_FOUND
        );
        assert!(m.agent_templates().await.is_empty());
    }

    #[tokio::test]
    async fn template_mutations_require_correct_version_shape() {
        let m = crate::session::test_manager(Config::default());
        assert_eq!(
            save_template(
                State(m.clone()),
                Extension(Cap::Root),
                proto(json!({"name":"reviewer","version":1,"defaults":{}}))
            )
            .await
            .unwrap_err()
            .0,
            StatusCode::BAD_REQUEST
        );
        assert_eq!(
            replace_template(
                State(m.clone()),
                Extension(Cap::Root),
                Path("reviewer".into()),
                proto(json!({"name":"reviewer","defaults":{}}))
            )
            .await
            .unwrap_err()
            .0,
            StatusCode::BAD_REQUEST
        );
        assert_eq!(
            destroy_template(
                State(m.clone()),
                Extension(Cap::Root),
                Path("reviewer".into()),
                Query(TemplateVersionQuery { version: None })
            )
            .await
            .unwrap_err()
            .0,
            StatusCode::BAD_REQUEST
        );
        assert!(m.agent_templates().await.is_empty());
    }

    #[tokio::test]
    async fn duplicate_is_independent_and_missing_sources_are_rejected() {
        let m = crate::session::test_manager(Config::default());
        save_template(
            State(m.clone()),
            Extension(Cap::Root),
            proto(json!({"name":"original","defaults":{}})),
        )
        .await
        .unwrap();
        let duplicate = save_template(
            State(m.clone()),
            Extension(Cap::Root),
            proto(json!({"name":"copy","duplicate":"original","description":"copied"})),
        )
        .await
        .unwrap()
        .0
        .template
        .unwrap();
        assert_eq!(duplicate.name, "copy");
        assert_eq!(duplicate.description, "copied");
        assert_eq!(m.agent_templates().await.len(), 2);
        assert_eq!(
            save_template(
                State(m.clone()),
                Extension(Cap::Root),
                proto(json!({"name":"missing-copy","duplicate":"missing"}))
            )
            .await
            .unwrap_err()
            .0,
            StatusCode::NOT_FOUND
        );
        assert_eq!(
            save_template(
                State(m.clone()),
                Extension(Cap::Root),
                proto(json!({"name":"capture","source":"missing"}))
            )
            .await
            .unwrap_err()
            .0,
            StatusCode::BAD_REQUEST
        );
        assert_eq!(
            list_templates(
                State(m.clone()),
                Extension(Cap::Root),
                Query(SpawnableTemplatesQuery {
                    project: "missing".into()
                })
            )
            .await
            .unwrap_err()
            .0,
            StatusCode::BAD_REQUEST
        );
        assert_eq!(
            list_spawnable_templates(
                State(m.clone()),
                Extension(Cap::Root),
                Default::default(),
                Query(SpawnableTemplatesQuery::default())
            )
            .await
            .unwrap_err()
            .0,
            StatusCode::BAD_REQUEST
        );
        let mut headers = axum::http::HeaderMap::new();
        headers.insert("x-slop-session", "host".parse().unwrap());
        let spawnable = list_spawnable_templates(
            State(m),
            Extension(Cap::Root),
            headers,
            Query(SpawnableTemplatesQuery::default()),
        )
        .await
        .unwrap()
        .0;
        assert!(spawnable.templates.is_empty());
    }
    #[tokio::test]
    async fn spawnable_catalog_filters_policy_and_enforces_agent_project() {
        use crate::config::{ProjectCfg, SessionCfg};
        use crate::grant::{Grant, Level};
        let mut cfg = Config::default();
        cfg.daemon.worker_templates.insert("enabled".into());
        cfg.projects = ["own", "other"]
            .into_iter()
            .map(|name| ProjectCfg {
                name: name.into(),
                dir: std::env::temp_dir().to_string_lossy().into_owned(),
                ..Default::default()
            })
            .collect();
        cfg.sessions.push(SessionCfg {
            name: "caller".into(),
            project: "own".into(),
            ..Default::default()
        });
        let m = crate::session::test_manager(cfg);
        for name in ["enabled", "disabled"] {
            save_template(
                State(m.clone()),
                Extension(Cap::Root),
                proto(json!({"name":name,"defaults":{}})),
            )
            .await
            .unwrap();
        }
        let cap = Cap::Scoped(Grant {
            grantor: "caller".into(),
            sessions: Default::default(),
            level: Level::Ro,
            revoked: Default::default(),
        });
        let catalog = list_spawnable_templates(
            State(m.clone()),
            Extension(cap.clone()),
            Default::default(),
            Query(SpawnableTemplatesQuery::default()),
        )
        .await
        .unwrap()
        .0;
        assert_eq!(catalog.project.as_deref(), Some("own"));
        assert_eq!(catalog.templates.len(), 1);
        assert_eq!(catalog.templates[0].name, "enabled");
        assert_eq!(
            list_spawnable_templates(
                State(m.clone()),
                Extension(cap),
                Default::default(),
                Query(SpawnableTemplatesQuery {
                    project: "other".into()
                })
            )
            .await
            .unwrap_err()
            .0,
            StatusCode::BAD_REQUEST
        );
        let root_catalog = list_templates(
            State(m),
            Extension(Cap::Root),
            Query(SpawnableTemplatesQuery {
                project: "other".into(),
            }),
        )
        .await
        .unwrap()
        .0;
        assert_eq!(root_catalog.project.as_deref(), Some("other"));
        assert_eq!(root_catalog.templates.len(), 2);
    }
}
