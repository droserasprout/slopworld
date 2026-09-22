//! Projects, library, file actions and errands.

use super::super::*;

use crate::process::{self, CaptureLimits};
use crate::session::validation::check_project_mounts;
use anyhow::anyhow;
use tokio::process::Command;

pub(super) fn routed_action_label(name: &str) -> Option<String> {
    ["view-", "search-", "link-", "edit-", "diff-"]
        .iter()
        .any(|prefix| name.starts_with(prefix))
        .then(|| name.to_string())
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
        self.reload_if_changed().await;
        settle(&mut p);
        self.update_cfg(|cfg| {
            let idx = cfg
                .projects
                .iter()
                .position(|x| x.name == name)
                .ok_or_else(|| anyhow!("no such project: {name}"))?;
            if cfg.projects[idx].temp != p.temp {
                bail!("project temporary mode cannot be changed after creation");
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
            Ok(())
        })
        .await?;
        self.announce_projects().await;
        self.announce_sessions().await;
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
                bail!("no such project: {name}");
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
                bail!("library item {name} is built in and cannot be edited");
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
                bail!("library item {name} is built in and cannot be deleted");
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
        let (p, path) = if host {
            (ProjectCfg::default(), absolute_path(raw_path)?)
        } else {
            let p = cfg
                .project(project.trim())
                .cloned()
                .ok_or_else(|| anyhow!("no such project: {project}"))?;
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
        // File actions are user operations; project scope validates paths, not execution.
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
        raw_path: &str,
        command: &str,
        host: bool,
    ) -> Result<String> {
        self.reload_if_changed().await;
        let cfg = self.config_for_worktree_path(project, raw_path).await?;
        let (p, s) = Self::resolve_file_action(&cfg, project, raw_path, command, host)?;
        Self::execute_file_action(&cfg, &s, &p).await
    }

    pub async fn file_action_command(
        self: &Arc<Self>,
        project: &str,
        raw_path: &str,
        command: &str,
        host: bool,
    ) -> Result<String> {
        self.reload_if_changed().await;
        let cfg = self.config_for_worktree_path(project, raw_path).await?;
        let path = if host {
            absolute_path(raw_path)?
        } else {
            let p = cfg
                .project(project.trim())
                .ok_or_else(|| anyhow!("no such project: {project}"))?;
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

    fn queue_errand_delivery(
        self: &Arc<Self>,
        session: &str,
        sc: &LibraryItemCfg,
        want: &RunWhere,
    ) {
        let text = render_template(&sc.text, &want.random_tips);
        if text.trim().is_empty() {
            return;
        }
        let m = self.clone();
        let target = session.to_string();
        let tips = want.random_tips.clone();
        tokio::spawn(async move { m.deliver(&target, &text, tips).await });
    }

    pub async fn run_errand(
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
        self.queue_errand_delivery(&session, &sc, &want);

        Ok(session)
    }

    pub(super) async fn deliver(
        self: &Arc<Self>,
        name: &str,
        text: &str,
        random_tips: Vec<String>,
    ) {
        match self.wait_ready(name).await {
            Ready::Gone => {
                tracing::warn!("{name} was gone before its library item text could be sent");
                return;
            }
            Ready::Timeout => tracing::warn!(
                "{name} never went quiet after {}ms; sending its library item text anyway",
                READY_MS
            ),
            Ready::Settled => {}
        }

        if let Err(e) = self.paste(name, text).await {
            tracing::error!("sending library item text to {name}: {e:#}");
            return;
        }
        // Delivery owns the complete startup prompt sequence. Keep its Enter out of send_keys,
        // whose generated-instruction hook can otherwise splice the first prompt incorrectly.
        if let Some(breadcrumbs) = self.consume_breadcrumbs(name, &random_tips).await {
            if !breadcrumbs.is_empty() {
                self.queue_paste(name, breadcrumbs).await;
                self.queue_input(
                    name,
                    Input::Gap(Duration::from_millis(DELIVERY_ENTER_GAP_MS)),
                )
                .await;
            }
            self.announce_sessions().await;
        }
        let enter = vec!["Enter".into()];
        self.capture_title_keys(name, &enter, false).await;
        self.queue_input(
            name,
            Input::Gap(Duration::from_millis(DELIVERY_ENTER_GAP_MS)),
        )
        .await;
        self.queue_input(
            name,
            Input::Keys {
                keys: enter,
                literal: false,
            },
        )
        .await;
    }

    pub(super) fn queue_auto_resume(self: &Arc<Self>, name: &str, run_id: u64) {
        let manager = self.clone();
        let name = name.to_string();
        tokio::spawn(async move { manager.auto_resume(&name, run_id).await });
    }

    async fn auto_resume(self: &Arc<Self>, name: &str, run_id: u64) {
        match self.wait_ready_for(name, Some(run_id)).await {
            Ready::Gone => {
                self.finish_auto_resume(name, run_id).await;
                return;
            }
            Ready::Timeout => {
                tracing::warn!(
                    "{name} never went quiet after {}ms; skipping auto-resume",
                    READY_MS
                );
                self.finish_auto_resume(name, run_id).await;
                return;
            }
            Ready::Settled => {}
        }

        // This is startup control input, not the agent's first prompt. Keep it out of title
        // capture and the breadcrumb Enter hook; breadcrumbs remain pending for the user's prompt.
        for input in auto_resume_inputs() {
            self.queue_input(name, input).await;
        }
        self.finish_auto_resume(name, run_id).await;
    }

    async fn finish_auto_resume(&self, name: &str, run_id: u64) {
        let changed = {
            let mut live = self.live.write().await;
            match live.get_mut(name) {
                Some(l) if l.run_id == run_id && l.auto_resume_pending => {
                    l.auto_resume_pending = false;
                    true
                }
                _ => false,
            }
        };
        if changed {
            self.announce_sessions().await;
        }
    }

    pub(super) async fn wait_ready(&self, name: &str) -> Ready {
        self.wait_ready_for(name, None).await
    }

    async fn wait_ready_for(&self, name: &str, run_id: Option<u64>) -> Ready {
        let deadline = now_ms() + READY_MS;
        let mut last_seq = u64::MAX;
        let mut still_since = 0u64;

        loop {
            let snap = {
                let live = self.live.read().await;
                live.get(name).map(|l| {
                    let printed = l
                        .screen
                        .as_ref()
                        .map(|s| s.lines.iter().any(|x| !strip_sgr(x).trim().is_empty()))
                        .unwrap_or(false);
                    (l.seq, printed, l.state, l.run_id)
                })
            };
            let Some((seq, printed, state, current_run_id)) = snap else {
                return Ready::Gone;
            };
            if state == State::Down || run_id.is_some_and(|expected| expected != current_run_id) {
                return Ready::Gone;
            }

            let now = now_ms();
            if !printed {
                still_since = 0;
            } else if seq != last_seq {
                last_seq = seq;
                still_since = now;
            } else if still_since > 0 && now.saturating_sub(still_since) >= SETTLE_MS {
                return Ready::Settled;
            }

            if now >= deadline {
                return Ready::Timeout;
            }
            tokio::time::sleep(Duration::from_millis(100)).await;
        }
    }
}

fn auto_resume_inputs() -> Vec<Input> {
    vec![
        Input::Bytes(b"/resume".to_vec()),
        Input::Gap(Duration::from_millis(ENTER_GAP_MS)),
        Input::Keys {
            keys: vec!["Enter".into()],
            literal: false,
        },
        Input::Gap(Duration::from_millis(ENTER_GAP_MS)),
        Input::Keys {
            keys: vec!["Enter".into()],
            literal: false,
        },
    ]
}

#[cfg(test)]
#[path = "library_tests.rs"]
mod tests;
