//! Preserve pane exit evidence and recover a disconnected reader of a live process.
//! Lifecycle owns teardown; tmux owns process status, and sandbox state owns storage.

use super::super::lifecycle::stop::reader_owned_by;
use super::*;
use std::future::Future;
use std::pin::Pin;
use tokio::time::Instant;

pub(super) const CAPTURE_RETRY_DELAY: Duration = Duration::from_secs(2);

fn retry_due(live: &Live, now: Instant) -> bool {
    live.capture.emu.is_none()
        && live.capture.reader_token.is_none()
        && live.capture.retry_at.is_some_and(|due| due <= now)
}

impl Manager {
    /// Maintenance owns retry scheduling; inspection and attachment retain the
    /// lifecycle boundary so stopped or replaced runs cannot be revived.
    pub(in crate::session::manager) async fn retry_capture_if_due(self: &Arc<Self>) {
        if !self
            .live
            .read()
            .await
            .values()
            .any(|live| retry_due(live, Instant::now()))
        {
            return;
        }
        let manager = self.clone();
        self.owned_session_operation(async move {
            // Recheck after acquiring the boundary, then claim due retries under
            // one live lock. Fresh tokens exclude old reader completions.
            let retries = {
                let mut live = manager.live.write().await;
                let now = Instant::now();
                live.iter_mut()
                    .filter(|(_, current)| retry_due(current, now))
                    .map(|(name, current)| {
                        let token = Arc::new(());
                        current.capture.reader_token = Some(token.clone());
                        current.capture.retry_at = None;
                        (name.clone(), token)
                    })
                    .collect::<Vec<_>>()
            };
            for (name, token) in retries {
                manager.reader_ended_inner(&name, &token).await;
            }
        })
        .await;
    }

    // Boxing breaks the reader -> recovery -> reader asynchronous type cycle.
    pub(super) fn reader_ended(
        self: &Arc<Self>,
        name: String,
        token: Arc<()>,
    ) -> Pin<Box<dyn Future<Output = ()> + Send + '_>> {
        Box::pin(async move {
            self.session_operation(self.reader_ended_inner(&name, &token))
                .await;
        })
    }

    /// The caller holds the session boundary through inspection and reader handoff.
    async fn reader_ended_inner(self: &Arc<Self>, name: &str, token: &Arc<()>) {
        let session = {
            let live = self.live.read().await;
            let Some(current) = live.get(name).filter(|l| reader_owned_by(l, token)) else {
                return;
            };
            current.cfg.clone()
        };
        let outcome = match self.confirm_pane_exit(name).await {
            Ok(outcome) => outcome,
            Err(error) => {
                tracing::warn!(session = %name, "control reader disconnected; pane status unavailable, process retained: {error}");
                self.release_completed_reader(name, token).await;
                return;
            }
        };
        let Some(reason) = outcome else {
            self.recover_capture(name, token).await;
            return;
        };
        let saved = self.save_pane_exit(name, &session, &reason).await;
        if session.worker {
            self.fail_worker_task_checked(
                &session.task_id,
                &session.state_id,
                format!("worker session {name}: {reason}"),
            )
            .await;
        }
        // Keep the dead pane if recording failed; it still holds evidence.
        let terminal = self.terminal_boundary(name).write_owned().await;
        if saved && let Err(error) = self.tmux.kill(name).await {
            tracing::debug!(session = %name, "exit pane cleanup: {error}");
        }
        drop(terminal);
        self.mark_down(name, token).await;
    }

    async fn confirm_pane_exit(&self, name: &str) -> Result<Option<String>> {
        match self.tmux.pane_exit(name).await {
            Ok(outcome) => Ok(outcome),
            Err(error) => {
                // A query failure is not evidence of process exit. Verify absence
                // against a successful server listing before failing its task.
                let names = self.tmux.list_checked().await?;
                if names.iter().any(|current| current == name) {
                    return Err(error);
                }
                Ok(Some("pane disappeared; exit status unknown".into()))
            }
        }
    }

    async fn recover_capture(self: &Arc<Self>, name: &str, token: &Arc<()>) {
        if !self.release_completed_reader(name, token).await {
            return;
        }
        tracing::warn!(session = %name, "control reader disconnected; reattaching to live pane");
        // Attachment takes the terminal writer before sampling dimensions, so a
        // resize admitted after retirement completes before the new mirror is built.
        if let Err(error) = self.spawn_reader(name).await {
            tracing::warn!(session = %name, "control reader recovery failed; process retained: {error}");
        }
    }

    async fn save_pane_exit(&self, name: &str, session: &SessionCfg, reason: &str) -> bool {
        let captured = self
            .tmux
            .capture(name, 200)
            .await
            .ok()
            .map(|screen| screen.lines);
        let (run_id, screen) = {
            let live = self.live.read().await;
            let current = live.get(name);
            let fallback = current.and_then(|live| live.screen.as_ref()).map(|screen| {
                screen
                    .lines
                    .iter()
                    .map(|line| line.to_string())
                    .collect::<Vec<_>>()
            });
            (current.map(|live| live.run_id), captured.or(fallback))
        };
        let record = serde_json::json!({
            "session": name,
            "task_id": session.task_id,
            "time_ms": crate::clock::unix_ms(),
            "reason": reason,
            "state_id": session.state_id,
            "run_id": run_id,
            "screen": screen,
        });
        let result = async {
            let dir = crate::sandbox::state_dir(session)?;
            tokio::fs::create_dir_all(&dir).await?;
            crate::paths::write_atomic_async(
                &dir.join("exit.json"),
                &record.to_string(),
                Some(0o600),
            )
            .await
        }
        .await;
        match result {
            Ok(()) => true,
            Err(error) => {
                tracing::warn!(session = %name, "saving pane exit evidence: {error}");
                false
            }
        }
    }
}

#[cfg(test)]
#[path = "exit_tests.rs"]
mod tests;
