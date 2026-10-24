//! Peer, status, and sanitized sandbox diagnostics for the selected caller.

use super::super::HOST;
use super::super::format::{print_json, print_status};
use super::super::http::{Endpoint, request, status_value};
use super::common::encode_component;
use crate::shared::protocol::routes;
use serde_json::{Value, json};

pub(super) fn run_peers(endpoint: &Endpoint, session: &str, json: bool) -> Result<(), String> {
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

pub(super) fn run_status(endpoint: &Endpoint, session: &str, json: bool) -> Result<(), String> {
    let v = status_value(endpoint, session);
    if json {
        print_json(&v);
    } else {
        print_status(&v);
    }
    Ok(())
}

pub(super) fn run_sandbox_inspect(
    endpoint: &Endpoint,
    session: &str,
    json: bool,
    name: &str,
) -> Result<(), String> {
    let v = request(
        endpoint,
        session,
        "GET",
        &format!("{}/{}/sandbox", routes::SESSIONS, encode_component(name)),
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
    let escaped: String = value
        .chars()
        .flat_map(|ch| {
            if ch.is_control() {
                ch.escape_default().collect::<Vec<_>>()
            } else {
                vec![ch]
            }
        })
        .collect();
    format!("'{}'", escaped.replace('\'', "'\\''"))
}

/// Extract session names, include host, then sort and deduplicate for display.
pub(crate) fn peer_names(v: &Value) -> Vec<&str> {
    let mut names: Vec<&str> = v
        .get("sessions")
        .and_then(Value::as_array)
        .map(|s| s.iter().filter_map(|s| s["name"].as_str()).collect())
        .unwrap_or_default();
    // Add the host mailbox explicitly because it has no session entry.
    names.push(HOST);
    names.sort_unstable();
    names.dedup();
    names
}

#[cfg(test)]
#[path = "diagnostics_tests.rs"]
mod tests;
