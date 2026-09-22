use super::{auto_resume_inputs, routed_action_label, Manager};
use crate::config::{Config, LibraryItemCfg, LibraryItemKind, ProjectCfg, SessionCfg};
use crate::session::Input;
use std::os::unix::process::ExitStatusExt;
use std::process::ExitStatus;

/// A normal exit with the given code, the way the OS hands it back: the low byte is the
/// signal (none here) and the code sits above it.
fn exit(code: i32) -> ExitStatus {
    ExitStatus::from_raw(code << 8)
}

#[test]
fn auto_resume_types_the_command_and_two_enters_in_order() {
    let input = auto_resume_inputs();
    assert_eq!(input.len(), 5);
    assert!(matches!(
        &input[0],
        Input::Bytes(bytes) if bytes == b"/resume"
    ));
    assert!(matches!(&input[1], Input::Gap(_)));
    assert!(matches!(
        &input[2],
        Input::Keys { keys, literal: false } if keys == &["Enter"]
    ));
    assert!(matches!(&input[3], Input::Gap(_)));
    assert!(matches!(
        &input[4],
        Input::Keys { keys, literal: false } if keys == &["Enter"]
    ));
}

/// stdout alone comes back trimmed of its trailing newline; empty output is spelled out
/// rather than handed back blank.
#[test]
fn file_action_result_returns_trimmed_stdout() {
    assert_eq!(
        Manager::format_file_action_result(
            b"4.0K\tfile\n".to_vec(),
            Vec::new(),
            false,
            false,
            exit(0),
        )
        .unwrap(),
        "4.0K\tfile"
    );
    assert_eq!(
        Manager::format_file_action_result(Vec::new(), Vec::new(), false, false, exit(0)).unwrap(),
        "(no output)"
    );
    assert_eq!(
        Manager::format_file_action_result(b"   \n".to_vec(), Vec::new(), false, false, exit(0))
            .unwrap(),
        "(no output)"
    );
}

/// stderr is appended below stdout, with a separating newline inserted only when stdout did
/// not already end in one.
#[test]
fn file_action_result_appends_stderr_below_stdout() {
    assert_eq!(
        Manager::format_file_action_result(
            b"out".to_vec(),
            b"warn".to_vec(),
            false,
            false,
            exit(0),
        )
        .unwrap(),
        "out\nwarn"
    );
    assert_eq!(
        Manager::format_file_action_result(
            b"out\n".to_vec(),
            b"warn".to_vec(),
            false,
            false,
            exit(0),
        )
        .unwrap(),
        "out\nwarn"
    );
}

/// A truncation marker is tacked on when either stream was cut short.
#[test]
fn file_action_result_flags_truncation() {
    let out =
        Manager::format_file_action_result(b"body".to_vec(), Vec::new(), true, false, exit(0))
            .unwrap();
    assert!(out.contains("[output truncated]"), "got {out:?}");
}

/// A non-zero exit is an error, and the combined output rides along in the message rather
/// than being returned as success.
#[test]
fn file_action_result_fails_on_nonzero_exit() {
    let err =
        Manager::format_file_action_result(Vec::new(), b"boom".to_vec(), false, false, exit(1))
            .unwrap_err();
    assert!(err.to_string().contains("boom"), "got {err}");
}

/// Breadcrumb and file-action library are handles on something, not runnable prompts; only
/// prompt and shell library may be launched as an agent errand.
#[test]
fn only_runnable_library_pass_the_errand_guard() {
    let sc = |kind| LibraryItemCfg {
        name: "x".into(),
        kind,
        ..Default::default()
    };
    assert!(Manager::validate_errand(&sc(LibraryItemKind::Prompt)).is_ok());
    assert!(Manager::validate_errand(&sc(LibraryItemKind::Shell)).is_ok());
    assert!(Manager::validate_errand(&sc(LibraryItemKind::Breadcrumb)).is_err());
    assert!(Manager::validate_errand(&sc(LibraryItemKind::FileAction)).is_err());
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
    assert!(error.to_string().contains("cannot be changed"), "{error}");
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
    let argv = crate::sandbox::host_argv(&cfg, &session, &project);
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

    assert!(Manager::resolve_file_action(
        &cfg,
        "repo",
        "/tmp/slopworld-project/file",
        "   ",
        false,
    )
    .is_err());
    assert!(Manager::resolve_file_action(
        &cfg,
        "repo",
        "/tmp/slopworld-project-other/file",
        "cat {{ absolute_path }}",
        false,
    )
    .is_err());
    assert!(Manager::resolve_file_action(
        &cfg,
        "missing",
        "/tmp/file",
        "cat {{ absolute_path }}",
        false,
    )
    .is_err());
}
