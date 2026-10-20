//! HTTP handlers for task mailboxes and worker creation.
use crate::api::protobuf::{ApiError, Proto, domain};
use crate::application::workers::Workers;
use crate::shared::wire;

use axum::Extension;
use axum::extract::{Path, Query, State};
use axum::http::{HeaderMap, StatusCode};

use crate::grant::{Cap, Level};
use crate::shared::protocol::SESSION_HEADER;

use super::super::types::*;
use super::{ApiResult, Mgr, err, guard};

/// Persistence identities stay private; the wire contract exposes participant labels.
fn task_view(task: crate::tasks::Task) -> wire::Task {
    wire::Task {
        id: task.id,
        from: task.from,
        to: task.to,
        body: task.body,
        status: task_status_view(task.status).into(),
        note: task.note,
        summary: task.summary,
        created_ms: task.created_ms,
        updated_ms: task.updated_ms,
        history: task
            .history
            .into_iter()
            .map(|update| wire::TaskUpdate {
                sender: update.sender,
                status: task_status_view(update.status).into(),
                note: update.note,
                timestamp_ms: update.timestamp_ms,
            })
            .collect(),
        worker: task.worker.map(|worker| wire::WorkerTask {
            session: worker.session,
            parent: worker.parent,
            durable: worker.durable,
        }),
    }
}

fn task_status_view(status: crate::tasks::Status) -> &'static str {
    use crate::shared::protocol::enums::task_status;
    use crate::tasks::Status;
    match status {
        Status::Queued => task_status::QUEUED,
        Status::Accepted => task_status::ACCEPTED,
        Status::Working => task_status::WORKING,
        Status::Done => task_status::DONE,
        Status::Failed => task_status::FAILED,
        Status::Canceled => task_status::CANCELED,
    }
}

/// The request must hold the session boundary through identity resolution and store access.
async fn task_identity(m: &Mgr, cap: &Cap, headers: &HeaderMap) -> Result<String, ApiError> {
    task_participant(m, cap, headers)
        .await
        .map(|participant| participant.identity)
}

async fn task_participant(
    m: &Mgr,
    cap: &Cap,
    headers: &HeaderMap,
) -> Result<crate::tasks::Participant, ApiError> {
    let name = task_principal(cap, headers)?;
    m.task_participant(&name)
        .await
        .map_err(|error| err(StatusCode::BAD_REQUEST, error))
}

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
                format!("Set the {SESSION_HEADER} header for this root task request."),
            )
        })?;
    // The root token represents the user at the keyboard.
    // A grant uses its grantor's session name as the caller identity.
    // A session named `host` cannot use the host identity through a grant.
    if who == crate::tasks::HOST && !cap.may_create() {
        return Err(err(
            StatusCode::FORBIDDEN,
            "Only the root token can use the host identity.",
        ));
    }
    Ok(who)
}

/// A task endpoint can be a live session or `host`.
/// The daemon checks `host` here because it is not a session.
/// It has no session record for the access check, and grants cannot include it.
/// Agents can use the host mailbox to report results to the user.
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
            format!("Session {from} does not exist."),
        ));
    }
    if !task_endpoint_known(&m, &q.to).await {
        return Err(err(
            StatusCode::BAD_REQUEST,
            format!("Session {} does not exist.", q.to),
        ));
    }
    if q.to != crate::tasks::HOST {
        guard(&m, &cap, &q.to, Level::Ro).await?;
    }
    let from = m
        .task_participant(&from)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    let to = m
        .task_participant(&q.to)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    let task = m
        .create_task_owned(from, to, q.body, None)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    m.application
        .spawn_task_summary(m.config_authority.daemon_settings(), task.clone());
    Ok(Proto(wire::TaskResult {
        task: Some(task_view(task)),
    }))
}

/// Create the task and child session as one operation.
/// The daemon saves the task before it starts the worker.
/// A failed start leaves a failed, queryable task and, when requested, a stopped session.
/// Root callers can choose any project. Scoped agents can use only their own project.
/// The daemon checks that the selected template is allowed before it creates the worker.
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
    let checkout = if q.new_worktree {
        if !q.worktree.is_empty() {
            return Err(err(
                StatusCode::BAD_REQUEST,
                "Choose an existing worktree or request a new one.",
            ));
        }
        crate::application::workers::WorkerCheckout::New {
            base: q.base,
            name: q.worktree_name,
        }
    } else {
        if !q.base.is_empty() || !q.worktree_name.is_empty() {
            return Err(err(
                StatusCode::BAD_REQUEST,
                "Set new_worktree to true to use base or worktree_name.",
            ));
        }
        crate::application::workers::WorkerCheckout::Existing(q.worktree)
    };
    let worker = Workers::new(&m)
        .create_worker(crate::application::workers::WorkerRequest {
            caller: from.clone(),
            project: q.project,
            template: q.template,
            body: q.body,
            durable: q.durable,
            checkout,
        })
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Proto(wire::WorkerResult {
        task: Some(task_view(worker.task)),
        worker: Some(wire::WorkerIdentity {
            name: worker.session.clone(),
            session: worker.session,
            parent: from,
            durable: q.durable,
        }),
    }))
}

pub(crate) async fn list_tasks(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Query(q): Query<ListTasksQuery>,
) -> ApiResult<wire::TasksReply> {
    let who = task_identity(&m, &cap, &headers).await?;
    if q.all && !cap.may_create() {
        return Err(err(
            StatusCode::FORBIDDEN,
            "Only the root token can list all tasks.",
        ));
    }
    let tasks = if q.all {
        m.task_store()
            .all_tasks_async()
            .await
            .map_err(|e| err(StatusCode::INTERNAL_SERVER_ERROR, e))?
    } else {
        m.task_store()
            .tasks_for_async(&who)
            .await
            .map_err(|e| err(StatusCode::INTERNAL_SERVER_ERROR, e))?
    };
    Ok(Proto(wire::TasksReply {
        tasks: tasks.into_iter().map(task_view).collect(),
    }))
}

pub(crate) async fn one_task(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Path(id): Path<String>,
) -> ApiResult<wire::TaskResult> {
    let who = task_identity(&m, &cap, &headers).await?;
    m.task_store()
        .task_for_async(&who, &id)
        .await
        .map_err(|e| err(StatusCode::INTERNAL_SERVER_ERROR, e))?
        .map(|task| {
            Proto(wire::TaskResult {
                task: Some(task_view(task)),
            })
        })
        .ok_or_else(|| {
            err(
                StatusCode::NOT_FOUND,
                format!("Task {id} was not found or is not available to this caller."),
            )
        })
}

pub(crate) async fn update_task(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Path(id): Path<String>,
    Proto(q): Proto<wire::UpdateTaskReq>,
) -> ApiResult<wire::TaskResult> {
    let q: UpdateTaskReq = domain(q)?;
    let who = task_participant(&m, &cap, &headers).await?;
    let task = m
        .task_store()
        .update_task_async(&who.identity, &who.name, &id, q.status, q.note)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Proto(wire::TaskResult {
        task: Some(task_view(task)),
    }))
}

pub(crate) async fn prune_tasks(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Query(q): Query<PruneTasksQuery>,
) -> ApiResult<wire::TaskBatchResult> {
    let who = task_identity(&m, &cap, &headers).await?;
    if q.all && !cap.may_create() {
        return Err(err(
            StatusCode::FORBIDDEN,
            "Only the root token can prune tasks for all participants.",
        ));
    }
    let removed = m
        .task_store()
        .prune_tasks_async(&who, q.all)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Proto(batch_view(removed, true)))
}

pub(crate) async fn cancel_tasks(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Proto(q): Proto<wire::RemoveTasksReq>,
) -> ApiResult<wire::TaskBatchResult> {
    let q: RemoveTasksReq = domain(q)?;
    let who = task_participant(&m, &cap, &headers).await?;
    let tasks = m
        .task_store()
        .cancel_tasks_async(&who.identity, &who.name, &q.ids, cap.may_create())
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Proto(batch_view(tasks, false)))
}

pub(crate) async fn remove_task(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Path(id): Path<String>,
) -> ApiResult<wire::TaskResult> {
    let who = task_identity(&m, &cap, &headers).await?;
    let task = m
        .task_store()
        .remove_task_async(&who, &id, cap.may_create())
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Proto(wire::TaskResult {
        task: Some(task_view(task)),
    }))
}

pub(crate) async fn remove_tasks(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Proto(q): Proto<wire::RemoveTasksReq>,
) -> ApiResult<wire::TaskBatchResult> {
    let q: RemoveTasksReq = domain(q)?;
    let who = task_identity(&m, &cap, &headers).await?;
    let removed = m
        .task_store()
        .remove_tasks_async(&who, &q.ids, cap.may_create())
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Proto(batch_view(removed, true)))
}

#[cfg(test)]
#[path = "tasks_tests.rs"]
mod tests;

fn batch_view(result: crate::tasks::BatchResult, removal: bool) -> wire::TaskBatchResult {
    wire::TaskBatchResult {
        removed: if removal {
            result.committed.len() as u64
        } else {
            0
        },
        committed: result.committed,
        unchanged: result.unchanged,
        absent: result.absent,
        failed: result
            .failed
            .into_iter()
            .map(|failure| wire::TaskBatchFailure {
                id: failure.id,
                error: failure.error,
            })
            .collect(),
        unattempted: result.unattempted,
        tasks: result.tasks.into_iter().map(task_view).collect(),
    }
}
