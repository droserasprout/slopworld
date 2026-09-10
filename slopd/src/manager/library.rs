//! Projects, library, file actions and errands.

use super::super::*;

use crate::process::{self, CaptureLimits};
use crate::sandbox::build_argv;
use anyhow::anyhow;
use tokio::process::Command;

pub(super) fn routed_action_label(name: &str) -> Option<String> {
    ["view-", "search-", "link-", "edit-", "diff-"]
        .iter()
        .any(|prefix| name.starts_with(prefix))
        .then(|| name.to_string())
}

impl Manager {
    pub async fn projects(&self) -> Vec<ProjectCfg> {
        self.cfg.read().await.projects.clone()
    }

    pub(super) async fn announce_projects(&self) {
        self.emit(Event::Projects {
            projects: self.projects().await,
        });
    }

    pub async fn add_project(self: &Arc<Self>, mut p: ProjectCfg) -> Result<()> {
        self.reload_if_changed().await;
        settle(&mut p);
        check_project(&p)?;
        p.limits.validate()?;
        crate::runtime::validate_limits(&p.limits)?;
        self.update_cfg(|cfg| {
            check_breadcrumbs(cfg, &p.breadcrumbs)?;
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
        check_project(&p)?;
        p.limits.validate()?;
        crate::runtime::validate_limits(&p.limits)?;

        let (old_dir, new_dir) = self
            .update_cfg(|cfg| {
                check_breadcrumbs(cfg, &p.breadcrumbs)?;
                let idx = cfg
                    .projects
                    .iter()
                    .position(|x| x.name == name)
                    .ok_or_else(|| anyhow!("no such project: {name}"))?;
                let old_dir = crate::config::expand(&cfg.projects[idx].dir);
                let new_dir = crate::config::expand(&p.dir);
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
                Ok((old_dir, new_dir))
            })
            .await?;

        if old_dir != new_dir {
            if let Err(error) = crate::manifest::remove(std::path::Path::new(&old_dir)) {
                tracing::warn!("could not remove old generated project manifest: {error:#}");
            }
        }

        let current = self.config().await;
        self.sync_manifests(&current).await;
        self.announce_projects().await;
        self.emit(Event::Sessions {
            sessions: self.views().await,
        });
        Ok(())
    }

    pub async fn remove_project(self: &Arc<Self>, name: &str) -> Result<()> {
        self.reload_if_changed().await;
        let old_dir = self
            .update_cfg(|cfg| {
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
                let old_dir = cfg
                    .project(name)
                    .map(|project| crate::config::expand(&project.dir))
                    .unwrap_or_default();
                cfg.projects.retain(|p| p.name != name);
                Ok(old_dir)
            })
            .await?;
        if !old_dir.is_empty() {
            if let Err(error) = crate::manifest::remove(std::path::Path::new(&old_dir)) {
                tracing::warn!("could not remove generated project manifest: {error:#}");
            }
        }
        self.announce_projects().await;
        Ok(())
    }

    pub async fn library(&self) -> Vec<LibraryItemCfg> {
        self.cfg.read().await.library_items_all()
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
            let attach_project = (sc.kind == LibraryItemKind::Breadcrumb
                && !sc.project.trim().is_empty())
            .then(|| sc.project.clone());
            cfg.library.push(sc.clone());
            if let Some(project) = attach_project {
                if let Some(p) = cfg.projects.iter_mut().find(|p| p.name == project) {
                    if !p.breadcrumbs.contains(&sc.name) {
                        p.breadcrumbs.push(sc.name.clone());
                    }
                }
            }
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
            let old = cfg.library[idx].clone();
            if sc.name != name {
                for p in &mut cfg.projects {
                    for attached in &mut p.breadcrumbs {
                        if attached == name {
                            *attached = sc.name.clone();
                        }
                    }
                }
                for s in &mut cfg.sessions {
                    for attached in &mut s.breadcrumbs {
                        if attached == name {
                            *attached = sc.name.clone();
                        }
                    }
                }
            }
            cfg.library[idx] = sc.clone();
            if sc.kind == LibraryItemKind::Breadcrumb {
                if !old.project.trim().is_empty() && old.project != sc.project {
                    if let Some(p) = cfg.projects.iter_mut().find(|p| p.name == old.project) {
                        p.breadcrumbs.retain(|b| b != &sc.name);
                    }
                }
                if !sc.project.trim().is_empty() {
                    if let Some(p) = cfg.projects.iter_mut().find(|p| p.name == sc.project) {
                        if !p.breadcrumbs.contains(&sc.name) {
                            p.breadcrumbs.push(sc.name.clone());
                        }
                    }
                }
            } else {
                for p in &mut cfg.projects {
                    p.breadcrumbs.retain(|b| b != name && b != &sc.name);
                }
                for s in &mut cfg.sessions {
                    s.breadcrumbs.retain(|b| b != name && b != &sc.name);
                }
            }
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
            let Some(sc) = cfg.library_item(name) else {
                bail!("no such library item: {name}");
            };
            if sc.kind == LibraryItemKind::Breadcrumb
                && (cfg
                    .projects
                    .iter()
                    .any(|p| p.breadcrumbs.iter().any(|b| b == name))
                    || cfg
                        .sessions
                        .iter()
                        .any(|s| s.breadcrumbs.iter().any(|b| b == name)))
            {
                bail!("breadcrumb {name} is still attached to a project or agent");
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
        if matches!(
            sc.kind,
            LibraryItemKind::Breadcrumb | LibraryItemKind::FileAction
        ) {
            bail!(
                "library item {} is not runnable as an agent errand",
                sc.name
            );
        }
        drop(cfg);
        self.run_errand(sc, want, false, false, "").await
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

    async fn run_file_action_command(argv: &[String]) -> Result<process::BoundedOutput> {
        let mut command = Command::new(&argv[0]);
        command.args(&argv[1..]);
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

    async fn execute_file_action(
        cfg: &Config,
        s: &SessionCfg,
        p: &ProjectCfg,
        host: bool,
    ) -> Result<String> {
        let argv = if host {
            crate::sandbox::host_argv(cfg, s, p)
        } else {
            crate::sandbox::prepare_network(cfg, s, p)?;
            build_argv(cfg, s, p)?
        };
        let output = Self::run_file_action_command(&argv).await?;
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
        let cfg = self.config().await;
        let (p, s) = Self::resolve_file_action(&cfg, project, raw_path, command, host)?;
        let result = Self::execute_file_action(&cfg, &s, &p, host).await;

        if !host {
            if let Err(e) = crate::sandbox::remove_ephemeral_state(&s) {
                tracing::warn!("removing file action private state: {e:#}");
            }
        }
        result
    }

    pub async fn file_action_command(
        self: &Arc<Self>,
        project: &str,
        raw_path: &str,
        command: &str,
        host: bool,
    ) -> Result<String> {
        self.reload_if_changed().await;
        let cfg = self.config().await;
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

        self.emit(Event::Sessions {
            sessions: self.views().await,
        });
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
        // whose interactive breadcrumb hook can otherwise splice the first prompt incorrectly.
        if let Some(breadcrumbs) = self.consume_breadcrumbs(name, &random_tips).await {
            if !breadcrumbs.is_empty() {
                self.queue_paste(name, breadcrumbs).await;
                self.queue_input(
                    name,
                    Input::Gap(Duration::from_millis(DELIVERY_ENTER_GAP_MS)),
                )
                .await;
            }
            self.emit(Event::Sessions {
                sessions: self.views().await,
            });
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

    async fn auto_resume(&self, name: &str, run_id: u64) {
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
            self.emit(Event::Sessions {
                sessions: self.views().await,
            });
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
        Input::Paste {
            bytes: b"/resume".to_vec(),
            bracketed: false,
        },
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
mod tests {
    use super::{auto_resume_inputs, routed_action_label, Manager};
    use crate::config::{Config, LibraryItemCfg, LibraryItemKind, ProjectCfg};
    use crate::session::Input;
    use std::os::unix::process::ExitStatusExt;
    use std::process::ExitStatus;

    /// A normal exit with the given code, the way the OS hands it back: the low byte is the
    /// signal (none here) and the code sits above it.
    fn exit(code: i32) -> ExitStatus {
        ExitStatus::from_raw(code << 8)
    }

    #[test]
    fn auto_resume_types_the_command_and_two_enters_in_order() {
        let input = auto_resume_inputs();
        assert_eq!(input.len(), 5);
        assert!(matches!(
            &input[0],
            Input::Paste {
                bytes,
                bracketed: false
            } if bytes == b"/resume"
        ));
        assert!(matches!(&input[1], Input::Gap(_)));
        assert!(matches!(
            &input[2],
            Input::Keys { keys, literal: false } if keys == &["Enter"]
        ));
        assert!(matches!(&input[3], Input::Gap(_)));
        assert!(matches!(
            &input[4],
            Input::Keys { keys, literal: false } if keys == &["Enter"]
        ));
    }

    /// stdout alone comes back trimmed of its trailing newline; empty output is spelled out
    /// rather than handed back blank.
    #[test]
    fn file_action_result_returns_trimmed_stdout() {
        assert_eq!(
            Manager::format_file_action_result(
                b"4.0K\tfile\n".to_vec(),
                Vec::new(),
                false,
                false,
                exit(0),
            )
            .unwrap(),
            "4.0K\tfile"
        );
        assert_eq!(
            Manager::format_file_action_result(Vec::new(), Vec::new(), false, false, exit(0))
                .unwrap(),
            "(no output)"
        );
        assert_eq!(
            Manager::format_file_action_result(
                b"   \n".to_vec(),
                Vec::new(),
                false,
                false,
                exit(0)
            )
            .unwrap(),
            "(no output)"
        );
    }

    /// stderr is appended below stdout, with a separating newline inserted only when stdout did
    /// not already end in one.
    #[test]
    fn file_action_result_appends_stderr_below_stdout() {
        assert_eq!(
            Manager::format_file_action_result(
                b"out".to_vec(),
                b"warn".to_vec(),
                false,
                false,
                exit(0),
            )
            .unwrap(),
            "out\nwarn"
        );
        assert_eq!(
            Manager::format_file_action_result(
                b"out\n".to_vec(),
                b"warn".to_vec(),
                false,
                false,
                exit(0),
            )
            .unwrap(),
            "out\nwarn"
        );
    }

    /// A truncation marker is tacked on when either stream was cut short.
    #[test]
    fn file_action_result_flags_truncation() {
        let out =
            Manager::format_file_action_result(b"body".to_vec(), Vec::new(), true, false, exit(0))
                .unwrap();
        assert!(out.contains("[output truncated]"), "got {out:?}");
    }

    /// A non-zero exit is an error, and the combined output rides along in the message rather
    /// than being returned as success.
    #[test]
    fn file_action_result_fails_on_nonzero_exit() {
        let err =
            Manager::format_file_action_result(Vec::new(), b"boom".to_vec(), false, false, exit(1))
                .unwrap_err();
        assert!(err.to_string().contains("boom"), "got {err}");
    }

    /// Breadcrumb and file-action library are handles on something, not runnable prompts; only
    /// prompt and shell library may be launched as an agent errand.
    #[test]
    fn only_runnable_library_pass_the_errand_guard() {
        let sc = |kind| LibraryItemCfg {
            name: "x".into(),
            kind,
            ..Default::default()
        };
        assert!(Manager::validate_errand(&sc(LibraryItemKind::Prompt)).is_ok());
        assert!(Manager::validate_errand(&sc(LibraryItemKind::Shell)).is_ok());
        assert!(Manager::validate_errand(&sc(LibraryItemKind::Breadcrumb)).is_err());
        assert!(Manager::validate_errand(&sc(LibraryItemKind::FileAction)).is_err());
    }

    #[test]
    fn routed_action_labels_keep_their_original_filename() {
        assert_eq!(
            routed_action_label("edit-README.md").as_deref(),
            Some("edit-README.md")
        );
        assert_eq!(
            routed_action_label("search-src/main.rs").as_deref(),
            Some("search-src/main.rs")
        );
        assert_eq!(
            routed_action_label("link-guide.md").as_deref(),
            Some("link-guide.md")
        );
        assert_eq!(routed_action_label("terminal-README.md"), None);
    }

    #[test]
    fn host_file_actions_do_not_need_a_project() {
        let cfg = Config::default();
        let (project, session) = Manager::resolve_file_action(
            &cfg,
            "",
            "/tmp/private state/file",
            "du -sh '/tmp/private state/file'",
            true,
        )
        .expect("host file action");

        assert!(project.name.is_empty());
        assert_eq!(
            session.cmd.as_deref(),
            Some("du -sh '/tmp/private state/file'")
        );
        let argv = crate::sandbox::host_argv(&cfg, &session, &project);
        assert!(!argv.iter().any(|part| part == "bwrap"));
    }

    #[test]
    fn project_file_actions_expand_and_quote_the_absolute_path() {
        let cfg = Config {
            projects: vec![ProjectCfg {
                name: "repo".into(),
                dir: "/tmp/slopworld-project".into(),
                ..Default::default()
            }],
            ..Default::default()
        };
        let (_, session) = Manager::resolve_file_action(
            &cfg,
            "repo",
            "/tmp/slopworld-project/src/file name.rs",
            "sed -n '1p' {{ absolute_path }}",
            false,
        )
        .unwrap();
        assert_eq!(
            session.cmd.as_deref(),
            Some("sed -n '1p' '/tmp/slopworld-project/src/file name.rs'")
        );

        assert!(Manager::resolve_file_action(
            &cfg,
            "repo",
            "/tmp/slopworld-project/file",
            "   ",
            false,
        )
        .is_err());
        assert!(Manager::resolve_file_action(
            &cfg,
            "repo",
            "/tmp/slopworld-project-other/file",
            "cat {{ absolute_path }}",
            false,
        )
        .is_err());
        assert!(Manager::resolve_file_action(
            &cfg,
            "missing",
            "/tmp/file",
            "cat {{ absolute_path }}",
            false,
        )
        .is_err());
    }
}
