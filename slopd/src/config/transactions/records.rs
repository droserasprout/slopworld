//! Accepted record backend shared by startup and manager mutations.
//! Selection loads and validates once; ordinary edits use accepted indexes only.

use super::*;
use crate::config::{ProjectCfg, SessionCfg};
use crate::storage::{layout::Stores, target::StorageBinding, transaction, workspace};
#[cfg(test)]
use anyhow::Context;
#[cfg(test)]
use std::path::Path;
use tokio::sync::OwnedMutexGuard;

pub(crate) struct Records {
    pub(crate) binding: StorageBinding,
    stores: Mutex<Stores>,
}

pub(super) struct PreparedRecords {
    owner: Arc<Records>,
    agents: Option<workspace::Prepared<SessionCfg>>,
    hosts: Option<workspace::Prepared<crate::config::HostTerminalCfg>>,
    projects: Option<workspace::Prepared<ProjectCfg>>,
    worktrees: Option<workspace::Prepared<crate::worktrees::Worktree>>,
}

impl Records {
    pub(crate) fn new(binding: StorageBinding, stores: Stores) -> Arc<Self> {
        Arc::new(Self {
            binding,
            stores: Mutex::new(stores),
        })
    }

    #[cfg(test)]
    pub(crate) fn fixture(cfg: &mut Config, path: &Path) -> Result<Arc<Self>> {
        let root = path.parent().context("fixture root")?;
        for id in cfg
            .projects
            .iter_mut()
            .map(|row| &mut row.id)
            .chain(cfg.host_terminals.iter_mut().map(|row| &mut row.id))
        {
            if id.is_empty() {
                *id = crate::storage_id::draft_identity();
            }
        }
        let binding = StorageBinding::new(root, &root.join("data"), path)?;
        let stores = Stores {
            agents: crate::config::fixtures::seed(&binding, &cfg.sessions)?,
            hosts: crate::config::fixtures::seed(&binding, &cfg.host_terminals)?,
            projects: crate::config::fixtures::seed(&binding, &cfg.projects)?,
            worktrees: workspace::Store::empty(),
        };
        crate::paths::write_private_toml(path, &toml::to_string_pretty(&cfg.settings)?)?;
        for (path, text) in
            crate::config::catalog::prepare_library(&Config::library_dirs_for(path), &cfg.library)?
        {
            crate::paths::write_private_toml(&path, &text)?;
        }
        Ok(Self::new(binding, stores))
    }

    pub(super) async fn recover(&self, gate: &OwnedMutexGuard<()>) -> Result<()> {
        transaction::recover(&self.binding, gate).await
    }

    pub(super) fn prepare(
        self: &Arc<Self>,
        mutation: ConfigMutation,
        next: &Config,
    ) -> Result<PreparedRecords> {
        let stores = self
            .stores
            .lock()
            .unwrap_or_else(|error| error.into_inner());
        let mut plan = PreparedRecords {
            owner: self.clone(),
            agents: None,
            hosts: None,
            projects: None,
            worktrees: None,
        };
        match mutation {
            ConfigMutation::Agents | ConfigMutation::ProjectReferences => {
                plan.agents = Some(stores.agents.prepare(&next.sessions)?);
            }
            ConfigMutation::HostShells | ConfigMutation::Projects => {}
            _ => bail!("record backend does not yet support this mutation owner"),
        }
        if matches!(
            mutation,
            ConfigMutation::HostShells | ConfigMutation::ProjectReferences
        ) {
            plan.hosts = Some(stores.hosts.prepare(&next.host_terminals)?);
        }
        if matches!(
            mutation,
            ConfigMutation::Projects | ConfigMutation::ProjectReferences
        ) {
            plan.projects = Some(stores.projects.prepare(&next.projects)?);
        }
        Ok(plan)
    }
}

impl PreparedRecords {
    pub(super) async fn commit(&self, gate: &OwnedMutexGuard<()>) -> Result<()> {
        transaction::commit_in_operation(&self.owner.binding, self.changes(), gate).await
    }

    fn changes(&self) -> Vec<transaction::Change> {
        let mut changes = Vec::new();
        for changes_for_store in [
            self.agents.as_ref().map(|plan| &plan.changes),
            self.hosts.as_ref().map(|plan| &plan.changes),
            self.projects.as_ref().map(|plan| &plan.changes),
            self.worktrees.as_ref().map(|plan| &plan.changes),
        ]
        .into_iter()
        .flatten()
        {
            changes.extend(changes_for_store.iter().cloned());
        }
        changes
    }
    pub(super) fn attach_worktrees(&mut self, values: &[crate::worktrees::Worktree]) -> Result<()> {
        let stores = self
            .owner
            .stores
            .lock()
            .unwrap_or_else(|error| error.into_inner());
        self.worktrees = Some(stores.worktrees.prepare(values)?);
        Ok(())
    }
    pub(super) fn publish(self) {
        let mut stores = self
            .owner
            .stores
            .lock()
            .unwrap_or_else(|error| error.into_inner());
        if let Some(plan) = self.agents {
            stores.agents.publish(plan);
        }
        if let Some(plan) = self.hosts {
            stores.hosts.publish(plan);
        }
        if let Some(plan) = self.projects {
            stores.projects.publish(plan);
        }
        if let Some(plan) = self.worktrees {
            stores.worktrees.publish(plan);
        }
    }
}

impl Records {
    pub(crate) fn worktrees(&self) -> crate::worktrees::Store {
        let stores = self
            .stores
            .lock()
            .unwrap_or_else(|error| error.into_inner());
        crate::worktrees::Store {
            worktrees: stores.worktrees.ordered(),
        }
    }
    pub(super) async fn save_worktrees(
        &self,
        values: Vec<crate::worktrees::Worktree>,
        gate: &OwnedMutexGuard<()>,
    ) -> Result<()> {
        self.recover(gate).await?;
        let plan = self
            .stores
            .lock()
            .unwrap_or_else(|error| error.into_inner())
            .worktrees
            .prepare(&values)?;
        transaction::commit_in_operation(&self.binding, plan.changes.clone(), gate).await?;
        self.stores
            .lock()
            .unwrap_or_else(|error| error.into_inner())
            .worktrees
            .publish(plan);
        Ok(())
    }
}
