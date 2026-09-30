use super::super::TASK_WAIT_INTERVAL;
use super::super::format::{emit, print_json, print_task};
use super::super::http::{Endpoint, request};
use super::common::{arg, only, option_value, rest, value_option};
use super::{Command, USAGE};
use crate::shared::protocol::{enums::task_status, routes};
use serde_json::{Value, json};
use std::thread;
use std::time::{Duration, Instant};

pub(crate) const TASK_ID_ENV: &str = "SLOPWORLD_TASK_ID";
pub(crate) const DELEGATE_USAGE: &str = "usage:
  slopctl task delegate AGENT TASK...

Send TASK to AGENT.
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

Manage delegated tasks. Omit ID when SLOPWORLD_TASK_ID is set.
For updates with an injected ID, all positional words are the note; use --id ID
to target another task. Use -- before a note beginning with a flag.
";
pub(crate) const TASK_LIST_USAGE: &str = "usage:
  slopctl task list [--all] [--sent] [--received] [--status STATUS]

List unfinished tasks that involve the current caller. The list shows the newest
tasks first. Done, failed, and canceled tasks stay hidden unless you use --all
or --status.

options:
  --all             include done, failed and canceled tasks
  --sent            show only tasks sent by you
  --received        show only tasks sent to you
  --status STATUS   show only tasks with this status
";
pub(crate) const TASK_SHOW_USAGE: &str = "usage:
  slopctl task show [ID]

Show one task by its exact ID. Omit ID when SLOPWORLD_TASK_ID is set.
";
pub(crate) const WAIT_USAGE: &str = "usage:
  slopctl task wait [ID]

Wait until one task reaches a terminal state. Then show the task. The command checks
task status internally. Do not use a status loop or a short timeout.
The command writes status changes and a 30-second heartbeat to stderr.
It writes the final result to stdout. Omit ID when SLOPWORLD_TASK_ID is set.
";
pub(crate) const ACCEPT_USAGE: &str = "usage:
  slopctl task accept [ID] [NOTE...]

Accept a queued task. Add an optional note. When SLOPWORLD_TASK_ID is set, omit ID; use --id ID to target another task.
";
pub(crate) const PROGRESS_USAGE: &str = "usage:
  slopctl task progress [ID] [NOTE...]

Set an accepted task to working. Add an optional note. When SLOPWORLD_TASK_ID is set, omit ID; use --id ID to target another task.
";
pub(crate) const FINISH_USAGE: &str = "usage:
  slopctl task finish [ID] [RESULT...]

Mark a task as done. Add an optional result. When SLOPWORLD_TASK_ID is set, omit ID; use --id ID to target another task.
";
pub(crate) const FAIL_USAGE: &str = "usage:
  slopctl task fail [ID] [ERROR...]

Mark a task as failed. Add an optional reason. When SLOPWORLD_TASK_ID is set, omit ID; use --id ID to target another task.
";
pub(crate) const REMOVE_USAGE: &str = "usage:
  slopctl task remove [ID]

Remove one task that is done, failed, or canceled. When SLOPWORLD_TASK_ID is set, omit ID.
";
pub(crate) const PRUNE_USAGE: &str = "usage:
  slopctl task prune [--include-active]

Remove terminal tasks that you sent or received. Add --include-active to remove every task.
This option requires the root token.
";
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
pub(super) fn parse_task_command(
    args: &[String],
    task_id: Option<&str>,
) -> Result<Command, String> {
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
                filter: InboxFilter::parse(args.get(2..).unwrap_or_default())?,
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
        Some(subcommand) => Err(format!(
            "unknown task subcommand: {subcommand}\n\n{TASK_USAGE}"
        )),
        None => Err(format!("task needs a subcommand\n\n{TASK_USAGE}")),
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
    let mut note_at = 2;
    let (flag, inline) = args
        .get(2)
        .map(|arg| value_option(arg))
        .unwrap_or(("", None));
    let id = if flag == "--id" {
        let id = option_value(args, &mut note_at, flag, inline)?;
        note_at += 1;
        id
    } else if let Some(id) = task_id {
        id.to_string()
    } else {
        note_at = 3;
        task_id_arg(args, 2, &format!("{name} needs a task id"), None)?
    };
    // A delimiter also permits notes that begin with --id or a help flag.
    if args.get(note_at).is_some_and(|arg| arg == "--") {
        note_at += 1;
    }
    let note = (args.len() > note_at).then(|| args.get(note_at..).unwrap_or_default().join(" "));
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
        Some("--include-active") => true,
        Some(flag) => return Err(format!("unknown flag: {flag}\n\n{TASK_USAGE}")),
    };
    only(args, 3)?;
    Ok(Command::Prune { all })
}

pub(super) fn run_delegate(
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

pub(super) fn print_wait_hint(v: &Value, json: bool) {
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

pub(super) fn run_inbox(
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
        // Use stderr so an empty inbox produces no stdout content.
        eprintln!("no tasks");
    } else {
        tasks.iter().for_each(|t| print_task(t));
    }
    Ok(())
}

pub(super) fn run_task(
    endpoint: &Endpoint,
    session: &str,
    json: bool,
    id: &str,
) -> Result<(), String> {
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

pub(super) fn run_wait(
    endpoint: &Endpoint,
    session: &str,
    json: bool,
    id: &str,
) -> Result<(), String> {
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
        // Show progress during long waits without adding diagnostics to the final JSON.
        // Reuse the response instead of making a separate status request.
        let status = v
            .get("task")
            .and_then(|task| task.get("status"))
            .and_then(Value::as_str)
            .ok_or_else(|| "task response is missing status".to_string())?;
        if status != reported_status || reported_at.elapsed() >= Duration::from_secs(30) {
            eprintln!(
                "Waiting for task {id}: {status} ({} s elapsed). The wait is active. Do not poll status separately.",
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

pub(super) fn run_update(
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

pub(super) fn run_remove(
    endpoint: &Endpoint,
    session: &str,
    json: bool,
    id: &str,
) -> Result<(), String> {
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

pub(super) fn run_prune(
    endpoint: &Endpoint,
    session: &str,
    json: bool,
    all: bool,
) -> Result<(), String> {
    let path = if all {
        format!("{}?all=true", routes::TASKS)
    } else {
        routes::TASKS.to_string()
    };
    let v = request(endpoint, session, "DELETE", &path, None)?;
    if json {
        print_json(&v);
    } else {
        println!(
            "removed {}",
            v.get("removed").and_then(Value::as_u64).unwrap_or(0)
        );
    }
    Ok(())
}

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
                    f.status = Some(s.clone());
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
        // Include both directions when neither flag is set or both flags are set.
        if self.sent != self.received && ((self.sent && !from_me) || (self.received && !to_me)) {
            return false;
        }
        match &self.status {
            Some(want) => status == want,
            // Omit terminal tasks by default. They remain available by ID.
            None => self.all || !matches!(status, "done" | "failed" | "canceled"),
        }
    }

    /// Filter tasks and sort them from newest to oldest.
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

pub(super) fn task_id_from_env() -> Option<String> {
    std::env::var(TASK_ID_ENV)
        .ok()
        .filter(|id| !id.trim().is_empty())
}

pub(super) fn task_id_arg(
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
