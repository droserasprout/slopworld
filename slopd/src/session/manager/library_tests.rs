use super::{auto_resume_inputs, routed_action_label, Manager};
use crate::config::{Config, LibraryItemCfg, LibraryItemKind, ProjectCfg, SessionCfg};
use crate::session::Input;
use std::os::unix::process::ExitStatusExt;
use std::process::ExitStatus;
use std::time::Duration;

/// Construct a normal Unix exit status with the supplied exit code in the high byte.
/// Keep the low byte zero to indicate no termination signal.
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

/// Remove trailing whitespace from stdout.
/// Return an explicit message when output is empty or contains only whitespace.
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

/// Append stderr below stdout. Insert a newline only if stdout does not already end with one.
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

/// Append a truncation marker when either output stream exceeds its limit.
#[test]
fn file_action_result_flags_truncation() {
    let out =
        Manager::format_file_action_result(b"body".to_vec(), Vec::new(), true, false, exit(0))
            .unwrap();
    assert!(out.contains("[output truncated]"), "got {out:?}");
}

/// Return an error for a nonzero exit code. Include combined output in the error message.
#[test]
fn file_action_result_fails_on_nonzero_exit() {
    let err =
        Manager::format_file_action_result(Vec::new(), b"boom".to_vec(), false, false, exit(1))
            .unwrap_err();
    assert!(err.to_string().contains("boom"), "got {err}");
}

/// Permit only prompt and shell library items as agent errands.
/// Reject breadcrumb and file-action items.
#[test]
fn only_runnable_library_pass_the_errand_guard() {
    let sc = |kind| LibraryItemCfg {
        name: "x".into(),
        kind,
        ..Default::default()
    };
    Manager::validate_errand(&sc(LibraryItemKind::Prompt)).unwrap();
    Manager::validate_errand(&sc(LibraryItemKind::Shell)).unwrap();
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

async fn delivery_fixture() -> (
    std::sync::Arc<Manager>,
    tokio::sync::mpsc::UnboundedReceiver<Input>,
) {
    use crate::session::{Live, TitleCapture};
    let manager = crate::session::test_manager(Config::default());
    let (tx, rx) = tokio::sync::mpsc::unbounded_channel();
    let mut live = Live::new(
        SessionCfg {
            name: "worker".into(),
            ..Default::default()
        },
        TitleCapture::default(),
    );
    live.ephemeral = true;
    live.input.sender = Some(tx);
    manager.live.write().await.insert("worker".into(), live);
    let mut emu = crate::emu::SessionEmu::new(80, 24);
    emu.feed(b"ready> ");
    manager.apply_frame("worker", emu.render()).await;
    (manager, rx)
}

async fn assert_prompt_submission(
    rx: &mut tokio::sync::mpsc::UnboundedReceiver<Input>,
    expected: &str,
) {
    let paste = tokio::time::timeout(std::time::Duration::from_secs(5), rx.recv())
        .await
        .unwrap()
        .unwrap();
    assert!(matches!(paste, Input::Paste { bytes } if bytes == expected.as_bytes()));
    let delay = tokio::time::timeout(std::time::Duration::from_secs(5), rx.recv())
        .await
        .unwrap()
        .unwrap();
    assert!(matches!(delay, Input::Gap(duration) if duration == super::DELIVERY_ENTER_GAP));
    let enter = tokio::time::timeout(std::time::Duration::from_secs(5), rx.recv())
        .await
        .unwrap()
        .unwrap();
    assert!(matches!(enter, Input::Keys { keys, literal: false } if keys == ["Enter"]));
    assert!(rx.try_recv().is_err(), "delivery must submit exactly once");
}

#[tokio::test]
async fn worker_bootstrap_is_pasted_intact_then_submitted_once() {
    let (manager, mut rx) = delivery_fixture().await;
    let prompt = "Read the task and accept it.\nPreserve λ and literal {{ placeholders }}.";
    manager
        .deliver(&manager.delivery_target("worker").await.unwrap(), prompt)
        .await;
    assert_prompt_submission(&mut rx, prompt).await;
}

#[tokio::test]
async fn errand_renders_tips_before_paste_and_submission() {
    let (manager, mut rx) = delivery_fixture().await;
    manager
        .queue_errand_delivery(
            "worker",
            &LibraryItemCfg {
                text: "{{ random_tip }}\n{{ random_tip }}".into(),
                ..Default::default()
            },
            &crate::session::RunWhere {
                random_tips: vec!["First context".into(), "Second context".into()],
                ..Default::default()
            },
        )
        .await;
    assert_prompt_submission(&mut rx, "First context\nSecond context").await;
}

#[tokio::test]
async fn stopped_or_missing_session_receives_no_prompt_or_enter() {
    let (manager, mut rx) = delivery_fixture().await;
    manager.live.write().await.get_mut("worker").unwrap().state = crate::session::State::Down;
    manager
        .deliver(
            &manager.delivery_target("worker").await.unwrap(),
            "do not submit",
        )
        .await;
    manager
        .queue_delivery("missing", "do not submit".into())
        .await;
    assert!(rx.try_recv().is_err());
}

#[tokio::test]
async fn delayed_prompts_reject_restart_and_replacement_during_readiness() {
    for replace_identity in [false, true] {
        let (manager, mut rx) = delivery_fixture().await;
        let target = manager.delivery_target("worker").await.unwrap();
        let mut delivery = Box::pin(manager.deliver(&target, "stale"));
        assert!(futures::poll!(delivery.as_mut()).is_pending());
        {
            let mut live = manager.live.write().await;
            let current = live.get_mut("worker").unwrap();
            if replace_identity {
                current.cfg.state_id = uuid::Uuid::new_v4().to_string();
            } else {
                current.run_id += 1;
            }
        }
        tokio::time::timeout(Duration::from_secs(2), delivery)
            .await
            .unwrap();
        assert!(rx.try_recv().is_err());
    }
}

#[tokio::test]
async fn prompt_and_auto_resume_recheck_ownership_after_waiting_for_admission() {
    for prompt in [Some("stale"), None] {
        let (manager, mut rx) = delivery_fixture().await;
        let target = manager.delivery_target("worker").await.unwrap();
        let guard = manager.session_boundary.write().await;
        let mut admission = Box::pin(manager.admit_delivery(&target, prompt));
        assert!(futures::poll!(admission.as_mut()).is_pending());
        {
            let mut live = manager.live.write().await;
            let current = live.get_mut("worker").unwrap();
            current.run_id += 1;
            current.input.auto_resume_pending = true;
        }
        drop(guard);
        admission.await;
        manager.finish_auto_resume(&target).await;
        assert!(rx.try_recv().is_err());
        assert!(
            manager.live.read().await["worker"]
                .input
                .auto_resume_pending
        );
    }
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
