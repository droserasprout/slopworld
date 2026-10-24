//! Worker task failure policy for lifecycle and startup events.
//! Callers own run/reader validation and operation guards; tasks owns durable,
//! identity-checked transitions and preserves terminal results. Cleanup stays in lifecycle.

use super::{SessionCfg, SessionRuntime, lifecycle::stop::DetachCause};

pub(crate) enum WorkerFailureCause {
    RegistrationFailed(String),
    StartFailed(String),
    ProcessExited { reason: Option<String> },
    Stopped,
    Removed,
    Forgotten,
    DeliveryTimedOut,
}

/// Capture the task identity before releasing live state, never resolve it by name later.
pub(crate) struct WorkerFailure {
    task_id: String,
    identity: String,
    note: String,
}

impl WorkerFailure {
    pub(crate) fn for_session(session: &SessionCfg, event: WorkerFailureCause) -> Option<Self> {
        if !session.worker || session.task_id.is_empty() {
            return None;
        }
        let name = &session.name;
        let note = match event {
            WorkerFailureCause::RegistrationFailed(error) => {
                format!("could not persist worker session: {error}")
            }
            WorkerFailureCause::StartFailed(error) => format!("worker failed to start: {error}"),
            WorkerFailureCause::ProcessExited {
                reason: Some(reason),
            } => {
                format!("worker session {name}: {reason}")
            }
            WorkerFailureCause::ProcessExited { reason: None } => {
                format!("worker session {name} exited")
            }
            WorkerFailureCause::Stopped => format!("The daemon stopped worker session {name}."),
            WorkerFailureCause::Removed => format!("The daemon removed worker session {name}."),
            WorkerFailureCause::Forgotten => {
                format!("Worker session {name} exited, or the daemon stopped it.")
            }
            WorkerFailureCause::DeliveryTimedOut => {
                "startup input withheld: agent readiness timed out".into()
            }
        };
        Some(Self {
            task_id: session.task_id.clone(),
            identity: session.state_id.clone(),
            note,
        })
    }

    pub(super) fn for_detach(session: &SessionCfg, cause: &DetachCause) -> Option<Self> {
        let event = match cause {
            DetachCause::Stop => WorkerFailureCause::Stopped,
            DetachCause::ProcessExit { .. } => WorkerFailureCause::ProcessExited { reason: None },
            DetachCause::ConfigRemoval => WorkerFailureCause::Removed,
            DetachCause::Forget => WorkerFailureCause::Forgotten,
        };
        Self::for_session(session, event)
    }

    pub(super) async fn apply(self, runtime: &SessionRuntime) {
        if let Err(error) = runtime
            .task_store()
            .fail_worker_async(&self.task_id, &self.identity, self.note)
            .await
        {
            tracing::error!(task = %self.task_id, "could not persist worker failure: {error:#}");
        }
    }
}

impl SessionRuntime {
    /// Startup-only recovery, after adoption and before requests can launch workers.
    /// Read persisted tasks as one-shot workers may have no saved session at all.
    /// A failed tmux query is not evidence of loss; leave tasks intact and retry startup.
    pub(crate) async fn recover_missing_worker_tasks(&self) -> anyhow::Result<()> {
        self.session_operation(async {
            let panes = self.tmux.list_checked().await?;
            // Task labels capture their original names. A renamed worker still
            // owns its task through the durable session identity.
            let surviving_tasks = {
                let live = self.live.read().await;
                live.iter()
                    .filter(|(name, row)| row.cfg.worker && panes.contains(name))
                    .map(|(_, row)| (row.cfg.state_id.clone(), row.cfg.task_id.clone()))
                    .collect::<std::collections::HashSet<_>>()
            };
            let tasks = self.task_store().all_tasks_async().await?;
            for task in tasks.into_iter().filter(|task| !task.status.is_terminal()) {
                let Some(worker) = &task.worker else { continue };
                if panes.contains(&worker.session)
                    || surviving_tasks.contains(&(task.to_id.clone(), task.id.clone()))
                {
                    continue;
                }
                self.task_store()
                    .fail_worker_async(
                        &task.id,
                        &task.to_id,
                        format!(
                            "worker session {} was lost while the daemon was offline",
                            worker.session
                        ),
                    )
                    .await?;
            }
            Ok(())
        })
        .await
    }

    /// Caller retains its operation guard and validates any delayed run or reader first.
    pub(crate) async fn record_worker_failure(
        &self,
        session: &SessionCfg,
        event: WorkerFailureCause,
    ) {
        if let Some(failure) = WorkerFailure::for_session(session, event) {
            failure.apply(self).await;
        }
    }
}

#[cfg(test)]
#[path = "worker_failures_tests.rs"]
mod tests;
