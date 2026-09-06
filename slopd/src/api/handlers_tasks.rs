//! Task mailbox and worker-construction HTTP boundaries.

use axum::extract::{Path, Query, State};
use axum::http::{HeaderMap, StatusCode};
use axum::{Extension, Json};
use serde_json::{json, Value};

use crate::grant::{Cap, Level};

use super::super::types::*;
use super::{err, guard, guard_create, ApiResult, Mgr};

pub(crate) fn task_principal(
    cap: &Cap,
    headers: &HeaderMap,
) -> Result<String, (StatusCode, Json<Value>)> {
    let who = cap
        .principal()
        .map(str::to_string)
        .or_else(|| {
            headers
                .get("x-slop-session")
                .and_then(|v| v.to_str().ok())
                .map(str::to_string)
        })
        .filter(|s| !s.trim().is_empty())
        .ok_or_else(|| {
            err(
                StatusCode::BAD_REQUEST,
                "root task requests need x-slop-session",
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
    Json(q): Json<CreateTaskReq>,
) -> ApiResult {
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
        .create_task(from, q.to, q.body)
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({ "task": task })))
}

/// Atomically coordinates the mailbox task and child-session setup. The task is written before
/// the worker can start; a failed launch leaves a failed, queryable task (and a durable stopped
/// session when requested). Only the root may mint a new worker session.
pub(crate) async fn spawn_worker(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Json(q): Json<SpawnWorkerReq>,
) -> ApiResult {
    guard_create(&cap)?;
    let from = task_principal(&cap, &headers)?;
    if !task_endpoint_known(&m, &from).await {
        return Err(err(
            StatusCode::BAD_REQUEST,
            format!("no such session: {from}"),
        ));
    }
    let worker = m
        .spawn_worker(from, q.parent, q.body, q.durable)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({
        "task": worker.task,
        "worker": {
            "name": worker.session.clone(),
            "session": worker.session,
        }
    })))
}

pub(crate) async fn list_tasks(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Query(q): Query<ListTasksQuery>,
) -> ApiResult {
    let who = task_principal(&cap, &headers)?;
    if q.all && !cap.may_create() {
        return Err(err(
            StatusCode::FORBIDDEN,
            "only the daemon's own token lists every task",
        ));
    }
    let tasks = if q.all {
        m.all_tasks()
    } else {
        m.tasks_for(&who)
    };
    Ok(Json(json!({ "tasks": tasks })))
}

pub(crate) async fn one_task(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Path(id): Path<String>,
) -> ApiResult {
    let who = task_principal(&cap, &headers)?;
    m.task_for(&who, &id)
        .map(|task| Json(json!({ "task": task })))
        .ok_or_else(|| err(StatusCode::NOT_FOUND, format!("no such task: {id}")))
}

pub(crate) async fn update_task(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Path(id): Path<String>,
    Json(q): Json<UpdateTaskReq>,
) -> ApiResult {
    let who = task_principal(&cap, &headers)?;
    let task = m
        .update_task(&who, &id, q.status, q.note)
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({ "task": task })))
}

pub(crate) async fn prune_tasks(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Query(q): Query<PruneTasksQuery>,
) -> ApiResult {
    let who = task_principal(&cap, &headers)?;
    if q.all && !cap.may_create() {
        return Err(err(
            StatusCode::FORBIDDEN,
            "only the daemon's own token prunes every mailbox",
        ));
    }
    let removed = m
        .prune_tasks(&who, q.all)
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({ "removed": removed })))
}

pub(crate) async fn cancel_tasks(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Json(q): Json<RemoveTasksReq>,
) -> ApiResult {
    let who = task_principal(&cap, &headers)?;
    let tasks = m
        .cancel_tasks(&who, &q.ids, cap.may_create())
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({ "tasks": tasks })))
}

pub(crate) async fn remove_task(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Path(id): Path<String>,
) -> ApiResult {
    let who = task_principal(&cap, &headers)?;
    let task = m
        .remove_task(&who, &id, cap.may_create())
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({ "task": task })))
}

pub(crate) async fn remove_tasks(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Json(q): Json<RemoveTasksReq>,
) -> ApiResult {
    let who = task_principal(&cap, &headers)?;
    let removed = m
        .remove_tasks(&who, &q.ids, cap.may_create())
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({ "removed": removed })))
}

#[cfg(test)]
mod tests {
    use super::*;
    use axum::http::HeaderValue;

    #[test]
    fn root_worker_requests_use_the_explicit_session_caller() {
        let mut headers = HeaderMap::new();
        headers.insert("x-slop-session", HeaderValue::from_static("caller"));

        assert_eq!(task_principal(&Cap::Root, &headers).unwrap(), "caller");
    }
}
