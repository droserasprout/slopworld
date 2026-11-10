//! Peer, status, and sanitized sandbox diagnostics for the selected caller.

use super::super::HOST;
use super::super::format::{print_json, print_status};
use super::super::http::{Endpoint, request, status_value};
use super::common::encode_component;
use crate::shared::protocol::routes;
use serde_json::{Value, json};
use std::fmt::Write;

pub(super) fn run_peers(endpoint: &Endpoint, session: &str, json: bool) -> Result<(), String> {
    let v = request(endpoint, session, "GET", routes::SESSIONS, None)?;
    if json {
        print_json(&json!(peer_names(&v)));
    } else {
        print!("{}", render_peers(&v, session));
    }
    Ok(())
}

#[expect(clippy::expect_used, reason = "formatting into a String cannot fail")]
fn render_peers(v: &Value, session: &str) -> String {
    let mut out = String::new();
    for name in peer_names(v) {
        let mark = if name == session { " (you)" } else { "" };
        writeln!(out, "{name}{mark}").expect("writing to a String cannot fail");
    }
    out
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
    print!("{}", render_sandbox(v));
}

#[expect(clippy::expect_used, reason = "formatting into a String cannot fail")]
fn render_sandbox(v: &Value) -> String {
    let mut out = String::new();
    writeln!(out, "session   {}", v["session"].as_str().unwrap_or("?"))
        .expect("writing to a String cannot fail");
    if v["host"].as_bool().unwrap_or(false) {
        writeln!(out, "sandbox   not applicable (host terminal)")
            .expect("writing to a String cannot fail");
        return out;
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
            writeln!(out, "{section}:").expect("writing to a String cannot fail");
            if let Some(args) = plan[section].as_array() {
                if args.is_empty() {
                    writeln!(out, "  (none)").expect("writing to a String cannot fail");
                } else {
                    for arg in args {
                        writeln!(out, "  {}", shell_quote(arg.as_str().unwrap_or("<arg>")))
                            .expect("writing to a String cannot fail");
                    }
                }
            }
        }
    } else {
        writeln!(out, "plan      no saved sandbox launch")
            .expect("writing to a String cannot fail");
    }
    let live = &v["live"];
    writeln!(
        out,
        "live      {}",
        live["status"].as_str().unwrap_or("unknown")
    )
    .expect("writing to a String cannot fail");
    if let Some(pid) = live["pane_pid"].as_u64() {
        writeln!(out, "pane pid  {pid}").expect("writing to a String cannot fail");
    }
    writeln!(
        out,
        "compare   {}",
        v["comparison"].as_str().unwrap_or("unknown")
    )
    .expect("writing to a String cannot fail");
    if let Some(processes) = live["processes"].as_array() {
        for process in processes {
            let pid = process["pid"].as_u64().unwrap_or(0);
            let ppid = process["ppid"].as_u64().unwrap_or(0);
            writeln!(out, "process   {pid} (parent {ppid})")
                .expect("writing to a String cannot fail");
            if let Some(argv) = process["argv"].as_array() {
                for arg in argv {
                    writeln!(out, "  {}", shell_quote(arg.as_str().unwrap_or("<arg>")))
                        .expect("writing to a String cannot fail");
                }
            }
        }
    }
    out
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
