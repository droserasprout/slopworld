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

#[test]
fn interleaved_credential_mounts_keep_their_original_order() {
    let Command::Start(start) = command(&[
        "start",
        "--credential-rw",
        "/one=/target1",
        "--credential-ro=/two=/target2",
        "--workspace",
        "/workspace",
        "--credential-rw=/three=/target3",
        "--credential-ro",
        "/four=/target4",
    ])
    .unwrap() else {
        panic!()
    };
    assert_eq!(
        start.credentials,
        vec![
            (false, "/one=/target1".into()),
            (true, "/two=/target2".into()),
            (false, "/three=/target3".into()),
            (true, "/four=/target4".into()),
        ]
    );
}

#[test]
fn docker_logs_receives_help_and_separators_verbatim() {
    for tail in [
        vec!["--help"],
        vec!["-f", "--tail", "100"],
        vec!["--", "--help", "--"],
        vec!["help"],
    ] {
        let mut words = vec!["logs"];
        words.extend(&tail);
        let Command::Logs(args) = command(&words).unwrap() else {
            panic!()
        };
        assert_eq!(args, tail.iter().map(OsString::from).collect::<Vec<_>>());
    }
}

#[test]
fn generated_help_covers_local_and_lifecycle_commands() {
    for name in [
        "paths", "licenses", "build", "doctor", "start", "stop", "restart", "status", "logs",
        "shell", "rm",
    ] {
        assert!(
            matches!(command(&["help", name]).unwrap(), Command::Help(text) if text.to_string().contains("Usage:"))
        );
    }
    assert!(
        matches!(command(&[]).unwrap(), Command::Help(text) if text.to_string().contains("Usage:"))
    );
    assert!(
        matches!(command(&["--version"]).unwrap(), Command::Help(text) if text.to_string().contains(env!("CARGO_PKG_VERSION")))
    );
    assert!(command(&["paths", "unknown"]).is_err());
    assert!(command(&["build", "--unknown"]).is_err());
}

#[cfg(unix)]
#[test]
fn parsing_preserves_non_utf8_paths_and_docker_arguments() {
    use std::os::unix::ffi::OsStringExt;
    let path = OsString::from_vec(b"/work-\xff".to_vec());
    let Command::Start(start) = parse(vec![
        "start".into(),
        "--workspace".into(),
        path.clone(),
        "--credential-ro".into(),
        path.clone(),
    ])
    .unwrap() else {
        panic!()
    };
    assert_eq!(start.workspaces, [PathBuf::from(&path)]);
    assert_eq!(start.credentials, [(true, path.clone())]);
    let Command::Build(source) =
        parse(vec!["build".into(), "--source".into(), path.clone()]).unwrap()
    else {
        panic!()
    };
    assert_eq!(source, PathBuf::from(&path));
    let Command::Logs(options) = parse(vec!["logs".into(), path.clone()]).unwrap() else {
        panic!()
    };
    assert_eq!(options, [path]);
}

#[test]
fn start_scalar_options_keep_last_value_and_validate_port_bounds() {
    for port in ["1", "65535"] {
        let Command::Start(start) = command(&[
            "start",
            "--port",
            "7718",
            "--port",
            port,
            "--memory",
            "4g",
            "--memory=8g",
        ])
        .unwrap() else {
            panic!()
        };
        assert_eq!(start.port, Some(port.parse().unwrap()));
        assert_eq!(start.memory, Some("8g".into()));
    }
}

#[test]
fn command_schema_is_consistent() {
    Cli::command().debug_assert();
}
