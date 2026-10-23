//! Resolve file-action checkout scopes and coordinate bounded host execution.
//! Application entry points refresh configuration; files owns path and command policy.
use crate::config::Config;
use crate::files::actions::{absolute_path, project_action_path};
use anyhow::{Result, anyhow, bail};
use std::sync::Arc;

#[derive(Clone)]
pub(crate) struct FileActions {
    configuration: super::Configuration,
    workspace: Arc<super::Workspace>,
}

impl FileActions {
    pub(crate) fn new(application: &super::Application) -> Self {
        Self {
            configuration: super::Configuration::new(application),
            workspace: application.workspace.clone(),
        }
    }

    pub(crate) async fn file_action(
        &self,
        project: &str,
        worktree: &str,
        raw_path: &str,
        command: &str,
        host: bool,
    ) -> Result<String> {
        self.configuration.reload_if_changed().await;
        file_action(self, project, worktree, raw_path, command, host).await
    }

    pub(crate) async fn file_action_command(
        &self,
        project: &str,
        worktree: &str,
        raw_path: &str,
        command: &str,
        host: bool,
    ) -> Result<String> {
        self.configuration.reload_if_changed().await;
        file_action_command(self, project, worktree, raw_path, command, host).await
    }
}

impl super::launch::Runtime for FileActions {
    async fn file_action_command(
        &self,
        project: &str,
        worktree: &str,
        path: &str,
        command: &str,
        host: bool,
    ) -> Result<String> {
        FileActions::file_action_command(self, project, worktree, path, command, host).await
    }
}
pub(crate) async fn worktree_for_path(
    runtime: &impl super::worktrees::Catalog,
    project: &str,
    raw_path: &str,
) -> Result<String> {
    let cfg = runtime.config().await;
    let Some(p) = cfg.project(project) else {
        return Ok(String::new());
    };
    let path = absolute_path(raw_path)?;
    let path = path.canonicalize().unwrap_or(path);
    let store = runtime.worktree_records();
    Ok(store
        .worktrees
        .iter()
        .filter(|w| !p.id.is_empty() && w.project_id == p.id && path.starts_with(&w.path))
        .max_by_key(|w| w.path.len())
        .map(|w| w.id.clone())
        .unwrap_or_default())
}
pub(crate) async fn config_for_action_scope(
    runtime: &impl super::worktrees::Catalog,
    project: &str,
    worktree: &str,
    path: &str,
) -> Result<Config> {
    let worktree = if worktree.is_empty() {
        "main"
    } else {
        worktree
    };
    if project.trim().is_empty() {
        if worktree != "main" {
            bail!("a project is required to select a worktree");
        }
        return Ok(runtime.config().await);
    }
    if !path.trim().is_empty() {
        let inferred = worktree_for_path(runtime, project, path).await?;
        let inferred = if inferred.is_empty() {
            "main"
        } else {
            &inferred
        };
        if inferred != worktree {
            bail!("File action path belongs to a different worktree");
        }
    }
    let mut cfg = runtime.config().await;
    let p = cfg
        .projects
        .iter_mut()
        .find(|p| p.name == project)
        .ok_or_else(|| anyhow!("Project {project:?} does not exist."))?;
    *p = crate::worktrees::resolution::resolve(&runtime.worktree_records(), p, worktree).await?;
    Ok(cfg)
}
pub(crate) async fn file_action(
    runtime: &impl super::worktrees::Catalog,
    project: &str,
    worktree: &str,
    raw_path: &str,
    command: &str,
    host: bool,
) -> Result<String> {
    let cfg = config_for_action_scope(runtime, project, worktree, raw_path).await?;
    let (p, s) = crate::files::actions::execution::resolve_file_action(
        &cfg, project, raw_path, command, host,
    )?;
    crate::files::actions::execution::execute_file_action(&cfg, &s, &p).await
}
pub(crate) async fn file_action_command(
    runtime: &impl super::worktrees::Catalog,
    project: &str,
    worktree: &str,
    raw_path: &str,
    command: &str,
    host: bool,
) -> Result<String> {
    let cfg = config_for_action_scope(runtime, project, worktree, raw_path).await?;
    if host && project.trim().is_empty() {
        absolute_path(raw_path)?;
    } else {
        let p = cfg
            .project(project.trim())
            .ok_or_else(|| anyhow!("Project {project:?} does not exist."))?;
        project_action_path(p, raw_path)?;
    }
    // This is prepared client command text, including quoted literal filenames.
    // Scope validation must not substitute placeholder-looking filename contents.
    Ok(command.to_owned())
}

impl super::worktrees::Catalog for FileActions {
    async fn config(&self) -> Config {
        self.configuration.config().await
    }
    fn worktree_records(&self) -> crate::worktrees::Store {
        self.workspace.configuration.records.worktrees()
    }
}
