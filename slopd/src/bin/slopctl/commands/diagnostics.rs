use super::super::HOST;
use super::super::format::{print_json, print_status};
use super::super::http::{Endpoint, request, status_value};
use super::Command;
use super::common::{arg, encode_component, only};
use crate::shared::protocol::routes;
use serde_json::{Value, json};

pub(crate) const PEERS_USAGE: &str = "usage:
  slopctl peers

List sessions visible to the current caller, including host.
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
pub(super) fn parse_sandbox(args: &[String]) -> Result<Command, String> {
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
