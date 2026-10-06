//! Create workers owned by tasks.
//! A worker is a sandboxed session with explicit ownership metadata and a persistent mailbox task.
//! Do not infer ownership from its name.

use super::super::*;
use crate::session::manager::config::ConfigMutation;

use anyhow::anyhow;

#[derive(Debug, Clone)]
pub struct WorkerSpawn {
    pub task: crate::tasks::Task,
    pub session: String,
}

impl Manager {
    /// Create the mailbox record first. Then create and launch the child session.
    /// A failed launch preserves the task and, for persistent workers, the stopped session.
    /// These records support diagnosis and recovery after a restart.
    #[cfg(test)]
    pub async fn spawn_worker(
        self: &Arc<Self>,
        caller: String,
        project: String,
        template: String,
        body: String,
        durable: bool,
    ) -> Result<WorkerSpawn> {
        self.spawn_worker_worktree(caller, project, template, body, durable, String::new())
            .await
    }

    pub(crate) async fn spawn_worker_worktree(
        self: &Arc<Self>,
        caller: String,
        project: String,
        template: String,
        body: String,
        durable: bool,
        worktree: String,
    ) -> Result<WorkerSpawn> {
        self.session_operation(
            self.spawn_worker_inner(caller, project, template, body, durable, worktree),
        )
        .await
    }

    async fn spawn_worker_inner(
        self: &Arc<Self>,
        caller: String,
        project: String,
        template_name: String,
        body: String,
        durable: bool,
        worktree: String,
    ) -> Result<WorkerSpawn> {
        let _spawn = self.worker_spawn.lock().await;
        self.reload_if_changed().await;
        let caller = caller.trim().to_string();
        if caller.is_empty() {
            bail!("a worker needs a caller identity");
        }
        if caller != crate::tasks::HOST && !self.session_known(&caller).await {
            bail!("no such caller session: {caller}");
        }
        let project = self.worker_project(&caller, project.trim()).await?;
        let template = self
            .spawnable_worker_template(&caller, &project, template_name.trim())
            .await?;
        let cfg = self.config().await;
        let p = cfg
            .project(&project)
            .cloned()
            .ok_or_else(|| anyhow!("no such worker project: {project}"))?;

        self.resolve_worktree(&p, &worktree).await?;

        // The selected template supplies behavior.
        // The invoking session owns the child in the task mailbox and sidebar.
        // Do not copy caller configuration into the worker.
        let mut session = worker_session_from_template(
            &template,
            self.fresh_worker_name(&caller).await,
            &project,
            &caller,
        );
        session.worktree = worktree;
        // A worker must have access to the daemon. Worker metadata supplies its task API capability.
        // The selected template supplies tool and state configuration.
        if cfg.network_of(&session, &p) == NetworkMode::None {
            bail!(
                "Worker template {} disables networking. Task API access requires a network.",
                template.name
            );
        }
        if cfg.command_of(&session).trim().is_empty() {
            bail!(
                "worker template {} does not resolve to an executable command",
                template.name
            );
        }

        let task = self.tasks.create_owned(
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
        )?;
        self.spawn_task_summary_request(task.clone());
        session.task_id = task.id.clone();

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
                self.fail_worker_task(
                    &task.id,
                    format!("could not persist worker session: {error:#}"),
                );
                return Err(error);
            }
            self.sync_from_config().await;
        } else {
            let mut live = Live::new(session.clone(), TitleCapture::default());
            live.ephemeral = true;
            self.live.write().await.insert(session.name.clone(), live);
        }

        if let Err(error) = self.start(&session.name).await {
            self.fail_worker_task(&task.id, format!("worker failed to start: {error:#}"));
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

/// Validate the caller's permitted project context.
/// Root callers may select any registered project. Agents may create workers only in their own project.
/// Repeat the HTTP handler check here to protect direct manager calls and future transports.
impl Manager {
    pub(crate) async fn worker_project(&self, caller: &str, requested: &str) -> Result<String> {
        let requested = requested.trim();
        let cfg = self.config().await;
        let caller_project = if caller != crate::tasks::HOST {
            let session = self
                .session_cfg(caller)
                .await
                .ok_or_else(|| anyhow!("no such caller session: {caller}"))?;
            if self.is_host(caller).await {
                bail!("A host terminal cannot own a worker. Choose an agent caller.");
            }
            let project = session.project.trim();
            if project.is_empty() {
                bail!("caller {caller} has no project context");
            }
            Some(project.to_string())
        } else {
            None
        };
        let project = if requested.is_empty() {
            caller_project
                .clone()
                .ok_or_else(|| anyhow!("a worker needs a project context"))?
        } else {
            requested.to_string()
        };
        if cfg.project(&project).is_none() {
            bail!("no such worker project: {project}");
        }
        if let Some(caller_project) = caller_project
            && caller_project != project
        {
            bail!("caller {caller} may spawn workers only in project {caller_project}");
        }
        Ok(project.clone())
    }

    pub(crate) async fn worker_template_names(&self) -> std::collections::BTreeSet<String> {
        self.config().await.daemon.worker_templates.clone()
    }

    /// Return the definition from the current catalog. Worker policy must permit scoped agent callers.
    /// The host user may select any catalog template.
    /// Validate before creating tasks or sessions so a deleted template cannot leave mailbox records or private state.
    pub(crate) async fn spawnable_worker_template(
        &self,
        caller: &str,
        project: &str,
        requested: &str,
    ) -> Result<AgentTemplate> {
        if requested.is_empty() {
            bail!("a worker needs a template");
        }
        let cfg = self.config().await;
        if caller != crate::tasks::HOST {
            let session = self
                .session_cfg(caller)
                .await
                .ok_or_else(|| anyhow!("no such caller session: {caller}"))?;
            if session.project.trim() != project {
                bail!("Caller {caller} cannot use project {project}.");
            }
        }
        if caller != crate::tasks::HOST && !cfg.daemon.worker_templates.contains(requested) {
            bail!("Worker policy does not allow agent template {requested}.");
        }
        drop(cfg);
        self.agent_templates()
            .await
            .into_iter()
            .find(|template| template.name == requested)
            .ok_or_else(|| anyhow!("no such agent template: {requested}"))
    }
}

/// Create a session from the selected template with a new private identity and hierarchy metadata owned by the daemon.
/// Workers use the selected project's current mounts without inheriting caller settings.
/// Stop workers after their tasks exit. The operator must explicitly request a retry.
fn worker_session_from_template(
    template: &AgentTemplate,
    name: String,
    project: &str,
    owner_name: &str,
) -> SessionCfg {
    let mut session = template.instantiate(name, project.to_string());
    session.worker = true;
    session.parent = owner_name.to_string();
    session.task_id.clear();
    session.autostart = false;
    session.auto_resume = false;
    session.worker_token = None;
    session
}

#[cfg(test)]
#[path = "workers_tests.rs"]
mod tests;
