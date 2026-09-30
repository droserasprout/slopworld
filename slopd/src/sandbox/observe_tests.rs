use super::*;

#[tokio::test]
async fn host_inspection_bypasses_invalid_sandbox_identity() {
    let socket_owner = crate::test_support::TmuxSocket::new();
    let tmux = Tmux::new(&socket_owner.path);
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
    let socket_owner = crate::test_support::TmuxSocket::new();
    let tmux = Tmux::new(&socket_owner.path);
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
    inspect_session(
        &tmux,
        "bad",
        &SessionCfg {
            state_id: "../invalid".into(),
            ..Default::default()
        },
        false,
    )
    .await
    .unwrap_err();
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
    assert!(rows.iter().all(|row| row.cgroups.is_empty()));
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
    assert!(row.cgroups.is_empty());
    std::fs::write(directory.join("cgroup"), "0::/user.slice/launch.scope\n").unwrap();
    std::fs::write(directory.join("cmdline"), []).unwrap();
    let row = read_proc_at(&root, 42).unwrap();
    assert_eq!(row.argv, ["<unavailable>"]);
    assert_eq!(row.cgroups, ["/user.slice/launch.scope"]);
    std::fs::write(directory.join("status"), "PPid: invalid\n").unwrap();
    assert!(read_proc_at(&root, 42).is_none());
    std::fs::remove_dir_all(root).unwrap();
}

fn process(pid: u32, ppid: u32, cgroup: Option<&str>) -> Process {
    Process {
        pid,
        ppid,
        argv: vec!["agent".into()],
        cgroups: cgroup.into_iter().map(str::to_owned).collect(),
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
    assert!(cgroup_paths("malformed\n0::/\n1:cpu:  \n").is_empty());
    assert_eq!(
        cgroup_paths("0::/\n1:cpu: /user.slice/launch.scope \n2:memory:/other"),
        ["/user.slice/launch.scope", "/other"]
    );
    assert_eq!(cgroup_paths("0::/unified.scope\n"), ["/unified.scope"]);
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
        cgroups: vec![group.into()],
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
    starting[1].cgroups = vec![shared.into()];
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
                cgroups: Vec::new(),
            }]
        ),
        "compatible"
    );
}

#[test]
fn later_controller_membership_keeps_reparented_scope_descendants() {
    let mut member = process(11, 10, None);
    member.cgroups = cgroup_paths("2:cpu:/unrelated\n3:memory:/user.slice/launch.scope\n");
    let mut reparented = process(12, 1, None);
    reparented.cgroups = member.cgroups.clone();
    assert!(in_scope(&member, "launch.scope"));
    let selected = select_tree(
        10,
        vec![process(10, 1, None), member, reparented],
        Some("launch.scope"),
    );
    assert_eq!(
        selected.iter().map(|p| p.pid).collect::<Vec<_>>(),
        [10, 11, 12]
    );
}

#[tokio::test]
async fn saved_plan_remains_visible_after_the_pane_exits() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    let session = SessionCfg {
        name: "gone".into(),
        state_id: uuid::Uuid::new_v4().to_string(),
        ..Default::default()
    };
    let saved = plan(vec![]);
    let dir = super::super::state_dir(&session).unwrap();
    std::fs::create_dir_all(&dir).unwrap();
    std::fs::write(
        dir.join("launch-plan.json"),
        serde_json::to_string(&saved).unwrap(),
    )
    .unwrap();
    let socket_owner = crate::test_support::TmuxSocket::new();
    let tmux = Tmux::new(&socket_owner.path);
    let view = inspect_session(&tmux, "gone", &session, false)
        .await
        .unwrap();
    assert_eq!(view["plan"]["session"], "gone");
    assert_eq!(view["live"]["status"], "not-running");
    assert_eq!(view["comparison"], "unavailable");
}
