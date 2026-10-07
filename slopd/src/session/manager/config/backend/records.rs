//! Accepted record backend shared by startup and manager mutations.
//! Selection loads and validates once; ordinary edits use accepted indexes only.

use super::*;
use crate::storage::{
    sessions::{Definition, Kind, Store},
    target::StorageBinding,
    transaction, workspace,
};

pub(in crate::session::manager) struct Records {
    pub(in crate::session::manager) binding: StorageBinding,
    stores: Mutex<Stores>,
}

struct Stores {
    agents: Store,
    hosts: Store,
    projects: workspace::Store<ProjectCfg>,
    worktrees: workspace::Store<crate::worktrees::Worktree>,
}

pub(in crate::session::manager::config) struct PreparedRecords {
    owner: Arc<Records>,
    agents: Option<workspace::Prepared<Definition>>,
    hosts: Option<workspace::Prepared<Definition>>,
    projects: Option<workspace::Prepared<ProjectCfg>>,
    worktrees: Option<workspace::Prepared<crate::worktrees::Worktree>>,
}

impl Records {
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
        let mut agents = Store::empty(Kind::Agent);
        let mut hosts = Store::empty(Kind::HostShell);
        let mut projects = workspace::Store::empty();
        let mut changes = Vec::new();
        for (store, values) in [
            (
                &mut agents,
                cfg.sessions
                    .iter()
                    .cloned()
                    .map(|row| Definition::Agent(Box::new(row)))
                    .collect::<Vec<_>>(),
            ),
            (
                &mut hosts,
                cfg.host_terminals
                    .iter()
                    .cloned()
                    .map(Definition::HostShell)
                    .collect(),
            ),
        ] {
            let plan = store.prepare(values)?;
            changes.extend(plan.changes.clone());
            store.publish_all(plan);
        }
        let plan = projects.prepare(cfg.projects.clone())?;
        changes.extend(plan.changes.clone());
        projects.publish(plan);
        for change in changes {
            let text = match change.mutation {
                transaction::Mutation::Create(text) | transaction::Mutation::Replace(text) => text,
                transaction::Mutation::Retire => bail!("initial fixture cannot retire records"),
            };
            crate::paths::write_private_toml(&change.target.resolve(&binding)?, &text)?;
        }
        crate::paths::write_private_toml(path, &toml::to_string_pretty(&cfg.settings)?)?;
        for (path, text) in
            crate::config::catalog::prepare_library(&Config::library_dirs_for(path), &cfg.library)?
        {
            crate::paths::write_private_toml(&path, &text)?;
        }
        Ok(Arc::new(Self {
            binding,
            stores: Mutex::new(Stores {
                agents,
                hosts,
                projects,
                worktrees: workspace::Store::empty(),
            }),
        }))
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
                plan.agents = Some(
                    stores.agents.prepare(
                        next.sessions
                            .iter()
                            .cloned()
                            .map(|row| Definition::Agent(Box::new(row)))
                            .collect(),
                    )?,
                );
            }
            ConfigMutation::HostShells | ConfigMutation::Projects => {}
            _ => bail!("record backend does not yet support this mutation owner"),
        }
        if matches!(
            mutation,
            ConfigMutation::HostShells | ConfigMutation::ProjectReferences
        ) {
            plan.hosts = Some(
                stores.hosts.prepare(
                    next.host_terminals
                        .iter()
                        .cloned()
                        .map(Definition::HostShell)
                        .collect(),
                )?,
            );
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
        let mut changes: Vec<_> = self
            .agents
            .iter()
            .chain(&self.hosts)
            .flat_map(|plan| plan.changes.clone())
            .collect();
        if let Some(plan) = &self.projects {
            changes.extend(plan.changes.clone());
        }
        if let Some(plan) = &self.worktrees {
            changes.extend(plan.changes.clone());
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
            stores.agents.publish_all(plan);
        }
        if let Some(plan) = self.hosts {
            stores.hosts.publish_all(plan);
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
    pub(in crate::session::manager) fn record_backend(&self) -> Option<Arc<Records>> {
        self.config_state
            .records
            .lock()
            .unwrap_or_else(|error| error.into_inner())
            .clone()
    }

    /// Load and validate the complete workspace before any runtime reconciliation.
    pub(in crate::session::manager) async fn load_record_backend(
        self: &Arc<Self>,
        binding: StorageBinding,
    ) -> Result<()> {
        self.session_operation(async {
            let gate = self.config_state.persist.clone().lock_owned().await;
            transaction::recover(&binding, &gate).await?;
            let agents = Store::load(&binding, Kind::Agent).await?;
            let hosts = Store::load(&binding, Kind::HostShell).await?;
            let projects = workspace::Store::<ProjectCfg>::load(&binding).await?;
            let worktrees = workspace::Store::<crate::worktrees::Worktree>::load(&binding).await?;
            let mut candidate = self.config().await;
            candidate.projects = projects.ordered();
            candidate.sessions = agents
                .ordered()
                .into_iter()
                .filter_map(|row| match row {
                    Definition::Agent(row) => Some(*row),
                    _ => None,
                })
                .collect();
            candidate.host_terminals = hosts
                .ordered()
                .into_iter()
                .filter_map(|row| match row {
                    Definition::HostShell(row) => Some(row),
                    _ => None,
                })
                .collect();
            let old = self.config().await;
            let snapshot = crate::worktrees::Store {
                worktrees: worktrees.ordered(),
            };
            let project_ids: std::collections::HashSet<_> =
                candidate.projects.iter().map(|p| p.id.as_str()).collect();
            anyhow::ensure!(
                snapshot
                    .worktrees
                    .iter()
                    .all(|w| project_ids.contains(w.project_id.as_str())),
                "worktree refers to an unknown project identity"
            );
            self.validate_worktree_snapshot(&old, &candidate, &snapshot)
                .await?;
            let change = prepare_candidate(&old, candidate)?;
            *self
                .config_state
                .records
                .lock()
                .unwrap_or_else(|error| error.into_inner()) = Some(Arc::new(Records {
                binding,
                stores: Mutex::new(Stores {
                    agents,
                    hosts,
                    projects,
                    worktrees,
                }),
            }));
            *self
                .config_state
                .library_revision
                .lock()
                .unwrap_or_else(|e| e.into_inner()) = crate::config::catalog::revision(
                &self
                    .record_backend()
                    .context("records missing")?
                    .binding
                    .config
                    .join("config.toml"),
            )
            .ok();
            self.publish_config(change).await;
            Ok(())
        })
        .await
    }
}

impl Manager {
    /// Workspace state is API-owned after load. Only independent catalogs retain
    /// supported live reload; a root or record edit cannot replace accepted state.
    pub(in crate::session::manager::config) async fn reload_record_libraries(
        self: &Arc<Self>,
        records: Arc<Records>,
    ) -> bool {
        let manager = self.clone();
        // Recovery writes files, so a canceled request must not release the
        // session or persistence guards while restoration is still in flight.
        self.owned_session_operation(async move {
            manager.reload_record_libraries_inner(&records).await
        })
        .await
    }

    async fn reload_record_libraries_inner(self: &Arc<Self>, records: &Records) -> bool {
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
