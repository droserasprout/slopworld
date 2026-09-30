mod agent;
mod common;
mod diagnostics;
mod task;
mod templates;
mod worker;
mod worktree;

use super::http::Endpoint;
use super::logs::{run_logs, LOGS_USAGE};

pub(crate) use task::{InboxFilter, UpdateAction};
pub(crate) use worker::{SpawnArgs, WorktreeChoice};

#[cfg(test)]
pub(crate) use agent::{run_agent_create, AGENT_CREATE_USAGE, AGENT_USAGE};
#[cfg(test)]
pub(crate) use diagnostics::{
    peer_names, PEERS_USAGE, SANDBOX_INSPECT_USAGE, SANDBOX_USAGE, STATUS_USAGE,
};
#[cfg(test)]
pub(crate) use task::{
    task_is_terminal, wait_for_task, TASK_LIST_USAGE, TASK_SHOW_USAGE, TASK_USAGE,
};
#[cfg(test)]
pub(crate) use templates::{TEMPLATES_USAGE, TEMPLATE_SHOW_USAGE, TEMPLATE_USAGE};
#[cfg(test)]
pub(crate) use worker::{run_spawn, SPAWN_USAGE, WORKER_USAGE};

pub(crate) const USAGE: &str = "slopctl - delegate work and inspect SlopWorld diagnostics

common delegation flow:
  slopctl task delegate AGENT TASK...  # create a task and keep its ID
  slopctl task wait ID                # block for its terminal result

The task wait command polls until the task reaches a terminal state. It has no
short timeout. Do not poll task, inbox, or status while it waits.

usage:
  slopctl task delegate AGENT TASK...
  slopctl task list [--all] [--sent] [--received] [--status STATUS]
  slopctl task show [ID]
  slopctl task wait [ID]
  slopctl task accept [ID] [NOTE...]
  slopctl task progress [ID] [NOTE...]
  slopctl task finish [ID] [RESULT...]
  slopctl task fail [ID] [ERROR...]
  slopctl task remove [ID]
  slopctl task prune [--include-active]
  slopctl worker spawn [--one-shot] [--worktree ID | --new-worktree] [--base REV] --project PROJECT --template TEMPLATE [--] TASK...
  slopctl agent create NAME --project PROJECT --template TEMPLATE [--start]
  slopctl template list [--project PROJECT]
  slopctl template show NAME [--project PROJECT]
  slopctl worktree list --project PROJECT
  slopctl worktree create --project PROJECT [--name NAME] [--base REV] [--path CHECKOUT]
  slopctl worktree remove ID --project PROJECT
  slopctl worktree rename ID --project PROJECT --name NAME
  slopctl sandbox inspect NAME
  slopctl peers
  slopctl status
  slopctl logs [game|daemon|all] [--lines N] [--follow]

Use --json anywhere to print JSON instead of human-readable output.

SLOPWORLD_SESSION identifies the caller. It defaults to `host`, the user at the
keyboard. The daemon accepts `host` only with the root token. When
SLOPWORLD_TASK_ID is set, omit IDs from task lifecycle commands.
SLOPD_ENDPOINT selects endpoint.toml. SLOPD_URL and SLOPD_TOKEN override it.
";

#[derive(Debug, PartialEq, Eq)]
pub(crate) enum Command {
    Worktree {
        action: String,
        project: String,
        name: String,
        base: String,
        path: String,
        id: String,
    },
    Help {
        usage: &'static str,
    },
    Logs {
        args: Vec<String>,
    },
    Delegate {
        to: String,
        body: String,
    },
    Spawn {
        project: String,
        template: String,
        durable: bool,
        worktree: WorktreeChoice,
        body: String,
    },
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

pub(crate) fn command_help(command: &str) -> Option<&'static str> {
    Some(match command {
        "worker" => worker::WORKER_USAGE,
        "worktree" => worktree::WORKTREE_USAGE,
        "template" => templates::TEMPLATE_USAGE,
        "agent" => agent::AGENT_USAGE,
        "task" => task::TASK_USAGE,
        "peers" => diagnostics::PEERS_USAGE,
        "status" => diagnostics::STATUS_USAGE,
        "sandbox" => diagnostics::SANDBOX_USAGE,
        "logs" => LOGS_USAGE,
        _ => return None,
    })
}

fn has_help(args: &[String]) -> bool {
    matches!(
        args.first().map(String::as_str),
        Some("-h" | "--help" | "help")
    )
}

pub(crate) fn parse_command(args: &[String]) -> Result<Command, String> {
    let task_id = task::task_id_from_env();
    parse_command_with_task_id(args, task_id.as_deref())
}

pub(crate) fn parse_command_with_task_id(
    args: &[String],
    task_id: Option<&str>,
) -> Result<Command, String> {
    let command = args
        .first()
        .map(String::as_str)
        .ok_or_else(|| format!("missing command\n\n{USAGE}"))?;
    let rest = args.get(1..).unwrap_or_default();
    if has_help(rest) {
        if let Some(usage) = command_help(command) {
            return Ok(Command::Help { usage });
        }
    }
    match command {
        "logs" => Ok(Command::Logs {
            args: rest.to_vec(),
        }),
        "worker" => worker::parse_worker_command(args),
        "worktree" => worktree::parse_worktree(args),
        "template" => templates::parse_template_command(args),
        "agent" => agent::parse_agent_command(args),
        "task" => task::parse_task_command(args, task_id),
        "peers" => {
            common::only(args, 1)?;
            Ok(Command::Peers)
        }
        "status" => {
            common::only(args, 1)?;
            Ok(Command::Status)
        }
        "sandbox" => diagnostics::parse_sandbox(args),
        command => Err(format!("unknown command: {command}\n\n{USAGE}")),
    }
}

impl Command {
    pub(crate) fn run(
        self,
        endpoint: &Endpoint,
        session: &str,
        json_output: bool,
    ) -> Result<(), String> {
        match self {
            Self::Worktree {
                action,
                project,
                name,
                base,
                path,
                id,
            } => worktree::run(
                endpoint,
                session,
                json_output,
                worktree::WorktreeArgs {
                    action,
                    project,
                    name,
                    base,
                    path,
                    id,
                },
            ),
            Self::Help { usage } => {
                print!("{usage}");
                Ok(())
            }
            Self::Logs { args } => run_logs(&args, json_output),
            Self::Delegate { to, body } => {
                task::run_delegate(endpoint, session, json_output, &to, &body)
            }
            Self::Spawn {
                project,
                template,
                durable,
                worktree,
                body,
            } => worker::run_spawn(
                endpoint,
                session,
                json_output,
                SpawnArgs {
                    project: &project,
                    template: &template,
                    durable,
                    worktree: &worktree,
                    body: &body,
                },
            ),
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
            Self::Inbox { filter } => task::run_inbox(endpoint, session, json_output, filter),
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
