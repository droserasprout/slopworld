//! Startup assembly of independently owned stores. No legacy conversion or live polling.
use super::{
    sessions::{Definition, Kind, Store},
    target::{StorageBinding, Target},
    workspace,
};
use crate::config::Config;
use anyhow::{Context, Result, ensure};

pub(crate) async fn reject_legacy(binding: &StorageBinding) -> Result<()> {
    ensure!(
        !tokio::fs::try_exists(crate::config::Config::recovery_path_for(&binding.settings)).await?,
        "legacy config recovery journal remains; stop slopd and run just migrate-storage"
    );
    for name in [
        "worktrees.toml",
        "tasks.toml",
        "tasks.journal",
        "grants.toml",
    ] {
        let path = Target::Legacy(name.into()).resolve(binding)?;
        if name == "grants.toml" && path == binding.data.join(name) {
            continue;
        }
        ensure!(
            !tokio::fs::try_exists(&path).await?,
            "legacy store {} remains; stop slopd and run just migrate-storage",
            path.display()
        );
    }
    Ok(())
}

pub(crate) async fn load(binding: &StorageBinding) -> Result<Config> {
    reject_legacy(binding).await?;
    let text = match tokio::fs::read_to_string(&binding.settings).await {
        Ok(text) => text,
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => String::new(),
        Err(error) => return Err(error).context("reading root settings"),
    };
    let mut cfg = crate::config::settings::document::replace(&Config::default(), &text)
        .context("loading settings; legacy inline workspace requires just migrate-storage")?
        .candidate;
    cfg.projects = workspace::Store::<crate::config::ProjectCfg>::load(binding)
        .await?
        .ordered();
    cfg.sessions = Store::load(binding, Kind::Agent)
        .await?
        .ordered()
        .into_iter()
        .filter_map(|row| match row {
            Definition::Agent(row) => Some(*row),
            _ => None,
        })
        .collect();
    cfg.host_terminals = Store::load(binding, Kind::HostShell)
        .await?
        .ordered()
        .into_iter()
        .filter_map(|row| match row {
            Definition::HostShell(row) => Some(row),
            _ => None,
        })
        .collect();
    cfg.library = Config::load_library_for(&binding.config.join("config.toml")).await?;
    validate(
        &cfg,
        &workspace::Store::<crate::worktrees::Worktree>::load(binding)
            .await?
            .ordered(),
    )?;
    Ok(cfg)
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
