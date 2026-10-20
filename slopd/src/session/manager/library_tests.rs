use super::{Manager, routed_action_label};
use crate::config::{Config, LibraryItemKind, ProjectCfg, SessionCfg};
use crate::session::LaunchContent;
use std::os::unix::process::ExitStatusExt;
use std::process::ExitStatus;
use std::time::Duration;

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
        Manager::format_file_action_result(b"4.0K\tfile\n", &[], false, false, exit(0),).unwrap(),
        "4.0K\tfile"
    );
    assert_eq!(
        Manager::format_file_action_result(&[], &[], false, false, exit(0)).unwrap(),
        "(no output)"
    );
    assert_eq!(
        Manager::format_file_action_result(b"   \n", &[], false, false, exit(0)).unwrap(),
        "(no output)"
    );
}

/// Append stderr below stdout. Insert a newline only if stdout does not already end with one.
#[test]
fn file_action_result_appends_stderr_below_stdout() {
    assert_eq!(
        Manager::format_file_action_result(b"out", b"warn", false, false, exit(0),).unwrap(),
        "out\nwarn"
    );
    assert_eq!(
        Manager::format_file_action_result(b"out\n", b"warn", false, false, exit(0),).unwrap(),
        "out\nwarn"
    );
}

/// Append a truncation marker when either output stream exceeds its limit.
#[test]
fn file_action_result_flags_truncation() {
    let out = Manager::format_file_action_result(b"body", &[], true, false, exit(0)).unwrap();
    assert!(out.contains("[output truncated]"), "got {out:?}");
}

/// Return an error for a nonzero exit code. Include combined output in the error message.
#[test]
fn file_action_result_fails_on_nonzero_exit() {
    let err = Manager::format_file_action_result(&[], b"boom", false, false, exit(1)).unwrap_err();
    assert!(err.to_string().contains("boom"), "got {err}");
}

/// Permit only prompt and shell library items as agent errands.
/// Reject breadcrumb and file-action items.
#[test]
fn only_runnable_library_pass_the_errand_guard() {
    let sc = |kind| LaunchContent {
        name: "x".into(),
        kind,
        ..Default::default()
    };
    sc(LibraryItemKind::Prompt).validate().unwrap();
    sc(LibraryItemKind::Shell).validate().unwrap();
    assert!(sc(LibraryItemKind::Breadcrumb).validate().is_err());
    assert!(sc(LibraryItemKind::FileAction).validate().is_err());
}

#[test]
fn routed_action_labels_keep_their_original_filename() {
    assert_eq!(
        routed_action_label("edit-README.md").as_deref(),
        Some("edit-README.md")
    );
    assert_eq!(
        routed_action_label("search-src/main.rs").as_deref(),
        Some("search-src/main.rs")
    );
    assert_eq!(
        routed_action_label("link-guide.md").as_deref(),
        Some("link-guide.md")
    );
    assert_eq!(routed_action_label("terminal-README.md"), None);
}

#[tokio::test]
async fn project_temporary_mode_is_immutable_after_creation() {
    let manager = crate::session::test_manager(Config {
        projects: vec![ProjectCfg {
            name: "scratch".into(),
            dir: "/tmp/slopworld-scratch".into(),
            temp: true,
            ..Default::default()
        }],
        ..Default::default()
    });
    let error = manager
        .update_project(
            "scratch",
            ProjectCfg {
                name: "scratch".into(),
                dir: "/tmp/slopworld-scratch".into(),
                temp: false,
                ..Default::default()
            },
        )
        .await
        .expect_err("temporary mode changed");
    assert!(
        error
            .to_string()
            .contains("cannot change project temporary mode"),
        "{error}"
    );
}

#[test]
fn host_file_actions_do_not_need_a_project() {
    let cfg = Config::default();
    let (project, session) = Manager::resolve_file_action(
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
    let output = Manager::execute_file_action(&cfg, &session, &project)
        .await
        .unwrap();
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
    let (_, session) = Manager::resolve_file_action(
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

    Manager::resolve_file_action(&cfg, "repo", "/tmp/slopworld-project/file", "   ", false)
        .unwrap_err();
    Manager::resolve_file_action(
        &cfg,
        "repo",
        "/tmp/slopworld-project-other/file",
        "cat {{ absolute_path }}",
        false,
    )
    .unwrap_err();
    Manager::resolve_file_action(
        &cfg,
        "missing",
        "/tmp/file",
        "cat {{ absolute_path }}",
        false,
    )
    .unwrap_err();
}

#[tokio::test]
async fn canceled_project_update_retains_boundary_until_commit() {
    let project = ProjectCfg {
        name: "original".into(),
        dir: "/tmp/slopworld-project-cancellation".into(),
        ..Default::default()
    };
    let manager = crate::session::test_manager(Config {
        projects: vec![project.clone()],
        ..Default::default()
    });
    let reached = std::sync::Arc::new(tokio::sync::Notify::new());
    let release = std::sync::Arc::new(tokio::sync::Notify::new());
    *manager.worktrees.relocation_pause.lock().unwrap() = Some((reached.clone(), release.clone()));
    let owner = manager.clone();
    let request = tokio::spawn(async move {
        owner
            .update_project(
                "original",
                ProjectCfg {
                    name: "renamed".into(),
                    ..project
                },
            )
            .await
    });
    tokio::time::timeout(Duration::from_secs(5), reached.notified())
        .await
        .unwrap();
    request.abort();
    assert!(request.await.unwrap_err().is_cancelled());
    manager.session_boundary.try_write().unwrap_err();
    release.notify_one();
    tokio::time::timeout(
        Duration::from_secs(5),
        manager.session_operation(async {
            assert!(manager.config().await.project("renamed").is_some());
        }),
    )
    .await
    .unwrap();
}
