//! Process-tree inspection for a tmux pane when process data is available.

use std::collections::HashSet;
use std::path::Path;

use anyhow::Result;
use serde_json::{json, Value};
use tokio::process::Command;

use crate::config::SessionCfg;
use crate::tmux::Tmux;

use super::{read_launch_plan, sanitize_process_argv, PlanView};

#[derive(Debug, Clone)]
struct Process {
    pid: u32,
    ppid: u32,
    argv: Vec<String>,
    cgroups: Vec<String>,
}

/// Return the saved plan and, when available, a redacted view of the pane's live process tree.
/// `plan` remains useful after a process exits. `comparison` does not treat a saved plan as proof of a successful launch.
pub(crate) async fn inspect_session(
    tmux: &Tmux,
    name: &str,
    session: &SessionCfg,
    host: bool,
) -> Result<Value> {
    if host {
        return Ok(json!({
            "session": name,
            "host": true,
            "plan": Value::Null,
            "live": { "status": "not-applicable", "processes": [] },
            "comparison": "not-applicable",
        }));
    }

    let session = session.clone();
    let plan = tokio::task::spawn_blocking(move || read_launch_plan(&session)).await??;
    let scope = plan.as_ref().and_then(expected_scope);
    let exists = tmux.exists(name).await;
    let (live, comparison) = if !exists {
        (
            json!({
                "status": "not-running",
                "pane_pid": Value::Null,
                "processes": [],
            }),
            "unavailable",
        )
    } else {
        let pane_pid = tmux.pane_pid(name).await;
        match pane_pid {
            Some(pid) => match observe_tree(pid, scope).await {
                Some((source, processes)) => {
                    let comparison = compare(plan.as_ref(), &processes);

                    let views: Vec<Value> = processes
                        .iter()
                        .map(|process| {
                            json!({
                                "pid": process.pid,
                                "ppid": process.ppid,
                                "argv": sanitize_process_argv(&process.argv),
                                // Use the cgroup to include descendants whose parent changed.
                                // Do not expose its name as a process argument.
                                "in_scope": scope.is_some_and(|scope| in_scope(process, scope)),
                            })
                        })
                        .collect();
                    (
                        json!({
                            "status": "live",
                            "pane_pid": pid,
                            "source": source,
                            "processes": views,
                        }),
                        comparison,
                    )
                }
                None => (
                    json!({
                        "status": "unavailable",
                        "pane_pid": pid,
                        "processes": [],
                    }),
                    "unavailable",
                ),
            },
            None => (
                json!({
                    "status": "unavailable",
                    "pane_pid": Value::Null,
                    "processes": [],
                }),
                "unavailable",
            ),
        }
    };

    Ok(json!({
        "session": name,
        "host": false,
        "plan": plan,
        "live": live,
        "comparison": comparison,
    }))
}

async fn observe_tree(root: u32, scope: Option<&str>) -> Option<(&'static str, Vec<Process>)> {
    let scope = scope.map(str::to_owned);
    if let Some(processes) = tokio::task::spawn_blocking(move || proc_tree(root, scope.as_deref()))
        .await
        .ok()
        .flatten()
    {
        return Some(("proc", processes));
    }
    ps_tree(root).await.map(|processes| ("ps", processes))
}

fn proc_tree(root: u32, scope: Option<&str>) -> Option<Vec<Process>> {
    let root_process = read_proc(root)?;
    let mut all = vec![root_process];
    let entries = std::fs::read_dir("/proc").ok()?;
    for entry in entries.flatten() {
        let name = entry.file_name();
        let Ok(pid) = name.to_string_lossy().parse::<u32>() else {
            continue;
        };
        if pid != root {
            if let Some(process) = read_proc(pid) {
                all.push(process);
            }
        }
    }

    Some(select_tree(root, all, scope))
}

fn expected_scope(plan: &PlanView) -> Option<&str> {
    plan.limits
        .iter()
        .find_map(|arg| super::plan::launch_scope(arg))
}

fn in_scope(process: &Process, scope: &str) -> bool {
    process
        .cgroups
        .iter()
        .any(|path| path.split('/').any(|part| part == scope))
}

fn select_tree(root: u32, mut all: Vec<Process>, scope: Option<&str>) -> Vec<Process> {
    let mut included = HashSet::from([root]);
    let mut verified_scope = false;
    loop {
        let before = included.len();
        // First confirm that an included process belongs to the saved launch scope.
        // Then use cgroup membership to include other processes.
        // Old plans without an explicit unit use process ancestry only.
        verified_scope |= scope.is_some_and(|scope| {
            all.iter()
                .any(|process| included.contains(&process.pid) && in_scope(process, scope))
        });
        for process in &all {
            if included.contains(&process.ppid)
                || (verified_scope && scope.is_some_and(|scope| in_scope(process, scope)))
            {
                included.insert(process.pid);
            }
        }
        if included.len() == before {
            break;
        }
    }
    all.retain(|process| included.contains(&process.pid));
    all.sort_by_key(|process| process.pid);
    all
}

fn read_proc(pid: u32) -> Option<Process> {
    read_proc_at(Path::new("/proc"), pid)
}

fn read_proc_at(proc_root: &Path, pid: u32) -> Option<Process> {
    let root = proc_root.join(pid.to_string());
    let bytes = std::fs::read(root.join("cmdline")).ok()?;
    let argv = bytes
        .split(|byte| *byte == 0)
        .filter(|arg| !arg.is_empty())
        .map(|arg| String::from_utf8_lossy(arg).into_owned())
        .collect::<Vec<_>>();
    let argv = if argv.is_empty() {
        vec!["<unavailable>".into()]
    } else {
        argv
    };
    let status = std::fs::read_to_string(root.join("status")).ok()?;
    let ppid = status
        .lines()
        .find_map(|line| line.strip_prefix("PPid:")?.trim().parse().ok())?;
    let cgroups = std::fs::read_to_string(root.join("cgroup"))
        .map(|text| cgroup_paths(&text))
        .unwrap_or_default();
    Some(Process {
        pid,
        ppid,
        argv,
        cgroups,
    })
}

fn cgroup_paths(text: &str) -> Vec<String> {
    text.lines()
        .filter_map(|line| line.rsplit_once(':'))
        .map(|(_, path)| path.trim())
        .filter(|path| !path.is_empty() && *path != "/")
        .map(str::to_owned)
        .collect()
}

async fn ps_tree(root: u32) -> Option<Vec<Process>> {
    let output = Command::new("ps")
        .args(["-eo", "pid=,ppid=,args="])
        .output()
        .await
        .ok()?;
    if !output.status.success() {
        return None;
    }
    parse_ps_tree(root, &String::from_utf8_lossy(&output.stdout))
}

fn parse_ps_tree(root: u32, output: &str) -> Option<Vec<Process>> {
    let mut all = Vec::new();
    for line in output.lines() {
        let mut fields = line.split_whitespace();
        let Some(pid) = fields.next().and_then(|value| value.parse().ok()) else {
            continue;
        };
        let Some(ppid) = fields.next().and_then(|value| value.parse().ok()) else {
            continue;
        };
        let argv = fields.map(str::to_string).collect::<Vec<_>>();
        all.push(Process {
            pid,
            ppid,
            argv: if argv.is_empty() {
                vec!["<unavailable>".into()]
            } else {
                argv
            },
            cgroups: Vec::new(),
        });
    }
    if !all.iter().any(|process| process.pid == root) {
        return None;
    }
    Some(select_tree(root, all, None))
}

fn compare(plan: Option<&PlanView>, processes: &[Process]) -> &'static str {
    let Some(plan) = plan else {
        return "unavailable";
    };
    let Some(command) = plan.command.first() else {
        return "different";
    };
    if command == super::plan::UNKNOWN_ARG || command == super::plan::REDACTED {
        return "unavailable";
    }
    let command = executable(command);
    if processes
        .iter()
        .filter_map(|process| process.argv.first())
        .map(|arg| executable(arg))
        .any(|arg| arg == command)
    {
        "compatible"
    } else {
        "different"
    }
}

fn executable(value: &str) -> &str {
    value.rsplit('/').next().unwrap_or(value)
}

#[cfg(test)]
#[path = "observe_tests.rs"]
mod tests;
