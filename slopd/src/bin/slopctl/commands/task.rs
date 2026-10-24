//! Task requests, inbox argument schema/filtering, and terminal-result waits.
//! `cli` owns task subcommands and task-ID resolution.

use super::super::TASK_WAIT_INTERVAL;
use super::super::format::{emit, print_json, print_task};
use super::super::http::{Endpoint, request};
use crate::shared::protocol::{enums::task_status, routes};
use serde_json::{Value, json};
use std::thread;
use std::time::{Duration, Instant};

pub(crate) const TASK_ID_ENV: &str = "SLOPWORLD_TASK_ID";
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
    filter: &InboxFilter,
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
    batch_succeeded(&v)
}

pub(crate) fn batch_succeeded(value: &Value) -> Result<(), String> {
    let failed = value
        .get("failed")
        .and_then(Value::as_array)
        .map_or(0, Vec::len);
    let unattempted = value
        .get("unattempted")
        .and_then(Value::as_array)
        .map_or(0, Vec::len);
    if failed == 0 && unattempted == 0 {
        return Ok(());
    }
    let committed = value
        .get("committed")
        .and_then(Value::as_array)
        .map_or(0, Vec::len);
    let failures = value
        .get("failed")
        .and_then(Value::as_array)
        .into_iter()
        .flatten()
        .map(|item| {
            format!(
                "{}: {}",
                item["id"].as_str().unwrap_or("unknown"),
                item["error"].as_str().unwrap_or("write failed")
            )
        })
        .collect::<Vec<_>>()
        .join("; ");
    let pending = value
        .get("unattempted")
        .and_then(Value::as_array)
        .into_iter()
        .flatten()
        .filter_map(Value::as_str)
        .collect::<Vec<_>>()
        .join(", ");
    Err(format!(
        "Task batch partially completed: {committed} committed, {failed} failed, {unattempted} unattempted. Failed: {failures}. Not attempted: {pending}. Retry the request to continue."
    ))
}

#[derive(Debug, Default, PartialEq, Eq, clap::Args)]
pub(crate) struct InboxFilter {
    #[arg(long)]
    pub(crate) all: bool,
    #[arg(long)]
    pub(crate) sent: bool,
    #[arg(long)]
    pub(crate) received: bool,
    #[arg(long)]
    pub(crate) status: Option<String>,
}

impl InboxFilter {
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

pub(crate) fn task_id_from_env() -> Option<String> {
    std::env::var(TASK_ID_ENV)
        .ok()
        .filter(|id| !id.trim().is_empty())
}
