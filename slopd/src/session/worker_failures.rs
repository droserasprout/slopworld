//! Worker task failure policy for lifecycle and startup events.
//! Callers own run/reader validation and operation guards; tasks owns durable,
//! identity-checked transitions and preserves terminal results. Cleanup stays in lifecycle.

use super::{SessionCfg, SessionRuntime, lifecycle::stop::DetachCause};
use std::{
    collections::HashMap,
    sync::{Arc, Mutex},
    time::Duration,
};
use tokio::time::Instant;

const RETRY_DELAY: Duration = Duration::from_secs(2);

// Runtime-owned evidence outlives disposable live rows. No background task owns a
// runtime reference: maintenance drives retries, and daemon startup recovers loss.
#[derive(Default)]
pub(super) struct PendingWorkerFailures {
    entries: Mutex<HashMap<(String, String), PendingFailure>>,
    commit: tokio::sync::Mutex<()>,
}

struct PendingFailure {
    failure: Arc<WorkerFailure>,
    retry_at: Instant,
}

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

    pub(super) async fn apply(self, runtime: &SessionRuntime) -> anyhow::Result<()> {
        let failure = runtime.retain_worker_failure(self);
        runtime.commit_worker_failure(failure).await
    }
}

impl SessionRuntime {
    /// Register evidence synchronously before retirement or any cancellation point.
    /// First evidence wins, including a detailed exit followed by generic cleanup.
    pub(super) fn retain_worker_failure(&self, failure: WorkerFailure) -> Arc<WorkerFailure> {
        let key = (failure.task_id.clone(), failure.identity.clone());
        let mut pending = self
            .worker_failures
            .entries
            .lock()
            .unwrap_or_else(|error| error.into_inner());
        let entry = pending.entry(key).or_insert_with(|| PendingFailure {
            failure: Arc::new(failure),
            retry_at: Instant::now(),
        });
        self.maintenance_wake().notify_one();
        entry.failure.clone()
    }

    pub(super) async fn commit_worker_failure(
        &self,
        failure: Arc<WorkerFailure>,
    ) -> anyhow::Result<()> {
        // Serialize attempts, but never hold the evidence map lock across task I/O.
        // Cancellation leaves the entry present; the store may finish independently,
        // and a later idempotent retry observes its terminal result.
        let _commit = self.worker_failures.commit.lock().await;
        let key = (failure.task_id.clone(), failure.identity.clone());
        if !self
            .worker_failures
            .entries
            .lock()
            .unwrap_or_else(|error| error.into_inner())
            .get(&key)
            .is_some_and(|entry| Arc::ptr_eq(&entry.failure, &failure))
        {
            return Ok(());
        }
        match self
            .task_store()
            .fail_worker_async(&failure.task_id, &failure.identity, failure.note.clone())
            .await
        {
            Ok(_) => {
                // Deleted tasks, changed identities and existing terminal outcomes
                // settle the pending evidence without recreating or overwriting them.
                self.worker_failures
                    .entries
                    .lock()
                    .unwrap_or_else(|error| error.into_inner())
                    .remove(&key);
                Ok(())
            }
            Err(error) => {
                if let Some(entry) = self
                    .worker_failures
                    .entries
                    .lock()
                    .unwrap_or_else(|error| error.into_inner())
                    .get_mut(&key)
                {
                    entry.retry_at = Instant::now() + RETRY_DELAY;
                }
                self.maintenance_wake().notify_one();
                tracing::error!(task = %failure.task_id, "could not persist worker failure; retained for retry: {error:#}");
                Err(error)
            }
        }
    }

    pub(super) fn worker_failure_delay(&self) -> Option<Duration> {
        let now = Instant::now();
        self.worker_failures
            .entries
            .lock()
            .unwrap_or_else(|error| error.into_inner())
            .values()
            .map(|entry| entry.retry_at.saturating_duration_since(now))
            .min()
    }

    pub(super) async fn retry_worker_failures(&self) {
        let due = self
            .worker_failures
            .entries
            .lock()
            .unwrap_or_else(|error| error.into_inner())
            .values()
            .filter(|entry| entry.retry_at <= Instant::now())
            .map(|entry| entry.failure.clone())
            .collect::<Vec<_>>();
        for failure in due {
            // The commit owner logs errors and retains evidence for the next deadline.
            drop(self.commit_worker_failure(failure).await);
        }
    }

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
                if surviving_tasks.contains(&(task.to_id.clone(), task.id.clone())) {
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
            // The commit owner logs errors and retains evidence for maintenance.
            drop(failure.apply(self).await);
        }
    }
}

#[cfg(test)]
#[path = "worker_failures_tests.rs"]
mod tests;
