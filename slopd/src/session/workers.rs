//! Worker session identity, guarded registration, and caller facts.
//! Application workers coordinates checkouts, tasks, startup and rollback.
use super::*;
use crate::application::workers;

impl SessionRuntime {
    pub(crate) async fn worker_registration_guard(&self) -> tokio::sync::MutexGuard<'_, ()> {
        self.worker_spawn.lock().await
    }

    pub(crate) async fn allocate_worker_identity(
        &self,
        session: &mut SessionCfg,
        caller: &str,
    ) -> Result<()> {
        session.name = self.fresh_worker_name(caller).await;
        session.state_id = self.allocate_agent_identity().await?;
        Ok(())
    }

    /// Install a temporary worker while application retains the worker spawn guard.
    pub(crate) async fn register_temporary_worker(&self, session: &SessionCfg) {
        let mut live = Live::new(session.clone(), TitleCapture::default());
        live.role = SessionRole::Agent(AgentLifetime::Temporary);
        self.live.write().await.insert(session.name.clone(), live);
    }

    pub(crate) async fn set_temporary_worker_access(&self, session: &SessionCfg) -> Result<()> {
        self.tmux.set_worker_config(&session.name, session).await?;
        if let Some(live) = self.live.write().await.get_mut(&session.name) {
            anyhow::ensure!(
                live.cfg.state_id == session.state_id,
                "Worker identity changed"
            );
            live.cfg.parent_access = session.parent_access;
            live.cfg.workers = session.workers.clone();
        }
        Ok(())
    }

    async fn fresh_worker_name(&self, owner: &str) -> String {
        let base = format!("{owner}-worker");
        let cfg = self.config().await;
        let live = self.live.read().await;
        if !live.contains_key(&base)
            && cfg.session(&base).is_none()
            && !cfg.host_terminals.iter().any(|tab| tab.name == base)
        {
            return base;
        }
        (2..)
            .map(|n| format!("{base}-{n}"))
            .find(|name| {
                !live.contains_key(name)
                    && cfg.session(name).is_none()
                    && !cfg.host_terminals.iter().any(|tab| tab.name == *name)
            })
            .unwrap_or_else(|| format!("{base}-{}", uuid::Uuid::new_v4()))
    }
}

impl SessionRuntime {
    /// Capture runtime facts while the caller retains its session operation lease.
    pub(crate) async fn worker_caller(&self, caller: &str) -> workers::Caller {
        if caller == crate::tasks::HOST {
            workers::Caller::Root
        } else if self.is_host(caller).await {
            workers::Caller::HostTerminal
        } else {
            match self.session_cfg(caller).await {
                Some(session) => workers::Caller::Agent {
                    project: session.project,
                    identity: session.state_id,
                    policy: session.workers,
                },
                None => workers::Caller::Missing,
            }
        }
    }
}

#[cfg(test)]
#[path = "workers_tests.rs"]
mod tests;
