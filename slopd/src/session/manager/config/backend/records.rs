//! Fixture-selected session record backend. No production format toggle exists.
//! Selection loads and validates once; ordinary edits use accepted indexes only.

use super::*;
use crate::storage::{
    sessions::{Definition, Kind, Prepared, Store},
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
    agents: Vec<Prepared>,
    hosts: Vec<Prepared>,
    projects: Option<workspace::Prepared<ProjectCfg>>,
    worktrees: Option<workspace::Prepared<crate::worktrees::Worktree>>,
}

impl Records {
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
            agents: Vec::new(),
            hosts: Vec::new(),
            projects: None,
            worktrees: None,
        };
        match mutation {
            ConfigMutation::Agents | ConfigMutation::ProjectReferences => {
                plan.agents = prepare_selected(
                    &stores.agents,
                    next.sessions
                        .iter()
                        .cloned()
                        .map(|row| Definition::Agent(Box::new(row)))
                        .collect(),
                )?;
            }
            ConfigMutation::HostShells | ConfigMutation::Projects => {}
            _ => bail!("record backend does not yet support this mutation owner"),
        }
        if matches!(
            mutation,
            ConfigMutation::HostShells | ConfigMutation::ProjectReferences
        ) {
            plan.hosts = prepare_selected(
                &stores.hosts,
                next.host_terminals
                    .iter()
                    .cloned()
                    .map(Definition::HostShell)
                    .collect(),
            )?;
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
            .map(|edit| edit.change.clone())
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
        for edit in self.agents {
            stores.agents.publish(edit);
        }
        for edit in self.hosts {
            stores.hosts.publish(edit);
        }
        if let Some(plan) = self.projects {
            stores.projects.publish(plan);
        }
        if let Some(plan) = self.worktrees {
            stores.worktrees.publish(plan);
        }
    }
}

/// Only the declared collection participates. Preview index changes privately so
/// names retired by this plan can be reused and each creation gets its own order.
fn prepare_selected(store: &Store, values: Vec<Definition>) -> Result<Vec<Prepared>> {
    let mut preview = store.clone();
    let mut edits = Vec::new();
    let ids: std::collections::HashSet<_> = values.iter().map(Definition::id).collect();
    anyhow::ensure!(ids.len() == values.len(), "duplicate session identity");
    for old in store.ordered() {
        if !ids.contains(old.id()) {
            let edit = preview
                .retire(old.id())
                .context("accepted session record disappeared")?;
            preview.publish(edit.clone());
            edits.push(edit);
        }
    }
    for value in &values {
        if let Some(old) = preview.get(value.id()) {
            if serialized(old)? == serialized(value)? {
                continue;
            }
            let edit = preview.update(value.id(), value.clone())?;
            preview.publish(edit.clone());
            edits.push(edit);
        } else {
            let edit = preview.create(value.clone())?;
            preview.publish(edit.clone());
            edits.push(edit);
        }
    }
    anyhow::ensure!(
        preview
            .ordered()
            .iter()
            .map(Definition::id)
            .eq(values.iter().map(Definition::id)),
        "record edits cannot reorder retained sessions"
    );
    Ok(edits)
}

fn serialized(value: &Definition) -> Result<toml::Value> {
    Ok(match value {
        Definition::Agent(row) => toml::Value::try_from(row)?,
        Definition::HostShell(row) => toml::Value::try_from(row)?,
    })
}

impl Manager {
    pub(in crate::session::manager) fn record_backend(&self) -> Option<Arc<Records>> {
        self.config_state
            .records
            .lock()
            .unwrap_or_else(|error| error.into_inner())
            .clone()
    }

    /// Fixtures seed records explicitly. This is not a migration or runtime toggle.
    pub(in crate::session::manager::config) async fn select_record_fixture(
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
        records: &Records,
    ) -> bool {
        let _gate = self.config_state.persist.lock().await;
        let anchor = records.binding.config.join("config.toml");
        let stamp = Config::library_stamp_for(&anchor);
        if *self
            .config_state
            .library_mtime
            .lock()
            .unwrap_or_else(|error| error.into_inner())
            == stamp
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
        self.publish_config(change).await;
        *self
            .config_state
            .library_mtime
            .lock()
            .unwrap_or_else(|error| error.into_inner()) = stamp;
        drop(_gate);
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
