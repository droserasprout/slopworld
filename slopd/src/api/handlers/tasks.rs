//! HTTP handlers for task mailboxes and worker creation.
use crate::api::protobuf::{ApiError, Proto, domain};
use crate::application::worker_tasks;
use crate::application::workers::Workers;
use crate::shared::wire;

use axum::Extension;
use axum::extract::{Path, Query, State};
use axum::http::{HeaderMap, StatusCode};

use crate::credentials::{Cap, Level};
use crate::shared::protocol::SESSION_HEADER;

use super::super::types::*;
use super::{ApiResult, App, err};

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
async fn task_identity(m: &App, cap: &Cap, headers: &HeaderMap) -> Result<String, ApiError> {
    task_participant(m, cap, headers)
        .await
        .map(|participant| participant.identity)
}

async fn task_participant(
    m: &App,
    cap: &Cap,
    headers: &HeaderMap,
) -> Result<crate::tasks::Participant, ApiError> {
    let name = task_principal(cap, headers)?;
    m.sessions
        .task_participant(&name)
        .await
        .map_err(|error| err(StatusCode::BAD_REQUEST, error))
}

pub(crate) fn task_principal(
    cap: &Cap,
    headers: &HeaderMap,
) -> Result<String, crate::api::protobuf::ApiError> {
    worker_tasks::principal(
        cap,
        headers.get(SESSION_HEADER).and_then(|v| v.to_str().ok()),
    )
    .map_err(operation_error)
}

/// A task endpoint can be a live session or `host`.
/// The daemon checks `host` here because it is not a session.
/// It has no session record for the access check, and grants cannot include it.
/// Agents can use the host mailbox to report results to the user.
pub(crate) async fn task_endpoint_known(m: &App, who: &str) -> bool {
    who == crate::tasks::HOST || m.sessions.session_known(who).await
}

pub(crate) async fn create_task(
    State(m): State<App>,
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
    if q.to != crate::tasks::HOST
        && !m.sessions.cap_ok(&cap, &q.to, Level::Ro).await
        && !m
            .sessions
            .worker_access(&cap, &q.to)
            .await
            .is_some_and(|access| access.followup)
    {
        return Err(err(StatusCode::FORBIDDEN, format!("not allowed: {}", q.to)));
    }
    let from = m
        .sessions
        .task_participant(&from)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    let to = m
        .sessions
        .task_participant(&q.to)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    let task = m
        .sessions
        .create_task_owned(from, to, q.body, None)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    m.services
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
    State(m): State<App>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Proto(value): Proto<wire::SpawnWorkerReq>,
) -> ApiResult<wire::WorkerResult> {
    let q: SpawnWorkerReq = super::super::parse_owned(
        domain(value)?,
        &[
            "name",
            "project",
            "template",
            "body",
            "durable",
            "worktree",
            "new_worktree",
            "base",
            "worktree_name",
            "planner_id",
        ],
    )?;
    let from = task_principal(&cap, &headers)?;
    if !q.planner_id.is_empty() && !matches!(cap, Cap::Root) {
        return Err(err(
            StatusCode::FORBIDDEN,
            "Only root can link Planner items.",
        ));
    }
    if q.new_worktree && !m.sessions.manage_worktrees_ok(&cap).await {
        return Err(err(StatusCode::FORBIDDEN, "Worktree management is denied."));
    }
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
        .create_linked_worker(
            crate::application::workers::WorkerRequest {
                caller: from.clone(),
                name: q.name,
                project: q.project,
                template: q.template,
                body: q.body,
                durable: q.durable,
                checkout,
            },
            q.planner_id,
        )
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
    State(m): State<App>,
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
        m.sessions
            .task_store()
            .all_tasks_async()
            .await
            .map_err(|e| err(StatusCode::INTERNAL_SERVER_ERROR, e))?
    } else {
        m.sessions
            .task_store()
            .tasks_for_async(&who)
            .await
            .map_err(|e| err(StatusCode::INTERNAL_SERVER_ERROR, e))?
    };
    Ok(Proto(wire::TasksReply {
        tasks: tasks.into_iter().map(task_view).collect(),
    }))
}

pub(crate) async fn one_task(
    State(m): State<App>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Path(id): Path<String>,
) -> ApiResult<wire::TaskResult> {
    let who = task_identity(&m, &cap, &headers).await?;
    // The operator can already list every task. Opening one from Planner or the
    // host task board must use the same visibility, including agent-to-agent work.
    let task = if matches!(cap, Cap::Root) && who == crate::tasks::HOST {
        m.sessions.task_store().task_any_async(&id).await
    } else {
        m.sessions.task_store().task_for_async(&who, &id).await
    };
    task.map_err(|e| err(StatusCode::INTERNAL_SERVER_ERROR, e))?
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
    State(m): State<App>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Path(id): Path<String>,
    Proto(q): Proto<wire::UpdateTaskReq>,
) -> ApiResult<wire::TaskResult> {
    let q: UpdateTaskReq = domain(q)?;
    let who = task_participant(&m, &cap, &headers).await?;
    let task = worker_tasks::update(&m, &cap, who, &id, q.status, q.note)
        .await
        .map_err(operation_error)?;
    Ok(Proto(wire::TaskResult {
        task: Some(task_view(task)),
    }))
}

pub(crate) async fn prune_tasks(
    State(m): State<App>,
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
        .sessions
        .task_store()
        .prune_tasks_async(&who, q.all)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Proto(batch_view(removed, true)))
}

fn operation_error(error: worker_tasks::OperationError) -> ApiError {
    use worker_tasks::Failure;
    let status = match error.failure {
        Failure::Invalid => StatusCode::BAD_REQUEST,
        Failure::Forbidden => StatusCode::FORBIDDEN,
        Failure::Missing => StatusCode::NOT_FOUND,
        Failure::Internal => StatusCode::INTERNAL_SERVER_ERROR,
        Failure::Unauthorized => StatusCode::UNAUTHORIZED,
    };
    err(status, error.message)
}

pub(crate) async fn followup_task(
    State(m): State<App>,
    Extension(cap): Extension<Cap>,
    generation: Option<Extension<crate::api::RootGeneration>>,
    headers: HeaderMap,
    Path(id): Path<String>,
    Proto(q): Proto<wire::WorkerFollowupReq>,
) -> ApiResult {
    worker_tasks::followup(
        &m,
        &cap,
        generation.map(|g| g.0.0),
        headers
            .get(SESSION_HEADER)
            .and_then(|v| v.to_str().ok())
            .map(str::to_owned),
        id,
        q.body,
    )
    .await
    .map_err(operation_error)?;
    Ok(Proto(wire::Ack { ok: true }))
}

pub(crate) async fn set_agent_permissions(
    State(m): State<App>,
    Path(name): Path<String>,
    Proto(q): Proto<wire::AgentPermissionsReq>,
) -> ApiResult {
    let access = domain(
        q.permissions
            .ok_or_else(|| err(StatusCode::BAD_REQUEST, "Worker permissions are required."))?,
    )?;
    let policy = q.workers.map(domain).transpose()?;
    super::ack_result(
        Workers::new(&m)
            .set_access(&name, &q.task_id, access, policy)
            .await,
    )
}

pub(crate) async fn cancel_tasks(
    State(m): State<App>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Proto(q): Proto<wire::RemoveTasksReq>,
) -> ApiResult<wire::TaskBatchResult> {
    let q: RemoveTasksReq = domain(q)?;
    let who = task_participant(&m, &cap, &headers).await?;
    let tasks = worker_tasks::cancel(&m, &cap, who, q.ids)
        .await
        .map_err(operation_error)?;
    Ok(Proto(batch_view(tasks, false)))
}

pub(crate) async fn remove_task(
    State(m): State<App>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Path(id): Path<String>,
) -> ApiResult<wire::TaskResult> {
    let who = task_identity(&m, &cap, &headers).await?;
    let task = m
        .sessions
        .task_store()
        .remove_task_async(&who, &id, cap.may_create())
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Proto(wire::TaskResult {
        task: Some(task_view(task)),
    }))
}

pub(crate) async fn remove_tasks(
    State(m): State<App>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Proto(q): Proto<wire::RemoveTasksReq>,
) -> ApiResult<wire::TaskBatchResult> {
    let q: RemoveTasksReq = domain(q)?;
    let who = task_identity(&m, &cap, &headers).await?;
    let removed = m
        .sessions
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
