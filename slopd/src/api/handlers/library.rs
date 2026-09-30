use crate::api::protobuf::{Proto, domain, reply};
use crate::shared::wire;
use axum::extract::{Path, State};

use serde_json::json;

use crate::config::LibraryItemCfg;
use crate::session::RunWhere;

use super::super::types::*;
use super::{ApiResult, Mgr, err};

pub(crate) async fn list_projects(State(m): State<Mgr>) -> ApiResult<wire::ProjectsReply> {
    reply(json!({ "projects": m.projects().await }))
}

pub(crate) async fn one_project(
    State(m): State<Mgr>,
    Path(name): Path<String>,
) -> ApiResult<wire::Project> {
    m.projects()
        .await
        .into_iter()
        .find(|p| p.config.name == name)
        .map(|p| reply(json!(p)))
        .ok_or_else(|| {
            err(
                axum::http::StatusCode::NOT_FOUND,
                format!("Project {name:?} does not exist."),
            )
        })?
}

pub(crate) async fn create_project(
    State(m): State<Mgr>,
    Proto(value): Proto<wire::Project>,
) -> ApiResult<wire::Ack> {
    let p = crate::api::parse_project(domain(value)?)?;
    super::ok_json(m.add_project(p).await)
}

pub(crate) async fn project_preview(
    Proto(req): Proto<wire::ProjectPreviewReq>,
) -> ApiResult<wire::ProjectPreviewResult> {
    let req: ProjectPreviewReq = domain(req)?;
    reply(json!({
        "name": req.name,
        "temp": req.temp,
        "dir": if req.temp { crate::paths::temp_dir(&req.name) } else { String::new() },
    }))
}

pub(crate) async fn update_project(
    State(m): State<Mgr>,
    Path(name): Path<String>,
    Proto(value): Proto<wire::Project>,
) -> ApiResult<wire::Ack> {
    let p = crate::api::parse_project(domain(value)?)?;
    super::ok_json(m.update_project(&name, p).await)
}

pub(crate) async fn destroy_project(
    State(m): State<Mgr>,
    Path(name): Path<String>,
) -> ApiResult<wire::Ack> {
    super::ok_json(m.remove_project(&name).await)
}

pub(crate) async fn list_library(State(m): State<Mgr>) -> ApiResult<wire::LibraryReply> {
    reply(json!({ "library": m.library().await }))
}

pub(crate) async fn create_library_item(
    State(m): State<Mgr>,
    Proto(sc): Proto<wire::LibraryItem>,
) -> ApiResult<wire::Ack> {
    let sc: LibraryItemCfg = domain(sc)?;
    super::ok_json(m.add_library_item(sc).await)
}

pub(crate) async fn update_library_item(
    State(m): State<Mgr>,
    Path(name): Path<String>,
    Proto(sc): Proto<wire::LibraryItem>,
) -> ApiResult<wire::Ack> {
    let sc: LibraryItemCfg = domain(sc)?;
    super::ok_json(m.update_library_item(&name, sc).await)
}

pub(crate) async fn destroy_library_item(
    State(m): State<Mgr>,
    Path(name): Path<String>,
) -> ApiResult<wire::Ack> {
    super::ok_json(m.remove_library_item(&name).await)
}

/// Return the temporary agent's name after startup, before delayed text delivery.
/// This prevents the client from waiting for the text and reaching its timeout.
/// The body selects the destination with `{"project":"..."}` or `{"temp":true}`.
/// `Option<Proto<wire::RunWhere>>` permits a missing body when the errand specifies its destination.
pub(crate) async fn run_library_item(
    State(m): State<Mgr>,
    Path(name): Path<String>,
    want: Option<Proto<wire::RunWhere>>,
) -> ApiResult<wire::SessionResult> {
    let session = m
        .run_library_item(
            &name,
            want.map(|Proto(w)| domain::<RunWhere>(w))
                .transpose()?
                .unwrap_or_default(),
        )
        .await
        .map_err(|e| err(axum::http::StatusCode::BAD_REQUEST, e))?;
    reply(json!({ "ok": true, "session": session }))
}

#[cfg(test)]
#[path = "library_tests.rs"]
mod tests;
