use super::{err, ApiResult, Mgr};
use crate::api::protobuf::{domain, reply, Proto};
use crate::grant::Cap;
use crate::shared::wire;
use axum::{
    extract::{Path, Query, State},
    http::{HeaderMap, StatusCode},
    Extension,
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
            "only the host registers external checkouts",
        ));
    }
    let caller = super::task_principal(&cap, &headers)?;
    q.project = m
        .worker_project(&caller, &q.project)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    reply(
        m.create_worktree(q)
            .await
            .map_err(|e| err(StatusCode::BAD_REQUEST, e))?,
    )
}

pub(crate) async fn remove_worktree(
    State(m): State<Mgr>,
    Path(id): Path<String>,
    Query(q): Query<WorktreeQuery>,
) -> ApiResult {
    super::ok_json(m.remove_worktree(q.project, id).await)
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
#[path = "handlers_worktrees_tests.rs"]
mod tests;
