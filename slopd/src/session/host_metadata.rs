//! Observed host paths and processes, stale-run rejection, and serialized tmux polling.
//! Application HostRecords owns saved records; lifecycle owns launch/adoption.

use super::*;
use std::collections::HashMap;
use std::future::Future;

fn update_host_process(l: &mut Live, command: &str) -> bool {
    let process_running = !crate::sandbox::is_shell_command(command);
    if l.process_running == process_running {
        return false;
    }
    l.process_running = process_running;
    true
}

/// Poll timing and the outstanding tmux metadata job.
#[derive(Default)]
pub(crate) struct HostMetadataPoll {
    // Epoch milliseconds; limits polling across maintenance callers.
    pub(super) checked: AtomicU64,
    // A slow listing must not overlap another or block activity classification.
    task: tokio::sync::Mutex<Option<JoinHandle<()>>>,
}

impl SessionRuntime {
    pub(super) async fn remember_host_path(self: &Arc<Self>, name: &str, path: &str) -> bool {
        self.remember_host_path_inner(name, None, path, true).await
    }

    async fn remember_host_path_for_run(
        self: &Arc<Self>,
        name: &str,
        run_id: u64,
        path: &str,
    ) -> bool {
        self.remember_host_path_inner(name, Some(run_id), path, false)
            .await
    }

    async fn remember_host_path_inner(
        self: &Arc<Self>,
        name: &str,
        run_id: Option<u64>,
        path: &str,
        sync_tmux: bool,
    ) -> bool {
        if path.trim().is_empty() {
            return false;
        }
        if let Some(run) = run_id {
            let live = self.live.read().await;
            if !live
                .get(name)
                .is_some_and(|l| l.role.is_host() && l.state != State::Down && l.run_id == run)
            {
                return false;
            }
        }
        let live_changed = {
            let mut live = self.live.write().await;
            live.get_mut(name)
                .filter(|l| {
                    l.role.is_host()
                        && l.host_path != path
                        && run_id.is_none_or(|run| l.run_id == run)
                })
                .map(|l| {
                    l.host_path = path.to_string();
                    true
                })
                .unwrap_or(false)
        };

        let (project, cfg_changed) = match self.host_records.remember_path(self, name, path).await {
            Ok(saved) => (saved.project, saved.changed),
            Err(error) => {
                tracing::warn!("could not persist host path for {name}: {error:#}");
                (None, false)
            }
        };

        if sync_tmux
            && (live_changed || cfg_changed)
            && let Some(project) = project
            && let Err(error) = self.tmux.set_host_metadata(name, &project, path).await
        {
            tracing::debug!("could not refresh host metadata for {name}: {error:#}");
        }
        live_changed || cfg_changed
    }

    async fn host_metadata_targets(&self) -> Vec<(String, u64)> {
        self.live
            .read()
            .await
            .values()
            .filter(|l| l.role.is_host() && l.state != State::Down)
            .map(|l| (l.cfg.name.clone(), l.run_id))
            .collect()
    }

    async fn apply_host_metadata(
        self: &Arc<Self>,
        targets: Vec<(String, u64)>,
        metadata: std::collections::HashMap<String, crate::tmux::HostMetadata>,
    ) {
        let mut changed = false;
        for (name, run_id) in targets {
            let Some(metadata) = metadata.get(&name) else {
                continue;
            };
            if let Some(path) = metadata.path.as_deref() {
                changed |= self.remember_host_path_for_run(&name, run_id, path).await;
            }
            if let Some(command) = metadata.command.as_deref() {
                let mut live = self.live.write().await;
                if let Some(l) = live
                    .get_mut(&name)
                    .filter(|l| l.role.is_host() && l.state != State::Down && l.run_id == run_id)
                {
                    changed |= update_host_process(l, command);
                }
            }
        }
        if changed {
            self.announce_sessions().await;
        }
    }

    pub(super) async fn start_host_metadata_poll(self: &Arc<Self>) {
        let tmux = self.tmux.clone();
        self.start_host_metadata_poll_with(move || async move {
            tmux.current_host_metadata_all().await
        })
        .await;
    }

    // Acquire inspection work only after admitting the poll. A slow inspection must
    // neither overlap another poll nor hold the session boundary while doing I/O.
    async fn start_host_metadata_poll_with<F>(self: &Arc<Self>, inspect: impl FnOnce() -> F)
    where
        F: Future<Output = Result<HashMap<String, crate::tmux::HostMetadata>>> + Send + 'static,
    {
        let mut poll = self.host_metadata.task.lock().await;
        if let Some(task) = poll.as_ref()
            && !task.is_finished()
        {
            return;
        }
        if let Some(task) = poll.take() {
            drop(task.await);
        }

        let targets = self.host_metadata_targets().await;
        if targets.is_empty() {
            return;
        }

        let manager = self.clone();
        let inspection = inspect();
        *poll = Some(tokio::spawn(async move {
            let started = std::time::Instant::now();
            match inspection.await {
                Ok(metadata) => {
                    let apply_manager = manager.clone();
                    manager
                        .session_operation(async move {
                            apply_manager.apply_host_metadata(targets, metadata).await;
                        })
                        .await;
                }
                Err(error) => {
                    tracing::debug!(error = %error, "Host metadata poll failed. Retaining known state.");
                }
            }
            tracing::debug!(
                target: "slopd::perf",
                lane = "host-metadata",
                elapsed_us = crate::clock::duration_us(started.elapsed()),
                "host metadata poll"
            );
        }));
    }
}

#[cfg(test)]
#[path = "host_metadata_tests.rs"]
pub(super) mod tests;
