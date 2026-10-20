//! Live worktree attachments, configuration validation and catalog adapters.
//! Application worktrees owns guarded mutations, recovery and client operations.
use super::super::*;
#[cfg(test)]
use crate::worktrees::git;
use crate::worktrees::{Store, Worktree};
use std::collections::HashMap;

impl Manager {
    /// Capture the caller's checkout selection while its request keeps identity stable.
    pub(crate) async fn caller_worktree(&self, caller: &str) -> String {
        self.session_cfg(caller)
            .await
            .map(|session| session.worktree)
            .unwrap_or_default()
    }

    /// Read the accepted index under session/worktree operation guards.
    pub(crate) fn worktree_records(&self) -> Store {
        self.workspace.configuration.records.worktrees()
    }

    pub(crate) async fn save_worktrees(&self, store: &Store) -> Result<()> {
        self.workspace.configuration.save_worktrees(store).await
    }

    /// Build the view from accepted records; offline changes load at startup.
    pub(super) async fn worktree_view_index(&self) -> Arc<HashMap<String, Worktree>> {
        Arc::new(
            self.worktree_records()
                .worktrees
                .into_iter()
                .map(|row| (row.id.clone(), row))
                .collect(),
        )
    }
    pub(super) async fn validate_worktree_config(&self, old: &Config, new: &Config) -> Result<()> {
        crate::worktrees::resolution::validate_config(&self.worktree_records(), old, new)?;
        let old_sessions = old.session_index();
        for session in &new.sessions {
            if let Some(previous) = old_sessions.get(session.name.as_str())
                && previous.worktree != session.worktree
                && self.tmux.exists(&session.name).await
            {
                bail!("stop session {} before changing its worktree", session.name);
            }
        }
        Ok(())
    }

    pub(crate) async fn resolve_worktree(&self, p: &ProjectCfg, id: &str) -> Result<ProjectCfg> {
        crate::worktrees::resolution::resolve(&self.worktree_records(), p, id).await
    }

    pub(super) async fn worktree_attachments(&self, w: &Worktree) -> Vec<String> {
        let cfg = self.config().await;
        let live = self.live.read().await;
        let mut users: Vec<String> = cfg
            .sessions
            .iter()
            .chain(live.values().map(|l| &l.cfg))
            .filter(|s| s.worktree == w.id)
            .map(|s| s.name.clone())
            .collect();
        for host in &cfg.host_terminals {
            if Path::new(&host.path).starts_with(&w.path) {
                users.push(host.name.clone());
            }
        }
        for l in live.values() {
            if l.role.is_host() && Path::new(&l.host_path).starts_with(&w.path) {
                users.push(l.cfg.name.clone());
            }
        }
        users.sort();
        users.dedup();
        users
    }
}

#[cfg(test)]
#[path = "worktrees_tests.rs"]
mod tests;

impl crate::worktrees::relocation::Persistence for Manager {
    async fn save_worktree_records(&self, store: &Store) -> Result<()> {
        self.save_worktrees(store).await
    }
}

impl crate::application::worktrees::Catalog for Manager {
    async fn config(&self) -> Config {
        Manager::config(self).await
    }
    fn worktree_records(&self) -> Store {
        Manager::worktree_records(self)
    }
}

impl crate::application::worktrees::Runtime for Manager {
    async fn allocate_worktree_identity(&self) -> Result<String> {
        Manager::allocate_worktree_identity(self).await
    }
    async fn worktree_attachments(&self, worktree: &Worktree) -> Vec<String> {
        Manager::worktree_attachments(self, worktree).await
    }
    async fn main_attachments(&self, project: &str, path: &Path) -> Vec<String> {
        self.views()
            .await
            .into_iter()
            .filter(|s| s.project == project && Path::new(&expand(&s.dir)) == path)
            .map(|s| s.name)
            .collect()
    }
    async fn announce_projects(&self) {
        crate::application::projects::publish(self).await;
    }
}
