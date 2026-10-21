//! Project catalog policy and checkout relocation coordination.
//! Application operations retain runtime leases through commit and rollback; session
//! adapters supply identity reservation, attachments and configuration transactions.
use crate::config::transactions::{ConfigMutation, PreparedConfigChange};
use crate::config::{Config, ProjectCfg, check_project, expand, validate_mount_paths};
use crate::session::SessionRuntime;
use crate::worktrees::{Store, Worktree, WorktreePhase, directories::CreatedDirectories};
use anyhow::{Context, Result, anyhow, bail};
use serde::Serialize;
use std::path::{Path, PathBuf};
use std::sync::Arc;

pub(crate) trait ProjectRuntime: crate::worktrees::relocation::Persistence + Sync {
    fn config(&self) -> impl std::future::Future<Output = Config> + Send;
    fn worktree_records(&self) -> Store;
    fn worktree_attachments(
        &self,
        worktree: &Worktree,
    ) -> impl std::future::Future<Output = Vec<String>> + Send;
    fn prepare(
        &self,
        replacement: ProjectReplacement,
    ) -> impl std::future::Future<Output = Result<Option<PreparedConfigChange>>> + Send;
    fn commit(
        &self,
        prepared: PreparedConfigChange,
        worktrees: Option<&Store>,
    ) -> impl std::future::Future<Output = Result<()>> + Send;
    fn after_relocations(&self) -> impl std::future::Future<Output = ()> + Send;
}

pub(crate) fn list(cfg: &Config) -> Vec<ProjectView> {
    cfg.projects
        .iter()
        .cloned()
        .map(ProjectView::from)
        .collect()
}

pub(crate) fn settle(project: &mut ProjectCfg) {
    if project.temp {
        project.dir = crate::paths::temp_dir(&project.name);
    }
}

/// Independent checkouts must be explicitly removed before their project record.
pub(crate) fn check_removal(cfg: &Config, records: &Store, name: &str) -> Result<()> {
    if let Some(project) = cfg.project(name)
        && !project.id.is_empty()
        && records.worktrees.iter().any(|w| w.project_id == project.id)
    {
        bail!("remove project worktrees explicitly first");
    }
    Ok(())
}

pub(crate) fn add(cfg: &mut Config, p: ProjectCfg) -> Result<()> {
    validate_mount_paths(&p)?;
    if cfg.project(&p.name).is_some() {
        bail!("project {} already exists", p.name);
    }
    cfg.projects.push(p);
    Ok(())
}
/// Validated project edit, prepared before any checkout movement. The session and
/// worktree operation guards keep its source identity stable through application.
pub(crate) struct ProjectReplacement {
    name: String,
    old: ProjectCfg,
    next: ProjectCfg,
}

impl ProjectReplacement {
    fn prepare(cfg: &Config, name: &str, mut next: ProjectCfg) -> Result<Self> {
        let old = cfg
            .project(name)
            .ok_or_else(|| anyhow!("Project {name:?} does not exist."))?;
        if old.temp != next.temp {
            bail!("The daemon cannot change project temporary mode after creation.");
        }
        next.id = old.id.clone();
        check_project(&next)?;
        validate_mount_paths(&next)?;
        if next.name != name && cfg.project(&next.name).is_some() {
            bail!("project {} already exists", next.name);
        }
        Ok(Self {
            name: name.to_owned(),
            old: old.clone(),
            next,
        })
    }

    pub(crate) fn apply(self, cfg: &mut Config) -> Result<()> {
        let project = cfg
            .projects
            .iter_mut()
            .find(|p| p.name == self.name)
            .ok_or_else(|| anyhow!("Project {:?} does not exist.", self.name))?;
        anyhow::ensure!(
            project.id == self.old.id,
            "project identity changed during replacement"
        );
        let renamed = self.next.name.clone();
        *project = self.next;
        if renamed != self.name {
            for item in cfg
                .library
                .iter_mut()
                .filter(|item| item.project == self.name)
            {
                item.project = renamed.clone();
            }
            for session in cfg.sessions.iter_mut().filter(|s| s.project == self.name) {
                session.project = renamed.clone();
            }
            for shell in cfg
                .host_terminals
                .iter_mut()
                .filter(|s| s.project == self.name)
            {
                shell.project = renamed.clone();
            }
        }
        Ok(())
    }
}
pub(crate) fn remove(cfg: &mut Config, name: &str) -> Result<()> {
    if cfg.project(name).is_none() {
        bail!("Project {name:?} does not exist.");
    }
    let users: Vec<&str> = cfg
        .sessions
        .iter()
        .filter(|s| s.project == name)
        .map(|s| s.name.as_str())
        .chain(
            cfg.host_terminals
                .iter()
                .filter(|s| s.project == name)
                .map(|s| s.name.as_str()),
        )
        .collect();
    if !users.is_empty() {
        bail!(
            "project {name} still has agents in it: {}. Remove or move them first.",
            users.join(", ")
        );
    }
    cfg.projects.retain(|p| p.name != name);
    Ok(())
}
pub(crate) async fn update(runtime: &impl ProjectRuntime, name: &str, p: ProjectCfg) -> Result<()> {
    let replacement = ProjectReplacement::prepare(&runtime.config().await, name, p)?;
    let RelocationPlan {
        store,
        moves,
        mut directories,
    } = plan_relocations(runtime, name, &replacement.old, &replacement.next).await?;
    let mut relocations = crate::worktrees::relocation::Relocations::new(store, moves)?;
    relocations.execute_pending(runtime).await?;
    runtime.after_relocations().await;
    let result = runtime.prepare(replacement).await;
    let result = match result {
        Ok(Some(prepared)) => {
            if relocations.has_moves() {
                runtime
                    .commit(prepared, Some(relocations.candidate()))
                    .await
            } else {
                runtime.commit(prepared, None).await
            }
        }
        Ok(None) => Ok(()),
        Err(error) => Err(error),
    };
    if let Err(error) = result {
        return match relocations.rollback(runtime).await {
            Ok(()) => Err(error),
            Err(rollback) => Err(anyhow!(
                "{error:#}; restoring project worktrees also failed: {rollback:#}"
            )),
        };
    }
    directories.commit();
    Ok(())
}

struct RelocationPlan {
    store: Store,
    moves: Vec<(usize, Worktree)>,
    directories: CreatedDirectories,
}

async fn plan_relocations(
    runtime: &impl ProjectRuntime,
    name: &str,
    old_project: &ProjectCfg,
    p: &ProjectCfg,
) -> Result<RelocationPlan> {
    let store = runtime.worktree_records();
    let mut planned = Vec::new();
    let mut directories = CreatedDirectories::default();
    if p.name != name {
        crate::config::project_name_component(&p.name)?;
        let local_root = PathBuf::from(expand(&old_project.dir)).join(".worktrees");
        let local_root = local_root.canonicalize().unwrap_or(local_root);
        for (i, w) in store
            .worktrees
            .iter()
            .enumerate()
            .filter(|(_, w)| w.managed && w.project_id == old_project.id)
        {
            // Project-local worktrees do not depend on the project's display name.
            if Path::new(&w.path).parent() == Some(local_root.as_path()) {
                continue;
            }
            if w.phase != WorktreePhase::Ready {
                bail!(
                    "finish worktree {} recovery before renaming the project",
                    w.id
                );
            }
            let users = runtime.worktree_attachments(w).await;
            if !users.is_empty() {
                bail!(
                    "Worktree {} remains attached to {}. Remove or move these sessions first.",
                    w.name,
                    users.join(", ")
                );
            }
            let old_path = Path::new(&w.path);
            if old_path.file_name().and_then(|s| s.to_str()) != Some(&w.name) {
                bail!("worktree path does not match its recorded name");
            }
            let root = old_path
                .parent()
                .and_then(Path::parent)
                .context("worktree root")?;
            let project_dir = root.join(&p.name);
            directories.create_all(&project_dir)?;
            let dest =
                crate::worktrees::checkout::checked_worktree_destination(&project_dir, &w.name)
                    .await?;
            if std::fs::symlink_metadata(&dest).is_ok() {
                bail!("destination {} already exists", dest.display());
            }
            let mut destination = w.clone();
            destination.path = dest.to_string_lossy().into_owned();
            planned.push((i, destination));
        }
    }
    Ok(RelocationPlan {
        store,
        moves: planned,
        directories,
    })
}

/// Editable project configuration alongside its daemon-expanded directory.
#[derive(Debug, Clone, Serialize)]
pub struct ProjectView {
    #[serde(flatten)]
    pub config: crate::config::ProjectCfg,
    pub expanded_dir: String,
}

impl From<crate::config::ProjectCfg> for ProjectView {
    fn from(config: crate::config::ProjectCfg) -> Self {
        Self {
            expanded_dir: crate::config::expand(&config.dir),
            config,
        }
    }
}

#[derive(Clone)]
pub(crate) struct Projects {
    sessions: Arc<SessionRuntime>,
    workspace: Arc<super::Workspace>,
    config_authority: Arc<super::ConfigAuthority>,
    services: Arc<super::Services>,
}

impl Projects {
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

    pub(crate) async fn projects(&self) -> Vec<ProjectView> {
        list(&*self.config_authority.read_config().await)
    }

    pub(crate) async fn add_project(&self, p: ProjectCfg) -> Result<()> {
        let manager = self.clone();
        self.sessions
            .owned_session_operation(async move { manager.add_project_inner(p).await })
            .await
    }

    async fn add_project_inner(&self, mut p: ProjectCfg) -> Result<()> {
        let _worktrees = self.workspace.worktrees.mutation.lock().await;
        self.configuration().reload_if_changed().await;
        settle(&mut p);
        p.id = self.sessions.allocate_project_identity().await?;
        check_project(&p)?;
        self.configuration()
            .update_cfg(ConfigMutation::Projects, |cfg| add(cfg, p))
            .await?;
        publish(&self.config_authority, &self.services).await;
        Ok(())
    }

    pub(crate) async fn update_project(&self, name: &str, mut p: ProjectCfg) -> Result<()> {
        settle(&mut p);
        let manager = self.clone();
        let name = name.to_string();
        // The detached owner retains the boundary and rollback plan after caller cancellation.
        self.sessions
            .owned_session_operation(Box::pin(async move {
                manager.configuration().reload_if_changed().await;
                manager.update_project_inner(&name, p).await?;
                publish(&manager.config_authority, &manager.services).await;
                manager.sessions.announce_sessions().await;
                manager
                    .services
                    .announce_library(manager.configuration().config().await)
                    .await;
                Ok(())
            }))
            .await
    }

    async fn update_project_inner(&self, name: &str, p: ProjectCfg) -> Result<()> {
        let _lock = self.workspace.worktrees.mutation.lock().await;
        update(self, name, p).await
    }

    pub(crate) async fn remove_project(&self, name: &str) -> Result<()> {
        let manager = self.clone();
        let name = name.to_owned();
        self.sessions
            .owned_session_operation(async move { manager.remove_project_inner(&name).await })
            .await
    }

    async fn remove_project_inner(&self, name: &str) -> Result<()> {
        let _worktrees = self.workspace.worktrees.mutation.lock().await;
        self.configuration().reload_if_changed().await;
        let cfg = self.configuration().config().await;
        check_removal(
            &cfg,
            &self.workspace.configuration.records.worktrees(),
            name,
        )?;
        self.configuration()
            .update_cfg(ConfigMutation::ProjectReferences, |cfg| remove(cfg, name))
            .await?;
        publish(&self.config_authority, &self.services).await;
        Ok(())
    }
}

/// Publish from accepted configuration, including callers holding runtime transaction guards.
pub(crate) async fn publish(authority: &super::ConfigAuthority, services: &super::Services) {
    let projects = list(&*authority.read_config().await);
    services.emit(super::Event::Projects { projects });
}

#[cfg(test)]
#[path = "projects_tests.rs"]
mod tests;

impl crate::application::projects::ProjectRuntime for Projects {
    async fn config(&self) -> Config {
        self.configuration().config().await
    }
    fn worktree_records(&self) -> crate::worktrees::Store {
        self.workspace.configuration.records.worktrees()
    }
    async fn worktree_attachments(&self, worktree: &crate::worktrees::Worktree) -> Vec<String> {
        self.sessions.worktree_attachments(worktree).await
    }
    async fn prepare(
        &self,
        replacement: crate::application::projects::ProjectReplacement,
    ) -> Result<Option<crate::config::transactions::PreparedConfigChange>> {
        self.configuration()
            .prepare_cfg_change(ConfigMutation::ProjectReferences, |cfg| {
                replacement.apply(cfg)?;
                Ok(((), true))
            })
            .await
            .map(|((), prepared)| prepared)
    }
    async fn commit(
        &self,
        prepared: crate::config::transactions::PreparedConfigChange,
        worktrees: Option<&crate::worktrees::Store>,
    ) -> Result<()> {
        if let Some(store) = worktrees {
            self.configuration()
                .commit_project_relocation(prepared, store)
                .await
        } else {
            self.configuration().commit_prepared_cfg(prepared).await
        }
    }
    async fn after_relocations(&self) {
        #[cfg(test)]
        {
            let pause = self
                .workspace
                .worktrees
                .relocation_pause
                .lock()
                .unwrap()
                .take();
            if let Some((reached, release)) = pause {
                reached.notify_one();
                release.notified().await;
            }
        }
    }
}
impl crate::worktrees::relocation::Persistence for Projects {
    async fn save_worktree_records(&self, store: &crate::worktrees::Store) -> Result<()> {
        self.workspace.configuration.save_worktrees(store).await
    }
}
