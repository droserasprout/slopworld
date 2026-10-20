//! Workspace identity reservation against accepted records and retained filesystem entries.
//! Callers retain session/worktree mutation guards through publication and supply live references.
use crate::config::Config;
use crate::storage::target::StorageBinding;
use crate::worktrees::Store;
use anyhow::{Context, Result};
use std::collections::HashSet;
use std::path::{Path, PathBuf};

/// Identity reservation needs record locations, never configuration mutation access.
pub(crate) struct WorkspaceIdentities {
    agents: PathBuf,
    projects: PathBuf,
    worktrees: PathBuf,
}

impl WorkspaceIdentities {
    pub(super) fn new(binding: &StorageBinding) -> Self {
        Self {
            agents: binding.data.join("agents"),
            projects: binding.config.join("projects"),
            worktrees: binding.data.join("worktrees"),
        }
    }

    pub(crate) fn allocate_agent_identity(&self, occupied: HashSet<String>) -> Result<String> {
        Reservation {
            occupied,
            records: self.agents.clone(),
            retained: None,
        }
        .allocate()
    }

    pub(crate) fn allocate_project_identity(
        &self,
        cfg: &Config,
        worktrees: &Store,
    ) -> Result<String> {
        let mut occupied: HashSet<_> = cfg.projects.iter().map(|p| p.id.clone()).collect();
        occupied.extend(worktrees.worktrees.iter().map(|w| w.project_id.clone()));
        Reservation {
            occupied,
            records: self.projects.clone(),
            retained: Some(crate::sandbox::cache::root()),
        }
        .allocate()
    }

    pub(crate) fn allocate_worktree_identity(&self, occupied: HashSet<String>) -> Result<String> {
        Reservation {
            occupied,
            records: self.worktrees.clone(),
            retained: None,
        }
        .allocate()
    }
}

struct Reservation {
    occupied: HashSet<String>,
    records: PathBuf,
    retained: Option<PathBuf>,
}

impl Reservation {
    fn allocate(&self) -> Result<String> {
        crate::storage_id::allocate(|id| self.contains(id))
    }

    fn contains(&self, id: &str) -> Result<bool> {
        Ok(self.occupied.contains(id)
            || occupied_path(&self.records.join(format!("{id}.toml")))?
            || match &self.retained {
                Some(root) => occupied_path(&root.join(id))?,
                None => false,
            })
    }
}

fn occupied_path(path: &Path) -> Result<bool> {
    match std::fs::symlink_metadata(path) {
        Ok(_) => Ok(true),
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(false),
        Err(error) => Err(error).context("checking retained workspace identity"),
    }
}

#[cfg(test)]
#[path = "workspace_identity_tests.rs"]
mod tests;
