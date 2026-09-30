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
async fn systemd_startup_uses_distinct_units_for_sockets_and_restarts() {
    let Some(root) = crate::test_support::isolated() else {
        return;
    };
    mock_tmux(
        &root,
        r#"
printf '%s\n' "$*" >> "$TMUX_TEST_LOG"
case "$3" in
list-sessions)
    if [ -f "$TMUX_TEST_LOG.ready.$2" ]; then
        printf 'no sessions' >&2
    else
        printf 'no server running on socket' >&2
    fi
    exit 1;;
start-server) : > "$TMUX_TEST_LOG.ready.$2";;
esac
"#,
    );
    let runner = root.join("bin/systemd-run");
    std::fs::write(
        &runner,
        r#"#!/bin/sh
printf '%s\n' "$@" >> "$TMUX_TEST_LOG.systemd"
while [ "$1" != '--' ]; do shift; done
shift
exec "$@"
"#,
    )
    .unwrap();
    std::fs::set_permissions(runner, std::fs::Permissions::from_mode(0o700)).unwrap();
    std::env::set_var("TMUX_TMPDIR", root.join("sockets"));

    Tmux::new("first").ensure_server().await.unwrap();
    Tmux::new("second").ensure_server().await.unwrap();
    std::fs::remove_file(root.join("commands.ready.first")).unwrap();
    Tmux::new("first").ensure_server().await.unwrap();

    let commands = std::fs::read_to_string(root.join("commands.systemd")).unwrap();
    let units: std::collections::HashSet<_> = commands
        .lines()
        .filter(|line| line.starts_with("--unit=slopworld-tmux-"))
        .collect();
    assert_eq!(units.len(), 3, "each server attempt owns a distinct unit");
    for flag in [
        "--property=Type=forking",
        "--collect",
        "--setenv=TMUX_TMPDIR",
    ] {
        assert_eq!(commands.lines().filter(|line| *line == flag).count(), 3);
    }
    assert!(commands.contains("tmux\n-L\nfirst\nstart-server\n;\nset-option\n-s\nexit-empty\noff"));
    assert!(commands.contains("tmux\n-L\nsecond\nstart-server\n;\nset-option\n-s\nexit-empty\noff"));
}

#[tokio::test]
async fn systemd_startup_preserves_the_explicit_fixture_socket_path() {
    let Some(root) = crate::test_support::isolated() else {
        return;
    };
    mock_tmux(
        &root,
        r#"
case "$3" in
list-sessions)
    if [ -f "$TMUX_TEST_LOG.ready" ]; then
        printf 'no sessions' >&2
    else
        printf 'no server running on socket' >&2
    fi
    exit 1;;
start-server) : > "$TMUX_TEST_LOG.ready";;
esac
"#,
    );
    let runner = root.join("bin/systemd-run");
    std::fs::write(
        &runner,
        r#"#!/bin/sh
printf '%s\n' "$@" >> "$TMUX_TEST_LOG.systemd"
while [ "$1" != '--' ]; do shift; done
shift
exec "$@"
"#,
    )
    .unwrap();
    std::fs::set_permissions(runner, std::fs::Permissions::from_mode(0o700)).unwrap();
    let socket = root.join("socket");
    Tmux::new(socket.to_str().unwrap())
        .ensure_server()
        .await
        .unwrap();
    let commands = std::fs::read_to_string(root.join("commands.systemd")).unwrap();
    assert!(commands.contains(&format!("tmux\n-S\n{}\nstart-server", socket.display())));
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
async fn successful_inline_command_without_ready_server_stops_session_creation() {
    let Some(root) = crate::test_support::isolated() else {
        return;
    };
    mock_tmux(
        &root,
        r#"
printf '%s\n' "$*" >> "$TMUX_TEST_LOG"
case "$3" in
list-sessions) printf 'no server running on socket' >&2; exit 1;;
esac
"#,
    );
    let error = Tmux::new("test")
        .spawn("name", "/tmp", 80, 24, &["true".into()], false)
        .await
        .unwrap_err();
    assert!(error.to_string().contains("left no ready server"));
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
