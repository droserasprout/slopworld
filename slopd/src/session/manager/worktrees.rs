//! Session guards, attachment checks, and views for application worktree operations.
use super::super::*;
use crate::worktrees::{Store, Worktree, WorktreePhase, git};
use anyhow::anyhow;
use std::collections::HashMap;

/// Worktree mutation serialization and relocation test coordination.
#[derive(Default)]
pub(crate) struct WorktreeState {
    pub(super) mutation: tokio::sync::Mutex<()>,
    #[cfg(test)]
    pub(super) relocation_pause:
        std::sync::Mutex<Option<(Arc<tokio::sync::Notify>, Arc<tokio::sync::Notify>)>>,
}

pub(crate) use crate::application::worktrees::WorktreeRequest;

pub(crate) struct WorktreeView {
    pub worktree: Worktree,
    pub branch: String,
    pub head: String,
    pub attachments: Vec<String>,
}

impl Manager {
    /// Read the accepted index under session/worktree operation guards.
    pub(crate) fn worktree_records(&self) -> Store {
        self.configuration.records.worktrees()
    }

    pub(crate) async fn save_worktrees(&self, store: &Store) -> Result<()> {
        self.configuration.save_worktrees(store).await
    }

    pub(crate) async fn rename_worktree(
        self: &Arc<Self>,
        project: String,
        id: String,
        name: String,
    ) -> Result<Worktree> {
        crate::config::project_name_component(&name).context("invalid worktree name")?;
        let manager = self.clone();
        self.owned_session_operation(Box::pin(async move {
            let _lock = manager.worktrees.mutation.lock().await;
            crate::application::worktrees::Coordinator::new(manager.as_ref())
                .rename(&project, &id, name)
                .await
        }))
        .await
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
    pub(crate) async fn worktree_base(
        &self,
        caller: &str,
        project: &str,
        revision: &str,
    ) -> Result<String> {
        let cfg = self.config().await;
        let project = cfg
            .project(project)
            .ok_or_else(|| anyhow!("unknown project"))?;
        let selected = if revision.is_empty() && caller != crate::tasks::HOST {
            self.session_cfg(caller)
                .await
                .map(|s| s.worktree)
                .unwrap_or_default()
        } else {
            String::new()
        };
        let project = self.resolve_worktree(project, &selected).await?;
        git(
            Path::new(&expand(&project.dir)),
            &[
                "rev-parse",
                "--verify",
                "--end-of-options",
                &format!(
                    "{}^{{commit}}",
                    if revision.is_empty() {
                        "HEAD"
                    } else {
                        revision
                    }
                ),
            ],
        )
        .await
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

    pub(crate) async fn worktree_list(&self, project: &str) -> Result<Vec<WorktreeView>> {
        let cfg = self.config().await;
        let p = cfg
            .project(project)
            .ok_or_else(|| anyhow!("no project {project}"))?;
        let mut rows = vec![Worktree {
            id: "main".into(),
            project_id: p.id.clone(),
            name: "main".into(),
            path: expand(&p.dir),
            phase: WorktreePhase::Ready,
            ..Default::default()
        }];
        rows.extend(
            self.worktree_records()
                .worktrees
                .into_iter()
                .filter(|w| !p.id.is_empty() && w.project_id == p.id),
        );
        let mut result = Vec::new();
        for mut w in rows {
            let path = Path::new(&w.path);
            let head = git(path, &["rev-parse", "--verify", "HEAD"])
                .await
                .unwrap_or_default();
            let branch = git(path, &["symbolic-ref", "--quiet", "--short", "HEAD"])
                .await
                .unwrap_or_default();
            // Keep interrupted requests visible for inspection during this daemon run.
            // Startup recovery identifies completed allocations but never restarts workers.
            if w.phase == WorktreePhase::Allocating {
                w.error = "Allocation was interrupted. Inspect the worktree before you remove or reuse it.".into();
            }
            if w.phase == WorktreePhase::Relocating {
                // The persisted error includes both source and destination; preserve it.
            } else if !path.is_dir() {
                w.phase = WorktreePhase::Error;
                w.error =
                    "The checkout directory is missing. Retry manual removal to reconcile its record."
                        .into();
            } else if w.managed && path.file_name().and_then(|s| s.to_str()) != Some(&w.name) {
                w.phase = WorktreePhase::Error;
                w.error = "The managed checkout path does not match its recorded name.".into();
            }
            let mut attachments = self.worktree_attachments(&w).await;
            if w.id == "main" {
                attachments = self
                    .views()
                    .await
                    .into_iter()
                    .filter(|s| s.project == project && Path::new(&expand(&s.dir)) == path)
                    .map(|s| s.name)
                    .collect();
            }
            result.push(WorktreeView {
                worktree: w,
                branch,
                head,
                attachments,
            });
        }
        Ok(result)
    }

    pub(crate) async fn create_worktree(self: &Arc<Self>, q: WorktreeRequest) -> Result<Worktree> {
        self.reload_if_changed().await;
        let manager = self.clone();
        Box::pin(self.owned_session_read_operation(async move {
            let _lock = manager.worktrees.mutation.lock().await;
            crate::application::worktrees::Coordinator::new(manager.as_ref())
                .create(&q)
                .await
        }))
        .await
    }

    pub(crate) async fn remove_worktree(
        self: &Arc<Self>,
        project: String,
        id: String,
    ) -> Result<()> {
        let manager = self.clone();
        self.owned_session_operation(Box::pin(async move {
            manager.remove_worktree_inner(&project, &id).await
        }))
        .await
    }

    async fn remove_worktree_inner(&self, project: &str, id: &str) -> Result<()> {
        let _mutation = self.worktrees.mutation.lock().await;
        crate::application::worktrees::Coordinator::new(self)
            .remove(project, id)
            .await
    }

    pub(crate) async fn recover_worktrees(&self) -> Result<()> {
        let _mutation = self.worktrees.mutation.lock().await;
        crate::application::worktrees::Coordinator::new(self)
            .recover_worktrees()
            .await
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
    async fn announce_projects(&self) {
        Manager::announce_projects(self).await;
    }
}
