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
    let q: SpawnWorkerReq =
        super::super::parse_owned(domain(value)?, &["project", "template", "body", "durable"])?;
    let from = task_principal(&cap, &headers)?;
    if !task_endpoint_known(&m, &from).await {
        return Err(err(
            StatusCode::BAD_REQUEST,
            format!("no such session: {from}"),
        ));
    }
    let worker = m
        .spawn_worker(from.clone(), q.project, q.template, q.body, q.durable)
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
mod tests {
    use super::*;
    use axum::http::HeaderValue;

    #[test]
    fn root_worker_requests_use_the_explicit_session_caller() {
        let mut headers = HeaderMap::new();
        headers.insert(SESSION_HEADER, HeaderValue::from_static("caller"));

        assert_eq!(task_principal(&Cap::Root, &headers).unwrap(), "caller");
    }

    #[tokio::test]
    async fn missing_template_worker_requests_are_rejected_before_task_creation() {
        let manager = crate::session::test_manager(crate::config::Config::default());
        let mut headers = HeaderMap::new();
        headers.insert(SESSION_HEADER, HeaderValue::from_static(crate::tasks::HOST));
        let result = spawn_worker(
            axum::extract::State(manager.clone()),
            Extension(Cap::Root),
            headers,
            Proto(wire::SpawnWorkerReq {
                body: Some("missing template".into()),
                ..Default::default()
            }),
        )
        .await;
        let Err((status, Proto(body))) = result else {
            panic!("missing template request unexpectedly succeeded");
        };
        assert_eq!(status, StatusCode::BAD_REQUEST);
        assert!(body.error.contains("template"));
        assert!(manager.tasks.all_tasks().is_empty());
    }
    fn caller(name: &str) -> HeaderMap {
        let mut headers = HeaderMap::new();
        headers.insert(SESSION_HEADER, name.parse().unwrap());
        headers
    }

    fn scoped(name: &str) -> Cap {
        Cap::Scoped(crate::grant::Grant {
            grantor: name.into(),
            sessions: Default::default(),
            level: Level::Ro,
            revoked: Default::default(),
        })
    }

    #[test]
    fn task_identity_rejects_missing_headers_and_host_impersonation() {
        for headers in [HeaderMap::new(), caller("   ")] {
            assert_eq!(
                task_principal(&Cap::Root, &headers).unwrap_err().0,
                StatusCode::BAD_REQUEST
            );
        }
        let mut invalid = HeaderMap::new();
        invalid.insert(SESSION_HEADER, HeaderValue::from_bytes(&[0xff]).unwrap());
        assert_eq!(
            task_principal(&Cap::Root, &invalid).unwrap_err().0,
            StatusCode::BAD_REQUEST
        );
        assert_eq!(
            task_principal(&scoped("alice"), &caller("host")).unwrap(),
            "alice"
        );
        assert_eq!(
            task_principal(&scoped("host"), &caller("alice"))
                .unwrap_err()
                .0,
            StatusCode::FORBIDDEN
        );
    }

    #[tokio::test]
    async fn mailbox_visibility_and_global_operations_respect_principal() {
        let m = crate::session::test_manager(crate::config::Config::default());
        let visible = m
            .tasks
            .create_task("alice".into(), "bob".into(), "visible".into())
            .unwrap();
        let hidden = m
            .tasks
            .create_task("carol".into(), "dave".into(), "private".into())
            .unwrap();
        let Proto(reply) = list_tasks(
            State(m.clone()),
            Extension(scoped("alice")),
            caller("carol"),
            Query(ListTasksQuery { all: false }),
        )
        .await
        .unwrap();
        assert_eq!(reply.tasks.len(), 1);
        assert_eq!(reply.tasks[0].id, visible.id);
        assert_eq!(
            one_task(
                State(m.clone()),
                Extension(scoped("alice")),
                caller("carol"),
                Path(hidden.id.clone())
            )
            .await
            .unwrap_err()
            .0,
            StatusCode::NOT_FOUND
        );
        let Proto(reply) = one_task(
            State(m.clone()),
            Extension(scoped("bob")),
            HeaderMap::new(),
            Path(visible.id),
        )
        .await
        .unwrap();
        assert_eq!(reply.task.unwrap().body, "visible");
        assert_eq!(
            list_tasks(
                State(m.clone()),
                Extension(scoped("alice")),
                HeaderMap::new(),
                Query(ListTasksQuery { all: true })
            )
            .await
            .unwrap_err()
            .0,
            StatusCode::FORBIDDEN
        );
        assert_eq!(
            prune_tasks(
                State(m.clone()),
                Extension(scoped("alice")),
                HeaderMap::new(),
                Query(PruneTasksQuery { all: true })
            )
            .await
            .unwrap_err()
            .0,
            StatusCode::FORBIDDEN
        );
        let Proto(reply) = list_tasks(
            State(m),
            Extension(Cap::Root),
            caller("host"),
            Query(ListTasksQuery { all: true }),
        )
        .await
        .unwrap();
        assert_eq!(reply.tasks.len(), 2);
    }

    #[tokio::test]
    async fn recipient_updates_and_removal_preserve_task_lifecycle() {
        let m = crate::session::test_manager(crate::config::Config::default());
        let task = m
            .tasks
            .create_task("alice".into(), "bob".into(), "work".into())
            .unwrap();
        let update = || {
            Proto(wire::UpdateTaskReq {
                status: Some("done".into()),
                note: Some("verified".into()),
            })
        };
        assert_eq!(
            update_task(
                State(m.clone()),
                Extension(scoped("alice")),
                HeaderMap::new(),
                Path(task.id.clone()),
                update()
            )
            .await
            .unwrap_err()
            .0,
            StatusCode::BAD_REQUEST
        );
        assert_eq!(
            remove_task(
                State(m.clone()),
                Extension(scoped("bob")),
                HeaderMap::new(),
                Path(task.id.clone())
            )
            .await
            .unwrap_err()
            .0,
            StatusCode::BAD_REQUEST
        );
        let Proto(reply) = update_task(
            State(m.clone()),
            Extension(scoped("bob")),
            HeaderMap::new(),
            Path(task.id.clone()),
            update(),
        )
        .await
        .unwrap();
        let updated = reply.task.unwrap();
        assert_eq!(updated.status, "done");
        assert_eq!(updated.note.as_deref(), Some("verified"));
        let Proto(reply) = remove_task(
            State(m.clone()),
            Extension(scoped("alice")),
            HeaderMap::new(),
            Path(task.id.clone()),
        )
        .await
        .unwrap();
        assert_eq!(reply.task.unwrap().id, task.id);
        assert!(m.tasks.all_tasks().is_empty());
    }

    #[tokio::test]
    async fn bulk_cancel_remove_and_prune_enforce_scope() {
        let m = crate::session::test_manager(crate::config::Config::default());
        let a = m
            .tasks
            .create_task("alice".into(), "bob".into(), "first".into())
            .unwrap();
        let b = m
            .tasks
            .create_task("carol".into(), "dave".into(), "second".into())
            .unwrap();
        let ids = || {
            Proto(wire::RemoveTasksReq {
                ids: vec![a.id.clone(), b.id.clone()],
            })
        };
        assert_eq!(
            cancel_tasks(
                State(m.clone()),
                Extension(scoped("bob")),
                HeaderMap::new(),
                ids()
            )
            .await
            .unwrap_err()
            .0,
            StatusCode::BAD_REQUEST
        );
        assert!(m
            .tasks
            .all_tasks()
            .iter()
            .all(|t| t.status == crate::tasks::Status::Queued));
        let Proto(reply) = cancel_tasks(
            State(m.clone()),
            Extension(Cap::Root),
            caller("host"),
            ids(),
        )
        .await
        .unwrap();
        assert_eq!(reply.tasks.len(), 2);
        assert!(reply.tasks.iter().all(|t| t.status == "canceled"));
        assert_eq!(
            remove_tasks(
                State(m.clone()),
                Extension(scoped("bob")),
                HeaderMap::new(),
                ids()
            )
            .await
            .unwrap_err()
            .0,
            StatusCode::BAD_REQUEST
        );
        assert_eq!(m.tasks.all_tasks().len(), 2);
        let Proto(reply) = prune_tasks(
            State(m.clone()),
            Extension(scoped("bob")),
            HeaderMap::new(),
            Query(PruneTasksQuery { all: false }),
        )
        .await
        .unwrap();
        assert_eq!(reply.removed, 1);
        assert_eq!(m.tasks.all_tasks()[0].id, b.id);
        let Proto(reply) = remove_tasks(
            State(m.clone()),
            Extension(Cap::Root),
            caller("host"),
            Proto(wire::RemoveTasksReq { ids: vec![b.id] }),
        )
        .await
        .unwrap();
        assert_eq!(reply.removed, 1);
        let Proto(reply) = prune_tasks(
            State(m.clone()),
            Extension(Cap::Root),
            caller("host"),
            Query(PruneTasksQuery { all: true }),
        )
        .await
        .unwrap();
        assert_eq!(reply.removed, 0);
        assert!(m.tasks.all_tasks().is_empty());
    }
    #[tokio::test]
    async fn unknown_task_endpoints_fail_before_persistence() {
        let m = crate::session::test_manager(crate::config::Config::default());
        for (from, to) in [("missing", "host"), ("host", "missing")] {
            let result = create_task(
                State(m.clone()),
                Extension(Cap::Root),
                caller(from),
                Proto(wire::CreateTaskReq {
                    to: Some(to.into()),
                    body: Some("work".into()),
                }),
            )
            .await;
            let (status, Proto(error)) = result.unwrap_err();
            assert_eq!(status, StatusCode::BAD_REQUEST);
            assert!(error.error.contains("no such session: missing"));
            assert!(m.tasks.all_tasks().is_empty());
        }
    }
}
