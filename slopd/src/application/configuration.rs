//! Configuration edits, catalog reload and publication coordination.
//! Transactions own persistence; sessions retain live validation, invalidation and leases.
use super::{Application, ConfigAuthority, LibraryMutation, LibraryRuntime, Services, Workspace};
use crate::config::transactions::{ConfigMutation, PreparedConfigChange, SettingsEdit};
use crate::config::{Config, patch::json_to_toml};
use crate::session::SessionRuntime;
use anyhow::Result;
use serde_json::Value;
use std::sync::Arc;

#[derive(Clone)]
pub(crate) struct Configuration {
    sessions: Arc<SessionRuntime>,
    workspace: Arc<Workspace>,
    config_authority: Arc<ConfigAuthority>,
    services: Arc<Services>,
}
impl Configuration {
    pub(crate) fn new(application: &Application) -> Self {
        Self::from_parts(
            application.sessions.clone(),
            application.workspace.clone(),
            application.config_authority.clone(),
            application.services.clone(),
        )
    }
    pub(crate) fn from_parts(
        sessions: Arc<SessionRuntime>,
        workspace: Arc<Workspace>,
        config_authority: Arc<ConfigAuthority>,
        services: Arc<Services>,
    ) -> Self {
        Self {
            sessions,
            workspace,
            config_authority,
            services,
        }
    }
}
impl Configuration {
    fn runtime(&self) -> super::config_runtime::ConfigRuntime {
        super::config_runtime::ConfigRuntime::new(
            self.sessions.clone(),
            self.workspace.clone(),
            self.config_authority.clone(),
        )
    }
    pub(crate) async fn config(&self) -> Config {
        self.config_authority.read_config().await.clone()
    }
    pub(crate) async fn update_cfg<T>(
        &self,
        mutation: ConfigMutation,
        update: impl FnOnce(&mut Config) -> Result<T>,
    ) -> Result<T> {
        self.update_cfg_if_changed(mutation, |cfg| update(cfg).map(|result| (result, true)))
            .await
    }
    pub(crate) async fn update_cfg_if_changed<T>(
        &self,
        mutation: ConfigMutation,
        update: impl FnOnce(&mut Config) -> Result<(T, bool)>,
    ) -> Result<T> {
        self.sessions
            .session_operation(async {
                let (result, prepared) = self.prepare_cfg_change(mutation, update).await?;
                if let Some(prepared) = prepared {
                    self.commit_prepared_cfg(prepared).await?;
                }
                Ok(result)
            })
            .await
    }
    pub(crate) async fn prepare_cfg_change<T>(
        &self,
        mutation: ConfigMutation,
        update: impl FnOnce(&mut Config) -> Result<(T, bool)>,
    ) -> Result<(T, Option<PreparedConfigChange>)> {
        self.workspace
            .configuration
            .prepare(&self.runtime(), mutation, update)
            .await
    }
    pub(crate) async fn commit_prepared_cfg(&self, prepared: PreparedConfigChange) -> Result<()> {
        let manager = self.clone();
        self.sessions
            .owned_session_operation(Box::pin(async move {
                manager
                    .workspace
                    .configuration
                    .commit(&manager.runtime(), prepared)
                    .await
            }))
            .await
    }
    pub(crate) async fn commit_project_relocation(
        &self,
        prepared: PreparedConfigChange,
        store: &crate::worktrees::Store,
    ) -> Result<()> {
        let manager = self.clone();
        let store = store.clone();
        self.sessions
            .owned_session_operation(Box::pin(async move {
                manager
                    .workspace
                    .configuration
                    .commit_project_relocation(&manager.runtime(), prepared, &store)
                    .await
            }))
            .await
    }
    pub(crate) async fn patch_config(&self, patch: Value) -> Result<()> {
        self.edit_record_settings(SettingsEdit::Patch(json_to_toml(patch)?))
            .await
    }
    pub(crate) async fn replace_config(&self, text: &str) -> Result<()> {
        self.edit_record_settings(SettingsEdit::Replace(text.to_owned()))
            .await
    }
    async fn edit_record_settings(&self, edit: SettingsEdit) -> Result<()> {
        let manager = self.clone();
        self.sessions
            .owned_session_operation(async move {
                manager
                    .workspace
                    .configuration
                    .edit_settings(&manager.runtime(), edit)
                    .await?;
                manager.finish_config_change().await;
                Ok(())
            })
            .await
    }
    pub(crate) async fn reload_if_changed(&self) -> bool {
        if self.sessions.session_request_active() {
            return false;
        }
        let manager = self.clone();
        self.sessions
            .owned_session_operation(async move {
                if !manager
                    .workspace
                    .configuration
                    .reload(&manager.runtime())
                    .await
                {
                    return false;
                }
                manager
                    .services
                    .announce_library(manager.config().await)
                    .await;
                true
            })
            .await
    }
    pub(crate) async fn reload_if_stale(&self) {
        if !self.workspace.configuration.library_is_current() {
            self.reload_if_changed().await;
        }
    }
    /// Reconcile only after releasing the persistence gate, then publish accepted views.
    async fn finish_config_change(&self) {
        self.sessions.sync_from_config().await;
        super::projects::publish(&self.config_authority, &self.services).await;
        self.services.announce_library(self.config().await).await;
    }
}

impl LibraryRuntime for Configuration {
    async fn reload(&self) {
        self.reload_if_changed().await;
    }
    async fn config(&self) -> Config {
        Configuration::config(self).await
    }
    async fn commit(&self, mutation: LibraryMutation) -> Result<()> {
        self.update_cfg(ConfigMutation::Library, |cfg| mutation.apply(cfg))
            .await
    }
    async fn announce_projects(&self) {
        super::projects::publish(&self.config_authority, &self.services).await;
    }
}

impl Configuration {
    /// Reload under the exclusive identity guard before authorization.
    pub(crate) async fn session_request<F: std::future::Future>(&self, request: F) -> F::Output {
        self.sessions
            .session_operation(async {
                self.reload_if_changed().await;
                self.sessions.admitted_request(request).await
            })
            .await
    }

    /// Read admission refreshes configuration before acquiring the shared identity guard.
    pub(crate) async fn session_read_request<F: std::future::Future>(
        &self,
        request: F,
    ) -> F::Output {
        self.reload_if_stale().await;
        self.sessions
            .session_read_operation(self.sessions.admitted_request(request))
            .await
    }
}

#[cfg(test)]
#[path = "configuration_tests.rs"]
mod tests;

impl Configuration {
    pub(crate) async fn remove_agent_record(&self, name: &str) -> Result<()> {
        self.update_cfg(ConfigMutation::Agents, |cfg| {
            cfg.sessions.retain(|session| session.name != name);
            Ok(())
        })
        .await
    }
}
