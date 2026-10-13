#![allow(
    clippy::unwrap_used,
    reason = "Test setup and assertions fail immediately"
)]
#![cfg(unix)]
use super::*;
use std::{fs, path::PathBuf};

// Fake Docker tests use an explicit non-root container identity, including in root CI.
fn run_with(command: Command, settings: &Settings, docker: &Docker) -> Result<()> {
    super::run_with(command, settings, docker, || Ok((1000, 1000)))
}

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
        // Read the script through sh instead of executing a freshly written file.
        // Parallel subprocesses can briefly inherit its writer and cause ETXTBSY.
        let docker = Docker::script(&executable);
        Self {
            root,
            settings,
            docker,
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
fn fake_docker_script_runs_while_a_writer_is_open() {
    let fixture = Fixture::new();
    // Model a writer inherited by a parallel subprocess without relying on timing.
    let _writer = fs::OpenOptions::new()
        .write(true)
        .open(fixture.root.path().join("docker"))
        .unwrap();
    fixture.docker.ready().unwrap();
    fixture.docker.run(&["info".into()], true).unwrap();
    assert_eq!(fixture.calls(), "CALL\ninfo\nCALL\ninfo\n");
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
    assert!(calls.contains("--user\n1000:1000"));
    for flag in [
        "--cap-drop\nALL",
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

#[test]
fn non_default_linux_identity_owns_private_tmpfs_mounts() {
    let (_profile, flags) = security((1001, 1002)).unwrap();
    let values: Vec<_> = flags.iter().map(|value| value.to_string_lossy()).collect();
    for path in ["/tmp:", "/run:", "/home/slop/.cache:"] {
        let mount = values.iter().find(|value| value.starts_with(path)).unwrap();
        assert!(mount.ends_with("uid=1001,gid=1002"));
    }
    assert!(values.iter().any(|value| value.contains("mode=0700")));
}

#[test]
#[cfg(target_os = "linux")]
fn fresh_linux_state_is_accessible_to_uid_1001() {
    const CHILD: &str = "SLOPCAR_TEST_UID_1001";
    if std::env::var_os(CHILD).is_none() {
        let output = std::process::Command::new("unshare")
            .args(["--user", "--map-user=1001", "--map-group=1001"])
            .arg(std::env::current_exe().unwrap())
            .args([
                "--exact",
                "lifecycle::tests::fresh_linux_state_is_accessible_to_uid_1001",
                "--nocapture",
            ])
            .env(CHILD, "1")
            .output()
            .unwrap();
        assert!(
            output.status.success(),
            "{}",
            String::from_utf8_lossy(&output.stderr)
        );
        return;
    }
    use std::os::unix::fs::{MetadataExt, PermissionsExt};
    assert_eq!(container_identity().unwrap(), (1001, 1001));
    let fixture = Fixture::new();
    super::run_with(
        Command::Start(fixture.start()),
        &fixture.settings,
        &fixture.docker,
        container_identity,
    )
    .unwrap();
    assert!(fixture.calls().contains("--user\n1001:1001"));
    for path in [&fixture.settings.config, &fixture.settings.data] {
        let metadata = fs::metadata(path).unwrap();
        assert_eq!(metadata.uid(), 1001);
        assert_eq!(metadata.permissions().mode() & 0o777, 0o700);
        fs::write(path.join("writable"), "yes").unwrap();
    }
}
