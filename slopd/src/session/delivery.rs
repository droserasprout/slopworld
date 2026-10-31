//! Delayed terminal input: run identity, readiness, ordered submission, and auto-resume.
//! Callers compose prompts; capture/input owns the queue and execution of admitted input.

use super::*;
use crate::session::input::ENTER_GAP;

// Stop waiting for a startup prompt after this interval.
const READY_TIMEOUT: Duration = Duration::from_secs(30);
// Require a stable, nonblank screen before delivering startup input.
const SETTLE_TIME: Duration = Duration::from_millis(750);
// Let bracketed paste update the agent interface before submitting it.
const DELIVERY_ENTER_GAP: Duration = Duration::from_secs(1);

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum Ready {
    Settled,
    // Startup continues in the background. Only Gone cancels queued typing.
    Timeout,
    Gone,
}

/// Admission can lose readiness while waiting for the session boundary.
#[derive(Debug, PartialEq, Eq)]
enum Admission {
    Submitted,
    Retry,
    Gone,
    Timeout,
}

/// An owned destination for delayed input. Names alone can identify a replacement run.
#[derive(Clone)]
pub(crate) struct DeliveryTarget {
    name: String,
    identity: String,
    run_id: u64,
    requires_paste_mode: bool,
    requires_codex_composer: bool,
    followup: bool,
}

impl DeliveryTarget {
    fn matches(&self, live: &Live) -> bool {
        live.cfg.state_id == self.identity && live.run_id == self.run_id
    }

    fn input_ready(&self, live: &Live) -> bool {
        let paste_ready = !self.requires_paste_mode
            || live
                .capture
                .emu
                .as_ref()
                .and_then(|emu| emu.lock().ok().map(|emu| emu.bracketed_paste_enabled()))
                .unwrap_or(false);
        // Codex enables paste while its startup UI is still consuming input.
        // A quiet banner is not its composer: require the visible cursor on the
        // composer row, and recheck this at admission after waiting on locks.
        let composer_ready = !self.requires_codex_composer
            || live.screen.as_ref().is_some_and(|screen| {
                screen
                    .lines
                    .get(usize::from(screen.cy))
                    .is_some_and(|line| strip_sgr(line).trim_start().starts_with('›'))
            });
        paste_ready && composer_ready
    }
}

impl SessionRuntime {
    /// Capture the destination before spawning, while the caller owns the session boundary.
    async fn delivery_target(&self, name: &str) -> Option<DeliveryTarget> {
        let cfg = self.config().await;
        self.live.read().await.get(name).map(|live| DeliveryTarget {
            name: name.to_string(),
            followup: false,
            identity: live.cfg.state_id.clone(),
            run_id: live.run_id,
            requires_codex_composer: !live.role.is_host()
                && crate::sandbox::shell_split(&cfg.command_of(&live.cfg))
                    .ok()
                    .and_then(|argv| argv.into_iter().next())
                    .is_some_and(|exe| {
                        Path::new(&exe)
                            .file_name()
                            .is_some_and(|name| name == "codex")
                    }),
            requires_paste_mode: !live.role.is_host()
                && live.cfg.command_snapshot.as_ref().map_or_else(
                    || {
                        !live
                            .cfg
                            .preset_table()
                            .command(&cfg.command_name(&live.cfg))
                            .is_some_and(|command| {
                                command.kind == crate::presets::CommandKind::Shell
                            })
                    },
                    |command| command.kind != crate::presets::CommandKind::Shell,
                ),
        })
    }

    /// Explicit follow-ups wake the current worker without exposing raw terminal input.
    /// Reject control characters and shell destinations; readiness and the input queue retain run identity.
    pub(crate) async fn prepare_followup(&self, name: &str, text: &str) -> Result<DeliveryTarget> {
        anyhow::ensure!(!text.trim().is_empty(), "Follow-up text cannot be empty.");
        anyhow::ensure!(
            !text
                .chars()
                .any(|c| c.is_control() && c != '\n' && c != '\t'),
            "Follow-up contains terminal control characters."
        );
        let cfg = self.config().await;
        let live = self.live.read().await;
        let current = live.get(name).context("Worker is not running")?;
        let executable = crate::sandbox::shell_split(&cfg.command_of(&current.cfg))?
            .into_iter()
            .next()
            .unwrap_or_default();
        anyhow::ensure!(
            current.cfg.worker && !current.role.is_host() && current.state != State::Down,
            "Follow-ups require a live task worker"
        );
        anyhow::ensure!(
            !crate::sandbox::is_shell_command(&executable),
            "Follow-ups cannot be sent to shell workers"
        );
        drop(live);
        let mut target = self
            .delivery_target(name)
            .await
            .context("Worker is not running")?;
        anyhow::ensure!(
            target.requires_paste_mode,
            "Follow-ups cannot be sent to shell templates"
        );
        self.tmux.require_agent_input(name).await?;
        target.followup = true;
        Ok(target)
    }

    /// Wait without session admission: the worker may need the API to become ready.
    pub(crate) async fn wait_followup(&self, target: &DeliveryTarget) -> Result<()> {
        match self
            .wait_ready_until(target, tokio::time::Instant::now() + READY_TIMEOUT)
            .await
        {
            Ready::Settled => Ok(()),
            Ready::Timeout => bail!("Worker was not ready; follow-up was not delivered."),
            Ready::Gone => bail!("Worker stopped or restarted; follow-up was not delivered."),
        }
    }

    /// Caller revalidates authority and task state under exclusive admission. Do not
    /// wait again here if readiness was lost while acquiring that boundary.
    pub(crate) async fn submit_followup(
        self: &Arc<Self>,
        target: &DeliveryTarget,
        text: &str,
    ) -> Result<()> {
        let prompt = format!("Follow-up from your supervising agent:\n{text}");
        match self
            .admit_delivery(
                target,
                Some(&prompt),
                tokio::time::Instant::now() + READY_TIMEOUT,
            )
            .await
        {
            Admission::Submitted => Ok(()),
            Admission::Gone => bail!("Worker stopped or restarted; follow-up was not delivered."),
            Admission::Retry | Admission::Timeout => {
                bail!("Worker was not ready; follow-up was not delivered.")
            }
        }
    }

    #[cfg(test)]
    async fn followup_delivery(self: &Arc<Self>, name: &str, text: &str) -> Result<()> {
        let target = self.prepare_followup(name, text).await?;
        self.wait_followup(&target).await?;
        self.submit_followup(&target, text).await
    }

    pub(crate) async fn queue_delivery(self: &Arc<Self>, name: &str, text: String) {
        if let Some(target) = self.delivery_target(name).await {
            let manager = self.clone();
            tokio::spawn(async move { manager.deliver(&target, &text).await });
        }
    }

    /// Submit an already composed worker or errand prompt to its original process run.
    async fn deliver(self: &Arc<Self>, target: &DeliveryTarget, text: &str) {
        self.deliver_until(target, text, tokio::time::Instant::now() + READY_TIMEOUT)
            .await;
    }

    async fn deliver_until(
        self: &Arc<Self>,
        target: &DeliveryTarget,
        text: &str,
        deadline: tokio::time::Instant,
    ) {
        if matches!(
            self.submit_when_ready(target, Some(text), deadline).await,
            Ready::Timeout
        ) {
            tracing::warn!(session = %target.name, "startup input withheld: readiness timed out");
            self.fail_timed_out_delivery(target).await;
        }
    }

    /// Readiness and admission share one deadline, including retries after capture
    /// recovery or a mode change. Once submitted, never enqueue the sequence again.
    async fn submit_when_ready(
        self: &Arc<Self>,
        target: &DeliveryTarget,
        prompt: Option<&str>,
        deadline: tokio::time::Instant,
    ) -> Ready {
        loop {
            match self.wait_ready_until(target, deadline).await {
                Ready::Settled => {}
                outcome => return outcome,
            }
            match self.admit_delivery(target, prompt, deadline).await {
                Admission::Submitted => return Ready::Settled,
                Admission::Gone => return Ready::Gone,
                Admission::Timeout => return Ready::Timeout,
                Admission::Retry => {}
            }
        }
    }

    async fn fail_timed_out_delivery(&self, target: &DeliveryTarget) {
        self.session_read_operation(async {
            let failure = self
                .live
                .read()
                .await
                .get(&target.name)
                .filter(|live| target.matches(live))
                .and_then(|live| {
                    WorkerFailure::for_session(&live.cfg, WorkerFailureCause::DeliveryTimedOut)
                });
            if let Some(failure) = failure {
                failure.apply(self).await;
            }
        })
        .await;
    }

    /// Recheck and admit the whole sequence under one boundary. The queue consumer
    /// keeps this run's identity through delays; no stale producer can target a new queue.
    async fn admit_delivery(
        self: &Arc<Self>,
        target: &DeliveryTarget,
        prompt: Option<&str>,
        deadline: tokio::time::Instant,
    ) -> Admission {
        self.session_read_operation(async {
            {
                let live = self.live.read().await;
                let Some(current) = live.get(&target.name).filter(|live| target.matches(live))
                else {
                    return Admission::Gone;
                };
                if tokio::time::Instant::now() >= deadline {
                    return Admission::Timeout;
                }
                if !target.input_ready(current) {
                    return Admission::Retry;
                }
            }
            if self.ensure_paste_ready(&target.name).await.is_err() {
                return Admission::Retry;
            }
            if let Some(text) = prompt {
                if target.followup {
                    if self.tmux.require_agent_input(&target.name).await.is_err() {
                        return Admission::Gone;
                    }
                    self.queue_input(
                        &target.name,
                        Input::AgentPaste {
                            bytes: text.as_bytes().to_vec(),
                        },
                    )
                    .await;
                    self.queue_input(&target.name, Input::Gap(DELIVERY_ENTER_GAP))
                        .await;
                    self.queue_input(&target.name, Input::AgentEnter).await;
                    return Admission::Submitted;
                }
                if self.paste(&target.name, text).await.is_err() {
                    return Admission::Retry;
                }
                let enter = vec!["Enter".into()];
                self.capture_title_keys(&target.name, &enter, false).await;
                self.queue_input(&target.name, Input::Gap(DELIVERY_ENTER_GAP))
                    .await;
                self.queue_input(
                    &target.name,
                    Input::Keys {
                        keys: enter,
                        literal: false,
                    },
                )
                .await;
            } else {
                // Auto-resume is startup control, excluded from title capture.
                for input in auto_resume_inputs() {
                    self.queue_input(&target.name, input).await;
                }
            }
            Admission::Submitted
        })
        .await
    }

    pub(super) async fn queue_auto_resume(self: &Arc<Self>, name: &str, run_id: u64) {
        if let Some(target) = self
            .delivery_target(name)
            .await
            .filter(|target| target.run_id == run_id)
        {
            let manager = self.clone();
            tokio::spawn(async move { manager.auto_resume(&target).await });
        }
    }

    async fn auto_resume(self: &Arc<Self>, target: &DeliveryTarget) {
        self.submit_when_ready(target, None, tokio::time::Instant::now() + READY_TIMEOUT)
            .await;
        self.finish_auto_resume(target).await;
    }

    async fn finish_auto_resume(&self, target: &DeliveryTarget) {
        let changed = {
            let mut live = self.live.write().await;
            match live.get_mut(&target.name) {
                Some(l) if target.matches(l) && l.input.auto_resume_pending => {
                    l.input.auto_resume_pending = false;
                    true
                }
                _ => false,
            }
        };
        if changed {
            self.announce_sessions().await;
        }
    }

    async fn wait_ready_until(
        &self,
        target: &DeliveryTarget,
        deadline: tokio::time::Instant,
    ) -> Ready {
        let mut last_seq = u64::MAX;
        let mut still_since = None;

        loop {
            let snap = {
                let live = self.live.read().await;
                live.get(&target.name).map(|l| {
                    let printed = l
                        .screen
                        .as_ref()
                        .map(|s| s.lines.iter().any(|x| !strip_sgr(x).trim().is_empty()))
                        .unwrap_or(false);
                    let paste_mode = target.input_ready(l);
                    let running = l.state != State::Down || l.capture.reader_token.is_some();
                    (l.seq, printed && paste_mode, running, target.matches(l))
                })
            };
            let Some((seq, printed, running, same_run)) = snap else {
                return Ready::Gone;
            };
            if !running || !same_run {
                return Ready::Gone;
            }

            let now = tokio::time::Instant::now();
            if now >= deadline {
                return Ready::Timeout;
            }
            if !printed {
                still_since = None;
            } else if seq != last_seq || still_since.is_none() {
                last_seq = seq;
                still_since = Some(now);
            } else if still_since.is_some_and(|since| now.duration_since(since) >= SETTLE_TIME) {
                return Ready::Settled;
            }

            tokio::time::sleep(Duration::from_millis(100)).await;
        }
    }
}

fn auto_resume_inputs() -> Vec<Input> {
    vec![
        Input::Bytes(b"/resume".to_vec()),
        Input::Gap(ENTER_GAP),
        Input::Keys {
            keys: vec!["Enter".into()],
            literal: false,
        },
        Input::Gap(ENTER_GAP),
        Input::Keys {
            keys: vec!["Enter".into()],
            literal: false,
        },
    ]
}

#[cfg(test)]
#[path = "delivery_tests.rs"]
mod tests;
