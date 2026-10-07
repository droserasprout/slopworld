//! Startup assembly of independently owned stores. No legacy conversion or live polling.
use super::{target::StorageBinding, transaction, workspace::Store};
use crate::config::{Config, HostTerminalCfg, ProjectCfg, SessionCfg, catalog};
use crate::worktrees::Worktree;
use anyhow::{Context, Result, ensure};

/// Validated startup state transfers its accepted indexes into the manager.
#[derive(Debug)]
pub(crate) struct Loaded {
    pub(crate) config: Config,
    pub(crate) binding: StorageBinding,
    pub(crate) stores: Stores,
    pub(crate) library_revision: Option<catalog::Revision>,
}

#[derive(Debug)]
pub(crate) struct Stores {
    pub(crate) agents: Store<SessionCfg>,
    pub(crate) hosts: Store<HostTerminalCfg>,
    pub(crate) projects: Store<ProjectCfg>,
    pub(crate) worktrees: Store<Worktree>,
}

async fn reject_retired(binding: &StorageBinding) -> Result<()> {
    ensure!(
        !occupied(&crate::config::Config::recovery_path_for(&binding.settings)).await?,
        "retired configuration recovery journal remains; this version cannot recover the old storage layout"
    );
    for name in [
        "worktrees.toml",
        "tasks.toml",
        "tasks.journal",
        "grants.toml",
    ] {
        let path = binding
            .settings
            .parent()
            .context("settings has no parent")?
            .join(name);
        if name == "grants.toml" && path == binding.data.join(name) {
            continue;
        }
        ensure!(
            !occupied(&path).await?,
            "retired storage file {} remains; this version requires per-record workspace storage",
            path.display()
        );
    }
    Ok(())
}

// Even a dangling alias is a retired input, not evidence of an empty store.
async fn occupied(path: &std::path::Path) -> Result<bool> {
    match tokio::fs::symlink_metadata(path).await {
        Ok(_) => Ok(true),
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(false),
        Err(error) => {
            Err(error).with_context(|| format!("checking retired store {}", path.display()))
        }
    }
}

/// The daemon reserves its endpoints before calling this recovery/loading stage.
pub(crate) async fn load(binding: &StorageBinding) -> Result<Loaded> {
    let gate = std::sync::Arc::new(tokio::sync::Mutex::new(()))
        .lock_owned()
        .await;
    transaction::recover(binding, &gate).await?;
    reject_retired(binding).await?;
    let text = match tokio::fs::read_to_string(&binding.settings).await {
        Ok(text) => text,
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => String::new(),
        Err(error) => return Err(error).context("reading root settings"),
    };
    let mut cfg = crate::config::settings::document::replace(&Config::default(), &text)
        .context("loading settings; inline workspace sections are no longer supported")?
        .candidate;
    let stores = Stores {
        projects: Store::load(binding).await?,
        agents: Store::load(binding).await?,
        hosts: Store::load(binding).await?,
        worktrees: Store::load(binding).await?,
    };
    cfg.projects = stores.projects.ordered();
    cfg.sessions = stores.agents.ordered();
    cfg.host_terminals = stores.hosts.ordered();
    let anchor = binding.config.join("config.toml");
    let revision = catalog::revision(&anchor).ok();
    cfg.library = Config::load_library_for(&anchor).await?;
    // Never accept a revision sampled after loading different catalog contents.
    let library_revision =
        revision.filter(|stamp| catalog::revision(&anchor).ok().as_ref() == Some(stamp));
    validate(&cfg, &stores.worktrees.ordered())?;
    if !tokio::fs::try_exists(&binding.settings).await? {
        crate::paths::create_atomic_async(
            &binding.settings,
            &toml::to_string_pretty(&cfg.settings)?,
            Some(0o600),
        )
        .await?;
    }
    Ok(Loaded {
        config: cfg,
        binding: binding.clone(),
        stores,
        library_revision,
    })
}

pub(crate) fn validate(cfg: &Config, worktrees: &[crate::worktrees::Worktree]) -> Result<()> {
    let mut names = std::collections::HashSet::new();
    for name in cfg
        .sessions
        .iter()
        .map(|s| &s.name)
        .chain(cfg.host_terminals.iter().map(|s| &s.name))
    {
        ensure!(names.insert(name), "duplicate agent/host-shell name {name}");
    }
    crate::config::validate_loaded(cfg)?;
    crate::session::validate_config(cfg)?;
    for worktree in worktrees {
        ensure!(
            cfg.projects.iter().any(|p| p.id == worktree.project_id),
            "worktree refers to unknown project identity"
        );
    }
    for session in &cfg.sessions {
        if !session.worktree.is_empty() && session.worktree != "main" {
            let project = cfg
                .project(&session.project)
                .context("agent refers to unknown project")?;
            ensure!(
                worktrees
                    .iter()
                    .any(|w| w.id == session.worktree && w.project_id == project.id),
                "agent refers to unknown or foreign worktree"
            );
        }
    }
    Ok(())
}

#[cfg(test)]
#[path = "layout_tests.rs"]
mod tests;
