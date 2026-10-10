#![allow(
    clippy::unwrap_used,
    reason = "Test setup and assertions fail immediately"
)]
use super::*;
fn command(arguments: &[&str]) -> Result<Command> {
    parse(arguments.iter().map(OsString::from).collect())
}
#[test]
fn rejects_invalid_ports_and_incomplete_or_unknown_options() {
    for port in ["0", "65536", "-1", "abc"] {
        assert!(command(&["start", "--port", port]).is_err());
    }
    for args in [
        &["start", "--workspace"][..],
        &["start", "--unknown", "value"],
        &["stop", "extra"],
        &["build", "--source"],
    ] {
        assert!(command(args).is_err());
    }
}
#[test]
fn preserves_repeated_mounts_and_log_options_with_spaces() {
    let Command::Start(start) = command(&[
        "start",
        "--workspace",
        "/work one",
        "--workspace",
        "/work two",
        "--credential-rw",
        "/auth=/home/slop/.codex/auth.json",
    ])
    .unwrap() else {
        panic!()
    };
    assert_eq!(
        start.workspaces,
        [PathBuf::from("/work one"), PathBuf::from("/work two")]
    );
    assert!(!start.credentials[0].0);
    let Command::Logs(args) = command(&["logs", "--since", "one hour ago"]).unwrap() else {
        panic!()
    };
    assert_eq!(
        args,
        [OsString::from("--since"), OsString::from("one hour ago")]
    );
}
