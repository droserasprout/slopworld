use super::format::{emit, print_json, print_status, print_task};
use super::http::{request, status_value, Endpoint};
use super::logs::LOGS_USAGE;
use super::{HOST, TASK_WAIT_INTERVAL};
use crate::shared::protocol::{enums::task_status, routes};
use serde_json::{json, Value};
use std::thread;
use std::time::{Duration, Instant};

pub(crate) const TASK_ID_ENV: &str = "SLOPWORLD_TASK_ID";

pub(crate) const DELEGATE_USAGE: &str = "usage:
  slopctl task delegate AGENT TASK...
  slopctl delegate AGENT TASK...

send TASK to AGENT. `delegate` is the short spelling; the task command is canonical.
";

pub(crate) const SPAWN_USAGE: &str = "usage:
  slopctl worker spawn [--durable] --project PROJECT --template TEMPLATE [--] TASK...
  slopctl spawn [--durable] --project PROJECT --template TEMPLATE [--] TASK...

create a task-owned worker from an agent template. Scoped callers may use only
templates enabled by worker policy. The worker receives the task body and its exact task id.
--durable keeps the child session after
exit. Options end before TASK; spawn is the short spelling.
Use -- before task text that starts with an option, such as --durable.

The project and template are required. The old parent/clone form is rejected;
choose the template explicitly so the daemon can enforce worker policy.
";

pub(crate) const WORKER_USAGE: &str = "usage:
  slopctl worker spawn [--durable] --project PROJECT --template TEMPLATE [--] TASK...

create a task-owned worker. The old bare `worker` spelling remains an alias for
`worker spawn` for compatibility.
";

pub(crate) const TEMPLATES_USAGE: &str = "usage:
  slopctl template list [--project PROJECT]
  slopctl templates [--project PROJECT]

list agent templates. Root callers see the complete catalog; agents see only
templates enabled for worker spawning in their project. --project selects the
worker project context for a root caller.
";

pub(crate) const TEMPLATE_USAGE: &str = "usage:
  slopctl template list [--project PROJECT]
  slopctl template show NAME [--project PROJECT]

discover and inspect agent templates. Use `template list` or `template show`.
";

pub(crate) const TEMPLATE_SHOW_USAGE: &str = "usage:
  slopctl template show NAME [--project PROJECT]

show one agent template. Agent callers may inspect only templates enabled for
worker spawning. --project selects the worker project context for a root caller.
";

pub(crate) const AGENT_USAGE: &str = "usage:
  slopctl agent create NAME --project PROJECT --template TEMPLATE [--start]

create and manage agents from the daemon's template catalog.
";

pub(crate) const AGENT_CREATE_USAGE: &str = "usage:
  slopctl agent create NAME --project PROJECT --template TEMPLATE [--start]

create an agent from a daemon catalog template. Creation does not start the
agent unless --start is supplied; the daemon returns the new identity.
";

pub(crate) const INBOX_USAGE: &str = "usage:
  slopctl task list [--all] [--sent] [--received] [--status STATUS]
  slopctl inbox [--all] [--sent] [--received] [--status STATUS]

list tasks involving the current caller, newest first. Finished, failed and canceled
tasks are hidden unless --all or --status is supplied.

options:
  --all             include finished, failed and canceled tasks
  --sent            show only tasks sent by you
  --received        show only tasks sent to you
  --status STATUS   show only tasks with this status
";

pub(crate) const TASK_USAGE: &str = "usage:
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

manage delegated tasks. The old `task ID` spelling remains an alias for
`task show ID`; explicit subcommands take precedence over that alias. When
SLOPWORLD_TASK_ID is set, ID may be omitted.
";

pub(crate) const TASK_LIST_USAGE: &str = "usage:
  slopctl task list [--all] [--sent] [--received] [--status STATUS]

list unfinished tasks involving the current caller, newest first. Finished,
failed and canceled tasks are hidden unless --all or --status is supplied.

options:
  --all             include finished, failed and canceled tasks
  --sent            show only tasks sent by you
  --received        show only tasks sent to you
  --status STATUS   show only tasks with this status
";

pub(crate) const TASK_SHOW_USAGE: &str = "usage:
  slopctl task show [ID]

show one task by its exact id. When SLOPWORLD_TASK_ID is set, ID may be omitted.
";

pub(crate) const WAIT_USAGE: &str = "usage:
  slopctl task wait [ID]
  slopctl wait [ID]

block until one task reaches a terminal state, then show it. `wait` is the short
spelling. This command checks
the task internally; do not replace it with a status loop or a short timeout.
Status changes and a 30-second heartbeat go to stderr; stdout holds the final result.
When SLOPWORLD_TASK_ID is set, ID may be omitted.
";

pub(crate) const ACCEPT_USAGE: &str = "usage:
  slopctl task accept [ID] [NOTE...]
  slopctl accept [ID] [NOTE...]

mark a queued task as accepted, optionally recording a note. When
SLOPWORLD_TASK_ID is set, ID may be omitted.
";

pub(crate) const PROGRESS_USAGE: &str = "usage:
  slopctl task progress [ID] [NOTE...]
  slopctl progress [ID] [NOTE...]

mark an accepted task as in progress, optionally recording a note. When
SLOPWORLD_TASK_ID is set, ID may be omitted.
";

pub(crate) const FINISH_USAGE: &str = "usage:
  slopctl task finish [ID] [RESULT...]
  slopctl finish [ID] [RESULT...]

mark a task as done, optionally recording its result. When SLOPWORLD_TASK_ID is
set, ID may be omitted.
";

pub(crate) const FAIL_USAGE: &str = "usage:
  slopctl task fail [ID] [ERROR...]
  slopctl fail [ID] [ERROR...]

mark a task as failed, optionally recording the reason. When
SLOPWORLD_TASK_ID is set, ID may be omitted.
";

pub(crate) const REMOVE_USAGE: &str = "usage:
  slopctl task remove [ID]
  slopctl rm [ID]

remove one task that has stopped moving. `rm` remains as a compatibility alias.
When SLOPWORLD_TASK_ID is set, ID may be omitted.
";

pub(crate) const PRUNE_USAGE: &str = "usage:
  slopctl task prune [--include-active]
  slopctl prune [--all]

remove terminal tasks. --include-active removes every task and is root-only;
legacy --all is an alias.
";

pub(crate) const PEERS_USAGE: &str = "usage:
  slopctl peers

list sessions visible to the current caller, including host.
";

pub(crate) const STATUS_USAGE: &str = "usage:
  slopctl status

show the current caller, endpoint, daemon reachability, and pending task counts.
";

pub(crate) const SANDBOX_USAGE: &str = "usage:
  slopctl sandbox inspect NAME

inspect a session's sanitized sandbox launch plan and live process tree.
";

pub(crate) const SANDBOX_INSPECT_USAGE: &str = "usage:
  slopctl sandbox inspect NAME

show the sanitized launch plan and, when available, the live process tree for NAME.
The saved plan remains available after a process exits or a daemon restart.
";

pub(crate) const USAGE: &str = "slopctl - delegate work and inspect SlopWorld diagnostics

common delegation flow:
  slopctl task delegate AGENT TASK...  # create a task and keep its ID
  slopctl task wait ID                # block for its terminal result

wait performs the polling internally and has no short completion timeout. Do not
loop over task, inbox or status while waiting.

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
  slopctl worker spawn [--durable] --project PROJECT --template TEMPLATE [--] TASK...
  slopctl agent create NAME --project PROJECT --template TEMPLATE [--start]
  slopctl template list [--project PROJECT]
  slopctl template show NAME [--project PROJECT]
  slopctl sandbox inspect NAME
  slopctl peers
  slopctl status
  slopctl logs [game|daemon|all] [--lines N] [--follow]

shortcuts:
  slopctl delegate AGENT TASK...
  slopctl spawn [--durable] --project PROJECT --template TEMPLATE [--] TASK...
  slopctl wait [ID]

`delegate`, `spawn`, and `wait` are documented short spellings. Existing root
task verbs, `inbox`, `templates`, `rm`, `task ID`, bare `worker` spawning, and
`prune --all` remain compatibility aliases. --json is accepted anywhere and
prints the answer as JSON instead of for a reader.

SLOPWORLD_SESSION identifies the caller, and defaults to `host` - the user at the
keyboard - which the daemon accepts only from the root token. When
SLOPWORLD_TASK_ID is set, task IDs may be omitted from task lifecycle commands.
SLOPD_ENDPOINT
selects endpoint.toml; SLOPD_URL and SLOPD_TOKEN override it.
";

#[derive(Debug, PartialEq, Eq)]
pub(crate) enum Command {
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

#[derive(Debug, PartialEq, Eq)]
pub(crate) enum UpdateAction {
    Accept,
    Progress,
    Finish,
    Fail,
}

impl UpdateAction {
    fn status(&self) -> &'static str {
        match self {
            Self::Accept => "accepted",
            Self::Progress => "working",
            Self::Finish => "done",
            Self::Fail => "failed",
        }
    }
}

pub(crate) fn command_help(command: &str) -> Option<&'static str> {
    Some(match command {
        "delegate" => DELEGATE_USAGE,
        "spawn" => SPAWN_USAGE,
        "worker" => WORKER_USAGE,
        "templates" => TEMPLATES_USAGE,
        "template" => TEMPLATE_USAGE,
        "agent" => AGENT_USAGE,
        "inbox" => INBOX_USAGE,
        "task" => TASK_USAGE,
        "wait" => WAIT_USAGE,
        "accept" => ACCEPT_USAGE,
        "progress" => PROGRESS_USAGE,
        "finish" => FINISH_USAGE,
        "fail" => FAIL_USAGE,
        "rm" => REMOVE_USAGE,
        "prune" => PRUNE_USAGE,
        "peers" => PEERS_USAGE,
        "status" => STATUS_USAGE,
        "sandbox" => SANDBOX_USAGE,
        "logs" => LOGS_USAGE,
        _ => return None,
    })
}

fn has_help(args: &[String]) -> bool {
    // Help is recognized immediately after a command name. Everything after a task recipient or
    // id may be literal task text or a note, including help words.
    matches!(
        args.first().map(String::as_str),
        Some("-h" | "--help" | "help")
    )
}

pub(crate) fn parse_command(args: &[String]) -> Result<Command, String> {
    let task_id = task_id_from_env();
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
    if has_help(&args[1..]) {
        if let Some(usage) = command_help(command) {
            return Ok(Command::Help { usage });
        }
    }
    match command {
        "logs" => Ok(Command::Logs {
            args: args[1..].to_vec(),
        }),
        "delegate" => parse_delegate(args, 1),
        "spawn" => parse_spawn(args, 1),
        "worker" => parse_worker_command(args),
        "templates" => parse_templates_command(args, 1),
        "template" => parse_template_command(args),
        "agent" => parse_agent_command(args),
        "inbox" => Ok(Command::Inbox {
            filter: InboxFilter::parse(&args[1..])?,
        }),
        "task" => parse_task_command(args, task_id),
        "wait" => {
            let id = task_id_arg(args, 1, "wait needs a task id", task_id)?;
            only(args, 2)?;
            Ok(Command::Wait { id })
        }
        "accept" | "progress" | "finish" | "fail" => {
            let id = task_id_arg(args, 1, &format!("{command} needs a task id"), task_id)?;
            let action = match command {
                "accept" => UpdateAction::Accept,
                "progress" => UpdateAction::Progress,
                "finish" => UpdateAction::Finish,
                _ => UpdateAction::Fail,
            };
            let note = (args.len() > 2).then(|| args[2..].join(" "));
            Ok(Command::Update { action, id, note })
        }
        "rm" => {
            let id = task_id_arg(args, 1, "rm needs a task id", task_id)?;
            only(args, 2)?;
            Ok(Command::Remove { id })
        }
        "prune" => {
            let all = match args.get(1).map(String::as_str) {
                None => false,
                Some("--all") => true,
                Some(flag) => return Err(format!("unknown flag: {flag}\n\n{USAGE}")),
            };
            only(args, 2)?;
            Ok(Command::Prune { all })
        }
        "peers" => {
            only(args, 1)?;
            Ok(Command::Peers)
        }
        "status" => {
            only(args, 1)?;
            Ok(Command::Status)
        }
        "sandbox" => parse_sandbox(args),
        command => Err(format!("unknown command: {command}\n\n{USAGE}")),
    }
}

fn parse_task_command(args: &[String], task_id: Option<&str>) -> Result<Command, String> {
    if matches!(
        args.get(1).map(String::as_str),
        Some("-h" | "--help" | "help")
    ) {
        return Ok(Command::Help { usage: TASK_USAGE });
    }

    match args.get(1).map(String::as_str) {
        Some("delegate") => {
            if matches!(
                args.get(2).map(String::as_str),
                Some("-h" | "--help" | "help")
            ) {
                return Ok(Command::Help {
                    usage: DELEGATE_USAGE,
                });
            }
            parse_delegate(args, 2)
        }
        Some("list") => {
            if matches!(
                args.get(2).map(String::as_str),
                Some("-h" | "--help" | "help")
            ) {
                return Ok(Command::Help {
                    usage: TASK_LIST_USAGE,
                });
            }
            Ok(Command::Inbox {
                filter: InboxFilter::parse(&args[2..])?,
            })
        }
        Some("show") => parse_task_show(args, task_id),
        Some("wait") => parse_task_wait(args, task_id),
        Some("accept") => parse_task_update(args, UpdateAction::Accept, task_id),
        Some("progress") => parse_task_update(args, UpdateAction::Progress, task_id),
        Some("finish") => parse_task_update(args, UpdateAction::Finish, task_id),
        Some("fail") => parse_task_update(args, UpdateAction::Fail, task_id),
        Some("remove") => parse_task_remove(args, task_id),
        Some("prune") => parse_task_prune(args),
        // Compatibility alias: `task ID` means `task show ID`. The explicit subcommand arms
        // above deliberately win when an id happens to be named `wait`, `list`, or another
        // canonical task verb.
        Some(_) => {
            let id = arg(args, 1, "task needs a task id")?.to_string();
            only(args, 2)?;
            Ok(Command::Task { id })
        }
        None => Err(format!(
            "task needs a subcommand or task id\n\n{TASK_USAGE}"
        )),
    }
}

fn parse_delegate(args: &[String], recipient_at: usize) -> Result<Command, String> {
    Ok(Command::Delegate {
        to: arg(args, recipient_at, "delegate needs the agent to send to")?.to_string(),
        body: rest(args, recipient_at + 1, "delegate needs a task body")?,
    })
}

fn parse_task_show(args: &[String], task_id: Option<&str>) -> Result<Command, String> {
    if matches!(
        args.get(2).map(String::as_str),
        Some("-h" | "--help" | "help")
    ) {
        return Ok(Command::Help {
            usage: TASK_SHOW_USAGE,
        });
    }
    let id = task_id_arg(args, 2, "task show needs a task id", task_id)?;
    only(args, 3)?;
    Ok(Command::Task { id })
}

fn parse_task_wait(args: &[String], task_id: Option<&str>) -> Result<Command, String> {
    if matches!(
        args.get(2).map(String::as_str),
        Some("-h" | "--help" | "help")
    ) {
        return Ok(Command::Help { usage: WAIT_USAGE });
    }
    let id = task_id_arg(args, 2, "task wait needs a task id", task_id)?;
    only(args, 3)?;
    Ok(Command::Wait { id })
}

fn parse_task_update(
    args: &[String],
    action: UpdateAction,
    task_id: Option<&str>,
) -> Result<Command, String> {
    let (name, usage) = match &action {
        UpdateAction::Accept => ("task accept", ACCEPT_USAGE),
        UpdateAction::Progress => ("task progress", PROGRESS_USAGE),
        UpdateAction::Finish => ("task finish", FINISH_USAGE),
        UpdateAction::Fail => ("task fail", FAIL_USAGE),
    };
    if matches!(
        args.get(2).map(String::as_str),
        Some("-h" | "--help" | "help")
    ) {
        return Ok(Command::Help { usage });
    }
    let id = task_id_arg(args, 2, &format!("{name} needs a task id"), task_id)?;
    let note = (args.len() > 3).then(|| args[3..].join(" "));
    Ok(Command::Update { action, id, note })
}

fn parse_task_remove(args: &[String], task_id: Option<&str>) -> Result<Command, String> {
    if matches!(
        args.get(2).map(String::as_str),
        Some("-h" | "--help" | "help")
    ) {
        return Ok(Command::Help {
            usage: REMOVE_USAGE,
        });
    }
    let id = task_id_arg(args, 2, "task remove needs a task id", task_id)?;
    only(args, 3)?;
    Ok(Command::Remove { id })
}

fn parse_task_prune(args: &[String]) -> Result<Command, String> {
    if matches!(
        args.get(2).map(String::as_str),
        Some("-h" | "--help" | "help")
    ) {
        return Ok(Command::Help { usage: PRUNE_USAGE });
    }
    let all = match args.get(2).map(String::as_str) {
        None => false,
        Some("--include-active" | "--all") => true,
        Some(flag) => return Err(format!("unknown flag: {flag}\n\n{TASK_USAGE}")),
    };
    only(args, 3)?;
    Ok(Command::Prune { all })
}

fn parse_worker_command(args: &[String]) -> Result<Command, String> {
    if matches!(
        args.get(1).map(String::as_str),
        Some("-h" | "--help" | "help")
    ) {
        return Ok(Command::Help {
            usage: WORKER_USAGE,
        });
    }
    if args.get(1).map(String::as_str) == Some("spawn") {
        if matches!(
            args.get(2).map(String::as_str),
            Some("-h" | "--help" | "help")
        ) {
            return Ok(Command::Help { usage: SPAWN_USAGE });
        }
        return parse_spawn(args, 2);
    }
    // Compatibility alias: before the command tree, `worker` was a bare spelling of spawn.
    parse_spawn(args, 1)
}

fn parse_sandbox(args: &[String]) -> Result<Command, String> {
    if matches!(
        args.get(1).map(String::as_str),
        Some("-h" | "--help" | "help")
    ) {
        return Ok(Command::Help {
            usage: SANDBOX_USAGE,
        });
    }
    if args.get(1).map(String::as_str) != Some("inspect") {
        return Err(format!(
            "sandbox needs the inspect subcommand\n\n{SANDBOX_USAGE}"
        ));
    }
    if matches!(
        args.get(2).map(String::as_str),
        Some("-h" | "--help" | "help")
    ) {
        return Ok(Command::Help {
            usage: SANDBOX_INSPECT_USAGE,
        });
    }
    let name = arg(args, 2, "sandbox inspect needs a session name")?.to_string();
    only(args, 3)?;
    Ok(Command::SandboxInspect { name })
}

fn parse_spawn(args: &[String], options_at: usize) -> Result<Command, String> {
    let mut durable = false;
    let mut project = None;
    let mut template = None;
    let mut i = options_at;
    while i < args.len() {
        match args[i].as_str() {
            "--" => {
                i += 1;
                break;
            }
            "--durable" => durable = true,
            "--project" => {
                i += 1;
                project = Some(
                    args.get(i)
                        .ok_or_else(|| format!("spawn --project needs a value\n\n{SPAWN_USAGE}"))?
                        .clone(),
                );
            }
            "--template" => {
                i += 1;
                template = Some(
                    args.get(i)
                        .ok_or_else(|| format!("spawn --template needs a value\n\n{SPAWN_USAGE}"))?
                        .clone(),
                );
            }
            // The first non-option is the task body. Everything after it is literal task text,
            // including words that look like flags. This keeps delegation parsing intact.
            _ => break,
        }
        i += 1;
    }
    let Some(project) = project else {
        return Err(format!(
            "spawn now requires --project PROJECT and --template TEMPLATE; the old parent/clone syntax is no longer supported\n\n{SPAWN_USAGE}"
        ));
    };
    let Some(template) = template else {
        return Err(format!(
            "spawn now requires --template TEMPLATE; workers are created from templates, not parent clones\n\n{SPAWN_USAGE}"
        ));
    };
    if args.len() <= i {
        return Err(format!("spawn needs a task body\n\n{SPAWN_USAGE}"));
    }
    Ok(Command::Spawn {
        project,
        template,
        durable,
        body: args[i..].join(" "),
    })
}

fn parse_template_command(args: &[String]) -> Result<Command, String> {
    if matches!(
        args.get(1).map(String::as_str),
        Some("-h" | "--help" | "help")
    ) {
        return Ok(Command::Help {
            usage: TEMPLATE_USAGE,
        });
    }
    if args.get(1).map(String::as_str) != Some("show") {
        if args.get(1).map(String::as_str) == Some("list") {
            if matches!(
                args.get(2).map(String::as_str),
                Some("-h" | "--help" | "help")
            ) {
                return Ok(Command::Help {
                    usage: TEMPLATES_USAGE,
                });
            }
            let project = optional_project(args, 2, TEMPLATES_USAGE)?;
            return Ok(Command::Templates { project });
        }
        return Err(format!(
            "template needs the list or show subcommand\n\n{TEMPLATE_USAGE}"
        ));
    }
    if matches!(
        args.get(2).map(String::as_str),
        Some("-h" | "--help" | "help")
    ) {
        return Ok(Command::Help {
            usage: TEMPLATE_SHOW_USAGE,
        });
    }
    let name = arg(args, 2, "template show needs a template name")?.to_string();
    let project = optional_project(args, 3, TEMPLATE_SHOW_USAGE)?;
    Ok(Command::TemplateShow { name, project })
}

fn parse_templates_command(args: &[String], project_at: usize) -> Result<Command, String> {
    if matches!(
        args.get(1).map(String::as_str),
        Some("-h" | "--help" | "help")
    ) {
        return Ok(Command::Help {
            usage: TEMPLATES_USAGE,
        });
    }
    let project = optional_project(args, project_at, TEMPLATES_USAGE)?;
    Ok(Command::Templates { project })
}

fn optional_project(
    args: &[String],
    at: usize,
    usage: &'static str,
) -> Result<Option<String>, String> {
    match args.get(at).map(String::as_str) {
        None => Ok(None),
        Some("--project") => {
            let project = args
                .get(at + 1)
                .ok_or_else(|| format!("--project needs a value\n\n{usage}"))?
                .clone();
            only(args, at + 2)?;
            Ok(Some(project))
        }
        Some(flag) => Err(format!("unexpected argument: {flag}\n\n{usage}")),
    }
}

fn parse_agent_command(args: &[String]) -> Result<Command, String> {
    if matches!(
        args.get(1).map(String::as_str),
        Some("-h" | "--help" | "help")
    ) {
        return Ok(Command::Help { usage: AGENT_USAGE });
    }
    if args.get(1).map(String::as_str) != Some("create") {
        return Err(format!(
            "agent needs the create subcommand\n\n{AGENT_USAGE}"
        ));
    }
    if matches!(
        args.get(2).map(String::as_str),
        Some("-h" | "--help" | "help")
    ) {
        return Ok(Command::Help {
            usage: AGENT_CREATE_USAGE,
        });
    }
    let name = arg(args, 2, "agent create needs a name")?.to_string();
    let mut project = None;
    let mut template = None;
    let mut start = false;
    let mut i = 3;
    while i < args.len() {
        match args[i].as_str() {
            "--project" => {
                i += 1;
                project = Some(
                    args.get(i)
                        .ok_or_else(|| {
                            format!("agent create --project needs a value\n\n{AGENT_CREATE_USAGE}")
                        })?
                        .clone(),
                );
            }
            "--template" => {
                i += 1;
                template = Some(
                    args.get(i)
                        .ok_or_else(|| {
                            format!("agent create --template needs a value\n\n{AGENT_CREATE_USAGE}")
                        })?
                        .clone(),
                );
            }
            "--start" => start = true,
            flag => {
                return Err(format!(
                    "unknown agent create option: {flag}\n\n{AGENT_CREATE_USAGE}"
                ))
            }
        }
        i += 1;
    }
    let project = project
        .ok_or_else(|| format!("agent create needs --project PROJECT\n\n{AGENT_CREATE_USAGE}"))?;
    let template = template
        .ok_or_else(|| format!("agent create needs --template TEMPLATE\n\n{AGENT_CREATE_USAGE}"))?;
    Ok(Command::AgentCreate {
        name,
        project,
        template,
        start,
    })
}

impl Command {
    pub(crate) fn run(self, endpoint: &Endpoint, session: &str, json: bool) -> Result<(), String> {
        match self {
            Self::Help { .. } => unreachable!("help is handled before endpoint load"),
            Self::Logs { .. } => unreachable!("local commands are dispatched before endpoint load"),
            Self::Delegate { to, body } => run_delegate(endpoint, session, json, &to, &body),
            Self::Spawn {
                project,
                template,
                durable,
                body,
            } => run_spawn(
                endpoint,
                session,
                json,
                SpawnArgs {
                    project: &project,
                    template: &template,
                    durable,
                    body: &body,
                },
            ),
            Self::Templates { project } => {
                run_templates(endpoint, session, json, project.as_deref())
            }
            Self::TemplateShow { name, project } => {
                run_template_show(endpoint, session, json, &name, project.as_deref())
            }
            Self::AgentCreate {
                name,
                project,
                template,
                start,
            } => run_agent_create(endpoint, session, json, &name, &project, &template, start),
            Self::Inbox { filter } => run_inbox(endpoint, session, json, filter),
            Self::Task { id } => run_task(endpoint, session, json, &id),
            Self::Wait { id } => run_wait(endpoint, session, json, &id),
            Self::Update { action, id, note } => {
                run_update(endpoint, session, json, &action, &id, note.as_deref())
            }
            Self::Remove { id } => run_remove(endpoint, session, json, &id),
            Self::Prune { all } => run_prune(endpoint, session, json, all),
            Self::Peers => run_peers(endpoint, session, json),
            Self::Status => run_status(endpoint, session, json),
            Self::SandboxInspect { name } => run_sandbox_inspect(endpoint, session, json, &name),
        }
    }
}

fn run_delegate(
    endpoint: &Endpoint,
    session: &str,
    json: bool,
    to: &str,
    body: &str,
) -> Result<(), String> {
    let v = request(
        endpoint,
        session,
        "POST",
        routes::TASKS,
        Some(json!({ "to": to, "body": body })),
    )?;
    emit(&v, json);
    print_wait_hint(&v, json);
    Ok(())
}

pub(crate) struct SpawnArgs<'a> {
    pub(crate) project: &'a str,
    pub(crate) template: &'a str,
    pub(crate) durable: bool,
    pub(crate) body: &'a str,
}

pub(crate) fn run_spawn(
    endpoint: &Endpoint,
    session: &str,
    json: bool,
    args: SpawnArgs<'_>,
) -> Result<(), String> {
    let v = request(
        endpoint,
        session,
        "POST",
        routes::WORKERS,
        Some(json!({
            "project": args.project,
            "template": args.template,
            "body": args.body,
            "durable": args.durable,
        })),
    )?;
    emit(&v, json);
    print_wait_hint(&v, json);
    Ok(())
}

fn template_catalog_path(session: &str, project: Option<&str>) -> String {
    let path = if session == HOST {
        routes::TEMPLATES.to_string()
    } else {
        routes::SPAWNABLE_TEMPLATES.to_string()
    };
    match project {
        Some(project) => format!("{path}?project={}", encode_component(project)),
        None => path,
    }
}

fn encode_component(value: &str) -> String {
    let mut encoded = String::with_capacity(value.len());
    for byte in value.bytes() {
        if byte.is_ascii_alphanumeric() || matches!(byte, b'-' | b'_' | b'.' | b'~') {
            encoded.push(byte as char);
        } else {
            encoded.push_str(&format!("%{byte:02X}"));
        }
    }
    encoded
}

fn run_templates(
    endpoint: &Endpoint,
    session: &str,
    json: bool,
    project: Option<&str>,
) -> Result<(), String> {
    let path = template_catalog_path(session, project);
    let v = request(endpoint, session, "GET", &path, None)?;
    if json {
        print_json(&v);
    } else {
        print_templates(&v);
    }
    Ok(())
}

fn run_template_show(
    endpoint: &Endpoint,
    session: &str,
    json: bool,
    name: &str,
    project: Option<&str>,
) -> Result<(), String> {
    let path = template_catalog_path(session, project);
    let v = request(endpoint, session, "GET", &path, None)?;
    let template = v
        .get("templates")
        .and_then(Value::as_array)
        .and_then(|templates| templates.iter().find(|template| template["name"] == name))
        .ok_or_else(|| format!("no accessible agent template: {name}"))?;
    if json {
        print_json(&json!({ "template": template }));
    } else {
        print_template_details(template);
    }
    Ok(())
}

fn print_templates(v: &Value) {
    let Some(templates) = v.get("templates").and_then(Value::as_array) else {
        println!("no templates");
        return;
    };
    if templates.is_empty() {
        println!("no templates");
        return;
    }
    for template in templates {
        print_template(template);
    }
}

fn print_template(template: &Value) {
    let name = template["name"].as_str().unwrap_or("?");
    let description = template["description"].as_str().unwrap_or("");
    if description.is_empty() {
        println!("{name}");
    } else {
        println!("{name}  -  {description}");
    }
}

fn print_template_details(template: &Value) {
    print_template(template);
    println!("version  {}", template["version"].as_u64().unwrap_or(0));
    let defaults = &template["defaults"];
    let command = defaults["command"]["name"].as_str().unwrap_or("");
    let cmd = defaults["cmd"].as_str().unwrap_or("");
    if !command.is_empty() {
        println!("command  {command}");
    }
    if !cmd.is_empty() {
        println!("cmd      {cmd}");
    }
    if let Some(sandbox) = defaults["sandbox"].as_array() {
        println!(
            "sandbox  {}",
            if sandbox.is_empty() {
                "(none)".into()
            } else {
                sandbox
                    .iter()
                    .filter_map(Value::as_str)
                    .collect::<Vec<_>>()
                    .join(", ")
            }
        );
    }
    println!(
        "network  {}",
        defaults["network"].as_str().unwrap_or("private")
    );
    println!(
        "autostart {}",
        defaults["autostart"].as_bool().unwrap_or(false)
    );
}

pub(crate) fn run_agent_create(
    endpoint: &Endpoint,
    session: &str,
    json_output: bool,
    name: &str,
    project: &str,
    template: &str,
    start: bool,
) -> Result<(), String> {
    let mut v = request(
        endpoint,
        session,
        "POST",
        &format!(
            "{}/{}/create",
            routes::TEMPLATES,
            encode_component(template)
        ),
        Some(json!({ "name": name, "project": project, "start": start })),
    )?;
    v["started"] = json!(start);
    if json_output {
        print_json(&v);
    } else {
        println!(
            "agent    {}\nproject  {}\ntemplate {}\nstarted  {}",
            name,
            project,
            template,
            if start { "yes" } else { "no" }
        );
    }
    Ok(())
}

fn print_wait_hint(v: &Value, json: bool) {
    if json {
        return;
    }
    if let Some(id) = v
        .get("task")
        .and_then(|task| task.get("id"))
        .and_then(Value::as_str)
    {
        println!("next     slopctl task wait {id}");
    }
}

fn run_inbox(
    endpoint: &Endpoint,
    session: &str,
    json: bool,
    filter: InboxFilter,
) -> Result<(), String> {
    let v = request(endpoint, session, "GET", routes::TASKS, None)?;
    let tasks = filter.apply(&v, session);
    if json {
        print_json(&Value::Array(tasks.into_iter().cloned().collect()));
    } else if tasks.is_empty() {
        // stderr, so an empty inbox stays an empty stdout for whatever is reading it.
        eprintln!("no tasks");
    } else {
        tasks.iter().for_each(|t| print_task(t));
    }
    Ok(())
}

fn run_task(endpoint: &Endpoint, session: &str, json: bool, id: &str) -> Result<(), String> {
    let v = request(
        endpoint,
        session,
        "GET",
        &format!("{}/{id}", routes::TASKS),
        None,
    )?;
    emit(&v, json);
    Ok(())
}

fn run_wait(endpoint: &Endpoint, session: &str, json: bool, id: &str) -> Result<(), String> {
    let v = wait_for_task(endpoint, session, id, TASK_WAIT_INTERVAL)?;
    emit(&v, json);
    Ok(())
}

pub(crate) fn wait_for_task(
    endpoint: &Endpoint,
    session: &str,
    id: &str,
    interval: Duration,
) -> Result<Value, String> {
    let started = Instant::now();
    let mut reported_status = String::new();
    let mut reported_at = started;
    loop {
        let v = request(
            endpoint,
            session,
            "GET",
            &format!("{}/{id}", routes::TASKS),
            None,
        )?;
        if task_is_terminal(&v)? {
            return Ok(v);
        }
        // Keep long waits visibly alive without mixing diagnostics into the final JSON.
        // Reuse the response we already fetched; observers need no separate status poll.
        let status = v["task"]["status"].as_str().unwrap();
        if status != reported_status || reported_at.elapsed() >= Duration::from_secs(30) {
            eprintln!(
                "waiting for task {id}: {status} ({}s elapsed); wait is active, no separate status polling needed",
                started.elapsed().as_secs()
            );
            reported_status = status.to_owned();
            reported_at = Instant::now();
        }
        thread::sleep(interval);
    }
}

pub(crate) fn task_is_terminal(v: &Value) -> Result<bool, String> {
    let task = v
        .get("task")
        .ok_or_else(|| "response is missing task".to_string())?;
    let status = task
        .get("status")
        .and_then(Value::as_str)
        .ok_or_else(|| "response task is missing status".to_string())?;
    match status {
        "queued" | "accepted" | "working" => Ok(false),
        task_status::DONE | task_status::FAILED | task_status::CANCELED => Ok(true),
        status => Err(format!("unknown task status: {status}")),
    }
}

fn run_update(
    endpoint: &Endpoint,
    session: &str,
    json: bool,
    action: &UpdateAction,
    id: &str,
    note: Option<&str>,
) -> Result<(), String> {
    let v = request(
        endpoint,
        session,
        "POST",
        &format!("{}/{id}", routes::TASKS),
        Some(json!({ "status": action.status(), "note": note })),
    )?;
    emit(&v, json);
    Ok(())
}

fn run_remove(endpoint: &Endpoint, session: &str, json: bool, id: &str) -> Result<(), String> {
    let v = request(
        endpoint,
        session,
        "DELETE",
        &format!("{}/{id}", routes::TASKS),
        None,
    )?;
    emit(&v, json);
    Ok(())
}

fn run_prune(endpoint: &Endpoint, session: &str, json: bool, all: bool) -> Result<(), String> {
    let path = if all {
        format!("{}?all=true", routes::TASKS)
    } else {
        routes::TASKS.to_string()
    };
    let v = request(endpoint, session, "DELETE", &path, None)?;
    if json {
        print_json(&v);
    } else {
        println!("removed {}", v["removed"].as_u64().unwrap_or(0));
    }
    Ok(())
}

fn run_peers(endpoint: &Endpoint, session: &str, json: bool) -> Result<(), String> {
    let v = request(endpoint, session, "GET", routes::SESSIONS, None)?;
    let names = peer_names(&v);
    if json {
        print_json(&json!(names));
    } else {
        for name in names {
            let mark = if name == session { " (you)" } else { "" };
            println!("{name}{mark}");
        }
    }
    Ok(())
}

fn run_status(endpoint: &Endpoint, session: &str, json: bool) -> Result<(), String> {
    let v = status_value(endpoint, session);
    if json {
        print_json(&v);
    } else {
        print_status(&v);
    }
    Ok(())
}

fn run_sandbox_inspect(
    endpoint: &Endpoint,
    session: &str,
    json: bool,
    name: &str,
) -> Result<(), String> {
    let v = request(
        endpoint,
        session,
        "GET",
        &format!("{}/{name}/sandbox", routes::SESSIONS),
        None,
    )?;
    if json {
        print_json(&v);
    } else {
        print_sandbox(&v);
    }
    Ok(())
}

fn print_sandbox(v: &Value) {
    println!("session   {}", v["session"].as_str().unwrap_or("?"));
    if v["host"].as_bool().unwrap_or(false) {
        println!("sandbox   not applicable (host terminal)");
        return;
    }
    if let Some(plan) = v.get("plan").filter(|plan| !plan.is_null()) {
        for section in [
            "limits",
            "pasta",
            "bwrap",
            "environment",
            "mounts",
            "command",
        ] {
            println!("{section}:");
            if let Some(args) = plan[section].as_array() {
                if args.is_empty() {
                    println!("  (none)");
                } else {
                    for arg in args {
                        println!("  {}", shell_quote(arg.as_str().unwrap_or("<arg>")));
                    }
                }
            }
        }
    } else {
        println!("plan      no saved sandbox launch");
    }
    let live = &v["live"];
    println!("live      {}", live["status"].as_str().unwrap_or("unknown"));
    if let Some(pid) = live["pane_pid"].as_u64() {
        println!("pane pid  {pid}");
    }
    println!(
        "compare   {}",
        v["comparison"].as_str().unwrap_or("unknown")
    );
    if let Some(processes) = live["processes"].as_array() {
        for process in processes {
            let pid = process["pid"].as_u64().unwrap_or(0);
            let ppid = process["ppid"].as_u64().unwrap_or(0);
            println!("process   {pid} (parent {ppid})");
            if let Some(argv) = process["argv"].as_array() {
                for arg in argv {
                    println!("  {}", shell_quote(arg.as_str().unwrap_or("<arg>")));
                }
            }
        }
    }
}

fn shell_quote(value: &str) -> String {
    if !value.is_empty()
        && value
            .bytes()
            .all(|byte| byte.is_ascii_alphanumeric() || b"_./:@%+=,-".contains(&byte))
    {
        return value.to_string();
    }
    format!("'{}'", value.replace('\'', "'\\''"))
}

/// What `inbox` was asked to leave out. The store hands back everything the caller is party to,
/// in the order it was written; the shaping is here, where the person reading it is.
#[derive(Debug, Default, PartialEq, Eq)]
pub(crate) struct InboxFilter {
    pub(crate) all: bool,
    pub(crate) sent: bool,
    pub(crate) received: bool,
    pub(crate) status: Option<String>,
}

impl InboxFilter {
    pub(crate) fn parse(args: &[String]) -> Result<Self, String> {
        let mut f = Self::default();
        let mut rest = args.iter();
        while let Some(arg) = rest.next() {
            match arg.as_str() {
                "--all" => f.all = true,
                "--sent" => f.sent = true,
                "--received" => f.received = true,
                "--status" => {
                    let s = rest.next().ok_or("--status needs a value")?;
                    f.status = Some(s.to_string());
                }
                flag => return Err(format!("unknown flag: {flag}\n\n{USAGE}")),
            }
        }
        Ok(f)
    }

    fn keeps(&self, task: &Value, me: &str) -> bool {
        let status = task["status"].as_str().unwrap_or("");
        let from_me = task["from"].as_str() == Some(me);
        let to_me = task["to"].as_str() == Some(me);
        // Neither direction flag, or both, means both - there is no third direction to ask for.
        if self.sent != self.received && ((self.sent && !from_me) || (self.received && !to_me)) {
            return false;
        }
        match &self.status {
            Some(want) => status == want,
            // Finished work is still readable by id; it just stops crowding the list.
            None => self.all || !matches!(status, "done" | "failed" | "canceled"),
        }
    }

    /// Newest first: what arrived while you were away is what the list is opened to find.
    pub(crate) fn apply<'a>(&self, v: &'a Value, me: &str) -> Vec<&'a Value> {
        let mut tasks: Vec<&Value> = v
            .get("tasks")
            .and_then(Value::as_array)
            .map(|t| t.iter().filter(|t| self.keeps(t, me)).collect())
            .unwrap_or_default();
        tasks.sort_by_key(|t| std::cmp::Reverse(t["created_ms"].as_u64().unwrap_or(0)));
        tasks
    }
}

pub(crate) fn peer_names(v: &Value) -> Vec<&str> {
    let mut names: Vec<&str> = v
        .get("sessions")
        .and_then(Value::as_array)
        .map(|s| s.iter().filter_map(|s| s["name"].as_str()).collect())
        .unwrap_or_default();
    // The host is a mailbox without being a session, so it is named here or it is never found.
    names.push(HOST);
    names.sort_unstable();
    names.dedup();
    names
}

fn arg<'a>(args: &'a [String], at: usize, missing: &str) -> Result<&'a str, String> {
    args.get(at)
        .map(String::as_str)
        .ok_or_else(|| missing.to_string())
}

fn task_id_from_env() -> Option<String> {
    std::env::var(TASK_ID_ENV)
        .ok()
        .filter(|id| !id.trim().is_empty())
}

fn task_id_arg(
    args: &[String],
    at: usize,
    missing: &str,
    task_id: Option<&str>,
) -> Result<String, String> {
    args.get(at)
        .cloned()
        .or_else(|| task_id.map(str::to_owned))
        .ok_or_else(|| missing.to_string())
}

fn rest(args: &[String], from: usize, missing: &str) -> Result<String, String> {
    if args.len() <= from {
        return Err(missing.to_string());
    }
    Ok(args[from..].join(" "))
}

fn only(args: &[String], at: usize) -> Result<(), String> {
    match args.get(at) {
        None => Ok(()),
        Some(extra) => Err(format!("unexpected argument: {extra}\n\n{USAGE}")),
    }
}
