//! Worker request policy and preparation, without task, checkout, or session allocation.
//! Session adapters capture caller facts and retain guards through commit and rollback.
use crate::agent_templates::AgentTemplate;
use crate::config::{Config, NetworkMode, SessionCfg};
use anyhow::{Result, anyhow, bail};

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

#[cfg(test)]
#[path = "workers_tests.rs"]
mod tests;
