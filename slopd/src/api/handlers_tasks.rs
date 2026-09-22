//! Task mailbox and worker-construction HTTP boundaries.
use crate::api::protobuf::{domain, reply, Proto};
use crate::shared::wire;

use axum::extract::{Path, Query, State};
use axum::http::{HeaderMap, StatusCode};
use axum::Extension;
use serde_json::json;

use crate::grant::{Cap, Level};
use crate::shared::protocol::SESSION_HEADER;

use super::super::types::*;
use super::{err, guard, ApiResult, Mgr};

pub(crate) fn task_principal(
    cap: &Cap,
    headers: &HeaderMap,
) -> Result<String, crate::api::protobuf::ApiError> {
    let who = cap
        .principal()
        .map(str::to_string)
        .or_else(|| {
            headers
                .get(SESSION_HEADER)
                .and_then(|v| v.to_str().ok())
                .map(str::to_string)
        })
        .filter(|s| !s.trim().is_empty())
        .ok_or_else(|| {
            err(
                StatusCode::BAD_REQUEST,
                format!("root task requests need {SESSION_HEADER}"),
            )
        })?;
    // The root token is the only thing that speaks for the user at the keyboard. A grant resolves
    // to its grantor's name, so this covers a session that happens to be called `host` too: it may
    // hold a grant, but it cannot wear the host's identity with it.
    if who == crate::tasks::HOST && !cap.may_create() {
        return Err(err(
            StatusCode::FORBIDDEN,
            "only the daemon's own token speaks as the host",
        ));
    }
    Ok(who)
}

/// A task endpoint is a live session or the host. The host is checked here rather than through
/// `guard`, because it is not a session: it has no host-ness flag to look up, appears in no
/// grant's scope, and names nothing a scoped caller could learn from reaching it. An agent
/// reporting back to the user is what the mailbox is for.
pub(crate) async fn task_endpoint_known(m: &Mgr, who: &str) -> bool {
    who == crate::tasks::HOST || m.session_known(who).await
}

pub(crate) async fn create_task(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Proto(q): Proto<wire::CreateTaskReq>,
) -> ApiResult<wire::TaskResult> {
    let q: CreateTaskReq = domain(q)?;
    let from = task_principal(&cap, &headers)?;
    if !task_endpoint_known(&m, &from).await {
        return Err(err(
            StatusCode::BAD_REQUEST,
            format!("no such session: {from}"),
        ));
    }
    if !task_endpoint_known(&m, &q.to).await {
        return Err(err(
            StatusCode::BAD_REQUEST,
            format!("no such session: {}", q.to),
        ));
    }
    if q.to != crate::tasks::HOST {
        guard(&m, &cap, &q.to, Level::Ro).await?;
    }
    let task = m
        .tasks
        .create_task(from, q.to, q.body)
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    m.spawn_task_summary_request(task.clone());
    reply(json!({ "task": task }))
}

/// Atomically coordinates the mailbox task and child-session setup. The task is written before
/// the worker can start; a failed launch leaves a failed, queryable task (and a durable stopped
/// session when requested). Root callers may choose any project, while scoped agents are limited
/// to their own project and the daemon's worker-template allowlist is checked before allocation.
pub(crate) async fn spawn_worker(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Proto(value): Proto<wire::SpawnWorkerReq>,
) -> ApiResult<wire::WorkerResult> {
    let q: SpawnWorkerReq = super::super::parse_owned(
        domain(value)?,
        &[
            "project",
            "template",
            "body",
            "durable",
            "worktree",
            "new_worktree",
            "base",
            "worktree_name",
        ],
    )?;
    let from = task_principal(&cap, &headers)?;
    if !task_endpoint_known(&m, &from).await {
        return Err(err(
            StatusCode::BAD_REQUEST,
            format!("no such session: {from}"),
        ));
    }
    let project = m
        .worker_project(&from, &q.project)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    if !q.new_worktree && (!q.base.is_empty() || !q.worktree_name.is_empty()) {
        return Err(err(
            StatusCode::BAD_REQUEST,
            "base and worktree_name require new_worktree",
        ));
    }
    if q.body.trim().is_empty() {
        return Err(err(
            StatusCode::BAD_REQUEST,
            "worker task body must not be empty",
        ));
    }
    let worktree = if q.new_worktree {
        if !q.worktree.is_empty() {
            return Err(err(
                StatusCode::BAD_REQUEST,
                "choose an existing worktree or a new worktree",
            ));
        }
        let template = m
            .spawnable_worker_template(&from, &project, &q.template)
            .await
            .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
        let cfg = m.config().await;
        let candidate = template.instantiate("worktree-validation".into(), project.clone());
        let owner = cfg
            .project(&project)
            .ok_or_else(|| err(StatusCode::BAD_REQUEST, "worker project disappeared"))?;
        if cfg.network_of(&candidate, owner) == crate::config::NetworkMode::None
            || cfg.command_of(&candidate).trim().is_empty()
        {
            return Err(err(
                StatusCode::BAD_REQUEST,
                "worker template needs networking and an executable command",
            ));
        }
        crate::runtime::validate_limits(&candidate.limits)
            .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
        let base = m
            .worktree_base(&from, &project, &q.base)
            .await
            .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
        m.create_worktree(crate::session::WorktreeRequest {
            project: project.clone(),
            name: q.worktree_name,
            base,
            path: String::new(),
        })
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?
        .id
    } else {
        q.worktree
    };
    let worker = m
        .spawn_worker_worktree(
            from.clone(),
            project,
            q.template,
            q.body,
            q.durable,
            worktree,
        )
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    reply(json!({
        "task": worker.task,
        "worker": {
            "name": worker.session.clone(),
            "session": worker.session,
            "parent": from,
            "durable": q.durable,
        }
    }))
}

pub(crate) async fn list_tasks(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Query(q): Query<ListTasksQuery>,
) -> ApiResult<wire::TasksReply> {
    let who = task_principal(&cap, &headers)?;
    if q.all && !cap.may_create() {
        return Err(err(
            StatusCode::FORBIDDEN,
            "only the daemon's own token lists every task",
        ));
    }
    let tasks = if q.all {
        m.tasks.all_tasks()
    } else {
        m.tasks.tasks_for(&who)
    };
    reply(json!({ "tasks": tasks }))
}

pub(crate) async fn one_task(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Path(id): Path<String>,
) -> ApiResult<wire::TaskResult> {
    let who = task_principal(&cap, &headers)?;
    m.tasks
        .task_for(&who, &id)
        .map(|task| reply(json!({ "task": task })))
        .ok_or_else(|| err(StatusCode::NOT_FOUND, format!("no such task: {id}")))?
}

pub(crate) async fn update_task(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Path(id): Path<String>,
    Proto(q): Proto<wire::UpdateTaskReq>,
) -> ApiResult<wire::TaskResult> {
    let q: UpdateTaskReq = domain(q)?;
    let who = task_principal(&cap, &headers)?;
    let task = m
        .tasks
        .update_task(&who, &id, q.status, q.note)
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    reply(json!({ "task": task }))
}

pub(crate) async fn prune_tasks(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Query(q): Query<PruneTasksQuery>,
) -> ApiResult<wire::Removed> {
    let who = task_principal(&cap, &headers)?;
    if q.all && !cap.may_create() {
        return Err(err(
            StatusCode::FORBIDDEN,
            "only the daemon's own token prunes every mailbox",
        ));
    }
    let removed = m
        .tasks
        .prune_tasks(&who, q.all)
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    reply(json!({ "removed": removed }))
}

pub(crate) async fn cancel_tasks(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Proto(q): Proto<wire::RemoveTasksReq>,
) -> ApiResult<wire::TasksReply> {
    let q: RemoveTasksReq = domain(q)?;
    let who = task_principal(&cap, &headers)?;
    let tasks = m
        .tasks
        .cancel_tasks(&who, &q.ids, cap.may_create())
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    reply(json!({ "tasks": tasks }))
}

pub(crate) async fn remove_task(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Path(id): Path<String>,
) -> ApiResult<wire::TaskResult> {
    let who = task_principal(&cap, &headers)?;
    let task = m
        .tasks
        .remove_task(&who, &id, cap.may_create())
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    reply(json!({ "task": task }))
}

pub(crate) async fn remove_tasks(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Proto(q): Proto<wire::RemoveTasksReq>,
) -> ApiResult<wire::Removed> {
    let q: RemoveTasksReq = domain(q)?;
    let who = task_principal(&cap, &headers)?;
    let removed = m
        .tasks
        .remove_tasks(&who, &q.ids, cap.may_create())
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    reply(json!({ "removed": removed }))
}

#[cfg(test)]
#[path = "handlers_tasks_tests.rs"]
mod tests;
