//! Worktree catalog lifecycle, recovery, and publication.
//! Application operations retain runtime leases through cancellation; session adapters
//! supply guards, configuration transactions and live attachment identity.
use crate::config::{Config, ProjectCfg, expand};
use crate::session::SessionRuntime;
use crate::worktrees::checkout::{
    checked_worktree_destination, create_managed_worktree_path, remove_worktree_checkout,
    validate_external_worktree,
};
use crate::worktrees::directories::CreatedDirectories;
use crate::worktrees::relocation::Persistence;
use crate::worktrees::{Store, Worktree, WorktreePhase, git};
use anyhow::{Context, Result, anyhow, bail};
use serde::Deserialize;
use std::path::{Path, PathBuf};
use std::sync::Arc;
#[derive(Clone, Default, Deserialize)]
pub(crate) struct WorktreeRequest {
    pub project: String,
    #[serde(default)]
    pub name: String,
    #[serde(default)]
    pub base: String,
    /// An existing external checkout path. An empty path allocates a managed worktree.
    #[serde(default)]
    pub path: String,
}

pub(crate) struct WorktreeView {
    pub worktree: Worktree,
    pub branch: String,
    pub head: String,
    pub attachments: Vec<String>,
}

struct PreparedWorktree {
    root: PathBuf,
    project: ProjectCfg,
    branch: String,
    base: String,
    worktree: Worktree,
}

pub(crate) trait Catalog: Sync {
    fn config(&self) -> impl std::future::Future<Output = Config> + Send;
    fn worktree_records(&self) -> Store;
}

/// Callers retain session and catalog guards through commit, recovery, or rollback.
struct Coordinator<'a> {
    runtime: &'a Worktrees,
}
impl<'a> Coordinator<'a> {
    fn new(runtime: &'a Worktrees) -> Self {
        Self { runtime }
    }
    async fn list(&self, project: &str) -> Result<Vec<WorktreeView>> {
        let cfg = self.runtime.config().await;
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
            self.runtime
                .worktree_records()
                .worktrees
                .into_iter()
                .filter(|w| !p.id.is_empty() && w.project_id == p.id),
        );
        let mut result = Vec::new();
        for mut w in rows {
            let (branch, head) = crate::worktrees::inspection::inspect(&mut w).await;
            let path = Path::new(&w.path);
            let mut attachments = self.runtime.sessions.worktree_attachments(&w).await;
            if w.id == "main" {
                attachments = self
                    .runtime
                    .sessions
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

    async fn create(&self, request: &WorktreeRequest) -> Result<Worktree> {
        let prepared = self.prepare_worktree(request).await?;
        self.finish_worktree_creation(prepared).await
    }
    async fn remove(&self, project: &str, id: &str) -> Result<()> {
        let mut removal = self.prepare_worktree_removal(project, id).await?;
        removal.record_intent(self.runtime).await?;
        let result = remove_worktree_checkout(&removal.project, &removal.worktree).await;
        removal.finish(self.runtime, result).await
    }
    async fn rename(&self, project: &str, id: &str, name: String) -> Result<Worktree> {
        let cfg = self.runtime.config().await;
        let p = cfg
            .project(project)
            .ok_or_else(|| anyhow!("no project {project}"))?;
        let store = self.runtime.worktree_records();
        let index = store
            .worktrees
            .iter()
            .position(|w| w.id == id && w.project_id == p.id)
            .ok_or_else(|| anyhow!("no worktree {id}"))?;
        let old = store
            .worktrees
            .get(index)
            .ok_or_else(|| anyhow!("no worktree {id}"))?
            .clone();
        if !old.managed || old.phase != WorktreePhase::Ready {
            bail!("only ready managed worktrees can be renamed");
        }
        let users = self.runtime.sessions.worktree_attachments(&old).await;
        if !users.is_empty() {
            bail!(
                "Worktree remains attached to {}. Remove or move these sessions first.",
                users.join(", ")
            );
        }
        let old_path = Path::new(&old.path);
        if old_path.file_name().and_then(|s| s.to_str()) != Some(&old.name) {
            bail!("worktree path does not match its recorded name");
        }
        let project_dir = old_path.parent().context("worktree root")?.to_path_buf();
        tokio::fs::create_dir_all(&project_dir).await?;
        let destination = checked_worktree_destination(&project_dir, &name).await?;
        let mut updated = old.clone();
        updated.name = name;
        updated.path = destination.to_string_lossy().into_owned();
        if old.path != updated.path {
            if tokio::fs::symlink_metadata(&destination).await.is_ok() {
                bail!("destination {} already exists", destination.display());
            }
            let mut relocations = crate::worktrees::relocation::Relocations::new(
                store,
                vec![(index, updated.clone())],
            )?;
            relocations.execute(self.runtime).await?;
        }
        Ok(updated)
    }
    async fn prepare_worktree(&self, q: &WorktreeRequest) -> Result<PreparedWorktree> {
        // Accepted projects already own a durable identity. Worktree creation
        // only reads it while holding the shared session and worktree guards.
        let project = self
            .runtime
            .config()
            .await
            .project(&q.project)
            .cloned()
            .ok_or_else(|| anyhow!("no project {}", q.project))?;
        let root = PathBuf::from(expand(&project.dir)).canonicalize()?;
        let repository = git(
            &root,
            &["rev-parse", "--path-format=absolute", "--git-common-dir"],
        )
        .await?;
        let id = self.runtime.sessions.allocate_worktree_identity().await?;
        let name = if q.name.is_empty() {
            id.get(..8).unwrap_or(&id).to_string()
        } else {
            q.name.clone()
        };
        crate::config::project_name_component(&name).context("invalid worktree name")?;
        let managed = q.path.is_empty();
        if managed {
            crate::worktrees::validate_new_branch(&root, &name).await?;
        }
        let branch = name.clone();
        let requested_path = if managed {
            let parent = crate::worktrees::project_root(&project);
            if !parent.is_absolute() {
                bail!("worktree root must be absolute");
            }
            parent.join(&name)
        } else {
            PathBuf::from(expand(&q.path)).canonicalize()?
        };
        if let Some(why) = crate::sandbox::refused(&requested_path.to_string_lossy()) {
            bail!("worktree reaches {why}");
        }
        if !managed {
            validate_external_worktree(&root, &repository, &requested_path).await?;
        }
        let base = if managed {
            crate::worktrees::inspection::resolve_commit(&root, &q.base).await?
        } else {
            // Registration preserves the existing checkout. Recovery must compare
            // against its HEAD, independent of the main checkout or requested base.
            crate::worktrees::inspection::resolve_commit(&requested_path, "").await?
        };
        let path = requested_path;
        Ok(PreparedWorktree {
            root,
            project: project.clone(),
            branch: branch.clone(),
            base: base.clone(),
            worktree: Worktree {
                id,
                project_id: project.id,
                name,
                path: path.to_string_lossy().into_owned(),
                repository,
                managed,
                initial_branch: if managed { branch } else { String::new() },
                base,
                phase: WorktreePhase::Allocating,
                error: String::new(),
                relocation: None,
            },
        })
    }
    async fn finish_worktree_creation(&self, prepared: PreparedWorktree) -> Result<Worktree> {
        let PreparedWorktree {
            root,
            project,
            branch,
            base,
            mut worktree,
        } = prepared;
        let mut directories = CreatedDirectories::default();
        let mut store = self.runtime.worktree_records();
        if store
            .worktrees
            .iter()
            .any(|registered| registered.path == worktree.path)
        {
            bail!("checkout already registered");
        }
        if worktree.managed {
            let path = create_managed_worktree_path(
                PathBuf::from(&worktree.path),
                &worktree.name,
                &mut directories,
            )
            .await?;
            worktree.path = path.to_string_lossy().into_owned();
            if store
                .worktrees
                .iter()
                .any(|registered| registered.path == worktree.path)
            {
                bail!("checkout already registered");
            }
        }
        let path = Path::new(&worktree.path);
        store.worktrees.push(worktree.clone());
        self.runtime.save_worktree_records(&store).await?;
        // The allocating record now owns crash recovery for this directory.
        directories.commit();

        let result = if worktree.managed {
            crate::worktrees::allocate(&root, path, &branch, &base).await
        } else {
            Ok(())
        }
        .and_then(|()| crate::sandbox::cache::reconcile(&project, path));
        worktree.phase = if result.is_ok() {
            WorktreePhase::Ready
        } else {
            WorktreePhase::Error
        };
        worktree.error = result
            .err()
            .map(|error| format!("{error:#}"))
            .unwrap_or_default();
        *store
            .worktrees
            .last_mut()
            .ok_or_else(|| anyhow!("created worktree disappeared before publication"))? =
            worktree.clone();
        self.runtime.save_worktree_records(&store).await?;
        crate::application::projects::publish(
            &self.runtime.config_authority,
            &self.runtime.services,
        )
        .await;
        if !worktree.error.is_empty() {
            bail!(
                "worktree {} retained after allocation failure: {}",
                worktree.id,
                worktree.error
            );
        }
        Ok(worktree)
    }
    async fn prepare_worktree_removal(&self, project: &str, id: &str) -> Result<WorktreeRemoval> {
        let cfg = self.runtime.config().await;
        let project = cfg
            .project(project)
            .ok_or_else(|| anyhow!("no project {project}"))?
            .clone();
        let store = self.runtime.worktree_records();
        let index = store
            .worktrees
            .iter()
            .position(|w| w.id == id && w.project_id == project.id && !project.id.is_empty())
            .ok_or_else(|| anyhow!("no removable worktree {id}"))?;
        let worktree = store
            .worktrees
            .get(index)
            .ok_or_else(|| anyhow!("no removable worktree {id}"))?
            .clone();
        if worktree.phase == WorktreePhase::Relocating {
            bail!(
                "Repair the interrupted relocation before removing this worktree: {}",
                worktree.error
            );
        }
        let users = self.runtime.sessions.worktree_attachments(&worktree).await;
        if !users.is_empty() {
            bail!(
                "Worktree remains attached to {}. Remove or move these sessions first.",
                users.join(", ")
            );
        }
        Ok(WorktreeRemoval {
            project,
            worktree,
            store,
            index,
        })
    }

    async fn recover_worktrees(&self) -> Result<()> {
        let mut store = self.runtime.worktree_records();
        let mut changed = false;
        for w in &mut store.worktrees {
            if !matches!(w.phase, WorktreePhase::Allocating | WorktreePhase::Removing) {
                continue;
            }
            changed = true;
            let complete = if w.phase == WorktreePhase::Allocating && Path::new(&w.path).is_dir() {
                crate::worktrees::resolution::completed_allocation(&self.runtime.config().await, w)
                    .await
                    .unwrap_or(false)
            } else {
                false
            };
            if complete {
                w.phase = WorktreePhase::Ready;
                w.error.clear();
            } else {
                w.error = format!(
                    "Operation {} was interrupted. Inspect the checkout or try manual removal.",
                    w.phase
                );
                w.phase = WorktreePhase::Error;
            }
        }
        if changed {
            self.runtime.save_worktree_records(&store).await?;
        }
        Ok(())
    }
}
/// The catalog mutation guard keeps this selected record and its index stable.
/// Retain the original record while the catalog stores the removal intent.
struct WorktreeRemoval {
    project: ProjectCfg,
    worktree: Worktree,
    store: Store,
    index: usize,
}

impl WorktreeRemoval {
    fn record_mut(&mut self) -> Result<&mut Worktree> {
        self.store
            .worktrees
            .get_mut(self.index)
            .ok_or_else(|| anyhow!("no removable worktree {}", self.worktree.id))
    }

    async fn record_intent(&mut self, manager: &Worktrees) -> Result<()> {
        self.record_mut()?.phase = WorktreePhase::Removing;
        manager.save_worktree_records(&self.store).await
    }

    async fn finish(mut self, manager: &Worktrees, result: Result<()>) -> Result<()> {
        match result {
            Ok(()) => {
                self.store.worktrees.remove(self.index);
                manager.save_worktree_records(&self.store).await
            }
            Err(error) => {
                // A failed deletion remains inspectable and retryable. A failed catalog
                // save after deletion instead leaves the durable removing intent for recovery.
                let phase = if Path::new(&self.worktree.path).is_dir() {
                    WorktreePhase::Ready
                } else {
                    WorktreePhase::Error
                };
                let record = self.record_mut()?;
                record.phase = phase;
                record.error = format!("{error:#}");
                manager.save_worktree_records(&self.store).await?;
                Err(error)
            }
        }
    }
}

#[derive(Clone)]
pub(crate) struct Worktrees {
    sessions: Arc<SessionRuntime>,
    workspace: Arc<super::Workspace>,
    config_authority: Arc<super::ConfigAuthority>,
    services: Arc<super::Services>,
}

impl Worktrees {
    pub(crate) fn from_parts(
        sessions: Arc<SessionRuntime>,
        workspace: Arc<super::Workspace>,
        config_authority: Arc<super::ConfigAuthority>,
        services: Arc<super::Services>,
    ) -> Self {
        Self {
            sessions,
            workspace,
            config_authority,
            services,
        }
    }
    fn configuration(&self) -> super::Configuration {
        super::Configuration::from_parts(
            self.sessions.clone(),
            self.workspace.clone(),
            self.config_authority.clone(),
            self.services.clone(),
        )
    }

    pub(crate) fn new(application: &super::Application) -> Self {
        Self {
            sessions: application.sessions.clone(),
            workspace: application.workspace.clone(),
            config_authority: application.config_authority.clone(),
            services: application.services.clone(),
        }
    }

    pub(crate) async fn rename_worktree(
        &self,
        project: String,
        id: String,
        name: String,
    ) -> Result<Worktree> {
        crate::config::project_name_component(&name).context("invalid worktree name")?;
        let manager = self.clone();
        self.sessions
            .owned_session_operation(Box::pin(async move {
                let _lock = manager.workspace.worktrees.mutation.lock().await;
                Coordinator::new(&manager).rename(&project, &id, name).await
            }))
            .await
    }
    pub(crate) async fn worktree_base(
        &self,
        caller: &str,
        project: &str,
        revision: &str,
    ) -> Result<String> {
        let cfg = self.configuration().config().await;
        let project = cfg
            .project(project)
            .ok_or_else(|| anyhow!("unknown project"))?;
        let selected = if revision.is_empty() && caller != crate::tasks::HOST {
            self.sessions.caller_worktree(caller).await
        } else {
            String::new()
        };
        let project = self.sessions.resolve_worktree(project, &selected).await?;
        crate::worktrees::inspection::resolve_commit(Path::new(&expand(&project.dir)), revision)
            .await
    }

    pub(crate) async fn worktree_list(
        &self,
        project: &str,
    ) -> Result<Vec<crate::application::worktrees::WorktreeView>> {
        Coordinator::new(self).list(project).await
    }

    pub(crate) async fn create_worktree(&self, q: WorktreeRequest) -> Result<Worktree> {
        self.configuration().reload_if_changed().await;
        let manager = self.clone();
        Box::pin(self.sessions.owned_session_read_operation(async move {
            let _lock = manager.workspace.worktrees.mutation.lock().await;
            Coordinator::new(&manager).create(&q).await
        }))
        .await
    }

    pub(crate) async fn remove_worktree(&self, project: String, id: String) -> Result<()> {
        let manager = self.clone();
        self.sessions
            .owned_session_operation(Box::pin(async move {
                manager.remove_worktree_inner(&project, &id).await
            }))
            .await
    }

    async fn remove_worktree_inner(&self, project: &str, id: &str) -> Result<()> {
        let _mutation = self.workspace.worktrees.mutation.lock().await;
        Coordinator::new(self).remove(project, id).await
    }

    pub(crate) async fn recover_worktrees(&self) -> Result<()> {
        let _mutation = self.workspace.worktrees.mutation.lock().await;
        Coordinator::new(self).recover_worktrees().await
    }
}

#[cfg(test)]
#[path = "worktrees_tests.rs"]
mod tests;

impl crate::worktrees::relocation::Persistence for Worktrees {
    async fn save_worktree_records(&self, store: &Store) -> Result<()> {
        self.workspace.configuration.save_worktrees(store).await
    }
}

impl crate::application::worktrees::Catalog for Worktrees {
    async fn config(&self) -> Config {
        self.configuration().config().await
    }
    fn worktree_records(&self) -> Store {
        self.workspace.configuration.records.worktrees()
    }
}
