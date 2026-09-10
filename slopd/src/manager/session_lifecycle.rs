//! Stopping, forgetting, and restarting live sessions.

use super::super::*;

pub(crate) enum DetachCause {
    Stop,
    ProcessExit { reader_token: Arc<()> },
    ConfigRemoval,
    Forget { reader_token: Option<Arc<()>> },
}

pub(crate) enum ReaderDisposition {
    Abort(JoinHandle<()>),
    CompletingCurrent(JoinHandle<()>),
    None,
}

pub(crate) struct CleanupPlan {
    pub(crate) name: String,
    session: SessionCfg,
    project: String,
    pub(crate) reader: ReaderDisposition,
    remove_ephemeral_state: bool,
    remove_temp_project: bool,
    revoke_grants: bool,
    worker_failure: Option<(String, String)>,
    announce_sessions: bool,
}

pub(crate) fn reader_owned_by(live: &Live, reader_token: &Arc<()>) -> bool {
    live.reader_token
        .as_ref()
        .is_some_and(|current| Arc::ptr_eq(current, reader_token))
}

pub(crate) fn take_reader_for_abort(live: &mut Live) -> ReaderDisposition {
    live.reader
        .take()
        .map(ReaderDisposition::Abort)
        .unwrap_or(ReaderDisposition::None)
}

fn take_reader_for_completion(live: &mut Live) -> ReaderDisposition {
    live.reader
        .take()
        .map(ReaderDisposition::CompletingCurrent)
        .unwrap_or(ReaderDisposition::None)
}

pub(crate) fn replace_reader(live: &mut Live, reader: JoinHandle<()>) -> ReaderDisposition {
    live.reader
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

/// Clear the process-owned part of a live row. The transition itself remains lock-local; all
/// cache, task, grant, and event effects happen in `execute_cleanup` after the lock is released.
pub(crate) fn reset_process_state(live: &mut Live) {
    live.set_state(State::Down);
    live.process_running = false;
    live.auto_resume_pending = false;
    live.bell = false;
    live.screen = None;
    live.emu = None;
    live.title = TitleCapture::default();
    live.reader_token = None;
    live.input = None;
}

impl Manager {
    /// Decide ownership and retain/remove the live row while the map is locked. The returned
    /// plan contains only owned values, so its executor never needs to hold the live lock across
    /// filesystem, tmux, task, grant, or event work.
    pub(super) fn detach_live_locked(
        &self,
        live: &mut HashMap<String, Live>,
        name: &str,
        cause: DetachCause,
    ) -> Option<CleanupPlan> {
        let current = live.get(name)?;
        let current_reader = match &cause {
            DetachCause::ProcessExit { reader_token }
            | DetachCause::Forget {
                reader_token: Some(reader_token),
            } => {
                if !reader_owned_by(current, reader_token) {
                    return None;
                }
                true
            }
            _ => false,
        };

        let forget_live = match &cause {
            DetachCause::Stop | DetachCause::ProcessExit { .. } => {
                current.ephemeral && !current.host
            }
            DetachCause::ConfigRemoval | DetachCause::Forget { .. } => true,
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
                DetachCause::Stop => format!("worker session {name} was stopped"),
                DetachCause::ProcessExit { .. } => format!("worker session {name} exited"),
                DetachCause::ConfigRemoval => format!("worker session {name} was removed"),
                DetachCause::Forget { .. } => {
                    format!("worker session {name} exited or was stopped")
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

    pub(super) async fn execute_cleanup(self: &Arc<Self>, plan: CleanupPlan) {
        finish_reader(plan.reader);
        self.forget_scroll(&plan.name);
        if plan.announce_sessions {
            // The visible transition goes first. Cleanup may query a tmux session that has
            // already disappeared, and must not hold back the next session snapshot.
            self.emit(Event::Sessions {
                sessions: self.views().await,
            });
        }
        self.clear_activity(&plan.name).await;
        self.clear_latest_title(&plan.name);
        if let Some((task_id, note)) = plan.worker_failure {
            self.fail_worker_task(&task_id, note);
        }
        if plan.remove_ephemeral_state {
            if let Err(error) = crate::sandbox::remove_ephemeral_state(&plan.session) {
                tracing::warn!(
                    "removing temporary private state for {}: {error:#}",
                    plan.name
                );
            }
        }
        if plan.remove_temp_project {
            self.temp.write().await.remove(&plan.project);
        }
        if plan.revoke_grants {
            self.revoke_grants(&plan.name).await;
        }
    }

    pub(super) fn clear_latest_title(&self, name: &str) {
        if let Err(error) = self.title_cache.clear_latest(name) {
            tracing::warn!(
                target: "slopd::titles",
                session = %name,
                error = %error,
                outcome = "cache_write_failed",
                "could not clear session title"
            );
        }
    }
}

impl Manager {
    pub async fn stop(self: &Arc<Self>, name: &str) -> Result<()> {
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
            self.forget_scroll(name);
            self.emit(Event::Sessions {
                sessions: self.views().await,
            });
            self.clear_activity(name).await;
            self.clear_latest_title(name);
        }
        Ok(())
    }

    async fn forget_inner(self: &Arc<Self>, name: &str, reader_token: Option<&Arc<()>>) {
        let plan = {
            let mut live = self.live.write().await;
            self.detach_live_locked(
                &mut live,
                name,
                DetachCause::Forget {
                    reader_token: reader_token.cloned(),
                },
            )
        };
        if let Some(plan) = plan {
            self.execute_cleanup(plan).await;
        }
    }

    pub(super) async fn forget(self: &Arc<Self>, name: &str) {
        self.forget_inner(name, None).await;
    }

    pub async fn restart(self: &Arc<Self>, name: &str) -> Result<()> {
        self.stop(name).await?;
        self.start(name).await
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn config_removal_does_not_delete_durable_private_state() {
        let manager = crate::session::test_manager(Config::default());
        let mut live = HashMap::new();
        live.insert(
            "agent".into(),
            Live::new(
                SessionCfg {
                    name: "agent".into(),
                    ..Default::default()
                },
                TitleCapture::default(),
            ),
        );

        let plan = manager
            .detach_live_locked(&mut live, "agent", DetachCause::ConfigRemoval)
            .expect("durable live row should produce a cleanup plan");

        assert!(!plan.remove_ephemeral_state);
    }

    #[test]
    fn config_removal_deletes_ephemeral_private_state() {
        let manager = crate::session::test_manager(Config::default());
        let mut live = HashMap::new();
        let mut ephemeral = Live::new(
            SessionCfg {
                name: "agent".into(),
                ..Default::default()
            },
            TitleCapture::default(),
        );
        ephemeral.ephemeral = true;
        live.insert("agent".into(), ephemeral);

        let plan = manager
            .detach_live_locked(&mut live, "agent", DetachCause::ConfigRemoval)
            .expect("ephemeral live row should produce a cleanup plan");

        assert!(plan.remove_ephemeral_state);
    }
}
