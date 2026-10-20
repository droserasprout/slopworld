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
    let result = Box::pin(spawn_worker(
        axum::extract::State(manager.clone()),
        Extension(Cap::Root),
        headers,
        Proto(wire::SpawnWorkerReq {
            body: Some("missing template".into()),
            ..Default::default()
        }),
    ))
    .await;
    let Err((status, Proto(body))) = result else {
        panic!("missing template request unexpectedly succeeded");
    };
    assert_eq!(status, StatusCode::BAD_REQUEST);
    assert!(body.error.contains("template"));
    assert!(manager.tasks.all_tasks_async().await.unwrap().is_empty());
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
    let m = mailbox_manager();
    let visible = mailbox_task(&m, "alice", "bob", "visible").await.unwrap();
    let hidden = mailbox_task(&m, "carol", "dave", "private").await.unwrap();
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
async fn renamed_recipient_updates_use_current_label_without_changing_authority() {
    let m = crate::session::test_manager(crate::config::Config {
        projects: vec![crate::config::ProjectCfg {
            id: crate::storage_id::draft_identity(),
            name: "repo".into(),
            dir: std::env::temp_dir().to_string_lossy().into_owned(),
            ..Default::default()
        }],
        sessions: ["alice", "bob", "carol"]
            .into_iter()
            .map(|name| crate::config::SessionCfg {
                name: name.into(),
                state_id: mailbox_identity(name),
                project: "repo".into(),
                ..Default::default()
            })
            .collect(),
        ..Default::default()
    });
    let task = mailbox_task(&m, "alice", "bob", "work").await.unwrap();
    let canceled = mailbox_task(&m, "alice", "bob", "cancel").await.unwrap();
    update_task(
        State(m.clone()),
        Extension(scoped("bob")),
        HeaderMap::new(),
        Path(task.id.clone()),
        Proto(wire::UpdateTaskReq {
            status: Some("accepted".into()),
            note: None,
        }),
    )
    .await
    .unwrap();
    // Rename the original recipient and reuse its old label for another identity.
    for (old, new) in [("bob", "renamed"), ("carol", "bob")] {
        let mut session = m.config().await.session(old).unwrap().clone();
        session.name = new.into();
        m.update(old, session).await.unwrap();
    }
    let update = || {
        Proto(wire::UpdateTaskReq {
            status: Some("working".into()),
            note: Some("reviewing".into()),
        })
    };
    update_task(
        State(m.clone()),
        Extension(scoped("bob")),
        HeaderMap::new(),
        Path(task.id.clone()),
        update(),
    )
    .await
    .unwrap_err();
    let Proto(reply) = update_task(
        State(m.clone()),
        Extension(scoped("renamed")),
        HeaderMap::new(),
        Path(task.id.clone()),
        update(),
    )
    .await
    .unwrap();
    let history = reply.task.unwrap().history;
    assert_eq!(history.len(), 2);
    assert_eq!(history[0].sender, "bob");
    assert_eq!(history[1].sender, "renamed");

    let Proto(reply) = cancel_tasks(
        State(m.clone()),
        Extension(scoped("renamed")),
        HeaderMap::new(),
        Proto(wire::RemoveTasksReq {
            ids: vec![canceled.id.clone()],
        }),
    )
    .await
    .unwrap();
    assert_eq!(reply.tasks[0].history[0].sender, "renamed");
    let reloaded = crate::tasks::Tasks::load(&m.cfg_path).unwrap();
    assert_eq!(
        reloaded
            .get(&mailbox_identity("bob"), &task.id)
            .unwrap()
            .history[1]
            .sender,
        "renamed"
    );
    assert_eq!(
        reloaded
            .get(&mailbox_identity("bob"), &canceled.id)
            .unwrap()
            .history[0]
            .sender,
        "renamed"
    );
}

#[tokio::test]
async fn recipient_updates_and_removal_preserve_task_lifecycle() {
    let m = mailbox_manager();
    let task = mailbox_task(&m, "alice", "bob", "work").await.unwrap();
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
    assert_eq!(updated.history.len(), 1);
    assert_eq!(updated.history[0].sender, "bob");
    assert_eq!(updated.history[0].status, "done");
    assert_eq!(updated.history[0].note.as_deref(), Some("verified"));
    assert_eq!(updated.history[0].timestamp_ms, updated.updated_ms);
    assert_eq!(
        remove_task(
            State(m.clone()),
            Extension(scoped("carol")),
            HeaderMap::new(),
            Path(task.id.clone()),
        )
        .await
        .unwrap_err()
        .0,
        StatusCode::BAD_REQUEST
    );
    let Proto(reply) = remove_task(
        State(m.clone()),
        Extension(scoped("alice")),
        HeaderMap::new(),
        Path(task.id.clone()),
    )
    .await
    .unwrap();
    assert_eq!(reply.task.unwrap().id, task.id);
    assert!(m.tasks.all_tasks_async().await.unwrap().is_empty());
}

#[tokio::test]
async fn bulk_cancel_remove_and_prune_enforce_scope() {
    let m = mailbox_manager();
    let a = mailbox_task(&m, "alice", "bob", "first").await.unwrap();
    let b = mailbox_task(&m, "carol", "dave", "second").await.unwrap();
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
    assert!(
        m.tasks
            .all_tasks_async()
            .await
            .unwrap()
            .iter()
            .all(|t| t.status == crate::tasks::Status::Queued)
    );
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
    assert_eq!(m.tasks.all_tasks_async().await.unwrap().len(), 2);
    assert_eq!(
        remove_tasks(
            State(m.clone()),
            Extension(scoped("alice")),
            HeaderMap::new(),
            ids(),
        )
        .await
        .unwrap_err()
        .0,
        StatusCode::BAD_REQUEST
    );
    let Proto(reply) = prune_tasks(
        State(m.clone()),
        Extension(scoped("bob")),
        HeaderMap::new(),
        Query(PruneTasksQuery { all: false }),
    )
    .await
    .unwrap();
    assert_eq!(reply.removed, 1);
    assert_eq!(m.tasks.all_tasks_async().await.unwrap()[0].id, b.id);
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
    assert!(m.tasks.all_tasks_async().await.unwrap().is_empty());
}
#[tokio::test]
async fn unknown_task_endpoints_fail_before_persistence() {
    let m = mailbox_manager();
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
        assert!(error.error.contains("Session missing does not exist."));
        assert!(m.tasks.all_tasks_async().await.unwrap().is_empty());
    }
}

fn mailbox_manager() -> Mgr {
    crate::session::test_manager(crate::config::Config {
        sessions: ["alice", "bob", "carol", "dave"]
            .into_iter()
            .map(|name| crate::config::SessionCfg {
                name: name.into(),
                state_id: mailbox_identity(name),
                ..Default::default()
            })
            .collect(),
        ..Default::default()
    })
}

#[tokio::test]
async fn batch_response_reports_partial_record_commits_and_idempotent_retry() {
    let m = mailbox_manager();
    let data = m.cfg_path.parent().unwrap().join("data");
    let a = mailbox_task(&m, "alice", "bob", "first").await.unwrap();
    let b = mailbox_task(&m, "alice", "bob", "second").await.unwrap();
    let c = mailbox_task(&m, "alice", "bob", "third").await.unwrap();
    let request = || {
        Proto(wire::RemoveTasksReq {
            ids: vec![c.id.clone(), b.id.clone(), a.id.clone()],
        })
    };
    let fault = crate::paths::fail_writes(&data.join("tasks").join(format!("{}.toml", b.id)));
    let Proto(result) = cancel_tasks(
        State(m.clone()),
        Extension(Cap::Root),
        caller("host"),
        request(),
    )
    .await
    .unwrap();
    assert_eq!(result.committed, std::slice::from_ref(&a.id));
    assert_eq!(result.failed[0].id, b.id);
    assert_eq!(result.unattempted, std::slice::from_ref(&c.id));
    drop(fault);
    let Proto(result) = cancel_tasks(
        State(m.clone()),
        Extension(Cap::Root),
        caller("host"),
        request(),
    )
    .await
    .unwrap();
    assert_eq!(result.unchanged, std::slice::from_ref(&a.id));
    assert_eq!(result.committed, [b.id.clone(), c.id.clone()]);
    let Proto(result) = remove_tasks(
        State(m.clone()),
        Extension(Cap::Root),
        caller("host"),
        request(),
    )
    .await
    .unwrap();
    assert_eq!(result.removed, 3);
    let Proto(result) = remove_tasks(State(m), Extension(Cap::Root), caller("host"), request())
        .await
        .unwrap();
    assert_eq!(result.absent.len(), 3);
    assert!(result.failed.is_empty());
    assert_eq!(result.removed, 0);
}

fn mailbox_identity(name: &str) -> String {
    let at = ["alice", "bob", "carol", "dave"]
        .iter()
        .position(|value| *value == name)
        .unwrap();
    format!("{at:016x}")
}

async fn mailbox_task(
    m: &Mgr,
    from: &str,
    to: &str,
    body: &str,
) -> anyhow::Result<crate::tasks::Task> {
    m.create_task_owned(
        crate::tasks::Participant {
            name: from.into(),
            identity: mailbox_identity(from),
        },
        crate::tasks::Participant {
            name: to.into(),
            identity: mailbox_identity(to),
        },
        body.into(),
        None,
    )
    .await
}
