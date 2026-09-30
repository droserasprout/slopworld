use super::*;
use std::os::unix::fs::PermissionsExt;

fn mock_tmux(root: &std::path::Path, script: &str) {
    let bin = root.join("bin");
    std::fs::create_dir_all(&bin).unwrap();
    let path = bin.join("tmux");
    std::fs::write(&path, format!("#!/bin/sh\n{script}")).unwrap();
    std::fs::set_permissions(path, std::fs::Permissions::from_mode(0o700)).unwrap();
    std::env::set_var("PATH", bin);
    std::env::set_var("TMUX_TEST_LOG", root.join("commands"));
}

#[tokio::test]
async fn server_check_errors_stop_spawn_before_session_creation() {
    let Some(root) = crate::test_support::isolated() else {
        return;
    };
    mock_tmux(&root, "printf '%s\n' \"$*\" >> \"$TMUX_TEST_LOG\"\nprintf 'error connecting to socket (Permission denied)' >&2\nexit 1\n");
    let tmux = Tmux::new("test");
    let error = tmux
        .spawn("name", "/tmp", 80, 24, &["true".into()], false)
        .await
        .unwrap_err();
    assert!(error.to_string().contains("Permission denied"));
    let commands = std::fs::read_to_string(root.join("commands")).unwrap();
    assert!(!commands.contains("start-server"));
    assert!(!commands.contains("new-session"));
}

#[tokio::test]
async fn inline_startup_failure_stops_session_creation() {
    let Some(root) = crate::test_support::isolated() else {
        return;
    };
    mock_tmux(
        &root,
        r#"
printf '%s\n' "$*" >> "$TMUX_TEST_LOG"
case "$3" in
list-sessions) printf 'no server running on socket' >&2; exit 1;;
start-server) exit 1;;
esac
"#,
    );
    let tmux = Tmux::new("test");
    assert!(tmux
        .spawn("name", "/tmp", 80, 24, &["true".into()], false)
        .await
        .is_err());
    let commands = std::fs::read_to_string(root.join("commands")).unwrap();
    assert!(commands.contains("start-server"));
    assert!(!commands.contains("new-session"));
}

#[tokio::test]
async fn failed_host_marker_rolls_back_the_created_session_id() {
    let Some(root) = crate::test_support::isolated() else {
        return;
    };
    mock_tmux(
        &root,
        r#"
printf '%s\n' "$*" >> "$TMUX_TEST_LOG"
case "$3" in
new-session) printf '$42\n';;
set-option) case "$*" in *@slopworld_host*) exit 1;; esac;;
esac
"#,
    );
    let tmux = Tmux::new("test");
    assert!(tmux
        .spawn("reused-name", "/tmp", 80, 24, &["true".into()], true)
        .await
        .is_err());
    let commands = std::fs::read_to_string(root.join("commands")).unwrap();
    assert!(commands.contains("kill-session -t $42"));
    assert!(!commands.contains("kill-session -t reused-name"));
}

#[tokio::test]
async fn clear_activity_attempts_both_removals_and_reports_failure() {
    let Some(root) = crate::test_support::isolated() else {
        return;
    };
    mock_tmux(
        &root,
        "printf '%s\n' \"$*\" >> \"$TMUX_TEST_LOG\"\nexit 1\n",
    );
    assert!(Tmux::new("test").clear_activity("name").await.is_err());
    let commands = std::fs::read_to_string(root.join("commands")).unwrap();
    assert!(commands.contains(ACTIVITY_STATE));
    assert!(commands.contains(ACTIVITY_SINCE));
}
