#![allow(
    clippy::unwrap_used,
    reason = "Test setup and assertions fail immediately"
)]
#![cfg(unix)]
use super::*;
use std::{fs, os::unix::fs::PermissionsExt, path::PathBuf};

struct Fixture {
    root: tempfile::TempDir,
    settings: Settings,
    docker: Docker,
    workspace: PathBuf,
}
impl Fixture {
    fn new() -> Self {
        let root = tempfile::tempdir().unwrap();
        let settings = crate::test_support::settings(root.path());
        let workspace = root.path().join("work with spaces");
        fs::create_dir(&workspace).unwrap();
        let executable = root.path().join("docker");
        // No Docker service or global environment changes: each fixture owns its transport.
        fs::write(
            &executable,
            r##"#!/bin/sh
here=$(dirname "$0")
printf 'CALL\n' >> "$here/calls"
printf '%s\n' "$@" >> "$here/calls"
case "$1" in
 info) exit 0 ;;
 container) test -f "$here/exists"; exit $? ;;
 image) test ! -f "$here/no-image"; exit $? ;;
 run)
   for arg in "$@"; do
     case "$arg" in seccomp=*) cp "${arg#seccomp=}" "$here/profile" || exit 10 ;; esac
   done
   test ! -f "$here/fail-run"; exit $? ;;
 *) exit 0 ;;
esac
"##,
        )
        .unwrap();
        fs::set_permissions(&executable, fs::Permissions::from_mode(0o755)).unwrap();
        Self {
            root,
            settings,
            docker: Docker {
                executable: executable.into_os_string(),
            },
            workspace,
        }
    }
    fn start(&self) -> Start {
        Start {
            workspaces: vec![self.workspace.clone()],
            ..Default::default()
        }
    }
    fn calls(&self) -> String {
        fs::read_to_string(self.root.path().join("calls")).unwrap()
    }
}
#[test]
fn creation_and_doctor_share_embedded_security_policy() {
    let fixture = Fixture::new();
    run_with(
        Command::Start(fixture.start()),
        &fixture.settings,
        &fixture.docker,
    )
    .unwrap();
    let calls = fixture.calls();
    for flag in [
        "--cap-drop\nALL",
        "--user\n1000:1000",
        "--read-only",
        "systempaths=unconfined",
        "/dev/net/tun",
        "127.0.0.1:7718:7718",
        "--memory\n8g",
        "--cpus\n4",
        "--pids-limit\n4096",
    ] {
        assert!(calls.contains(flag), "{flag}");
    }
    assert!(calls.contains(&format!(
        "source={},target={}",
        fixture.workspace.display(),
        fixture.workspace.display()
    )));
    let profile: serde_json::Value =
        serde_json::from_slice(&fs::read(fixture.root.path().join("profile")).unwrap()).unwrap();
    let embedded: serde_json::Value =
        serde_json::from_slice(include_bytes!("../seccomp.json")).unwrap();
    assert_eq!(profile, embedded);
    let seccomp = calls
        .lines()
        .find_map(|line| line.strip_prefix("seccomp="))
        .unwrap();
    assert!(!std::path::Path::new(seccomp).exists());
    run_with(Command::Doctor, &fixture.settings, &fixture.docker).unwrap();
    assert!(fixture.calls().ends_with("test-image\nslopcar-doctor\n"));
}
#[test]
fn existing_container_reuses_mounts_and_rejects_changes_before_seeding() {
    let fixture = Fixture::new();
    fs::write(fixture.root.path().join("exists"), "").unwrap();
    run_with(
        Command::Start(Start::default()),
        &fixture.settings,
        &fixture.docker,
    )
    .unwrap();
    assert!(fixture.calls().ends_with("CALL\nstart\ntest-car\n"));
    assert!(
        run_with(
            Command::Start(fixture.start()),
            &fixture.settings,
            &fixture.docker
        )
        .is_err()
    );
    assert!(!fixture.settings.config.exists());
}
#[test]
fn failed_creation_keeps_config_for_retry_and_remove_preserves_state() {
    let fixture = Fixture::new();
    fs::write(fixture.root.path().join("fail-run"), "").unwrap();
    assert!(
        run_with(
            Command::Start(fixture.start()),
            &fixture.settings,
            &fixture.docker
        )
        .is_err()
    );
    let original = fs::read(fixture.settings.config.join("config.toml")).unwrap();
    fs::remove_file(fixture.root.path().join("fail-run")).unwrap();
    run_with(
        Command::Start(fixture.start()),
        &fixture.settings,
        &fixture.docker,
    )
    .unwrap();
    assert_eq!(
        fs::read(fixture.settings.config.join("config.toml")).unwrap(),
        original
    );
    fs::write(fixture.root.path().join("exists"), "").unwrap();
    for _ in 0..2 {
        run_with(Command::Remove, &fixture.settings, &fixture.docker).unwrap();
    }
    assert_eq!(
        fs::read(fixture.settings.config.join("config.toml")).unwrap(),
        original
    );
    assert!(fixture.settings.data.is_dir());
}
#[test]
fn invalid_mounts_or_missing_image_leave_state_uncreated() {
    let fixture = Fixture::new();
    let start = Start {
        workspaces: vec![fixture.settings.home.clone()],
        ..Default::default()
    };
    assert!(run_with(Command::Start(start), &fixture.settings, &fixture.docker).is_err());
    assert!(!fixture.settings.config.exists());
    assert!(!fixture.calls().contains("CALL\nrun\n"));
    fs::write(fixture.root.path().join("no-image"), "").unwrap();
    assert!(
        run_with(
            Command::Start(fixture.start()),
            &fixture.settings,
            &fixture.docker
        )
        .is_err()
    );
    assert!(!fixture.settings.config.exists());
}

#[test]
fn build_uses_explicit_checkout_and_preserves_source_paths_with_spaces() {
    let fixture = Fixture::new();
    let checkout = fixture.root.path().join("checkout with spaces");
    fs::create_dir_all(checkout.join("slopcar")).unwrap();
    fs::create_dir(checkout.join("slopd")).unwrap();
    fs::write(checkout.join("slopcar/Dockerfile"), "FROM scratch").unwrap();
    fs::write(checkout.join("slopd/Cargo.toml"), "").unwrap();
    run_with(
        Command::Build(checkout.clone()),
        &fixture.settings,
        &fixture.docker,
    )
    .unwrap();
    assert!(fixture.calls().ends_with(&format!(
        "CALL\nbuild\n--tag\ntest-image\n--file\n{}/slopcar/Dockerfile\n{}\n",
        checkout.display(),
        checkout.display()
    )));
    assert!(
        run_with(
            Command::Build(fixture.workspace.clone()),
            &fixture.settings,
            &fixture.docker
        )
        .is_err()
    );
}
