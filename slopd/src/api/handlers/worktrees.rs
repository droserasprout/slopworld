//! Worktree HTTP authorization and wire projection; recovery fields stay in durable records.
use super::{ApiResult, App, err};
use crate::api::protobuf::{Proto, domain};
use crate::application::workers::Workers;
use crate::application::worktrees::Worktrees;
use crate::grant::Cap;
use crate::shared::wire;
use axum::{
    Extension,
    extract::{Path, Query, State},
    http::{HeaderMap, StatusCode},
};
use serde::Deserialize;

#[derive(Deserialize)]
pub(crate) struct WorktreeQuery {
    #[serde(default)]
    project: String,
}

pub(crate) async fn list_worktrees(
    State(m): State<App>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Query(q): Query<WorktreeQuery>,
) -> ApiResult<wire::WorktreesReply> {
    let caller = worktree_principal(&cap, &headers)?;
    let project = Workers::new(&m)
        .worker_project(&caller, &q.project)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    let views = Worktrees::new(&m)
        .worktree_list(&project)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Proto(wire::WorktreesReply {
        worktrees: views
            .into_iter()
            .map(|view| wire::Worktree {
                branch: view.branch,
                head: view.head,
                attachments: view.attachments,
                ..worktree_reply(view.worktree)
            })
            .collect(),
    }))
}

pub(crate) async fn create_worktree(
    State(m): State<App>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Proto(q): Proto<wire::CreateWorktreeReq>,
) -> ApiResult<wire::Worktree> {
    let mut q: crate::application::worktrees::WorktreeRequest = domain(q)?;
    if !q.path.is_empty() && !cap.may_create() {
        return Err(err(
            StatusCode::FORBIDDEN,
            "Only the root token can register an external checkout.",
        ));
    }
    let caller = worktree_principal(&cap, &headers)?;
    q.project = Workers::new(&m)
        .worker_project(&caller, &q.project)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    let worktree = Box::pin(Worktrees::new(&m).create_worktree(q))
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Proto(worktree_reply(worktree)))
}

pub(crate) async fn remove_worktree(
    State(m): State<App>,
    Path(id): Path<String>,
    Query(q): Query<WorktreeQuery>,
) -> ApiResult {
    super::ack_result(Worktrees::new(&m).remove_worktree(q.project, id).await)
}

pub(crate) async fn rename_worktree(
    State(m): State<App>,
    Path(id): Path<String>,
    Query(q): Query<WorktreeQuery>,
    Proto(req): Proto<wire::CreateWorktreeReq>,
) -> ApiResult<wire::Worktree> {
    let req: crate::application::worktrees::WorktreeRequest = domain(req)?;
    let worktree = Worktrees::new(&m)
        .rename_worktree(q.project, id, req.name)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Proto(worktree_reply(worktree)))
}

pub(crate) async fn preview_worktree(
    State(m): State<App>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Proto(q): Proto<wire::CreateWorktreeReq>,
) -> ApiResult<wire::WorktreeBase> {
    let caller = worktree_principal(&cap, &headers)?;
    let project = Workers::new(&m)
        .worker_project(&caller, &q.project)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    let commit = Worktrees::new(&m)
        .worktree_base(&caller, &project, q.base.as_deref().unwrap_or(""))
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Proto(wire::WorktreeBase { commit }))
}

/// Worktrees belong to projects, so root requests need no agent or worker owner.
/// Scoped requests retain the grantor's project restriction regardless of the session header.
fn worktree_principal(
    cap: &Cap,
    headers: &HeaderMap,
) -> Result<String, crate::api::protobuf::ApiError> {
    match cap {
        Cap::Root => Ok(crate::tasks::HOST.into()),
        Cap::Scoped(_) => super::task_principal(cap, headers),
    }
}

/// Public state retains phase/error wording; structured relocation intent is storage-only.
fn worktree_reply(worktree: crate::worktrees::Worktree) -> wire::Worktree {
    wire::Worktree {
        id: worktree.id,
        project_id: worktree.project_id,
        name: worktree.name,
        path: worktree.path,
        repository: worktree.repository,
        managed: worktree.managed,
        initial_branch: worktree.initial_branch,
        base: worktree.base,
        phase: worktree.phase.to_string(),
        error: worktree.error,
        ..Default::default()
    }
}

#[cfg(test)]
#[path = "worktrees_tests.rs"]
mod tests;
