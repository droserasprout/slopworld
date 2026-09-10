use super::format::{emit, print_json, print_status, print_task};
use super::http::{request, status_value, Endpoint};
use super::logs::LOGS_USAGE;
use super::{HOST, TASK_WAIT_INTERVAL};
use crate::wire::{enums::task_status, routes};
use serde_json::{json, Value};
use std::thread;
use std::time::Duration;

pub(crate) const DELEGATE_USAGE: &str = "usage:
  slopctl delegate AGENT TASK...

send TASK to AGENT. The task body is the remainder of the command line.
";

pub(crate) const SPAWN_USAGE: &str = "usage:
  slopctl spawn [--durable] PARENT TASK...
  slopctl worker [--durable] PARENT TASK...

create a task-owned child worker by cloning PARENT. The worker receives the
task body and its exact task id. --durable keeps the child session after exit.
This command is available to the root caller only.
";

pub(crate) const INBOX_USAGE: &str = "usage:
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
  slopctl task ID

show one task by its exact id.
";

pub(crate) const WAIT_USAGE: &str = "usage:
  slopctl wait ID

block until one task reaches a terminal state, then show it. This command checks
the task internally; do not replace it with a status loop or a short timeout.
";

pub(crate) const ACCEPT_USAGE: &str = "usage:
  slopctl accept ID [NOTE...]

mark a queued task as accepted, optionally recording a note.
";

pub(crate) const PROGRESS_USAGE: &str = "usage:
  slopctl progress ID [NOTE...]

mark an accepted task as in progress, optionally recording a note.
";

pub(crate) const FINISH_USAGE: &str = "usage:
  slopctl finish ID [RESULT...]

mark a task as done, optionally recording its result.
";

pub(crate) const FAIL_USAGE: &str = "usage:
  slopctl fail ID [ERROR...]

mark a task as failed, optionally recording the reason.
";

pub(crate) const REMOVE_USAGE: &str = "usage:
  slopctl rm ID

remove one task that has stopped moving.
";

pub(crate) const PRUNE_USAGE: &str = "usage:
  slopctl prune [--all]

remove terminal tasks. --all removes every task and is root-only.
";

pub(crate) const PEERS_USAGE: &str = "usage:
  slopctl peers

list sessions visible to the current caller, including host.
";

pub(crate) const STATUS_USAGE: &str = "usage:
  slopctl status

show the current caller, endpoint, daemon reachability, and pending task counts.
";

pub(crate) const USAGE: &str = "slopctl - delegate work and inspect SlopWorld diagnostics

common delegation flow:
  slopctl delegate AGENT TASK...  # create a task and keep its ID
  slopctl wait ID                # block for its terminal result

wait performs the polling internally and has no short completion timeout. Do not
loop over task, inbox or status while waiting.

usage:
  slopctl delegate AGENT TASK...
  slopctl spawn [--durable] PARENT TASK...
  slopctl inbox [--all] [--sent] [--received] [--status STATUS]
  slopctl task ID
  slopctl wait ID
  slopctl accept ID [NOTE...]
  slopctl progress ID [NOTE...]
  slopctl finish ID [RESULT...]
  slopctl fail ID [ERROR...]
  slopctl rm ID
  slopctl prune [--all]
  slopctl peers
  slopctl status
  slopctl logs [game|daemon|all] [--lines N] [--follow]

inbox shows unfinished work in both directions, newest first; --all adds what is
done, failed and canceled. rm takes a terminal task, prune takes all of them. --json is
accepted anywhere and prints the answer as JSON instead of for a reader.

SLOPWORLD_SESSION identifies the caller, and defaults to `host` - the user at the
keyboard - which the daemon accepts only from the root token. SLOPD_ENDPOINT
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
        parent: String,
        durable: bool,
        body: String,
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
        "spawn" | "worker" => SPAWN_USAGE,
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
        "logs" => LOGS_USAGE,
        _ => return None,
    })
}

fn has_help(args: &[String]) -> bool {
    // Everything after a task recipient or id may be literal task text.
    matches!(
        args.first().map(String::as_str),
        Some("-h" | "--help" | "help")
    )
}

pub(crate) fn parse_command(args: &[String]) -> Result<Command, String> {
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
        "delegate" => Ok(Command::Delegate {
            to: arg(args, 1, "delegate needs the agent to send to")?.to_string(),
            body: rest(args, 2, "delegate needs a task body")?,
        }),
        "spawn" | "worker" => parse_spawn(args),
        "inbox" => Ok(Command::Inbox {
            filter: InboxFilter::parse(&args[1..])?,
        }),
        "task" => {
            let id = arg(args, 1, "task needs a task id")?.to_string();
            only(args, 2)?;
            Ok(Command::Task { id })
        }
        "wait" => {
            let id = arg(args, 1, "wait needs a task id")?.to_string();
            only(args, 2)?;
            Ok(Command::Wait { id })
        }
        "accept" | "progress" | "finish" | "fail" => {
            let id = arg(args, 1, &format!("{command} needs a task id"))?.to_string();
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
            let id = arg(args, 1, "rm needs a task id")?.to_string();
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
        command => Err(format!("unknown command: {command}\n\n{USAGE}")),
    }
}

fn parse_spawn(args: &[String]) -> Result<Command, String> {
    let mut durable = false;
    let mut i = 1;
    while i < args.len() {
        match args[i].as_str() {
            "--durable" => durable = true,
            flag if flag.starts_with('-') => {
                return Err(format!("unknown spawn option: {flag}\n\n{USAGE}"));
            }
            // The parent ends option parsing; the remainder is the task body.
            _ => break,
        }
        i += 1;
    }
    if args.len() < i + 2 {
        return Err(format!("spawn needs a parent and a task body\n\n{USAGE}"));
    }
    Ok(Command::Spawn {
        parent: args[i].clone(),
        durable,
        body: args[i + 1..].join(" "),
    })
}

impl Command {
    pub(crate) fn run(self, endpoint: &Endpoint, session: &str, json: bool) -> Result<(), String> {
        match self {
            Self::Help { .. } => unreachable!("help is handled before endpoint load"),
            Self::Logs { .. } => unreachable!("local commands are dispatched before endpoint load"),
            Self::Delegate { to, body } => run_delegate(endpoint, session, json, &to, &body),
            Self::Spawn {
                parent,
                durable,
                body,
            } => run_spawn(
                endpoint,
                session,
                json,
                SpawnArgs {
                    parent: &parent,
                    durable,
                    body: &body,
                },
            ),
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
    pub(crate) parent: &'a str,
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
            "parent": args.parent,
            "body": args.body,
            "durable": args.durable,
        })),
    )?;
    emit(&v, json);
    print_wait_hint(&v, json);
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
        println!("next     slopctl wait {id}");
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
