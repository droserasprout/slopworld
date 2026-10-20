//! Session adapters for library transactions, saved launches, and file-action scopes.
//! delivery.rs owns delayed terminal input; callers compose prompts before submission.

use super::super::*;
use crate::session::manager::config::ConfigMutation;

use crate::application::{LibraryMutation, LibraryRuntime};

pub(super) fn routed_action_label(name: &str) -> Option<String> {
    ["view-", "search-", "link-", "edit-", "diff-"]
        .iter()
        .any(|prefix| name.starts_with(prefix))
        .then(|| name.to_string())
}

impl Manager {
    pub async fn library(&self) -> Vec<LibraryItemCfg> {
        self.application.library(self.config().await).await
    }
    pub(super) async fn announce_library(&self) {
        self.application.announce_library(self.config().await).await;
    }
    pub async fn add_library_item(self: &Arc<Self>, sc: LibraryItemCfg) -> Result<()> {
        self.application
            .mutate_library(self, LibraryMutation::Add(sc))
            .await
    }
    pub async fn update_library_item(
        self: &Arc<Self>,
        name: &str,
        sc: LibraryItemCfg,
    ) -> Result<()> {
        self.application
            .mutate_library(
                self,
                LibraryMutation::Replace {
                    name: name.into(),
                    item: sc,
                },
            )
            .await
    }
    pub async fn remove_library_item(self: &Arc<Self>, name: &str) -> Result<()> {
        self.application
            .mutate_library(self, LibraryMutation::Remove(name.into()))
            .await
    }

    pub async fn run_library_item(self: &Arc<Self>, name: &str, want: RunWhere) -> Result<String> {
        self.reload_if_changed().await;
        let cfg = self.config().await;
        let request = crate::application::launch::plan_library_launch(&cfg, name, want)?;
        drop(cfg);
        self.run_launch(request).await
    }

    pub async fn file_action(
        self: &Arc<Self>,
        project: &str,
        worktree: &str,
        raw_path: &str,
        command: &str,
        host: bool,
    ) -> Result<String> {
        self.reload_if_changed().await;
        crate::application::file_actions::file_action(
            self.as_ref(),
            project,
            worktree,
            raw_path,
            command,
            host,
        )
        .await
    }

    pub async fn file_action_command(
        self: &Arc<Self>,
        project: &str,
        worktree: &str,
        raw_path: &str,
        command: &str,
        host: bool,
    ) -> Result<String> {
        self.reload_if_changed().await;
        crate::application::file_actions::file_action_command(
            self.as_ref(),
            project,
            worktree,
            raw_path,
            command,
            host,
        )
        .await
    }

    pub(super) async fn queue_errand_delivery(
        self: &Arc<Self>,
        session: &str,
        sc: &LaunchContent,
        want: &LaunchOptions,
    ) {
        let text = render_prompt(&sc.text, &want.random_tips);
        if text.trim().is_empty() {
            return;
        }
        self.queue_delivery(session, text).await;
    }
}

#[cfg(test)]
#[path = "library_tests.rs"]
mod tests;

impl LibraryRuntime for Arc<Manager> {
    async fn reload(&self) {
        self.reload_if_changed().await;
    }
    async fn config(&self) -> Config {
        Manager::config(self).await
    }
    async fn commit(&self, mutation: LibraryMutation) -> Result<()> {
        self.update_cfg(ConfigMutation::Library, |cfg| mutation.apply(cfg))
            .await
    }
    async fn announce_projects(&self) {
        Manager::announce_projects(self).await;
    }
}
