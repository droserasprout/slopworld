//! Worker task authorization, destination planning, and follow-up audit coordination.
//! Session owns identity and delivery leases; tasks owns mutation policy and persistence.
use super::{Application, Configuration};
use crate::grant::Cap;
use crate::shared::protocol::SESSION_HEADER;
use anyhow::Context;

#[derive(Debug, Clone, Copy)]
pub(crate) enum Failure {
    Invalid,
    Forbidden,
    Missing,
    Internal,
    Unauthorized,
}

#[derive(Debug)]
pub(crate) struct OperationError {
    pub(crate) failure: Failure,
    pub(crate) message: String,
}
fn err(failure: Failure, error: impl std::fmt::Display) -> OperationError {
    OperationError {
        failure,
        message: error.to_string(),
    }
}

pub(crate) fn principal(
    cap: &Cap,
    requested_caller: Option<&str>,
) -> Result<String, OperationError> {
    let who = cap
        .principal()
        .map(str::to_string)
        .or_else(|| requested_caller.map(str::to_owned))
        .filter(|s| !s.trim().is_empty())
        .ok_or_else(|| {
            err(
                Failure::Invalid,
                format!("Set the {SESSION_HEADER} header for this root task request."),
            )
        })?;
    // The root token represents the user at the keyboard.
    // A grant uses its grantor's session name as the caller identity.
    // A session named `host` cannot use the host identity through a grant.
    if who == crate::tasks::HOST && !cap.may_create() {
        return Err(err(
            Failure::Forbidden,
            "Only the root token can use the host identity.",
        ));
    }
    Ok(who)
}

/// Task participation and each worker action permission are checked independently.
async fn require_cancel_permission(
    m: &Application,
    cap: &Cap,
    who: &str,
    id: &str,
) -> Result<(), OperationError> {
    if cap.may_create() {
        return Ok(());
    }
    if let Some(task) = m
        .sessions
        .task_store()
        .task_for_async(who, id)
        .await
        .map_err(|e| err(Failure::Internal, e))?
        && task.worker.is_some()
        && task.from_id == who
    {
        let target = m
            .sessions
            .views()
            .await
            .into_iter()
            .find(|view| view.worker.task_id == id);
        let access = match target {
            Some(target) => m.sessions.worker_access(cap, &target.name).await,
            None => None,
        };
        if !access.is_some_and(|access| access.cancel) {
            return Err(err(
                Failure::Forbidden,
                "The parent does not have permission for this worker action.",
            ));
        }
    }
    Ok(())
}

pub(crate) async fn followup_target(
    m: &Application,
    cap: &Cap,
    caller: &str,
    id: &str,
) -> Result<String, OperationError> {
    let who = m
        .sessions
        .task_participant(caller)
        .await
        .map_err(|e| err(Failure::Invalid, e))?
        .identity;
    let task = m
        .sessions
        .task_store()
        .task_for_async(&who, id)
        .await
        .map_err(|e| err(Failure::Internal, e))?
        .ok_or_else(|| err(Failure::Missing, "Task is unavailable."))?;
    if task.worker.is_none() || (!cap.may_create() && task.from_id != who) {
        return Err(err(
            Failure::Forbidden,
            "Only a worker's parent can send a follow-up.",
        ));
    }
    if task.status == crate::tasks::Status::Canceled {
        return Err(err(Failure::Invalid, "Task is canceled."));
    }
    let target = m
        .sessions
        .views()
        .await
        .into_iter()
        .find(|view| view.worker.task_id == id);
    if !cap.may_create() && task.from_id == who {
        let access = match &target {
            Some(target) => m.sessions.worker_access(cap, &target.name).await,
            None => None,
        };
        if !access.is_some_and(|access| access.followup) {
            return Err(err(
                Failure::Forbidden,
                "The parent does not have permission for this worker action.",
            ));
        }
    }
    let target = target.ok_or_else(|| err(Failure::Missing, "Worker no longer exists."))?;
    let cfg = m
        .sessions
        .session_cfg(&target.name)
        .await
        .ok_or_else(|| err(Failure::Missing, "Worker no longer exists."))?;
    if cfg.state_id != task.to_id {
        return Err(err(Failure::Forbidden, "Worker identity changed."));
    }
    Ok(target.name)
}

pub(crate) async fn followup(
    m: &Application,
    cap: &Cap,
    generation: Option<u64>,
    requested_caller: Option<String>,
    id: String,
    body: String,
) -> Result<(), OperationError> {
    let validate_credentials = || {
        if !cap.is_valid()
            || (cap.may_create()
                && generation.is_none_or(|g| g != m.config_authority.root_generation()))
        {
            Err(err(Failure::Unauthorized, "capability revoked"))
        } else {
            Ok(())
        }
    };
    // Prepare under admission, then wait for readiness outside the boundary.
    let target = Configuration::new(m)
        .session_request(async {
            validate_credentials()?;
            let name =
                followup_target(m, cap, &principal(cap, requested_caller.as_deref())?, &id).await?;
            m.sessions
                .prepare_followup(&name, &body)
                .await
                .map_err(|e| err(Failure::Invalid, e))
        })
        .await?;
    m.sessions
        .wait_followup(&target)
        .await
        .map_err(|e| err(Failure::Invalid, e))?;
    // Revalidate before handing the audit and submission to an owned operation.
    Configuration::new(m)
        .session_request(async {
            validate_credentials()?;
            followup_target(m, cap, &principal(cap, requested_caller.as_deref())?, &id).await?;
            let sessions = m.sessions.clone();
            let sender = principal(cap, requested_caller.as_deref())?;
            m.sessions
                .owned_session_operation(async move {
                    let (audit_id, audit_sender, audit_body) =
                        (id.clone(), sender.clone(), body.clone());
                    sessions
                        .task_store()
                        .run(move |tasks| {
                            tasks.followup_note(
                                &audit_id,
                                &audit_sender,
                                format!("Follow-up requested: {audit_body}"),
                            )
                        })
                        .await?;
                    let result = sessions.submit_followup(&target, &body).await;
                    let outcome = followup_outcome(&result);
                    let saved = sessions
                        .task_store()
                        .run(move |tasks| tasks.followup_note(&id, &sender, outcome))
                        .await;
                    saved.context("Follow-up delivery completed, but saving its outcome failed")?;
                    result
                })
                .await
                .map_err(|e| err(Failure::Invalid, e))
        })
        .await
}

fn followup_outcome(result: &anyhow::Result<()>) -> String {
    match result {
        Ok(()) => "Follow-up submitted to the worker's current run; task status unchanged.".into(),
        Err(error) => format!("Follow-up not delivered: {error:#}"),
    }
}

pub(crate) async fn cancel(
    m: &Application,
    cap: &Cap,
    who: crate::tasks::Participant,
    ids: Vec<String>,
) -> Result<crate::tasks::BatchResult, OperationError> {
    let mut supervised = Vec::new();
    for id in &ids {
        require_cancel_permission(m, cap, &who.identity, id).await?;
        if !cap.may_create() {
            supervised.push(id.clone());
        }
    }
    let (identity, sender, ids, force) = (who.identity, who.name, ids, cap.may_create());
    let tasks = if supervised.is_empty() {
        m.sessions
            .task_store()
            .cancel_tasks_async(&identity, &sender, &ids, force)
            .await
    } else {
        m.sessions
            .task_store()
            .run(move |tasks| tasks.cancel_supervised(&identity, &sender, &ids, force, &supervised))
            .await
    }
    .map_err(|e| err(Failure::Invalid, e))?;
    Ok(tasks)
}

/// The store retains transition policy; application adds relationship authorization.
pub(crate) async fn update(
    m: &Application,
    cap: &Cap,
    who: crate::tasks::Participant,
    id: &str,
    status: crate::tasks::Status,
    note: Option<String>,
) -> Result<crate::tasks::Task, OperationError> {
    if status == crate::tasks::Status::Canceled {
        require_cancel_permission(m, cap, &who.identity, id).await?;
    }
    m.sessions
        .task_store()
        .update_task_async(&who.identity, &who.name, id, status, note)
        .await
        .map_err(|e| err(Failure::Invalid, e))
}
