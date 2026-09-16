//! Temporary and host errand session construction.

use super::super::*;
use anyhow::anyhow;

impl Manager {
    /// Reserve the live row for an errand before startup. This owner handles name allocation,
    /// temporary project creation, host-tab persistence, and the optional agent-settings clone; launch
    /// and delivery remain lifecycle concerns in their callers.
    pub(super) async fn create_errand_session(
        &self,
        cfg: &Config,
        sc: &LibraryItemCfg,
        want: &RunWhere,
        host: bool,
        persistent_host: bool,
        like: &str,
    ) -> Result<String> {
        self.session_operation(self.create_errand_session_within_boundary(
            cfg,
            sc,
            want,
            host,
            persistent_host,
            like,
        ))
        .await
    }

    async fn create_errand_session_within_boundary(
        &self,
        cfg: &Config,
        sc: &LibraryItemCfg,
        want: &RunWhere,
        host: bool,
        persistent_host: bool,
        like: &str,
    ) -> Result<String> {
        // Resolve execution before reserving a row or allocating a temporary workspace.
        // Missing settings must never silently gain network access or lose resource caps.
        let template_name = sc.agent_template.trim();
        if host && (!template_name.is_empty() || !like.is_empty()) {
            bail!("choose host execution or agent settings, not both");
        }
        if !template_name.is_empty() && !like.is_empty() {
            bail!("choose an agent template or a source agent, not both");
        }
        if !host && template_name.is_empty() && like.is_empty() {
            bail!("choose Host or an agent template for this errand before running it");
        }
        let template = if template_name.is_empty() {
            None
        } else {
            Some(
                self.agent_templates()
                    .await
                    .into_iter()
                    .find(|t| t.name == template_name)
                    .ok_or_else(|| anyhow!("no such agent template: {template_name}"))?,
            )
        };
        if let Some(template) = &template {
            crate::runtime::validate_limits(&template.defaults.limits)?;
        }
        if !like.is_empty() && cfg.session(like).is_none() {
            bail!("no such session to clone sandbox from: {like}");
        }
        let item_name = sc.name.as_str();
        let asked = match want.project.as_deref().map(str::trim) {
            Some(project) if !project.is_empty() => Some(project.to_string()),
            _ => None,
        };
        let fresh = want.temp || (asked.is_none() && sc.link == LibraryItemLink::Temp);
        let named = match (&asked, sc.link) {
            _ if fresh => String::new(),
            (Some(project), _) => project.clone(),
            (None, LibraryItemLink::Ask) => {
                bail!(
                    "library item {item_name} asks where to run; name a project or ask for a temporary one"
                )
            }
            (None, _) => sc.project.clone(),
        };
        if !fresh && cfg.project(&named).is_none() {
            bail!("no such project: {named}");
        }
        let name = if persistent_host {
            let live = self.live.read().await;
            free_name(&live, cfg, &crate::sandbox::host_session_name(&named))
        } else {
            let live = self.live.read().await;
            free_name(&live, cfg, &slug(&sc.name))
        };
        check_name(&name)?;

        {
            let live = self.live.read().await;
            if let Some(existing) = live.get(&name) {
                if existing.host == host {
                    if persistent_host {
                        let path = cfg
                            .project(&named)
                            .map(|project| crate::config::expand(&project.dir))
                            .unwrap_or_default();
                        drop(live);
                        self.remember_host_terminal(&name, &named, &path).await?;
                    }
                    return Ok(name);
                }
                bail!("session {name} already exists");
            }
        }

        if persistent_host {
            let path = cfg
                .project(&named)
                .map(|project| crate::config::expand(&project.dir))
                .ok_or_else(|| anyhow!("no such project: {named}"))?;
            self.remember_host_terminal(&name, &named, &path).await?;
        }

        let mut live = self.live.write().await;
        if let Some(existing) = live.get(&name) {
            if existing.host == host {
                return Ok(name);
            }
            bail!("session {name} already exists");
        }

        let project = if fresh {
            let mut temp = self.temp.write().await;
            let project_name = free_project_name(cfg, &temp, &name);
            temp.insert(
                project_name.clone(),
                ProjectCfg {
                    name: project_name.clone(),
                    dir: crate::config::temp_dir(&project_name),
                    temp: true,
                    mounts: Vec::new(),
                },
            );
            project_name
        } else {
            named
        };

        let base = cfg.session_for(sc, name.clone(), project.clone());
        let mut session = if let Some(template) = template {
            let mut session = template.instantiate(name.clone(), project);
            // A shell errand or explicit command keeps its own executable while retaining the
            // template's sandbox additions, snapshots, network/DNS and limits.
            if sc.kind == LibraryItemKind::Shell
                || sc
                    .command
                    .as_deref()
                    .is_some_and(|cmd| !cmd.trim().is_empty())
            {
                session.sandbox = cfg.sandbox_of(&session, &ProjectCfg::default());
                session.command = base.command;
                session.cmd = base.cmd;
                session.command_snapshot = None;
            }
            session
        } else {
            base
        };
        session.autostart = false;
        session.auto_resume = false;
        // The tmux-safe session name is slugged, which turns a file extension's dot into a
        // dash. Keep the original routed-tab label in the ephemeral session metadata so the
        // client can display the filename exactly as Files supplied it.
        session.label = super::library::routed_action_label(&sc.name);
        if !like.is_empty() {
            if let Some(source) = cfg.session(like) {
                let project = cfg
                    .project(&source.project)
                    .context("source agent project is unavailable")?;
                let table = source.preset_table();
                session.sandbox = cfg.sandbox_of(source, project);
                session.sandbox_snapshots =
                    crate::sandbox::presets_for(cfg, source, project, &table)
                        .into_iter()
                        .cloned()
                        .collect();
                session.persistent_tmp = source.persistent_tmp;
                session.network = source.network;
                session.dns = source.dns.clone();
                session.limits = source.limits;
            } else {
                bail!("no such session to clone sandbox from: {like}");
            }
        }
        let mut state = Live::new(session, TitleCapture::default());
        // less can retain blank leading rows when SIGWINCH arrives during LESSOPEN.
        // Create the PTY and emulator at the viewer's size before starting the command.
        if let (Some(cols), Some(rows)) = (want.cols, want.rows) {
            state.cols = cols.clamp(
                crate::shared::protocol::TERMINAL_MIN_COLS,
                crate::shared::protocol::TERMINAL_MAX_COLS,
            );
            state.rows = rows.clamp(
                crate::shared::protocol::TERMINAL_MIN_ROWS,
                crate::shared::protocol::TERMINAL_MAX_ROWS,
            );
        }
        state.ephemeral = true;
        state.host = host;
        state.persistent_host = persistent_host;
        if persistent_host {
            state.host_path = cfg
                .project(&state.cfg.project)
                .map(|project| crate::config::expand(&project.dir))
                .unwrap_or_default();
        }
        live.insert(name.clone(), state);
        Ok(name)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[tokio::test]
    async fn errands_require_host_or_settings_before_allocating_and_copy_template_once() {
        let cfg = Config {
            projects: vec![ProjectCfg {
                name: "repo".into(),
                dir: "/tmp".into(),
                ..Default::default()
            }],
            ..Default::default()
        };
        let manager = crate::session::test_manager(cfg.clone());
        let mut item = LibraryItemCfg {
            name: "review".into(),
            project: "repo".into(),
            ..Default::default()
        };
        let want = RunWhere::default();
        let error = manager
            .create_errand_session(&cfg, &item, &want, false, false, "")
            .await
            .unwrap_err();
        assert!(error.to_string().contains("Host or an agent template"));
        assert!(manager.live.read().await.is_empty());
        assert!(manager.temp.read().await.is_empty());
        let template: AgentTemplate = serde_json::from_value(serde_json::json!({
            "name": "offline", "defaults": {"cmd":"review-agent", "network":"none", "limits":{"memory_mb":256},
                "autostart":true,"auto_resume":true,"persistent_tmp":true,
                "sandbox":["captured"], "sandbox_presets":[{"name":"captured","ro":["/usr"]}]}
        })).unwrap();
        manager.templates.write().await.create(template).unwrap();
        item.agent_template = "offline".into();
        let name = manager
            .create_errand_session(&cfg, &item, &want, false, false, "")
            .await
            .unwrap();
        {
            let live = manager.live.read().await;
            let session = &live[&name].cfg;
            assert_eq!(session.network, crate::config::NetworkMode::None);
            assert_eq!(session.limits.memory_mb, Some(256));
            assert_eq!(session.cmd.as_deref(), Some("review-agent"));
            assert_eq!(session.sandbox_snapshots[0].ro, ["/usr"]);
            assert!(session.persistent_tmp);
            assert!(!session.autostart && !session.auto_resume);
        }
        manager.templates.write().await.templates.clear();
        assert_eq!(
            manager.live.read().await[&name].cfg.limits.memory_mb,
            Some(256)
        );
        assert!(manager
            .create_errand_session(&cfg, &item, &want, false, false, "")
            .await
            .is_err());
        item.agent_template.clear();
        item.name = "host-command".into();
        let host = manager
            .create_errand_session(&cfg, &item, &want, true, false, "")
            .await
            .unwrap();
        assert!(manager.live.read().await[&host].host);
    }
}
