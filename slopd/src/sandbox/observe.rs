//! Best-effort observation of the process tree behind a tmux pane.

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
    cgroup: Option<String>,
}

/// Return the saved intent and, when the pane still exists, a sanitized live process tree.
/// `plan` remains useful after a process exits, but `comparison` never calls a saved plan proof
/// of successful launch.
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

    let plan = read_launch_plan(session)?;
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
                                // The cgroup is used to include reparented descendants, but its
                                // name is not an operator-safe process argument.
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
    if let Some(processes) = proc_tree(root, scope) {
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
    plan.limits.iter().find_map(|arg| {
        let unit = arg.strip_prefix("--unit=")?;
        let id = unit.strip_prefix("slopworld-")?.strip_suffix(".scope")?;
        uuid::Uuid::parse_str(id).ok()?;
        Some(unit)
    })
}

fn in_scope(process: &Process, scope: &str) -> bool {
    process
        .cgroup
        .as_deref()
        .is_some_and(|path| path.split('/').any(|part| part == scope))
}

fn select_tree(root: u32, mut all: Vec<Process>, scope: Option<&str>) -> Vec<Process> {
    let mut included = HashSet::from([root]);
    let mut verified_scope = false;
    loop {
        let before = included.len();
        // Only extend through cgroups after a pane descendant proves membership in the exact
        // saved launch scope. Old plans without an explicit unit use ancestry alone.
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
    let cgroup = std::fs::read_to_string(root.join("cgroup"))
        .ok()
        .and_then(|text| cgroup_key(&text));
    Some(Process {
        pid,
        ppid,
        argv,
        cgroup,
    })
}

fn cgroup_key(text: &str) -> Option<String> {
    text.lines()
        .filter_map(|line| line.rsplit_once(':'))
        .map(|(_, path)| path.trim())
        .find(|path| !path.is_empty() && *path != "/")
        .map(str::to_string)
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
            cgroup: None,
        });
    }
    if !all.iter().any(|process| process.pid == root) {
        return None;
    }
    let mut included = HashSet::from([root]);
    loop {
        let before = included.len();
        for process in &all {
            if included.contains(&process.ppid) {
                included.insert(process.pid);
            }
        }
        if included.len() == before {
            break;
        }
    }
    all.retain(|process| included.contains(&process.pid));
    all.sort_by_key(|process| process.pid);
    Some(all)
}

fn compare(plan: Option<&PlanView>, processes: &[Process]) -> &'static str {
    let Some(plan) = plan else {
        return "unavailable";
    };
    let Some(command) = plan.command.first() else {
        return "different";
    };
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
mod tests {
    use super::*;

    #[tokio::test]
    async fn host_inspection_bypasses_invalid_sandbox_identity() {
        let tmux = Tmux::new(format!("slopd-observe-{}", uuid::Uuid::new_v4()));
        let view = inspect_session(
            &tmux,
            "host",
            &SessionCfg {
                state_id: "../invalid".into(),
                ..Default::default()
            },
            true,
        )
        .await
        .unwrap();
        assert_eq!(view["session"], "host");
        assert_eq!(view["host"], true);
        assert!(view["plan"].is_null());
        assert_eq!(view["live"]["status"], "not-applicable");
        assert_eq!(view["live"]["processes"], json!([]));
        assert_eq!(view["comparison"], "not-applicable");
    }

    #[tokio::test]
    async fn missing_pane_is_not_evidence_of_a_successful_launch() {
        let tmux = Tmux::new(format!("slopd-observe-{}", uuid::Uuid::new_v4()));
        let session = SessionCfg {
            state_id: uuid::Uuid::new_v4().to_string(),
            ..Default::default()
        };
        let view = inspect_session(&tmux, "gone", &session, false)
            .await
            .unwrap();
        assert!(view["plan"].is_null());
        assert_eq!(view["live"]["status"], "not-running");
        assert!(view["live"]["pane_pid"].is_null());
        assert_eq!(view["live"]["processes"], json!([]));
        assert_eq!(view["comparison"], "unavailable");
        assert!(inspect_session(
            &tmux,
            "bad",
            &SessionCfg {
                state_id: "../invalid".into(),
                ..Default::default()
            },
            false
        )
        .await
        .is_err());
    }

    #[test]
    fn ps_output_selects_unsorted_descendants_and_tolerates_missing_arguments() {
        let output = " 13 12 /bin/agent --token secret\ninvalid\n12 10\n10 1 /bin/sh\n20 1 unrelated\n21 nope ignored\n";
        let rows = parse_ps_tree(10, output).unwrap();
        assert_eq!(
            rows.iter().map(|row| row.pid).collect::<Vec<_>>(),
            [10, 12, 13]
        );
        assert_eq!(rows[1].argv, ["<unavailable>"]);
        assert_eq!(rows[2].argv, ["/bin/agent", "--token", "secret"]);
        assert!(rows.iter().all(|row| row.cgroup.is_none()));
        assert!(parse_ps_tree(99, output).is_none());
        assert!(parse_ps_tree(10, "").is_none());
    }

    #[test]
    fn proc_files_preserve_argument_boundaries_and_handle_vanished_processes() {
        let root = std::env::temp_dir().join(format!("slopd-proc-{}", uuid::Uuid::new_v4()));
        let directory = root.join("42");
        std::fs::create_dir_all(&directory).unwrap();
        assert!(read_proc_at(&root, 42).is_none());
        std::fs::write(directory.join("cmdline"), b"/bin/agent\0two words\0\xff\0").unwrap();
        assert!(read_proc_at(&root, 42).is_none());
        std::fs::write(directory.join("status"), "Name: agent\nPPid:\t7\n").unwrap();
        let row = read_proc_at(&root, 42).unwrap();
        assert_eq!((row.pid, row.ppid), (42, 7));
        assert_eq!(row.argv, ["/bin/agent", "two words", "�"]);
        assert!(row.cgroup.is_none());
        std::fs::write(directory.join("cgroup"), "0::/user.slice/launch.scope\n").unwrap();
        std::fs::write(directory.join("cmdline"), []).unwrap();
        let row = read_proc_at(&root, 42).unwrap();
        assert_eq!(row.argv, ["<unavailable>"]);
        assert_eq!(row.cgroup.as_deref(), Some("/user.slice/launch.scope"));
        std::fs::write(directory.join("status"), "PPid: invalid\n").unwrap();
        assert!(read_proc_at(&root, 42).is_none());
        std::fs::remove_dir_all(root).unwrap();
    }

    fn process(pid: u32, ppid: u32, cgroup: Option<&str>) -> Process {
        Process {
            pid,
            ppid,
            argv: vec!["agent".into()],
            cgroup: cgroup.map(str::to_string),
        }
    }

    fn plan(limits: Vec<String>) -> PlanView {
        PlanView {
            version: 1,
            session: "test".into(),
            limits,
            pasta: Vec::new(),
            bwrap: Vec::new(),
            environment: Vec::new(),
            mounts: Vec::new(),
            command: vec!["agent".into()],
            argv: Vec::new(),
        }
    }

    #[test]
    fn launch_scope_requires_a_valid_slopworld_uuid_unit() {
        for invalid in [
            "slopworld-11111111-1111-4111-8111-111111111111.scope",
            "--unit=other-11111111-1111-4111-8111-111111111111.scope",
            "--unit=slopworld-not-a-uuid.scope",
            "--unit=slopworld-11111111-1111-4111-8111-111111111111.service",
        ] {
            assert_eq!(
                expected_scope(&plan(vec![invalid.into()])),
                None,
                "{invalid}"
            );
        }
        let scope = "slopworld-11111111-1111-4111-8111-111111111111.scope";
        let plan = plan(vec![
            "--user".into(),
            "--unit=invalid".into(),
            format!("--unit={scope}"),
        ]);
        assert_eq!(expected_scope(&plan), Some(scope));
        assert_eq!(expected_scope(&self::plan(Vec::new())), None);
    }

    #[test]
    fn scope_membership_matches_whole_path_components() {
        for (group, expected) in [
            (None, false),
            (Some("/user.slice/launch.scope"), true),
            (Some("/user.slice/launch.scope/child"), true),
            (Some("/user.slice/prefix-launch.scope"), false),
            (Some("/user.slice/launch.scope-suffix"), false),
        ] {
            assert_eq!(in_scope(&process(1, 0, group), "launch.scope"), expected);
        }
    }

    #[test]
    fn tree_selection_reaches_unsorted_descendants_and_reparented_children() {
        let rows = vec![
            process(32, 31, None),
            process(31, 1, Some("/launch.scope")),
            process(13, 12, Some("/launch.scope")),
            process(12, 11, None),
            process(11, 10, None),
            process(10, 1, None),
            process(99, 1, Some("/launch.scope-suffix")),
            process(98, 98, None),
        ];
        let ids = |rows: Vec<Process>| rows.into_iter().map(|row| row.pid).collect::<Vec<_>>();
        assert_eq!(ids(select_tree(10, rows.clone(), None)), [10, 11, 12, 13]);
        assert_eq!(
            ids(select_tree(10, rows, Some("launch.scope"))),
            [10, 11, 12, 13, 31, 32]
        );
    }

    #[test]
    fn cgroup_parser_skips_root_empty_and_malformed_entries() {
        assert_eq!(cgroup_key("malformed\n0::/\n1:cpu:  \n"), None);
        assert_eq!(
            cgroup_key("0::/\n1:cpu: /user.slice/launch.scope \n2:memory:/other"),
            Some("/user.slice/launch.scope".into())
        );
        assert_eq!(
            cgroup_key("0::/unified.scope\n"),
            Some("/unified.scope".into())
        );
    }

    #[test]
    fn comparison_uses_executable_not_an_argument_or_prefix() {
        let mut plan = plan(Vec::new());
        plan.command = vec!["/opt/bin/agent".into()];
        let mut row = process(1, 0, None);
        row.argv = vec!["/bin/shell".into(), "agent".into()];
        assert_eq!(compare(Some(&plan), &[row.clone()]), "different");
        row.argv = vec!["agent-helper".into()];
        assert_eq!(compare(Some(&plan), &[row.clone()]), "different");
        row.argv.clear();
        assert_eq!(compare(Some(&plan), &[row.clone()]), "different");
        row.argv = vec!["/another/bin/agent".into()];
        assert_eq!(compare(Some(&plan), &[row.clone()]), "compatible");
        plan.command.clear();
        assert_eq!(compare(Some(&plan), &[row]), "different");
    }

    #[test]
    fn cgroup_expansion_requires_the_exact_launch_scope_and_a_descendant() {
        let scope = "slopworld-11111111-1111-4111-8111-111111111111.scope";
        let process = |pid, ppid, group: &str| Process {
            pid,
            ppid,
            argv: vec!["agent".into()],
            cgroup: Some(group.into()),
        };
        let shared = "/user.slice/tmux.service";
        let own = format!("/user.slice/{scope}");
        let all = vec![
            process(10, 1, shared),
            process(11, 10, &own),
            process(12, 1, &own),   // reparented within the proven scope
            process(20, 1, shared), // another pane
            process(21, 20, "/user.slice/other.scope"),
        ];
        let ids = |rows: Vec<Process>| rows.into_iter().map(|p| p.pid).collect::<Vec<_>>();
        assert_eq!(ids(select_tree(10, all.clone(), Some(scope))), [10, 11, 12]);
        assert_eq!(ids(select_tree(10, all.clone(), None)), [10, 11]);
        let mut starting = all;
        starting[1].cgroup = Some(shared.into());
        assert_eq!(ids(select_tree(10, starting, Some(scope))), [10, 11]);
    }

    #[test]
    fn process_comparison_does_not_claim_success_without_a_live_command() {
        let plan = PlanView {
            version: 1,
            session: "a".into(),
            limits: Vec::new(),
            pasta: Vec::new(),
            bwrap: Vec::new(),
            environment: Vec::new(),
            mounts: Vec::new(),
            command: vec!["codex".into()],
            argv: Vec::new(),
        };
        assert_eq!(compare(Some(&plan), &[]), "different");
        assert_eq!(compare(None, &[]), "unavailable");
        assert_eq!(
            compare(
                Some(&plan),
                &[Process {
                    pid: 1,
                    ppid: 0,
                    argv: vec!["/usr/bin/codex".into()],
                    cgroup: None,
                }]
            ),
            "compatible"
        );
    }
}
