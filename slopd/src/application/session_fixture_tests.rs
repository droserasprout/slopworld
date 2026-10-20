//! Application handles for runtime fixtures. Live-state construction stays in session tests.
use crate::config::{Config, LibraryItemCfg};
use crate::session::{RunWhere, SessionRuntime};
use anyhow::Result;
use std::sync::Arc;

impl SessionRuntime {
    pub(crate) fn configuration(self: &Arc<Self>) -> crate::application::Configuration {
        crate::application::Configuration::from_parts(
            self.clone(),
            self.workspace.clone(),
            self.config_authority.clone(),
            self.application.clone(),
        )
    }
}

impl SessionRuntime {
    /// Exercise production ownership and commit while replacing fixture membership.
    pub(crate) async fn replace_workspace_fixture(self: &Arc<Self>, cfg: &Config) -> Result<()> {
        self.configuration()
            .update_cfg(
                crate::config::transactions::ConfigMutation::ProjectReferences,
                |next| {
                    next.projects = cfg.projects.clone();
                    next.sessions = cfg.sessions.clone();
                    next.host_terminals = cfg.host_terminals.clone();
                    Ok(())
                },
            )
            .await
    }
}

impl SessionRuntime {
    pub(crate) fn edits(self: &Arc<Self>) -> crate::application::SessionEdits {
        crate::application::SessionEdits::new(&crate::application::Application::from_runtime(self))
    }
}
impl SessionRuntime {
    pub(crate) async fn create_test_errand_session(
        self: &Arc<Self>,
        cfg: &Config,
        item: &LibraryItemCfg,
        want: &RunWhere,
        host: bool,
        persistent: bool,
        like: &str,
    ) -> Result<String> {
        let execution = crate::application::launch::select_execution(
            host,
            persistent,
            &item.agent_template,
            like,
        )?;
        let request =
            crate::application::launch::from_library(item.clone(), want.clone(), execution)?;
        crate::application::launch::Launches::new(&crate::application::Application::from_runtime(
            self,
        ))
        .create_errand_session(cfg, &request)
        .await
    }
}

impl SessionRuntime {
    pub(crate) fn configuration_host_records_fixture(&self) -> super::HostRecords {
        super::HostRecords::new(self.workspace.clone(), self.config_authority.clone())
    }
}

impl SessionRuntime {
    pub(crate) async fn spawnable_worker_template(
        &self,
        caller: &str,
        project: &str,
        requested: &str,
    ) -> Result<crate::agent_templates::AgentTemplate> {
        crate::application::workers::template(
            &self.config().await,
            caller,
            &self.worker_caller(caller).await,
            project,
            requested,
            &self.application.templates.list().await,
        )
    }
    pub(crate) async fn save_worktrees(&self, store: &crate::worktrees::Store) -> Result<()> {
        self.workspace.configuration.save_worktrees(store).await
    }
}

impl crate::application::worktrees::Catalog for SessionRuntime {
    async fn config(&self) -> Config {
        SessionRuntime::config(self).await
    }
    fn worktree_records(&self) -> crate::worktrees::Store {
        SessionRuntime::worktree_records(self)
    }
}
