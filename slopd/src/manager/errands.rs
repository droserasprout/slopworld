//! Temporary and host errand session construction.

use super::super::*;
use anyhow::anyhow;

impl Manager {
    /// Reserve the live row for an errand before startup. This owner handles name allocation,
    /// temporary project creation, host-tab persistence, and the optional sandbox clone; launch
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
        let template = cfg.project(&sc.project).cloned().unwrap_or_default();

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
                    ..template
                },
            );
            project_name
        } else {
            named
        };

        let mut session = cfg.session_for(sc, name.clone(), project);
        // The tmux-safe session name is slugged, which turns a file extension's dot into a
        // dash. Keep the original routed-tab label in the ephemeral session metadata so the
        // client can display the filename exactly as Files supplied it.
        session.label = super::library::routed_action_label(&sc.name);
        if !like.is_empty() {
            if let Some(source) = cfg.session(like) {
                session.sandbox = source.sandbox.clone();
                session.persistent_tmp = source.persistent_tmp;
                session.network = source.network;
                session.dns = source.dns.clone();
                session.limits = source.limits;
                session.mounts = source.mounts.clone();
            } else {
                bail!("no such session to clone sandbox from: {like}");
            }
        }
        let mut state = Live::new(session, TitleCapture::default());
        // less can retain blank leading rows when SIGWINCH arrives during LESSOPEN.
        // Create the PTY and emulator at the viewer's size before starting the command.
        if let (Some(cols), Some(rows)) = (want.cols, want.rows) {
            state.cols = cols.clamp(
                crate::wire::TERMINAL_MIN_COLS,
                crate::wire::TERMINAL_MAX_COLS,
            );
            state.rows = rows.clamp(
                crate::wire::TERMINAL_MIN_ROWS,
                crate::wire::TERMINAL_MAX_ROWS,
            );
        }
        state.ephemeral = true;
        state.host = host;
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
