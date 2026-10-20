use super::*;
use std::os::unix::process::ExitStatusExt;
use std::process::ExitStatus;
/// Construct a normal Unix exit status with the supplied exit code in the high byte.
/// Keep the low byte zero to indicate no termination signal.
fn exit(code: i32) -> ExitStatus {
    ExitStatus::from_raw(code << 8)
}

/// Remove trailing whitespace from stdout.
/// Return an explicit message when output is empty or contains only whitespace.
#[test]
fn file_action_result_returns_trimmed_stdout() {
    assert_eq!(
        format_file_action_result(b"4.0K\tfile\n", &[], false, false, exit(0),).unwrap(),
        "4.0K\tfile"
    );
    assert_eq!(
        format_file_action_result(&[], &[], false, false, exit(0)).unwrap(),
        "(no output)"
    );
    assert_eq!(
        format_file_action_result(b"   \n", &[], false, false, exit(0)).unwrap(),
        "(no output)"
    );
}

/// Append stderr below stdout. Insert a newline only if stdout does not already end with one.
#[test]
fn file_action_result_appends_stderr_below_stdout() {
    assert_eq!(
        format_file_action_result(b"out", b"warn", false, false, exit(0),).unwrap(),
        "out\nwarn"
    );
    assert_eq!(
        format_file_action_result(b"out\n", b"warn", false, false, exit(0),).unwrap(),
        "out\nwarn"
    );
}

/// Append a truncation marker when either output stream exceeds its limit.
#[test]
fn file_action_result_flags_truncation() {
    let out = format_file_action_result(b"body", &[], true, false, exit(0)).unwrap();
    assert!(out.contains("[output truncated]"), "got {out:?}");
}

/// Return an error for a nonzero exit code. Include combined output in the error message.
#[test]
fn file_action_result_fails_on_nonzero_exit() {
    let err = format_file_action_result(&[], b"boom", false, false, exit(1)).unwrap_err();
    assert!(err.to_string().contains("boom"), "got {err}");
}

#[test]
fn host_file_actions_do_not_need_a_project() {
    let cfg = Config::default();
    let (project, session) = resolve_file_action(
        &cfg,
        "",
        "/tmp/private state/file",
        "du -sh '/tmp/private state/file'",
        true,
    )
    .expect("host file action");

    assert!(project.name.is_empty());
    assert_eq!(
        session.cmd.as_deref(),
        Some("du -sh '/tmp/private state/file'")
    );
    let argv = crate::sandbox::host_argv(&cfg, &session, &project).unwrap();
    assert!(!argv.iter().any(|part| part == "bwrap"));
}

#[tokio::test]
async fn project_file_actions_run_on_host_without_private_state() {
    let cfg = Config::default();
    let project = ProjectCfg {
        name: "repo".into(),
        dir: "/tmp".into(),
        ..Default::default()
    };
    let session = SessionCfg {
        cmd: Some("pwd".into()),
        sandbox: vec!["missing-preset-must-not-be-loaded".into()],
        persistent_tmp: true,
        ..Default::default()
    };
    let state = crate::sandbox::state_dir(&session).unwrap();
    assert!(!state.exists());
    let output = execute_file_action(&cfg, &session, &project).await.unwrap();
    assert_eq!(
        std::path::Path::new(&output).canonicalize().unwrap(),
        std::path::Path::new("/tmp").canonicalize().unwrap()
    );
    assert!(!state.exists());
}

#[test]
fn project_file_actions_expand_and_quote_the_absolute_path() {
    let cfg = Config {
        projects: vec![ProjectCfg {
            name: "repo".into(),
            dir: "/tmp/slopworld-project".into(),
            ..Default::default()
        }],
        ..Default::default()
    };
    let (_, session) = resolve_file_action(
        &cfg,
        "repo",
        "/tmp/slopworld-project/src/file name.rs",
        "sed -n '1p' {{ absolute_path }}",
        false,
    )
    .unwrap();
    assert_eq!(
        session.cmd.as_deref(),
        Some("sed -n '1p' '/tmp/slopworld-project/src/file name.rs'")
    );

    resolve_file_action(&cfg, "repo", "/tmp/slopworld-project/file", "   ", false).unwrap_err();
    resolve_file_action(
        &cfg,
        "repo",
        "/tmp/slopworld-project-other/file",
        "cat {{ absolute_path }}",
        false,
    )
    .unwrap_err();
    resolve_file_action(
        &cfg,
        "missing",
        "/tmp/file",
        "cat {{ absolute_path }}",
        false,
    )
    .unwrap_err();
}

#[tokio::test]
async fn command_errors_and_failed_execution_keep_action_context() {
    assert!(
        run_file_action_command(&[], "")
            .await
            .unwrap_err()
            .to_string()
            .contains("no executable")
    );
    let missing = "/slopworld/nonexistent-file-action-program".to_owned();
    assert!(
        run_file_action_command(&[missing], "")
            .await
            .unwrap_err()
            .to_string()
            .contains("starting file action")
    );
    let cfg = Config::default();
    let session = SessionCfg {
        cmd: Some("sh -c 'printf failure >&2; exit 7'".into()),
        ..Default::default()
    };
    let error = execute_file_action(&cfg, &session, &ProjectCfg::default())
        .await
        .unwrap_err();
    assert!(
        error.to_string().contains("file action failed: failure"),
        "{error}"
    );
}
