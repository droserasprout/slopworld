//! Stopping, forgetting, and restarting live sessions.
//!
//! Detach under the live-map lock, then execute the owned cleanup plan after releasing it.
//! Explicit stops, reader exits, and configuration removal share this cleanup policy.

use crate::session::*;

/// Why a row is being detached; controls retention, cleanup, and notifications.
pub(crate) enum DetachCause {
    Stop,
    ProcessExit { reader_token: Arc<()> },
    ConfigRemoval,
    Forget,
}

/// Reader handle taken under the live lock and handled after releasing it.
pub(crate) enum ReaderDisposition {
    Abort(JoinHandle<()>),
    /// The exiting reader must finish its own cleanup rather than abort itself.
    CompletingCurrent(JoinHandle<()>),
    None,
}

/// Owned teardown decisions; carries no live-map references into asynchronous cleanup.
pub(crate) struct CleanupPlan {
    // Detached session identity.
    pub(crate) name: String,
    session: SessionCfg,
    project: String,

    // Resources and authority to release.
    pub(crate) reader: ReaderDisposition,
    remove_ephemeral_state: bool,
    remove_temp_project: bool,
    revoke_grants: bool,

    // Task outcome and client publication.
    worker_failure: Option<(String, String)>,
    announce_sessions: bool,
}

// Reader ownership and handoff, shared with startup and capture.

/// Match the reader instance, so an old exit cannot detach a replacement with the same name.
pub(crate) fn reader_owned_by(live: &Live, reader_token: &Arc<()>) -> bool {
    live.capture
        .reader_token
        .as_ref()
        .is_some_and(|current| Arc::ptr_eq(current, reader_token))
}

pub(crate) fn take_reader_for_abort(live: &mut Live) -> ReaderDisposition {
    live.capture
        .reader
        .take()
        .map(ReaderDisposition::Abort)
        .unwrap_or(ReaderDisposition::None)
}

fn take_reader_for_completion(live: &mut Live) -> ReaderDisposition {
    live.capture
        .reader
        .take()
        .map(ReaderDisposition::CompletingCurrent)
        .unwrap_or(ReaderDisposition::None)
}

pub(crate) fn replace_reader(live: &mut Live, reader: JoinHandle<()>) -> ReaderDisposition {
    live.capture
        .reader
        .replace(reader)
        .map(ReaderDisposition::Abort)
        .unwrap_or(ReaderDisposition::None)
}

pub(crate) fn finish_reader(reader: ReaderDisposition) {
    match reader {
        ReaderDisposition::Abort(reader) => reader.abort(),
        ReaderDisposition::CompletingCurrent(reader) => drop(reader),
        ReaderDisposition::None => {}
    }
}

/// Invalidate a process run while holding the live lock.
/// Preserve breadcrumbs and traces; callers handle the reader separately.
pub(crate) fn reset_process_state(live: &mut Live) {
    // Reject captures from the old run even if its session name is reused immediately.
    live.run_id = live.run_id.wrapping_add(1);
    live.set_state(State::Down);
    live.process_running = false;

    live.input.auto_resume_pending = false;
    live.bell = false;
    live.screen = None;
    live.capture.emu = None;
    live.title.reset();
    live.capture.reader_token = None;
    live.activity_at = None;
    live.input.sender = None;
}

impl Manager {
    /// Retain or remove the row under the map lock and return its deferred cleanup.
    /// Missing rows and exits from replaced readers require no action.
    pub(in crate::session::manager) fn detach_live_locked(
        &self,
        live: &mut HashMap<String, Live>,
        name: &str,
        cause: DetachCause,
    ) -> Option<CleanupPlan> {
        let current = live.get(name)?;
        let current_reader = match &cause {
            DetachCause::ProcessExit { reader_token } => {
                if !reader_owned_by(current, reader_token) {
                    return None;
                }
                true
            }
            _ => false,
        };

        // Saved sessions stay available to restart; disposable sessions are forgotten.
        let forget_live = match &cause {
            DetachCause::Stop | DetachCause::ProcessExit { .. } => {
                current.ephemeral && !current.persistent_host
            }
            DetachCause::ConfigRemoval | DetachCause::Forget => true,
        };

        let mut current = live.remove(name)?;
        let reader = if current_reader {
            take_reader_for_completion(&mut current)
        } else {
            take_reader_for_abort(&mut current)
        };

        let session = current.cfg.clone();
        let project = session.project.clone();
        let worker_failure = session.worker.then(|| {
            let note = match &cause {
                DetachCause::Stop => format!("The daemon stopped worker session {name}."),
                DetachCause::ProcessExit { .. } => format!("worker session {name} exited"),
                DetachCause::ConfigRemoval => format!("The daemon removed worker session {name}."),
                DetachCause::Forget => {
                    format!("Worker session {name} exited, or the daemon stopped it.")
                }
            };
            (session.task_id.clone(), note)
        });

        let plan = CleanupPlan {
            name: name.to_string(),
            session,
            project,
            reader,
            remove_ephemeral_state: forget_live && current.ephemeral && !current.host,
            remove_temp_project: forget_live,
            revoke_grants: forget_live
                || current.cfg.worker
                || matches!(&cause, DetachCause::ConfigRemoval),
            worker_failure,
            announce_sessions: matches!(
                &cause,
                DetachCause::Stop | DetachCause::ProcessExit { .. }
            ),
        };

        if !forget_live {
            reset_process_state(&mut current);
            live.insert(name.to_string(), current);
        }
        Some(plan)
    }

    /// Release resources and publish the outcome after the live-map lock is released.
    pub(in crate::session::manager) async fn execute_cleanup(self: &Arc<Self>, plan: CleanupPlan) {
        // Abort owned readers before any cancellation point.
        finish_reader(plan.reader);
        if plan.revoke_grants {
            // The session boundary prevents name reuse until cleanup finishes.
            self.invalidate_session(&plan.name).await;
        }
        self.forget_scroll(&plan.name);

        if plan.announce_sessions {
            // Publish the new session list before persistence and filesystem cleanup.
            self.announce_sessions().await;
        }

        self.clear_activity(&plan.name).await;
        self.clear_latest_title(&plan.name);
        if let Some((task_id, note)) = plan.worker_failure {
            self.fail_worker_task(&task_id, note);
        }

        // Only disposable sandbox state is deleted; durable private state survives.
        if plan.remove_ephemeral_state
            && let Err(error) = crate::sandbox::remove_ephemeral_state(&plan.session)
        {
            tracing::warn!(
                "removing temporary private state for {}: {error:#}",
                plan.name
            );
        }
        if plan.remove_temp_project {
            self.temp.write().await.remove(&plan.project);
        }
    }

    pub(in crate::session::manager) fn clear_latest_title(&self, name: &str) {
        self.title_cache.clear_latest(name);
    }
}

// Explicit lifecycle requests share the session-operation boundary.
impl Manager {
    pub async fn stop(self: &Arc<Self>, name: &str) -> Result<()> {
        self.session_operation(self.stop_inner(name)).await
    }

    async fn stop_inner(self: &Arc<Self>, name: &str) -> Result<()> {
        let _terminal = self.terminal_boundary(name).write_owned().await;
        if self.tmux.exists(name).await {
            self.tmux.kill(name).await?;
        }

        let plan = {
            let mut live = self.live.write().await;
            self.detach_live_locked(&mut live, name, DetachCause::Stop)
        };
        if let Some(plan) = plan {
            self.execute_cleanup(plan).await;
        } else {
            // Repeated stops still clear stale caches after the live row is gone.
            self.forget_scroll(name);
            self.announce_sessions().await;
            self.clear_activity(name).await;
            self.clear_latest_title(name);
        }
        Ok(())
    }

    /// Drop daemon state without issuing a tmux kill.
    pub(in crate::session::manager) async fn forget(self: &Arc<Self>, name: &str) {
        self.session_operation(self.forget_inner(name)).await
    }

    async fn forget_inner(self: &Arc<Self>, name: &str) {
        let _terminal = self.terminal_boundary(name).write_owned().await;
        let plan = {
            let mut live = self.live.write().await;
            self.detach_live_locked(&mut live, name, DetachCause::Forget)
        };
        if let Some(plan) = plan {
            self.execute_cleanup(plan).await;
        }
    }

    pub async fn restart(self: &Arc<Self>, name: &str) -> Result<()> {
        self.session_operation(self.restart_inner(name)).await
    }

    async fn restart_inner(self: &Arc<Self>, name: &str) -> Result<()> {
        self.stop(name).await?;
        self.start(name).await
    }
}

#[cfg(test)]
#[path = "stop_tests.rs"]
mod tests;
