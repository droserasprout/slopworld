//! Temporary and host errand allocation and live construction.
//! Application errands owns configuration and display policy; this owner retains guards.

use super::*;
use crate::application::errands::ErrandSourceSettings;
use anyhow::anyhow;

struct ErrandSessionPreparation {
    template: Option<crate::agent_templates::AgentTemplate>,
    fresh: bool,
    project_name: String,
    selected_worktree: String,
    worktree_path: Option<ProjectCfg>,
    source: Option<SessionCfg>,
    source_settings: Option<ErrandSourceSettings>,
}

struct ErrandLiveRequest<'a> {
    cfg: &'a Config,
    item: &'a LaunchContent,
    location: &'a LaunchOptions,
    name: &'a str,
    role: SessionRole,
}

impl SessionRuntime {
    /// Reserve an errand's live row before startup.
    /// Own name allocation, temporary project creation, and host-tab storage.
    /// Capture application-prepared agent settings inside the session boundary.
    /// Callers control launch and input delivery.
    pub(crate) async fn create_errand_session(
        self: &Arc<Self>,
        cfg: &Config,
        request: &LaunchRequest,
    ) -> Result<String> {
        self.session_operation(self.create_errand_session_inner(cfg, request))
            .await
    }

    async fn create_errand_session_inner(
        self: &Arc<Self>,
        cfg: &Config,
        request: &LaunchRequest,
    ) -> Result<String> {
        let sc = &request.content;
        let want = &request.options;
        let host = request.execution.is_host();
        let role = match request.execution {
            ExecutionSource::Host(HostLifetime::Persistent) => SessionRole::HostShell,
            ExecutionSource::Host(HostLifetime::Disposable) => SessionRole::HostAction,
            ExecutionSource::Template(_) | ExecutionSource::CloneAgent(_) => {
                SessionRole::Agent(AgentLifetime::Temporary)
            }
        };
        let persistent_host = role == SessionRole::HostShell;
        let mut preparation = self.prepare_errand_session(cfg, request).await?;
        // Serialized launches reuse a running editor even across concurrent requests
        // or a mod restart. Return its handle so the client focuses the existing pane.
        if host
            && !persistent_host
            && want.context.intent == "edit"
            && !want.context.reader.path.is_empty()
        {
            let live = self.live.read().await;
            if let Some(editor) = live.values().find(|row| {
                row.role == SessionRole::HostAction
                    && row.state != State::Down
                    && row.launch.intent == "edit"
                    && row.launch.reader.path == want.context.reader.path
                    && row.launch.reader.scope == want.context.reader.scope
                    && row.cfg.worktree == preparation.selected_worktree
                    && (preparation.fresh || row.cfg.project == preparation.project_name)
            }) {
                return Ok(editor.cfg.name.clone());
            }
        }
        let name = self
            .errand_session_name(cfg, sc, want, persistent_host, &preparation)
            .await?;

        {
            let live = self.live.read().await;
            if let Some(existing) = live.get(&name) {
                if existing.role.is_host() == host {
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

        preparation.source_settings =
            crate::application::errands::capture_source_settings(cfg, preparation.source.as_ref())?;
        if persistent_host {
            self.remember_errand_host_terminal(&name, &preparation)
                .await?;
        }

        let mut live = self.live.write().await;
        if let Some(existing) = live.get(&name) {
            if existing.role.is_host() == host {
                return Ok(name);
            }
            bail!("session {name} already exists");
        }
        let state = self
            .build_errand_live(
                ErrandLiveRequest {
                    cfg,
                    item: sc,
                    location: want,
                    name: &name,
                    role,
                },
                preparation,
            )
            .await;
        live.insert(name.clone(), state);
        Ok(name)
    }

    async fn errand_session_name(
        &self,
        cfg: &Config,
        sc: &LaunchContent,
        want: &LaunchOptions,
        persistent_host: bool,
        preparation: &ErrandSessionPreparation,
    ) -> Result<String> {
        let live = self.live.read().await;
        if !persistent_host && !want.context.intent.is_empty() {
            // The session boundary reserves the handle through live-row publication.
            // Retry collisions rather than adding a suffix outside the shared ID syntax.
            return crate::storage_id::allocate(|id| {
                Ok(live.contains_key(id)
                    || cfg.sessions.iter().any(|session| session.name == id)
                    || cfg.host_terminals.iter().any(|tab| tab.name == id))
            });
        }
        let preferred = if persistent_host {
            crate::sandbox::host_session_name(&preparation.project_name)
        } else {
            slug(&sc.name)
        };
        let name = free_name(&live, cfg, &preferred);
        check_name(&name)?;
        Ok(name)
    }

    async fn remember_errand_host_terminal(
        self: &Arc<Self>,
        name: &str,
        preparation: &ErrandSessionPreparation,
    ) -> Result<()> {
        let path = preparation
            .worktree_path
            .as_ref()
            .map(|project| crate::config::expand(&project.dir))
            .ok_or_else(|| anyhow!("Project {:?} does not exist.", preparation.project_name))?;
        self.remember_host_terminal(name, &preparation.project_name, &path)
            .await
    }

    async fn build_errand_live(
        &self,
        request: ErrandLiveRequest<'_>,
        mut preparation: ErrandSessionPreparation,
    ) -> Live {
        let project = if preparation.fresh {
            let mut temp = self.temp.write().await;
            let project_name = free_project_name(request.cfg, &temp, request.name);
            temp.insert(
                project_name.clone(),
                ProjectCfg {
                    name: project_name.clone(),
                    dir: crate::paths::temp_dir(&project_name),
                    temp: true,
                    mounts: Vec::new(),
                    ..Default::default()
                },
            );
            project_name
        } else {
            preparation.project_name.clone()
        };

        let (session, context) = crate::application::errands::build_session(
            request.cfg,
            request.item,
            request.location,
            request.name,
            project,
            preparation.template.take(),
            preparation.source_settings.take(),
        );
        let mut state = Live::new(session, TitleCapture::default());
        state.launch = context;
        // less can retain blank leading rows when SIGWINCH arrives during LESSOPEN.
        // Create the PTY and emulator at the viewer's size before starting the command.
        if let (Some(cols), Some(rows)) = (request.location.cols, request.location.rows) {
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
        state.role = request.role;
        if request.role == SessionRole::HostShell {
            state.host_path = preparation
                .worktree_path
                .as_ref()
                .map(|project| crate::config::expand(&project.dir))
                .unwrap_or_default();
        }
        state
    }

    async fn prepare_errand_session(
        &self,
        cfg: &Config,
        request: &LaunchRequest,
    ) -> Result<ErrandSessionPreparation> {
        let crate::application::errands::ErrandSelection {
            template,
            source,
            fresh,
            project_name,
            selected_worktree,
        } = crate::application::errands::select(
            cfg,
            request,
            &self.application.templates.list().await,
        )?;
        let worktree_path = if !fresh {
            let project = cfg
                .project(&project_name)
                .ok_or_else(|| anyhow!("Project {project_name:?} does not exist."))?;
            Some(self.resolve_worktree(project, &selected_worktree).await?)
        } else {
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
