//! Saved host-shell identity and path edits through guarded configuration transactions.
//! Session owns observed live state and stale-run checks; this owner retains no runtime.
use super::{ConfigAuthority, Workspace, config_runtime::ConfigRuntime};
use crate::config::{Config, transactions::ConfigMutation};
use crate::session::SessionRuntime;
use anyhow::{Result, bail};
use std::sync::Arc;

#[derive(Clone)]
pub(crate) struct HostRecords {
    workspace: Arc<Workspace>,
    authority: Arc<ConfigAuthority>,
}

pub(crate) struct SavedHostPath {
    pub(crate) project: Option<String>,
    pub(crate) changed: bool,
}

impl HostRecords {
    pub(crate) fn new(workspace: Arc<Workspace>, authority: Arc<ConfigAuthority>) -> Self {
        Self {
            workspace,
            authority,
        }
    }

    pub(crate) async fn remember(
        &self,
        sessions: &Arc<SessionRuntime>,
        name: &str,
        project: &str,
        path: &str,
    ) -> Result<()> {
        self.update(sessions, |cfg| {
            if cfg.session(name).is_some() {
                bail!("host terminal {name} conflicts with an agent session");
            }
            if let Some(tab) = cfg.host_terminals.iter_mut().find(|tab| tab.name == name) {
                tab.project = project.to_string();
                tab.path = path.to_string();
            } else {
                let id = crate::storage_id::allocate(|id| {
                    Ok(cfg.host_terminals.iter().any(|tab| tab.id == id))
                })?;
                cfg.host_terminals.push(crate::config::HostTerminalCfg {
                    id,
                    name: name.to_string(),
                    label: None,
                    project: project.to_string(),
                    path: path.to_string(),
                    autostart: true,
                });
            }
            Ok(((), true))
        })
        .await
    }

    pub(crate) async fn remember_path(
        &self,
        sessions: &Arc<SessionRuntime>,
        name: &str,
        path: &str,
    ) -> Result<SavedHostPath> {
        self.update(sessions, |cfg| {
            let mut outcome = SavedHostPath {
                project: None,
                changed: false,
            };
            if let Some(tab) = cfg.host_terminals.iter_mut().find(|tab| tab.name == name) {
                outcome.project = Some(tab.project.clone());
                if tab.path != path {
                    tab.path = path.to_string();
                    outcome.changed = true;
                }
            }
            let changed = outcome.changed;
            Ok((outcome, changed))
        })
        .await
    }

    async fn update<T>(
        &self,
        sessions: &Arc<SessionRuntime>,
        update: impl FnOnce(&mut Config) -> Result<(T, bool)>,
    ) -> Result<T> {
        sessions
            .session_operation(async {
                let runtime = ConfigRuntime::new(
                    sessions.clone(),
                    self.workspace.clone(),
                    self.authority.clone(),
                );
                let (result, prepared) = self
                    .workspace
                    .configuration
                    .prepare(&runtime, ConfigMutation::HostShells, update)
                    .await?;
                if let Some(prepared) = prepared {
                    let workspace = self.workspace.clone();
                    // Commit and publication retain the identity guard even if the caller leaves.
                    sessions
                        .owned_session_operation(async move {
                            workspace.configuration.commit(&runtime, prepared).await
                        })
                        .await?;
                }
                Ok(result)
            })
            .await
    }
}

#[cfg(test)]
#[path = "host_records_tests.rs"]
mod tests;
