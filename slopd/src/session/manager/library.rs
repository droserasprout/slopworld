//! Projects, library, file actions and errands.

use super::super::*;
use crate::clock::unix_ms;

use crate::process::{self, CaptureLimits};
use crate::session::input::ENTER_GAP;
use crate::session::validation::check_project_mounts;
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

/// An owned destination for delayed input. Names alone can identify a replacement run.
#[derive(Clone)]
pub(super) struct DeliveryTarget {
    name: String,
    identity: String,
    run_id: u64,
}

impl DeliveryTarget {
    fn matches(&self, live: &Live) -> bool {
        live.cfg.state_id == self.identity && live.run_id == self.run_id
    }
}

impl Manager {
    pub async fn projects(&self) -> Vec<ProjectView> {
        self.cfg
            .read()
            .await
            .projects
            .iter()
            .cloned()
            .map(ProjectView::from)
            .collect()
    }

    pub(super) async fn announce_projects(&self) {
        self.emit(Event::Projects {
            projects: self.projects().await,
        });
    }

    pub async fn add_project(self: &Arc<Self>, mut p: ProjectCfg) -> Result<()> {
        self.reload_if_changed().await;
        settle(&mut p);
        p.id = uuid::Uuid::new_v4().to_string();
        check_project(&p)?;
        self.update_cfg(|cfg| {
            check_project_mounts(cfg, &p)?;
            if cfg.project(&p.name).is_some() {
                bail!("project {} already exists", p.name);
            }
            cfg.projects.push(p);
            Ok(())
        })
        .await?;
        self.announce_projects().await;
        Ok(())
    }

    pub async fn update_project(self: &Arc<Self>, name: &str, mut p: ProjectCfg) -> Result<()> {
        settle(&mut p);
        let manager = self.clone();
        let name = name.to_string();
        // The detached owner retains the boundary and rollback plan after caller cancellation.
        tokio::spawn(async move {
            manager
                .session_operation(async {
                    manager.reload_if_changed().await;
                    manager.update_project_inner(&name, p).await?;
                    manager.announce_projects().await;
                    manager.announce_sessions().await;
                    Ok::<_, anyhow::Error>(())
                })
                .await
        })
        .await
        .map_err(|error| anyhow!("project update task failed: {error}"))?
    }

    async fn update_project_inner(&self, name: &str, mut p: ProjectCfg) -> Result<()> {
        let _lock = self.worktrees.mutation.lock().await;
        let old_project = self
            .config()
            .await
            .project(name)
            .cloned()
            .ok_or_else(|| anyhow!("Project {name:?} does not exist."))?;
        if old_project.temp != p.temp {
            bail!("The daemon cannot change project temporary mode after creation.");
        }
        p.id = old_project.id.clone();
        check_project(&p)?;
        check_project_mounts(&self.config().await, &p)?;
        let store = crate::worktrees::Store::load(&self.cfg_path).await?;
        let mut planned = Vec::new();
        if p.name != name {
            crate::config::project_name_component(&p.name)?;
            if self.config().await.project(&p.name).is_some() {
                bail!("project {} already exists", p.name);
            }
            let local_root = PathBuf::from(expand(&old_project.dir)).join(".worktrees");
            let local_root = local_root.canonicalize().unwrap_or(local_root);
            for (i, w) in store
                .worktrees
                .iter()
                .enumerate()
                .filter(|(_, w)| w.managed && w.project_id == old_project.id)
            {
                // Project-local worktrees do not depend on the project's display name.
                if Path::new(&w.path).parent() == Some(local_root.as_path()) {
                    continue;
                }
                if w.phase != "ready" {
                    bail!(
                        "finish worktree {} recovery before renaming the project",
                        w.id
                    );
                }
                let users = self.worktree_attachments(w).await;
                if !users.is_empty() {
                    bail!(
                        "Worktree {} remains attached to {}. Remove or move these sessions first.",
                        w.name,
                        users.join(", ")
                    );
                }
                let old_path = Path::new(&w.path);
                if old_path.file_name().and_then(|s| s.to_str()) != Some(&w.name) {
                    bail!("worktree path does not match its recorded name");
                }
                let root = old_path
                    .parent()
                    .and_then(Path::parent)
                    .context("worktree root")?;
                let project_dir = root.join(&p.name);
                std::fs::create_dir_all(&project_dir)?;
                if std::fs::symlink_metadata(&project_dir)?
                    .file_type()
                    .is_symlink()
                {
                    bail!("worktree project directory cannot be a symlink");
                }
                let dest = project_dir.canonicalize()?.join(&w.name);
                if let Some(why) = crate::sandbox::refused(&dest.to_string_lossy()) {
                    bail!("worktree reaches {why}");
                }
                if std::fs::symlink_metadata(&dest).is_ok() {
                    bail!("destination {} already exists", dest.display());
                }
                let mut destination = w.clone();
                destination.path = dest.to_string_lossy().into_owned();
                planned.push((i, destination));
            }
        }
        let mut relocations = crate::worktrees::relocation::Relocations::new(store, planned);
        relocations.execute(&self.cfg_path).await?;
        #[cfg(test)]
        {
            let pause = self.worktrees.relocation_pause.lock().unwrap().take();
            if let Some((reached, release)) = pause {
                reached.notify_one();
                release.notified().await;
            }
        }
        let result = self
            .update_cfg_if_changed_inner(|cfg| {
                let idx = cfg
                    .projects
                    .iter()
                    .position(|x| x.name == name)
                    .ok_or_else(|| anyhow!("Project {name:?} does not exist."))?;
                if cfg.projects[idx].temp != p.temp {
                    bail!("The daemon cannot change project temporary mode after creation.");
                }
                p.id = cfg.projects[idx].id.clone();
                if p.id.is_empty() {
                    p.id = uuid::Uuid::new_v4().to_string();
                }
                check_project(&p)?;
                check_project_mounts(cfg, &p)?;
                if p.name != name && cfg.project(&p.name).is_some() {
                    bail!("project {} already exists", p.name);
                }
                let renamed = p.name.clone();
                cfg.projects[idx] = p;
                if renamed != name {
                    for s in cfg.sessions.iter_mut().filter(|s| s.project == name) {
                        s.project = renamed.clone();
                    }
                }
                Ok(((), true))
            })
            .await;
        if let Err(error) = result {
            return match relocations.rollback(&self.cfg_path).await {
                Ok(()) => Err(error),
                Err(rollback) => Err(anyhow!(
                    "{error:#}; restoring project worktrees also failed: {rollback:#}"
                )),
            };
        }
        Ok(())
    }

    pub async fn remove_project(self: &Arc<Self>, name: &str) -> Result<()> {
        self.reload_if_changed().await;
        let cfg = self.config().await;
        if let Some(p) = cfg.project(name) {
            if !p.id.is_empty()
                && crate::worktrees::Store::load(&self.cfg_path)
                    .await?
                    .worktrees
                    .iter()
                    .any(|w| w.project_id == p.id)
            {
                bail!("remove project worktrees explicitly first");
            }
        }
        self.update_cfg(|cfg| {
            if cfg.project(name).is_none() {
                bail!("Project {name:?} does not exist.");
            }
            let users: Vec<&str> = cfg
                .sessions
                .iter()
                .filter(|s| s.project == name)
                .map(|s| s.name.as_str())
                .collect();
            if !users.is_empty() {
                bail!(
                    "project {name} still has agents in it: {}. Remove or move them first.",
                    users.join(", ")
                );
            }
            cfg.projects.retain(|p| p.name != name);
            Ok(())
        })
        .await?;
        self.announce_projects().await;
        Ok(())
    }

    pub async fn library(&self) -> Vec<LibraryItemCfg> {
        let cfg = self.config().await;
        tokio::task::spawn_blocking(move || cfg.library_items_all())
            .await
            .unwrap_or_default()
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
            cfg.library[idx] = sc.clone();
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
        let mut command = Command::new(&argv[0]);
        command.args(&argv[1..]);
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

        if !self.tmux.exists(&session).await {
            if let Err(e) = self.start(&session).await {
                if !persistent_host {
                    self.forget(&session).await;
                }
                return Err(e);
            }
        }

        self.announce_sessions().await;
        self.queue_errand_delivery(&session, &sc, &want).await;

        Ok(session)
    }

    /// Capture the destination before spawning, while the caller owns the session boundary.
    pub(super) async fn delivery_target(&self, name: &str) -> Option<DeliveryTarget> {
        self.live.read().await.get(name).map(|live| DeliveryTarget {
            name: name.to_string(),
            identity: live.cfg.state_id.clone(),
            run_id: live.run_id,
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
        match self.wait_ready_for(target).await {
            Ready::Gone => return,
            Ready::Timeout => tracing::warn!(
                "{} did not become idle within {} ms. Sending its library item text anyway.",
                target.name,
                READY_TIMEOUT.as_millis()
            ),
            Ready::Settled => {}
        }
        self.admit_delivery(target, Some(text)).await;
    }

    /// Recheck and admit the whole sequence under one boundary. The queue consumer
    /// keeps this run's identity through delays; no stale producer can target a new queue.
    async fn admit_delivery(self: &Arc<Self>, target: &DeliveryTarget, prompt: Option<&str>) {
        self.session_read_operation(async {
            if !self
                .live
                .read()
                .await
                .get(&target.name)
                .is_some_and(|live| target.matches(live))
                || self.ensure_paste_ready(&target.name).await.is_err()
            {
                return;
            }
            if let Some(text) = prompt {
                if self.paste(&target.name, text).await.is_err() {
                    return;
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
        })
        .await;
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
        if matches!(self.wait_ready_for(target).await, Ready::Settled) {
            self.admit_delivery(target, None).await;
        }
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

    async fn wait_ready_for(&self, target: &DeliveryTarget) -> Ready {
        let started = unix_ms();
        let mut last_seq = u64::MAX;
        let mut still_since = 0u64;

        loop {
            let snap = {
                let live = self.live.read().await;
                live.get(&target.name).map(|l| {
                    let printed = l
                        .screen
                        .as_ref()
                        .map(|s| s.lines.iter().any(|x| !strip_sgr(x).trim().is_empty()))
                        .unwrap_or(false);
                    (l.seq, printed, l.state, target.matches(l))
                })
            };
            let Some((seq, printed, state, same_run)) = snap else {
                return Ready::Gone;
            };
            if state == State::Down || !same_run {
                return Ready::Gone;
            }

            let now = unix_ms();
            if !printed {
                still_since = 0;
            } else if seq != last_seq {
                last_seq = seq;
                still_since = now;
            } else if still_since > 0
                && Duration::from_millis(now.saturating_sub(still_since)) >= SETTLE_TIME
            {
                return Ready::Settled;
            }

            if Duration::from_millis(now.saturating_sub(started)) >= READY_TIMEOUT {
                return Ready::Timeout;
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
