//! Stopping, forgetting, and restarting live sessions.

use super::super::*;

impl Manager {
    pub async fn stop(self: &Arc<Self>, name: &str) -> Result<()> {
        let worker_task = self
            .live
            .read()
            .await
            .get(name)
            .filter(|l| l.cfg.worker)
            .map(|l| l.cfg.task_id.clone());
        if self.tmux.exists(name).await {
            self.tmux.kill(name).await?;
        }
        if self.is_ephemeral(name).await && !self.is_host(name).await {
            self.forget(name).await;
            return Ok(());
        }
        if let Some(l) = self.live.write().await.get_mut(name) {
            l.set_state(State::Down);
            l.process_running = false;
            l.auto_resume_pending = false;
            l.screen = None;
            l.emu = None;
            // A stopped run must not leave its generated task title on the downed
            // session. Resetting the capture also makes late title responses from
            // this run stale before the next start.
            l.title = TitleCapture::default();
            l.reader_token = None;
            if let Some(h) = l.reader.take() {
                h.abort();
            }
        }
        self.forget_scroll(name);
        // Removal is the visible transition. Publish it before filesystem, sandbox and tmux
        // cleanup so a dead ephemeral pane cannot hold the client on its old session list.
        self.emit(Event::Sessions {
            sessions: self.views().await,
        });
        self.clear_activity(name).await;
        if let Err(error) = self.title_cache.clear_latest(name) {
            tracing::warn!(
                target: "slopd::titles",
                session = %name,
                error = %error,
                outcome = "cache_write_failed",
                "could not clear session title"
            );
        }
        if let Some(task_id) = worker_task {
            self.fail_worker_task(&task_id, format!("worker session {name} was stopped"));
            self.revoke_grants(name).await;
        }
        Ok(())
    }

    async fn forget_inner(self: &Arc<Self>, name: &str, reader_token: Option<&Arc<()>>) {
        let (handle, project, session) = {
            let mut live = self.live.write().await;
            let Some(l) = live.get(name) else { return };
            if let Some(reader_token) = reader_token {
                if !l
                    .reader_token
                    .as_ref()
                    .is_some_and(|current| Arc::ptr_eq(current, reader_token))
                {
                    return;
                }
            }
            let mut l = live.remove(name).expect("live entry checked above");
            (l.reader.take(), l.cfg.project.clone(), l.cfg)
        };
        if session.worker {
            self.fail_worker_task(
                &session.task_id,
                format!("worker session {name} exited or was stopped"),
            );
        }
        self.forget_scroll(name);
        self.clear_activity(name).await;
        if let Err(error) = self.title_cache.clear_latest(name) {
            tracing::warn!(
                target: "slopd::titles",
                session = %name,
                error = %error,
                outcome = "cache_write_failed",
                "could not clear session title"
            );
        }
        if let Err(e) = crate::sandbox::remove_ephemeral_state(&session) {
            tracing::warn!("removing temporary private state for {name}: {e:#}");
        }
        self.temp.write().await.remove(&project);
        self.revoke_grants(name).await;
        if reader_token.is_none() {
            if let Some(h) = handle {
                h.abort();
            }
        }
    }

    pub(super) async fn forget(self: &Arc<Self>, name: &str) {
        self.forget_inner(name, None).await;
    }

    pub(super) async fn forget_from_reader(self: &Arc<Self>, name: &str, reader_token: &Arc<()>) {
        self.forget_inner(name, Some(reader_token)).await;
    }

    pub async fn restart(self: &Arc<Self>, name: &str) -> Result<()> {
        self.stop(name).await?;
        self.start(name).await
    }
}
