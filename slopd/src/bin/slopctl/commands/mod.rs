//! Remote execution requests and dispatch. Command modules may own reusable Clap
//! argument schemas; `cli` owns the command tree, help, and task ID resolution.

mod agent;
mod common;
mod config;
mod diagnostics;
mod task;
mod templates;
mod worker;
mod worktree;

use super::http::Endpoint;

#[cfg(test)]
pub(crate) use agent::run_agent_create;
pub(crate) use config::ConfigArgs;
pub(crate) use task::task_id_from_env;
pub(crate) use task::{InboxFilter, UpdateAction};
#[cfg(test)]
pub(crate) use task::{task_is_terminal, wait_for_task};
pub(crate) use worker::SpawnArgs;
#[cfg(test)]
pub(crate) use worker::run_spawn;
pub(crate) use worktree::WorktreeArgs;

#[derive(Debug, PartialEq, Eq)]
pub(crate) enum RemoteCommand {
    Config(config::ConfigArgs),
    Worktree(worktree::WorktreeArgs),
    Delegate {
        to: String,
        body: String,
    },
    Spawn(SpawnArgs),
    Templates {
        project: Option<String>,
    },
    TemplateShow {
        name: String,
        project: Option<String>,
    },
    AgentCreate {
        name: String,
        project: String,
        template: String,
        start: bool,
    },
    Inbox {
        filter: InboxFilter,
    },
    Task {
        id: String,
    },
    Wait {
        id: String,
    },
    Update {
        action: UpdateAction,
        id: String,
        note: Option<String>,
    },
    Remove {
        id: String,
    },
    Prune {
        all: bool,
    },
    Peers,
    Status,
    SandboxInspect {
        name: String,
    },
}

impl RemoteCommand {
    pub(crate) fn run(
        self,
        endpoint: &Endpoint,
        session: &str,
        json_output: bool,
    ) -> Result<(), String> {
        match self {
            Self::Config(args) => config::run(endpoint, session, json_output, args),
            Self::Worktree(args) => worktree::run(endpoint, session, json_output, args),
            Self::Delegate { to, body } => {
                task::run_delegate(endpoint, session, json_output, &to, &body)
            }
            Self::Spawn(args) => worker::run_spawn(endpoint, session, json_output, &args),
            Self::Templates { project } => {
                templates::run_templates(endpoint, session, json_output, project.as_deref())
            }
            Self::TemplateShow { name, project } => templates::run_template_show(
                endpoint,
                session,
                json_output,
                &name,
                project.as_deref(),
            ),
            Self::AgentCreate {
                name,
                project,
                template,
                start,
            } => agent::run_agent_create(
                endpoint,
                session,
                json_output,
                &name,
                &project,
                &template,
                start,
            ),
            Self::Inbox { filter } => task::run_inbox(endpoint, session, json_output, &filter),
            Self::Task { id } => task::run_task(endpoint, session, json_output, &id),
            Self::Wait { id } => task::run_wait(endpoint, session, json_output, &id),
            Self::Update { action, id, note } => task::run_update(
                endpoint,
                session,
                json_output,
                &action,
                &id,
                note.as_deref(),
            ),
            Self::Remove { id } => task::run_remove(endpoint, session, json_output, &id),
            Self::Prune { all } => task::run_prune(endpoint, session, json_output, all),
            Self::Peers => diagnostics::run_peers(endpoint, session, json_output),
            Self::Status => diagnostics::run_status(endpoint, session, json_output),
            Self::SandboxInspect { name } => {
                diagnostics::run_sandbox_inspect(endpoint, session, json_output, &name)
            }
        }
    }
}

#[cfg(test)]
pub(crate) use diagnostics::peer_names;
