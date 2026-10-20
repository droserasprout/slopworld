//! Worker policy and checkout/task/session orchestration, including cancellation rollback.
//! Session owns caller facts, identity reservation, registration and lifecycle guards.
use super::worktrees::WorktreeRequest;
use crate::agent_templates::AgentTemplate;
use crate::application::worktrees::Worktrees;
use crate::config::transactions::ConfigMutation;
use crate::config::{Config, NetworkMode, SessionCfg};
use crate::session::SessionRuntime;
use anyhow::{Context, Result, anyhow, bail};
use std::sync::Arc;

#[derive(Debug, Clone)]
pub struct WorkerSpawn {
    pub task: crate::tasks::Task,
    pub session: String,
}

/// Checkout selection is exclusive by construction; HTTP decodes its request flags.
pub(crate) enum WorkerCheckout {
    Existing(String),
    New { base: String, name: String },
}

pub(crate) struct WorkerRequest {
    pub caller: String,
    pub project: String,
    pub template: String,
    pub body: String,
    pub durable: bool,
    pub checkout: WorkerCheckout,
}

/// Runtime facts captured by the session owner under its operation boundary.
pub(crate) enum Caller {
    Root,
    Agent { project: String },
    HostTerminal,
    Missing,
}

pub(crate) struct PreparedWorker {
    pub session: SessionCfg,
    pub caller: String,
    pub body: String,
    pub durable: bool,
    pub checkout: WorkerCheckout,
}

pub(crate) fn project(
    cfg: &Config,
    caller: &str,
    context: &Caller,
    requested: &str,
) -> Result<String> {
    let caller_project = match context {
        Caller::Root => None,
        Caller::Missing => bail!("no such caller session: {caller}"),
        Caller::HostTerminal => {
            bail!("A host terminal cannot own a worker. Choose an agent caller.")
        }
        Caller::Agent { project } => {
            let project = project.trim();
            if project.is_empty() {
                bail!("caller {caller} has no project context");
            }
            Some(project)
        }
    };
    let requested = requested.trim();
    let project = if requested.is_empty() {
        caller_project.ok_or_else(|| anyhow!("a worker needs a project context"))?
    } else {
        requested
    };
    if cfg.project(project).is_none() {
        bail!("no such worker project: {project}");
    }
    if let Some(own) = caller_project
        && own != project
    {
        bail!("caller {caller} may spawn workers only in project {own}");
    }
    Ok(project.to_owned())
}

pub(crate) fn template(
    cfg: &Config,
    caller: &str,
    context: &Caller,
    project: &str,
    requested: &str,
    templates: &[AgentTemplate],
) -> Result<AgentTemplate> {
    if requested.is_empty() {
        bail!("a worker needs a template");
    }
    match context {
        Caller::Root => {}
        Caller::Missing => bail!("no such caller session: {caller}"),
        Caller::HostTerminal => {
            bail!("A host terminal cannot own a worker. Choose an agent caller.")
        }
        Caller::Agent { project: own } => {
            if own.trim() != project {
                bail!("Caller {caller} cannot use project {project}.");
            }
            if !cfg.daemon.worker_templates.contains(requested) {
                bail!("Worker policy does not allow agent template {requested}.");
            }
        }
    }
    templates
        .iter()
        .find(|template| template.name == requested)
        .cloned()
        .ok_or_else(|| anyhow!("no such agent template: {requested}"))
}

pub(crate) fn prepare(
    cfg: &Config,
    context: &Caller,
    templates: &[AgentTemplate],
    request: WorkerRequest,
) -> Result<PreparedWorker> {
    let WorkerRequest {
        caller,
        project: requested,
        template: name,
        body,
        durable,
        checkout,
    } = request;
    if body.trim().is_empty() {
        bail!("Worker task text cannot be empty.");
    }
    let caller = caller.trim().to_owned();
    if caller.is_empty() {
        bail!("a worker needs a caller identity");
    }
    let project = project(cfg, &caller, context, &requested)?;
    let template = template(cfg, &caller, context, &project, name.trim(), templates)?;
    let session =
        worker_session_from_template(&template, "worker-validation".into(), &project, &caller);
    let p = cfg
        .project(&project)
        .ok_or_else(|| anyhow!("no such worker project: {project}"))?;
    // Workers must reach the task API; their template supplies execution settings.
    if cfg.network_of(&session, p) == NetworkMode::None {
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
    crate::runtime::validate_limits(&session.limits)?;
    Ok(PreparedWorker {
        session,
        caller,
        body,
        durable,
        checkout,
    })
}

/// Create a session from the selected template with a new private identity and hierarchy metadata owned by the daemon.
/// Workers use the selected project's current mounts without inheriting caller settings.
/// Stop workers after their tasks exit. The operator must explicitly request a retry.
pub(crate) fn worker_session_from_template(
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

/// A newly allocated checkout belongs to the request until worker startup commits it.
/// On cancellation, a detached cleanup task removes it unless a failed durable session
/// still references it. Existing checkouts never enter this guard.
struct NewWorktree {
    worktrees: Worktrees,
    project: String,
    id: Option<String>,
}

impl NewWorktree {
    async fn allocate(
        manager: Arc<SessionRuntime>,
        worktrees: Worktrees,
        request: WorktreeRequest,
    ) -> anyhow::Result<(String, Self)> {
        // Inherit the caller's boundary and reload context before spawning.
        // Keep allocation and its rollback owner together so cancellation drops the guard.
        manager
            .clone()
            .owned_session_operation(Box::pin(async move {
                let project = request.project.clone();
                let created = Box::pin(worktrees.create_worktree(request)).await?;
                Ok((
                    created.id.clone(),
                    Self {
                        worktrees,
                        project,
                        id: Some(created.id),
                    },
                ))
            }))
            .await
    }

    async fn rollback(&mut self) {
        let Some(id) = self.id.take() else { return };
        if let Err(error) = self
            .worktrees
            .remove_worktree(self.project.clone(), id)
            .await
        {
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
        let worktrees = self.worktrees.clone();
        let project = self.project.clone();
        tokio::spawn(async move {
            if let Err(error) = worktrees.remove_worktree(project, id).await {
                tracing::warn!("new worker worktree retained after cancellation: {error:#}");
            }
        });
    }
}

/// Worker operations retain the runtime boundary across allocation and owned startup.
#[derive(Clone)]
pub(crate) struct Workers {
    sessions: Arc<SessionRuntime>,
    workspace: Arc<super::Workspace>,
    config_authority: Arc<super::ConfigAuthority>,
    services: Arc<super::Services>,
}

impl Workers {
    fn configuration(&self) -> super::Configuration {
        super::Configuration::from_parts(
            self.sessions.clone(),
            self.workspace.clone(),
            self.config_authority.clone(),
            self.services.clone(),
        )
    }

    pub(crate) fn new(application: &super::Application) -> Self {
        Self {
            sessions: application.sessions.clone(),
            workspace: application.workspace.clone(),
            config_authority: application.config_authority.clone(),
            services: application.services.clone(),
        }
    }

    #[cfg(test)]
    pub(crate) async fn spawn_worker(
        &self,
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
    pub(crate) async fn create_worker(&self, request: WorkerRequest) -> Result<WorkerSpawn> {
        self.configuration()
            .session_request(self.create_worker_inner(request))
            .await
    }

    async fn create_worker_inner(&self, request: WorkerRequest) -> Result<WorkerSpawn> {
        let cfg = self.configuration().config().await;
        let caller = request.caller.trim();
        let context = self.sessions.worker_caller(caller).await;
        let templates = self.services.templates.list().await;
        let PreparedWorker {
            mut session,
            caller,
            body,
            durable,
            checkout,
        } = prepare(&cfg, &context, &templates, request)?;
        let project = session.project.clone();
        let p = cfg
            .project(&project)
            .context("prepared worker project disappeared")?;
        let (worktree, mut allocated) = match checkout {
            WorkerCheckout::Existing(id) => {
                self.sessions.resolve_worktree(p, &id).await?;
                (id, None)
            }
            WorkerCheckout::New { base, name } => {
                let base = Worktrees::from_parts(
                    self.sessions.clone(),
                    self.workspace.clone(),
                    self.config_authority.clone(),
                    self.services.clone(),
                )
                .worktree_base(&caller, &project, &base)
                .await?;
                let (id, owned) = NewWorktree::allocate(
                    self.sessions.clone(),
                    Worktrees::from_parts(
                        self.sessions.clone(),
                        self.workspace.clone(),
                        self.config_authority.clone(),
                        self.services.clone(),
                    ),
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
            .sessions
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
        &self,
        mut session: SessionCfg,
        caller: String,
        body: String,
        durable: bool,
        cfg: Config,
    ) -> Result<WorkerSpawn> {
        let sessions = &self.sessions;
        let _spawn = sessions.worker_registration_guard().await;
        sessions
            .allocate_worker_identity(&mut session, &caller)
            .await?;
        let task = sessions
            .create_task_owned(
                sessions.task_participant(&caller).await?,
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
        self.services
            .spawn_task_summary(self.config_authority.daemon_settings(), task.clone());
        session.task_id = task.id.clone();

        if let Err(error) = self.register_task_worker(&session, durable).await {
            sessions
                .record_worker_failure(
                    &session,
                    crate::session::WorkerFailureCause::RegistrationFailed(format!("{error:#}")),
                )
                .await;
            return Err(error);
        }

        if let Err(error) = sessions.start(&session.name).await {
            sessions
                .record_worker_failure(
                    &session,
                    crate::session::WorkerFailureCause::StartFailed(format!("{error:#}")),
                )
                .await;
            if !durable {
                sessions.forget(&session.name).await;
            }
            return Err(error);
        }

        sessions.announce_sessions().await;
        sessions
            .queue_delivery(&session.name, cfg.daemon.instructions.worker_prompt.clone())
            .await;
        Ok(WorkerSpawn {
            task,
            session: session.name,
        })
    }

    pub(crate) async fn worker_project(&self, caller: &str, requested: &str) -> Result<String> {
        project(
            &self.configuration().config().await,
            caller,
            &self.sessions.worker_caller(caller).await,
            requested,
        )
    }

    pub(crate) async fn worker_template_names(&self) -> std::collections::BTreeSet<String> {
        self.configuration()
            .config()
            .await
            .daemon
            .worker_templates
            .clone()
    }
}

#[cfg(test)]
#[path = "workers_tests.rs"]
pub(crate) mod tests;

impl Workers {
    /// Register the task-owned session while the application retains its spawn guard.
    /// The application records task failure if durable configuration cannot commit.
    async fn register_task_worker(&self, session: &SessionCfg, durable: bool) -> Result<()> {
        if durable {
            self.configuration()
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
                .await?;
            self.sessions.sync_from_config().await;
        } else {
            self.sessions.register_temporary_worker(session).await;
        }

        Ok(())
    }
}
