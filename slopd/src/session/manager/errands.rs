//! Temporary and host errand session construction.

use super::super::*;
use anyhow::anyhow;

struct ErrandSessionPreparation {
    template: Option<crate::session::AgentTemplate>,
    fresh: bool,
    project_name: String,
    selected_worktree: String,
    worktree_path: Option<ProjectCfg>,
    source: Option<SessionCfg>,
    source_settings: Option<ErrandSourceSettings>,
}

struct ErrandSourceSettings {
    sandbox: Vec<String>,
    sandbox_snapshots: Vec<crate::presets::SandboxPreset>,
    persistent_tmp: bool,
    network: crate::config::NetworkMode,
    dns: crate::config::DnsConfig,
    limits: crate::config::Limits,
}

impl Manager {
    /// Reserve an errand's live row before startup.
    /// Manage name allocation, temporary project creation, host-tab storage, and optional copying of agent settings here.
    /// Callers control launch and input delivery.
    pub(super) async fn create_errand_session(
        &self,
        cfg: &Config,
        sc: &LibraryItemCfg,
        want: &RunWhere,
        host: bool,
        persistent_host: bool,
        like: &str,
    ) -> Result<String> {
        self.session_operation(self.create_errand_session_inner(
            cfg,
            sc,
            want,
            host,
            persistent_host,
            like,
        ))
        .await
    }

    async fn create_errand_session_inner(
        &self,
        cfg: &Config,
        sc: &LibraryItemCfg,
        want: &RunWhere,
        host: bool,
        persistent_host: bool,
        like: &str,
    ) -> Result<String> {
        let mut preparation = self
            .prepare_errand_session(cfg, sc, want, host, like)
            .await?;
        let name = if persistent_host {
            let live = self.live.read().await;
            free_name(
                &live,
                cfg,
                &crate::sandbox::host_session_name(&preparation.project_name),
            )
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
                        let path = preparation
                            .worktree_path
                            .as_ref()
                            .map(|project| crate::config::expand(&project.dir))
                            .unwrap_or_default();
                        drop(live);
                        self.remember_host_terminal(&name, &preparation.project_name, &path)
                            .await?;
                    }
                    return Ok(name);
                }
                bail!("session {name} already exists");
            }
        }

        // A source agent is copied only after identity conflicts have been handled, but before
        // persistence or temporary-project creation can change live state.
        if let Some(source) = &preparation.source {
            let project = cfg
                .project(&source.project)
                .context("source agent project is unavailable")?;
            let table = source.preset_table();
            preparation.source_settings = Some(ErrandSourceSettings {
                sandbox: cfg.sandbox_of(source, project),
                sandbox_snapshots: crate::sandbox::presets_for(cfg, source, project, &table)?
                    .into_iter()
                    .cloned()
                    .collect(),
                persistent_tmp: source.persistent_tmp,
                network: source.network,
                dns: source.dns.clone(),
                limits: source.limits,
            });
        }

        if persistent_host {
            let path = preparation
                .worktree_path
                .as_ref()
                .map(|project| crate::config::expand(&project.dir))
                .ok_or_else(|| anyhow!("Project {:?} does not exist.", preparation.project_name))?;
            self.remember_host_terminal(&name, &preparation.project_name, &path)
                .await?;
        }

        let mut live = self.live.write().await;
        if let Some(existing) = live.get(&name) {
            if existing.host == host {
                return Ok(name);
            }
            bail!("session {name} already exists");
        }

        let project = if preparation.fresh {
            let mut temp = self.temp.write().await;
            let project_name = free_project_name(cfg, &temp, &name);
            temp.insert(
                project_name.clone(),
                ProjectCfg {
                    name: project_name.clone(),
                    dir: crate::config::temp_dir(&project_name),
                    temp: true,
                    mounts: Vec::new(),
                    ..Default::default()
                },
            );
            project_name
        } else {
            preparation.project_name.clone()
        };

        let mut base = cfg.session_for(sc, name.clone(), project.clone());
        base.worktree = preparation.selected_worktree.clone();
        let mut session = if let Some(template) = preparation.template.take() {
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
        // Session name normalization replaces a file extension's dot with a dash for tmux.
        // Preserve the original tab label in temporary session metadata.
        // The client can then display the exact filename supplied by Files.
        session.label = super::library::routed_action_label(&sc.name);
        if let Some(source) = preparation.source_settings {
            session.sandbox = source.sandbox;
            session.sandbox_snapshots = source.sandbox_snapshots;
            session.persistent_tmp = source.persistent_tmp;
            session.network = source.network;
            session.dns = source.dns;
            session.limits = source.limits;
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
        state.cfg.worktree = preparation.selected_worktree;
        state.ephemeral = true;
        state.host = host;
        state.persistent_host = persistent_host;
        if persistent_host {
            state.host_path = preparation
                .worktree_path
                .as_ref()
                .map(|project| crate::config::expand(&project.dir))
                .unwrap_or_default();
        }
        live.insert(name.clone(), state);
        Ok(name)
    }

    async fn prepare_errand_session(
        &self,
        cfg: &Config,
        sc: &LibraryItemCfg,
        want: &RunWhere,
        host: bool,
        like: &str,
    ) -> Result<ErrandSessionPreparation> {
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
        let source = if like.is_empty() {
            None
        } else {
            Some(
                cfg.session(like)
                    .cloned()
                    .ok_or_else(|| anyhow!("no such session to clone sandbox from: {like}"))?,
            )
        };
        let item_name = sc.name.as_str();
        let asked = match want.project.as_deref().map(str::trim) {
            Some(project) if !project.is_empty() => Some(project.to_string()),
            _ => None,
        };
        let fresh = want.temp || (asked.is_none() && sc.link == LibraryItemLink::Temp);
        let project_name = match (&asked, sc.link) {
            _ if fresh => String::new(),
            (Some(project), _) => project.clone(),
            (None, LibraryItemLink::Ask) => {
                bail!(
                    "Choose a project or request a temporary project for library item {item_name:?}."
                )
            }
            (None, _) => sc.project.clone(),
        };
        let selected_worktree = if want.worktree.is_empty() {
            source
                .as_ref()
                .map(|s| s.worktree.clone())
                .unwrap_or_default()
        } else {
            want.worktree.clone()
        };
        let worktree_path = if !fresh {
            let project = cfg
                .project(&project_name)
                .ok_or_else(|| anyhow!("Project {project_name:?} does not exist."))?;
            Some(self.resolve_worktree(project, &selected_worktree).await?)
        } else {
            if !want.worktree.is_empty() {
                bail!("temporary errands cannot select a worktree");
            }
            None
        };
        Ok(ErrandSessionPreparation {
            template,
            fresh,
            project_name,
            selected_worktree,
            worktree_path,
            source,
            source_settings: None,
        })
    }
}

#[cfg(test)]
#[path = "errands_tests.rs"]
mod tests;
