//! Projects, shortcuts, file actions and errands.

use super::super::*;
use std::process::Stdio;

use crate::sandbox::build_argv;
use anyhow::anyhow;
use tokio::process::Command;

impl Manager {
    pub async fn projects(&self) -> Vec<ProjectCfg> {
        self.cfg.read().await.projects.clone()
    }

    pub(super) async fn announce_projects(&self) {
        let _ = self.events.send(Event::Projects {
            projects: self.projects().await,
        });
    }

    pub async fn add_project(self: &Arc<Self>, mut p: ProjectCfg) -> Result<()> {
        self.reload_if_changed().await;
        settle(&mut p);
        check_project(&p)?;
        let mut cfg = self.cfg.write().await;
        check_breadcrumbs(&cfg, &p.breadcrumbs)?;
        if cfg.project(&p.name).is_some() {
            bail!("project {} already exists", p.name);
        }
        cfg.projects.push(p);
        self.save_cfg(&cfg)?;
        drop(cfg);
        self.announce_projects().await;
        Ok(())
    }

    pub async fn update_project(self: &Arc<Self>, name: &str, mut p: ProjectCfg) -> Result<()> {
        self.reload_if_changed().await;
        settle(&mut p);
        check_project(&p)?;

        let mut cfg = self.cfg.write().await;
        check_breadcrumbs(&cfg, &p.breadcrumbs)?;
        let idx = cfg
            .projects
            .iter()
            .position(|x| x.name == name)
            .ok_or_else(|| anyhow!("no such project: {name}"))?;
        if p.name != name && cfg.project(&p.name).is_some() {
            bail!("project {} already exists", p.name);
        }
        for s in cfg.sessions.iter().filter(|s| s.project == name) {
            cfg.network_of(s, &p).with_context(|| {
                format!(
                    "project {} cannot lower its network ceiling below agent {}",
                    p.name, s.name
                )
            })?;
        }

        let renamed = p.name.clone();
        cfg.projects[idx] = p;
        if renamed != name {
            for s in cfg.sessions.iter_mut().filter(|s| s.project == name) {
                s.project = renamed.clone();
            }
        }
        self.save_cfg(&cfg)?;
        drop(cfg);

        self.announce_projects().await;
        let _ = self.events.send(Event::Sessions {
            sessions: self.views().await,
        });
        Ok(())
    }

    pub async fn remove_project(self: &Arc<Self>, name: &str) -> Result<()> {
        self.reload_if_changed().await;
        let mut cfg = self.cfg.write().await;
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
        self.save_cfg(&cfg)?;
        drop(cfg);
        self.announce_projects().await;
        Ok(())
    }

    pub async fn shortcuts(&self) -> Vec<ShortcutCfg> {
        self.cfg.read().await.shortcuts_all()
    }

    pub(super) async fn announce_shortcuts(&self) {
        let _ = self.events.send(Event::Shortcuts {
            shortcuts: self.shortcuts().await,
        });
    }

    pub async fn add_shortcut(self: &Arc<Self>, mut sc: ShortcutCfg) -> Result<()> {
        self.reload_if_changed().await;
        let mut cfg = self.cfg.write().await;
        sc.builtin = false;
        check_shortcut(&cfg, &sc)?;
        if cfg
            .shortcuts
            .iter()
            .any(|existing| existing.name == sc.name)
        {
            bail!("shortcut {} already exists", sc.name);
        }
        let attach_project = (sc.kind == ShortcutKind::Breadcrumb && !sc.project.trim().is_empty())
            .then(|| sc.project.clone());
        cfg.shortcuts.push(sc.clone());
        if let Some(project) = attach_project {
            if let Some(p) = cfg.projects.iter_mut().find(|p| p.name == project) {
                if !p.breadcrumbs.contains(&sc.name) {
                    p.breadcrumbs.push(sc.name.clone());
                }
            }
        }
        self.save_cfg(&cfg)?;
        drop(cfg);
        self.announce_shortcuts().await;
        Ok(())
    }

    pub async fn update_shortcut(self: &Arc<Self>, name: &str, mut sc: ShortcutCfg) -> Result<()> {
        self.reload_if_changed().await;
        let mut cfg = self.cfg.write().await;
        sc.builtin = false;
        if cfg.is_builtin_shortcut(name) {
            bail!("shortcut {name} is built in and cannot be edited");
        }
        check_shortcut(&cfg, &sc)?;
        let idx = cfg
            .shortcuts
            .iter()
            .position(|x| x.name == name)
            .ok_or_else(|| anyhow!("no such shortcut: {name}"))?;
        if sc.name != name
            && cfg
                .shortcuts
                .iter()
                .any(|existing| existing.name == sc.name)
        {
            bail!("shortcut {} already exists", sc.name);
        }
        let old = cfg.shortcuts[idx].clone();
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
        cfg.shortcuts[idx] = sc.clone();
        if sc.kind == ShortcutKind::Breadcrumb {
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
        self.save_cfg(&cfg)?;
        drop(cfg);
        self.announce_projects().await;
        self.announce_shortcuts().await;
        Ok(())
    }

    pub async fn remove_shortcut(self: &Arc<Self>, name: &str) -> Result<()> {
        self.reload_if_changed().await;
        let mut cfg = self.cfg.write().await;
        if cfg.is_builtin_shortcut(name) {
            bail!("shortcut {name} is built in and cannot be deleted");
        }
        let Some(sc) = cfg.shortcut(name) else {
            bail!("no such shortcut: {name}");
        };
        if sc.kind == ShortcutKind::Breadcrumb
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
        cfg.shortcuts.retain(|s| s.name != name);
        self.save_cfg(&cfg)?;
        drop(cfg);
        self.announce_shortcuts().await;
        Ok(())
    }

    pub async fn run_shortcut(self: &Arc<Self>, name: &str, want: RunWhere) -> Result<String> {
        self.reload_if_changed().await;
        let cfg = self.config().await;
        let sc = cfg
            .shortcut(name)
            .ok_or_else(|| anyhow!("no such shortcut: {name}"))?
            .clone();
        check_shortcut(&cfg, &sc)?;
        if matches!(sc.kind, ShortcutKind::Breadcrumb | ShortcutKind::FileAction) {
            bail!("shortcut {} is not runnable as an agent errand", sc.name);
        }
        drop(cfg);
        self.run_errand(sc, want, false).await
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
        let command = normalize_action_command(raw_path, &path, command);
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
    ) -> Result<((Vec<u8>, bool), (Vec<u8>, bool), std::process::ExitStatus)> {
        let mut child = Command::new(&argv[0])
            .args(&argv[1..])
            .kill_on_drop(true)
            .stdin(Stdio::null())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .spawn()
            .context("starting file action")?;
        let stdout = child
            .stdout
            .take()
            .context("capturing file action stdout")?;
        let stderr = child
            .stderr
            .take()
            .context("capturing file action stderr")?;

        let collected = tokio::time::timeout(FILE_ACTION_TIMEOUT, async {
            let stdout = read_action_output(stdout);
            let stderr = read_action_output(stderr);
            let status = child.wait();
            let (stdout, stderr, status) = tokio::join!(stdout, stderr, status);
            Ok::<_, anyhow::Error>((stdout?, stderr?, status?))
        })
        .await;

        match collected {
            Ok(result) => result,
            Err(_) => {
                let _ = child.kill().await;
                let _ = child.wait().await;
                bail!("file action timed out")
            }
        }
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
        let ((stdout, stdout_truncated), (stderr, stderr_truncated), status) =
            Self::run_file_action_command(&argv).await?;
        Self::format_file_action_result(stdout, stderr, stdout_truncated, stderr_truncated, status)
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
        Ok(normalize_action_command(raw_path, &path, command))
    }

    fn validate_errand(sc: &ShortcutCfg) -> Result<()> {
        if matches!(sc.kind, ShortcutKind::Breadcrumb | ShortcutKind::FileAction) {
            bail!("shortcut {} is not runnable as an agent errand", sc.name);
        }
        Ok(())
    }

    async fn create_errand_session(
        &self,
        cfg: &Config,
        sc: &ShortcutCfg,
        want: &RunWhere,
        host: bool,
    ) -> Result<String> {
        let name = sc.name.as_str();
        let asked = match want.project.as_deref().map(str::trim) {
            Some(p) if !p.is_empty() => Some(p.to_string()),
            _ => None,
        };
        let fresh = want.temp || (asked.is_none() && sc.link == ShortcutLink::Temp);
        let named = match (&asked, sc.link) {
            _ if fresh => String::new(),
            (Some(p), _) => p.clone(),
            (None, ShortcutLink::Ask) => {
                bail!(
                    "shortcut {name} asks where to run; name a project or ask for a temporary one"
                )
            }
            (None, _) => sc.project.clone(),
        };
        if !fresh && cfg.project(&named).is_none() {
            bail!("no such project: {named}");
        }
        let template = cfg.project(&sc.project).cloned().unwrap_or_default();

        let mut live = self.live.write().await;
        let name = free_name(&live, cfg, &slug(&sc.name));
        check_name(&name)?;

        let project = if fresh {
            let mut temp = self.temp.write().await;
            let pname = free_project_name(cfg, &temp, &name);
            temp.insert(
                pname.clone(),
                ProjectCfg {
                    name: pname.clone(),
                    dir: crate::config::temp_dir(&pname),
                    temp: true,
                    ..template
                },
            );
            pname
        } else {
            named
        };

        let mut l = Live::new(
            cfg.session_for(sc, name.clone(), project),
            TitleCapture::default(),
        );
        l.ephemeral = true;
        l.host = host;
        live.insert(name.clone(), l);
        Ok(name)
    }

    fn queue_errand_delivery(self: &Arc<Self>, session: &str, sc: &ShortcutCfg, want: &RunWhere) {
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
        sc: ShortcutCfg,
        want: RunWhere,
        host: bool,
    ) -> Result<String> {
        self.reload_if_changed().await;
        let cfg = self.config().await;
        Self::validate_errand(&sc)?;
        let session = self.create_errand_session(&cfg, &sc, &want, host).await?;

        if let Err(e) = self.start(&session).await {
            self.forget(&session).await;
            return Err(e);
        }

        let _ = self.events.send(Event::Sessions {
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
                tracing::warn!("{name} was gone before its shortcut text could be sent");
                return;
            }
            Ready::Timeout => tracing::warn!(
                "{name} never went quiet after {}ms; sending its shortcut text anyway",
                READY_MS
            ),
            Ready::Settled => {}
        }

        if let Err(e) = self.paste(name, text).await {
            tracing::error!("sending shortcut text to {name}: {e:#}");
            return;
        }
        tokio::time::sleep(Duration::from_millis(ENTER_GAP_MS)).await;
        self.send_keys(name, vec!["Enter".into()], false, random_tips)
            .await;
    }

    pub(super) async fn wait_ready(&self, name: &str) -> Ready {
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
                    (l.seq, printed, l.state)
                })
            };
            let Some((seq, printed, state)) = snap else {
                return Ready::Gone;
            };
            if state == State::Down {
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

#[cfg(test)]
mod tests {
    use super::Manager;
    use crate::config::Config;

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
}
