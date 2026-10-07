//! Startup assembly of independently owned stores. No legacy conversion or live polling.
use super::{
    sessions::{Definition, Kind, Store},
    target::StorageBinding,
    workspace,
};
use crate::config::Config;
use anyhow::{Context, Result, ensure};

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

pub(crate) async fn load(binding: &StorageBinding) -> Result<Config> {
    reject_retired(binding).await?;
    let text = match tokio::fs::read_to_string(&binding.settings).await {
        Ok(text) => text,
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => String::new(),
        Err(error) => return Err(error).context("reading root settings"),
    };
    let mut cfg = crate::config::settings::document::replace(&Config::default(), &text)
        .context("loading settings; inline workspace sections are no longer supported")?
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

#[cfg(test)]
#[path = "layout_tests.rs"]
mod tests;
