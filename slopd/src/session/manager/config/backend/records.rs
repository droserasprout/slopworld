//! Accepted record backend shared by startup and manager mutations.
//! Selection loads and validates once; ordinary edits use accepted indexes only.

use super::*;
use crate::storage::{layout::Stores, target::StorageBinding, transaction, workspace};

pub(in crate::session::manager) struct Records {
    pub(in crate::session::manager) binding: StorageBinding,
    stores: Mutex<Stores>,
}

pub(in crate::session::manager::config) struct PreparedRecords {
    owner: Arc<Records>,
    agents: Option<workspace::Prepared<SessionCfg>>,
    hosts: Option<workspace::Prepared<crate::config::HostTerminalCfg>>,
    projects: Option<workspace::Prepared<ProjectCfg>>,
    worktrees: Option<workspace::Prepared<crate::worktrees::Worktree>>,
}

impl Records {
    pub(in crate::session::manager) fn new(binding: StorageBinding, stores: Stores) -> Arc<Self> {
        Arc::new(Self {
            binding,
            stores: Mutex::new(stores),
        })
    }

    #[cfg(test)]
    pub(in crate::session::manager) fn fixture(cfg: &mut Config, path: &Path) -> Result<Arc<Self>> {
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
            agents: crate::config::fixtures::seed(&binding, cfg.sessions.clone())?,
            hosts: crate::config::fixtures::seed(&binding, cfg.host_terminals.clone())?,
            projects: crate::config::fixtures::seed(&binding, cfg.projects.clone())?,
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

    pub(in crate::session::manager::config) async fn recover(
        &self,
        gate: &OwnedMutexGuard<()>,
    ) -> Result<()> {
        transaction::recover(&self.binding, gate).await
    }

    pub(in crate::session::manager::config) fn prepare(
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
                plan.agents = Some(stores.agents.prepare(next.sessions.clone())?);
            }
            ConfigMutation::HostShells | ConfigMutation::Projects => {}
            _ => bail!("record backend does not yet support this mutation owner"),
        }
        if matches!(
            mutation,
            ConfigMutation::HostShells | ConfigMutation::ProjectReferences
        ) {
            plan.hosts = Some(stores.hosts.prepare(next.host_terminals.clone())?);
        }
        if matches!(
            mutation,
            ConfigMutation::Projects | ConfigMutation::ProjectReferences
        ) {
            plan.projects = Some(stores.projects.prepare(next.projects.clone())?);
        }
        Ok(plan)
    }
}

impl PreparedRecords {
    pub(in crate::session::manager::config) async fn commit(
        &self,
        gate: &OwnedMutexGuard<()>,
    ) -> Result<()> {
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
    pub(in crate::session::manager::config) fn attach_worktrees(
        &mut self,
        values: Vec<crate::worktrees::Worktree>,
    ) -> Result<()> {
        let stores = self
            .owner
            .stores
            .lock()
            .unwrap_or_else(|error| error.into_inner());
        self.worktrees = Some(stores.worktrees.prepare(values)?);
        Ok(())
    }
    pub(in crate::session::manager::config) fn publish(self) {
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

impl Manager {
    /// Workspace state is API-owned after load. Only independent catalogs retain
    /// supported live reload; a root or record edit cannot replace accepted state.
    pub(in crate::session::manager::config) async fn reload_record_libraries(
        self: &Arc<Self>,
    ) -> bool {
        let manager = self.clone();
        // Recovery writes files, so a canceled request must not release the
        // session or persistence guards while restoration is still in flight.
        self.owned_session_operation(async move { manager.reload_record_libraries_inner().await })
            .await
    }

    async fn reload_record_libraries_inner(self: &Arc<Self>) -> bool {
        let records = &self.config_state.records;
        let gate = self.config_state.persist.clone().lock_owned().await;
        // A failed rollback can leave valid-looking but uncommitted catalog files.
        // Recover before sampling revisions, including the unchanged fast path.
        if let Err(error) = records.recover(&gate).await {
            tracing::warn!(
                "workspace recovery failed; keeping accepted library catalog: {error:#}"
            );
            return false;
        }
        let anchor = records.binding.config.join("config.toml");
        let Ok(stamp) = crate::config::catalog::revision(&anchor) else {
            return false;
        };
        if self
            .config_state
            .library_revision
            .lock()
            .unwrap_or_else(|e| e.into_inner())
            .as_ref()
            == Some(&stamp)
        {
            return false;
        }
        let old = self.config().await;
        let result = async {
            let mut next = old.clone();
            next.library = Config::load_library_for(&anchor).await?;
            prepare_candidate(&old, next)
        }
        .await;
        let change = match result {
            Ok(change) => change,
            Err(error) => {
                tracing::warn!(
                    "library changed on disk but is invalid, keeping accepted catalog: {error:#}"
                );
                return false;
            }
        };
        if crate::config::catalog::revision(&anchor).ok().as_ref() != Some(&stamp) {
            return false;
        }
        self.publish_config(change).await;
        *self
            .config_state
            .library_revision
            .lock()
            .unwrap_or_else(|e| e.into_inner()) = Some(stamp);
        drop(gate);
        self.announce_library().await;
        true
    }
}

impl Records {
    pub(in crate::session::manager) fn worktrees(&self) -> crate::worktrees::Store {
        let stores = self
            .stores
            .lock()
            .unwrap_or_else(|error| error.into_inner());
        crate::worktrees::Store {
            worktrees: stores.worktrees.ordered(),
        }
    }
    pub(in crate::session::manager) async fn save_worktrees(
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
            .prepare(values)?;
        transaction::commit_in_operation(&self.binding, plan.changes.clone(), gate).await?;
        self.stores
            .lock()
            .unwrap_or_else(|error| error.into_inner())
            .worktrees
            .publish(plan);
        Ok(())
    }
}
