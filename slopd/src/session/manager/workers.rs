//! Guarded worker checkout allocation, task/session commit, and failure cleanup.
//! Application workers owns preflight policy and template construction.
//! A worker is a sandboxed session with explicit ownership metadata and a persistent mailbox task.
//! Do not infer ownership from its name.

use super::super::*;
use crate::session::manager::config::ConfigMutation;

use crate::application::workers::{self, WorkerSpawn};
pub(crate) use crate::application::workers::{WorkerCheckout, WorkerRequest};
#[cfg(test)]
use workers::worker_session_from_template;

/// A newly allocated checkout belongs to the request until worker startup commits it.
/// On cancellation, a detached cleanup task removes it unless a failed durable session
/// still references it. Existing checkouts never enter this guard.
struct NewWorktree {
    manager: Arc<Manager>,
    project: String,
    id: Option<String>,
}

impl NewWorktree {
    async fn allocate(
        manager: Arc<Manager>,
        request: crate::session::WorktreeRequest,
    ) -> anyhow::Result<(String, Self)> {
        // Inherit the caller's boundary and reload context before spawning.
        // Keep allocation and its rollback owner together so cancellation drops the guard.
        manager
            .clone()
            .owned_session_operation(Box::pin(async move {
                let project = request.project.clone();
                let created = Box::pin(manager.create_worktree(request)).await?;
                Ok((
                    created.id.clone(),
                    Self {
                        manager,
                        project,
                        id: Some(created.id),
                    },
                ))
            }))
            .await
    }

    async fn rollback(&mut self) {
        let Some(id) = self.id.take() else { return };
        if let Err(error) = self.manager.remove_worktree(self.project.clone(), id).await {
            tracing::warn!("new worker worktree retained after failed startup: {error:#}");
        }
    }

    fn keep(&mut self) {
        self.id = None;
    }
}

impl Drop for NewWorktree {
    fn drop(&mut self) {
        let Some(id) = self.id.take() else { return };
        let manager = self.manager.clone();
        let project = self.project.clone();
        tokio::spawn(async move {
            if let Err(error) = manager.remove_worktree(project, id).await {
                tracing::warn!("new worker worktree retained after cancellation: {error:#}");
            }
        });
    }
}

impl Manager {
    #[cfg(test)]
    pub async fn spawn_worker(
        self: &Arc<Self>,
        caller: String,
        project: String,
        template: String,
        body: String,
        durable: bool,
    ) -> Result<WorkerSpawn> {
        self.create_worker(WorkerRequest {
            caller,
            project,
            template,
            body,
            durable,
            checkout: WorkerCheckout::Existing(String::new()),
        })
        .await
    }

    /// Validate and allocate under one accepted configuration and identity boundary.
    /// Abandoned allocation rolls back; once startup begins its owned operation finishes
    /// commit or failure cleanup even if the requesting transport disappears.
    pub(crate) async fn create_worker(
        self: &Arc<Self>,
        request: WorkerRequest,
    ) -> Result<WorkerSpawn> {
        self.session_request(self.create_worker_inner(request))
            .await
    }

    async fn create_worker_inner(self: &Arc<Self>, request: WorkerRequest) -> Result<WorkerSpawn> {
        let cfg = self.config().await;
        let caller = request.caller.trim();
        let context = self.worker_caller(caller).await;
        let templates = self.application.templates.list().await;
        let workers::PreparedWorker {
            mut session,
            caller,
            body,
            durable,
            checkout,
        } = workers::prepare(&cfg, &context, &templates, request)?;
        let project = session.project.clone();
        let p = cfg
            .project(&project)
            .context("prepared worker project disappeared")?;
        let (worktree, mut allocated) = match checkout {
            WorkerCheckout::Existing(id) => {
                self.resolve_worktree(p, &id).await?;
                (id, None)
            }
            WorkerCheckout::New { base, name } => {
                let base = self.worktree_base(&caller, &project, &base).await?;
                let (id, owned) = NewWorktree::allocate(
                    self.clone(),
                    WorktreeRequest {
                        project,
                        name,
                        base,
                        path: String::new(),
                    },
                )
                .await?;
                (id, Some(owned))
            }
        };
        session.worktree = worktree;
        let manager = self.clone();
        let result = self
            .owned_session_operation(Box::pin(async move {
                manager
                    .commit_worker(session, caller, body, durable, cfg)
                    .await
            }))
            .await;
        if let Some(owned) = &mut allocated {
            if result.is_ok() {
                owned.keep();
            } else {
                owned.rollback().await;
            }
        }
        result
    }

    /// Persist the task before registering and starting the session. Keep failed durable
    /// sessions for diagnosis; their checkout reference prevents allocation rollback.
    async fn commit_worker(
        self: &Arc<Self>,
        mut session: SessionCfg,
        caller: String,
        body: String,
        durable: bool,
        cfg: Config,
    ) -> Result<WorkerSpawn> {
        let _spawn = self.worker_spawn.lock().await;
        session.name = self.fresh_worker_name(&caller).await;
        session.state_id = self.allocate_agent_identity().await?;
        let task = self
            .create_task_owned(
                self.task_participant(&caller).await?,
                crate::tasks::Participant {
                    name: session.name.clone(),
                    identity: session.state_id.clone(),
                },
                body,
                Some(crate::tasks::WorkerTask {
                    session: session.name.clone(),
                    parent: caller,
                    durable,
                }),
            )
            .await?;
        self.application
            .spawn_task_summary(self.config_authority.daemon_settings(), task.clone());
        session.task_id = task.id.clone();

        self.register_task_worker(&session, durable).await?;

        if let Err(error) = self.start(&session.name).await {
            self.fail_worker_task_checked(
                &task.id,
                &session.state_id,
                format!("worker failed to start: {error:#}"),
            )
            .await;
            if !durable {
                self.forget(&session.name).await;
            }
            return Err(error);
        }

        self.announce_sessions().await;
        self.queue_delivery(&session.name, cfg.daemon.instructions.worker_prompt.clone())
            .await;
        Ok(WorkerSpawn {
            task,
            session: session.name,
        })
    }

    /// Register the task-owned session before startup; record a failed task if
    /// durable configuration cannot commit, while the spawn guard remains held.
    async fn register_task_worker(
        self: &Arc<Self>,
        session: &SessionCfg,
        durable: bool,
    ) -> Result<()> {
        if durable {
            let persisted = self
                .update_cfg(ConfigMutation::Agents, |cfg| {
                    if cfg.session(&session.name).is_some()
                        || cfg
                            .host_terminals
                            .iter()
                            .any(|tab| tab.name == session.name)
                    {
                        bail!("worker name became unavailable: {}", session.name);
                    }
                    cfg.sessions.push(session.clone());
                    Ok(())
                })
                .await;
            if let Err(error) = persisted {
                self.fail_worker_task_checked(
                    &session.task_id,
                    &session.state_id,
                    format!("could not persist worker session: {error:#}"),
                )
                .await;
                return Err(error);
            }
            self.sync_from_config().await;
        } else {
            let mut live = Live::new(session.clone(), TitleCapture::default());
            live.role = SessionRole::Agent(AgentLifetime::Temporary);
            self.live.write().await.insert(session.name.clone(), live);
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

impl Manager {
    /// Capture runtime facts while the caller retains its session operation lease.
    async fn worker_caller(&self, caller: &str) -> workers::Caller {
        if caller == crate::tasks::HOST {
            workers::Caller::Root
        } else if self.is_host(caller).await {
            workers::Caller::HostTerminal
        } else {
            match self.session_cfg(caller).await {
                Some(session) => workers::Caller::Agent {
                    project: session.project,
                },
                None => workers::Caller::Missing,
            }
        }
    }

    pub(crate) async fn worker_project(&self, caller: &str, requested: &str) -> Result<String> {
        workers::project(
            &self.config().await,
            caller,
            &self.worker_caller(caller).await,
            requested,
        )
    }

    pub(crate) async fn worker_template_names(&self) -> std::collections::BTreeSet<String> {
        self.config().await.daemon.worker_templates.clone()
    }

    #[cfg(test)]
    pub(crate) async fn spawnable_worker_template(
        &self,
        caller: &str,
        project: &str,
        requested: &str,
    ) -> Result<AgentTemplate> {
        workers::template(
            &self.config().await,
            caller,
            &self.worker_caller(caller).await,
            project,
            requested,
            &self.application.templates.list().await,
        )
    }
}

#[cfg(test)]
#[path = "workers_tests.rs"]
mod tests;
