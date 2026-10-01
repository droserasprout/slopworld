use super::{ApiResult, Mgr, err};
use crate::api::protobuf::{Proto, domain, reply};
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
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Query(q): Query<WorktreeQuery>,
) -> ApiResult<wire::WorktreesReply> {
    let caller = super::task_principal(&cap, &headers)?;
    let project = m
        .worker_project(&caller, &q.project)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    reply(
        serde_json::json!({"worktrees": m.worktree_list(&project).await.map_err(|e| err(StatusCode::BAD_REQUEST, e))?}),
    )
}

pub(crate) async fn create_worktree(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Proto(q): Proto<wire::CreateWorktreeReq>,
) -> ApiResult<wire::Worktree> {
    let mut q: crate::session::WorktreeRequest = domain(q)?;
    if !q.path.is_empty() && !cap.may_create() {
        return Err(err(
            StatusCode::FORBIDDEN,
            "Only the root token can register an external checkout.",
        ));
    }
    let caller = super::task_principal(&cap, &headers)?;
    q.project = m
        .worker_project(&caller, &q.project)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    reply(
        Box::pin(m.create_worktree(q))
            .await
            .map_err(|e| err(StatusCode::BAD_REQUEST, e))?,
    )
}

pub(crate) async fn remove_worktree(
    State(m): State<Mgr>,
    Path(id): Path<String>,
    Query(q): Query<WorktreeQuery>,
) -> ApiResult {
    super::ack_result(m.remove_worktree(q.project, id).await)
}

pub(crate) async fn rename_worktree(
    State(m): State<Mgr>,
    Path(id): Path<String>,
    Query(q): Query<WorktreeQuery>,
    Proto(req): Proto<wire::CreateWorktreeReq>,
) -> ApiResult<wire::Worktree> {
    let req: crate::session::WorktreeRequest = domain(req)?;
    reply(
        m.rename_worktree(q.project, id, req.name)
            .await
            .map_err(|e| err(StatusCode::BAD_REQUEST, e))?,
    )
}

pub(crate) async fn preview_worktree(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Proto(q): Proto<wire::CreateWorktreeReq>,
) -> ApiResult<wire::WorktreeBase> {
    let caller = super::task_principal(&cap, &headers)?;
    let project = m
        .worker_project(&caller, &q.project)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    let commit = m
        .worktree_base(&caller, &project, q.base.as_deref().unwrap_or(""))
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    reply(serde_json::json!({"commit":commit}))
}

#[cfg(test)]
#[path = "worktrees_tests.rs"]
mod tests;
