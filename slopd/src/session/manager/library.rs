//! Projects, library, file actions and errands.

use super::super::*;

use crate::process::{self, CaptureLimits};
use crate::session::input::ENTER_GAP;
use anyhow::anyhow;
use tokio::process::Command;

// Bound host file-action execution time.
const FILE_ACTION_TIMEOUT: Duration = Duration::from_secs(15);
// Bound captured output from each file-action stream.
const FILE_ACTION_STREAM_LIMIT: usize = 4096;
// Stop waiting for a startup prompt after this interval.
const READY_TIMEOUT: Duration = Duration::from_secs(30);
// Require a stable, nonblank screen before delivering startup input.
const SETTLE_TIME: Duration = Duration::from_millis(750);
// Let bracketed paste update the agent interface before submitting it.
const DELIVERY_ENTER_GAP: Duration = Duration::from_secs(1);

pub(super) fn routed_action_label(name: &str) -> Option<String> {
    ["view-", "search-", "link-", "edit-", "diff-"]
        .iter()
        .any(|prefix| name.starts_with(prefix))
        .then(|| name.to_string())
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
pub(super) struct DeliveryTarget {
    name: String,
    identity: String,
    run_id: u64,
    requires_paste_mode: bool,
}

impl DeliveryTarget {
    fn matches(&self, live: &Live) -> bool {
        live.cfg.state_id == self.identity && live.run_id == self.run_id
    }

    fn input_ready(&self, live: &Live) -> bool {
        !self.requires_paste_mode
            || live
                .capture
                .emu
                .as_ref()
                .and_then(|emu| emu.lock().ok().map(|emu| emu.bracketed_paste_enabled()))
                .unwrap_or(false)
    }
}

impl Manager {
    pub async fn library(&self) -> Vec<LibraryItemCfg> {
        let cfg = self.config().await;
        match tokio::task::spawn_blocking(move || cfg.library_items_all()).await {
            Ok(items) => {
                *self
                    .library_snapshot
                    .lock()
                    .unwrap_or_else(|error| error.into_inner()) = items.clone();
                items
            }
            Err(error) => {
                tracing::error!(
                    "library discovery task failed; keeping its last snapshot: {error}"
                );
                self.library_snapshot
                    .lock()
                    .unwrap_or_else(|error| error.into_inner())
                    .clone()
            }
        }
    }

    pub(super) async fn announce_library(&self) {
        self.emit(Event::Library {
            library: self.library().await,
        });
    }

    pub async fn add_library_item(self: &Arc<Self>, mut sc: LibraryItemCfg) -> Result<()> {
        self.reload_if_changed().await;
        self.update_cfg(|cfg| {
            sc.builtin = false;
            check_library_item(cfg, &sc)?;
            if cfg.library.iter().any(|existing| existing.name == sc.name) {
                bail!("library item {} already exists", sc.name);
            }
            cfg.library.push(sc.clone());
            Ok(())
        })
        .await?;
        self.announce_library().await;
        Ok(())
    }

    pub async fn update_library_item(
        self: &Arc<Self>,
        name: &str,
        mut sc: LibraryItemCfg,
    ) -> Result<()> {
        self.reload_if_changed().await;
        self.update_cfg(|cfg| {
            sc.builtin = false;
            if cfg.is_builtin_library_item(name) {
                bail!("The daemon cannot edit built-in library item {name}.");
            }
            check_library_item(cfg, &sc)?;
            let idx = cfg
                .library
                .iter()
                .position(|x| x.name == name)
                .ok_or_else(|| anyhow!("no such library item: {name}"))?;
            if sc.name != name && cfg.library.iter().any(|existing| existing.name == sc.name) {
                bail!("library item {} already exists", sc.name);
            }
            let item = cfg
                .library
                .get_mut(idx)
                .ok_or_else(|| anyhow!("library item {} does not exist", sc.name))?;
            *item = sc.clone();
            Ok(())
        })
        .await?;
        self.announce_projects().await;
        self.announce_library().await;
        Ok(())
    }

    pub async fn remove_library_item(self: &Arc<Self>, name: &str) -> Result<()> {
        self.reload_if_changed().await;
        self.update_cfg(|cfg| {
            if cfg.is_builtin_library_item(name) {
                bail!("The daemon cannot delete built-in library item {name}.");
            }
            if cfg.library_item(name).is_none() {
                bail!("no such library item: {name}");
            }
            cfg.library.retain(|s| s.name != name);
            Ok(())
        })
        .await?;
        self.announce_library().await;
        Ok(())
    }

    pub async fn run_library_item(self: &Arc<Self>, name: &str, want: RunWhere) -> Result<String> {
        self.reload_if_changed().await;
        let cfg = self.config().await;
        let sc = cfg
            .library_item(name)
            .ok_or_else(|| anyhow!("no such library item: {name}"))?
            .clone();
        check_library_item(&cfg, &sc)?;
        Self::validate_errand(&sc)?;
        drop(cfg);
        let host = sc.host;
        self.run_errand(sc, want, host, false, "").await
    }

    fn resolve_file_action(
        cfg: &Config,
        project: &str,
        raw_path: &str,
        command: &str,
        host: bool,
    ) -> Result<(ProjectCfg, SessionCfg)> {
        let (p, path) = if host && project.trim().is_empty() {
            (ProjectCfg::default(), absolute_path(raw_path)?)
        } else {
            let p = cfg
                .project(project.trim())
                .cloned()
                .ok_or_else(|| anyhow!("Project {project:?} does not exist."))?;
            let path = project_action_path(&p, raw_path)?;
            (p, path)
        };
        if command.trim().is_empty() {
            bail!("file action has no command");
        }
        let command = normalize_action_command(&path, command);
        let s = SessionCfg {
            name: "file-action".into(),
            project: p.name.clone(),
            command: cfg.defaults.shell.clone(),
            cmd: Some(command),
            ..Default::default()
        };
        Ok((p, s))
    }

    async fn run_file_action_command(
        argv: &[String],
        directory: &str,
    ) -> Result<process::BoundedOutput> {
        let (program, arguments) = argv
            .split_first()
            .ok_or_else(|| anyhow!("command has no executable"))?;
        let mut command = Command::new(program);
        command.args(arguments);
        if !directory.is_empty() {
            command.current_dir(directory);
        }
        process::run_bounded(
            &mut command,
            FILE_ACTION_TIMEOUT,
            CaptureLimits {
                stdout: FILE_ACTION_STREAM_LIMIT,
                stderr: FILE_ACTION_STREAM_LIMIT,
            },
        )
        .await
        .map_err(|error| {
            if error.downcast_ref::<process::TimedOut>().is_some() {
                anyhow!("file action timed out")
            } else {
                error.context("starting file action")
            }
        })
    }

    fn format_file_action_result(
        stdout: Vec<u8>,
        stderr: Vec<u8>,
        stdout_truncated: bool,
        stderr_truncated: bool,
        status: std::process::ExitStatus,
    ) -> Result<String> {
        let mut text = String::from_utf8_lossy(&stdout).into_owned();
        if !stderr.is_empty() {
            if !text.is_empty() && !text.ends_with('\n') {
                text.push('\n');
            }
            text.push_str(&String::from_utf8_lossy(&stderr));
        }
        if stdout_truncated || stderr_truncated {
            text.push_str("\n[output truncated]");
        }
        if !status.success() {
            bail!("file action failed: {}", text.trim());
        }
        Ok(if text.trim().is_empty() {
            "(no output)".into()
        } else {
            text.trim_end().into()
        })
    }

    async fn execute_file_action(cfg: &Config, s: &SessionCfg, p: &ProjectCfg) -> Result<String> {
        // File actions execute on the host as user operations.
        // Project scope validates their paths without restricting execution to the project sandbox.
        let argv = crate::sandbox::host_argv(cfg, s, p);
        let output = Self::run_file_action_command(&argv, &expand(&p.dir)).await?;
        Self::format_file_action_result(
            output.stdout,
            output.stderr,
            output.stdout_truncated,
            output.stderr_truncated,
            output.status,
        )
    }

    pub async fn file_action(
        self: &Arc<Self>,
        project: &str,
        worktree: &str,
        raw_path: &str,
        command: &str,
        host: bool,
    ) -> Result<String> {
        self.reload_if_changed().await;
        let cfg = self
            .config_for_action_scope(project, worktree, raw_path)
            .await?;
        let (p, s) = Self::resolve_file_action(&cfg, project, raw_path, command, host)?;
        Self::execute_file_action(&cfg, &s, &p).await
    }

    pub async fn file_action_command(
        self: &Arc<Self>,
        project: &str,
        worktree: &str,
        raw_path: &str,
        command: &str,
        host: bool,
    ) -> Result<String> {
        self.reload_if_changed().await;
        let cfg = self
            .config_for_action_scope(project, worktree, raw_path)
            .await?;
        let path = if host && project.trim().is_empty() {
            absolute_path(raw_path)?
        } else {
            let p = cfg
                .project(project.trim())
                .ok_or_else(|| anyhow!("Project {project:?} does not exist."))?;
            project_action_path(p, raw_path)?
        };
        Ok(normalize_action_command(&path, command))
    }

    fn validate_errand(sc: &LibraryItemCfg) -> Result<()> {
        if matches!(
            sc.kind,
            LibraryItemKind::Breadcrumb | LibraryItemKind::FileAction
        ) {
            bail!(
                "library item {} is not runnable as an agent errand",
                sc.name
            );
        }
        Ok(())
    }

    async fn queue_errand_delivery(
        self: &Arc<Self>,
        session: &str,
        sc: &LibraryItemCfg,
        want: &RunWhere,
    ) {
        let text = render_prompt(&sc.text, &want.random_tips);
        if text.trim().is_empty() {
            return;
        }
        self.queue_delivery(session, text).await;
    }

    pub async fn run_errand(
        self: &Arc<Self>,
        sc: LibraryItemCfg,
        want: RunWhere,
        host: bool,
        persistent_host: bool,
        like: &str,
    ) -> Result<String> {
        self.session_operation(self.run_errand_inner(sc, want, host, persistent_host, like))
            .await
    }

    async fn run_errand_inner(
        self: &Arc<Self>,
        sc: LibraryItemCfg,
        want: RunWhere,
        host: bool,
        persistent_host: bool,
        like: &str,
    ) -> Result<String> {
        self.reload_if_changed().await;
        let cfg = self.config().await;
        Self::validate_errand(&sc)?;
        let session = self
            .create_errand_session(&cfg, &sc, &want, host, persistent_host, like)
            .await?;

        if !self.tmux.exists(&session).await
            && let Err(e) = self.start(&session).await
        {
            if !persistent_host {
                self.forget(&session).await;
            }
            return Err(e);
        }

        self.announce_sessions().await;
        self.queue_errand_delivery(&session, &sc, &want).await;

        Ok(session)
    }

    /// Capture the destination before spawning, while the caller owns the session boundary.
    pub(super) async fn delivery_target(&self, name: &str) -> Option<DeliveryTarget> {
        let cfg = self.config().await;
        self.live.read().await.get(name).map(|live| DeliveryTarget {
            name: name.to_string(),
            identity: live.cfg.state_id.clone(),
            run_id: live.run_id,
            requires_paste_mode: !live.host
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

    pub(super) async fn queue_delivery(self: &Arc<Self>, name: &str, text: String) {
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
            let task = self
                .live
                .read()
                .await
                .get(&target.name)
                .filter(|live| target.matches(live) && live.cfg.worker)
                .map(|live| live.cfg.task_id.clone());
            if let Some(task) = task {
                self.fail_worker_task(&task, "startup input withheld: agent readiness timed out");
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
#[path = "library_tests.rs"]
mod tests;
