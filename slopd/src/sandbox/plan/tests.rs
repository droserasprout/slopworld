use super::*;

#[test]
fn lowering_preserves_wrappers_and_section_order() {
    let plan = LaunchPlan {
        cache_dirs: Vec::new(),
        session: "a".into(),
        limits: vec!["systemd-run".into(), "--".into()],
        pasta: vec!["pasta".into(), "--".into()],
        bwrap: vec!["bwrap".into(), "--clearenv".into()],
        environment: vec!["--setenv".into(), "EMPTY".into(), String::new()],
        mounts: vec!["--bind".into(), "/host".into(), "/guest".into()],
        command: vec!["agent".into(), "two words".into()],
        known_secrets: vec!["worker-secret".into()],
    };
    assert_eq!(
        plan.lower(),
        vec![
            "systemd-run",
            "--",
            "pasta",
            "--",
            "bwrap",
            "--clearenv",
            "--bind",
            "/host",
            "/guest",
            "--setenv",
            "EMPTY",
            "",
            "--",
            "agent",
            "two words"
        ]
    );
}

#[test]
fn environment_and_sensitive_command_values_are_redacted_structurally() {
    let plan = LaunchPlan {
        cache_dirs: Vec::new(),
        session: "a".into(),
        limits: Vec::new(),
        pasta: Vec::new(),
        bwrap: Vec::new(),
        environment: vec![
            "--setenv".into(),
            "SLOPD_TOKEN".into(),
            "worker-secret".into(),
            "PATH=/secret/path".into(),
        ],
        mounts: Vec::new(),
        command: vec![
            "agent".into(),
            "--api-key".into(),
            "worker-secret".into(),
            "--model=opus".into(),
            "unknown positional".into(),
        ],
        known_secrets: vec!["worker-secret".into()],
    };
    let view = plan.view();
    assert_eq!(view.environment[2], REDACTED);
    assert_eq!(view.environment[3], "PATH=<redacted>");
    assert_eq!(view.command[1], "--api-key");
    assert_eq!(view.command[2], REDACTED);
    assert_eq!(view.command[3], UNKNOWN_ARG);
    assert_eq!(view.command[4], UNKNOWN_ARG);
    assert!(
        !serde_json::to_string(&view)
            .unwrap()
            .contains("worker-secret")
    );
}

#[test]
fn restart_redaction_does_not_need_the_original_secret() {
    let view = PlanView {
        version: 1,
        session: "worker".into(),
        limits: Vec::new(),
        pasta: Vec::new(),
        bwrap: Vec::new(),
        environment: vec!["--setenv".into(), "SLOPD_TOKEN".into(), "old-secret".into()],
        mounts: Vec::new(),
        command: vec!["agent".into(), "--token=old-secret".into()],
        argv: Vec::new(),
    };
    let safe = sanitize_view(view);
    let text = serde_json::to_string(&safe).unwrap();
    assert!(!text.contains("old-secret"));
    assert!(text.contains(UNKNOWN_ARG));
}

#[test]
fn live_process_and_tmux_diagnostics_use_the_same_safe_boundary() {
    let process = sanitize_process_argv(&[
        "agent".into(),
        "--worker-token".into(),
        "worker-secret".into(),
        "SLOPD_TOKEN=worker-secret".into(),
        "--model=opus".into(),
    ]);
    assert!(process.iter().all(|arg| arg == UNKNOWN_ARG));
    let hostile = sanitize_process_argv(&[
        "descendant-secret".into(),
        "https://user:secret@host/?q=value".into(),
        "SECRET=value".into(),
        "--secret-token=value".into(),
        "--help".into(),
    ]);
    assert_eq!(
        hostile,
        [UNKNOWN_ARG, UNKNOWN_ARG, UNKNOWN_ARG, UNKNOWN_ARG, "--help"]
    );
    assert_eq!(
        sanitize_diagnostic("tmux: worker-secret appeared in the command"),
        "details omitted by launch redaction policy"
    );
}

#[test]
fn unknown_assignments_do_not_preserve_url_credentials() {
    let args = vec![
        "agent".into(),
        "https://user:secret@host/?q=value".into(),
        "https://user:secret@host/?token=value".into(),
    ];
    let safe = sanitize_command(&args, &[]);
    assert_eq!(safe, ["agent", UNKNOWN_ARG, UNKNOWN_ARG]);
}

#[test]
fn shell_quote_keeps_argument_boundaries_for_special_values() {
    assert_eq!(shell_quote(""), "''");
    assert_eq!(shell_quote("a b"), "'a b'");
    assert_eq!(shell_quote("a'b"), "'a'\\''b'");
}

#[cfg(unix)]
#[test]
fn saved_projection_is_private_and_replaces_atomically() {
    use std::os::unix::fs::PermissionsExt;

    let dir = std::env::temp_dir().join(format!(
        "slopd-launch-plan-{}-{}",
        std::process::id(),
        uuid::Uuid::new_v4()
    ));
    std::fs::create_dir_all(&dir).unwrap();
    let path = dir.join("launch-plan.json");
    let plan = LaunchPlan {
        cache_dirs: Vec::new(),
        session: "worker".into(),
        limits: Vec::new(),
        pasta: Vec::new(),
        bwrap: Vec::new(),
        environment: vec![
            "--setenv".into(),
            "SLOPD_TOKEN".into(),
            "worker-secret".into(),
        ],
        mounts: Vec::new(),
        command: vec!["agent".into(), "--token".into(), "worker-secret".into()],
        known_secrets: vec!["worker-secret".into()],
    };

    plan.save_at(&path).unwrap();
    let first = std::fs::read_to_string(&path).unwrap();
    assert!(!first.contains("worker-secret"));
    assert_eq!(
        std::fs::metadata(&path).unwrap().permissions().mode() & 0o777,
        0o600
    );
    assert!(!dir.join("launch-plan.json.tmp").exists());

    let mut replacement = plan.clone();
    replacement.session = "replacement".into();
    replacement.save_at(&path).unwrap();
    assert_eq!(
        serde_json::from_str::<PlanView>(&std::fs::read_to_string(&path).unwrap())
            .unwrap()
            .session,
        "replacement"
    );

    std::fs::remove_dir_all(dir).unwrap();
}

#[test]
fn overlapping_secrets_are_redacted_independent_of_order() {
    for secrets in [
        vec!["token", "token-long"],
        vec!["token-long", "token"],
        vec!["aba", "bab"],
    ] {
        let secrets = secrets.into_iter().map(str::to_owned).collect::<Vec<_>>();
        let input = if secrets[0] == "aba" {
            "ababa"
        } else {
            "token-long"
        };
        assert_eq!(redact_known(input, &secrets), REDACTED);
    }
}

#[test]
fn known_secrets_in_session_and_assignment_keys_are_redacted() {
    let plan = LaunchPlan {
        cache_dirs: Vec::new(),
        session: "SECRET-worker".into(),
        limits: vec![],
        pasta: vec![],
        bwrap: vec![],
        mounts: vec![],
        environment: vec!["SECRET_KEY=value".into()],
        command: vec!["SECRET_KEY=value".into(), "SECRET_KEY=value".into()],
        known_secrets: vec!["SECRET".into()],
    };
    let view = plan.view();
    assert_eq!(view.environment, ["<redacted>_KEY=<redacted>"]);
    assert_eq!(
        view.command,
        ["<redacted>_KEY=<redacted>", "<redacted>_KEY=<redacted>"]
    );
    assert!(!serde_json::to_string(&view).unwrap().contains("SECRET"));
}

#[test]
fn modified_saved_plan_cannot_reintroduce_unknown_credentials() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    let session = SessionCfg {
        name: "configured".into(),
        state_id: uuid::Uuid::new_v4().to_string(),
        ..Default::default()
    };
    let dir = super::super::state_dir(&session).unwrap();
    std::fs::create_dir_all(&dir).unwrap();
    let scope = format!("--unit=slopworld-{}.scope", uuid::Uuid::new_v4());
    let hostile = PlanView {
        version: 1,
        session: "unknown-secret".into(),
        limits: vec!["unknown-secret".into(), scope.clone()],
        pasta: vec!["unknown-secret".into()],
        bwrap: vec!["unknown-secret".into()],
        environment: vec!["unknown-secret=value".into()],
        mounts: vec!["unknown-secret".into()],
        command: vec!["unknown-secret".into(), "--help".into()],
        argv: vec!["unknown-secret".into()],
    };
    std::fs::write(
        dir.join("launch-plan.json"),
        serde_json::to_string(&hostile).unwrap(),
    )
    .unwrap();
    let safe = read(&session).unwrap().unwrap();
    assert_eq!(safe.session, "configured");
    assert_eq!(safe.limits[1], scope);
    assert_eq!(safe.command, [UNKNOWN_ARG, "--help"]);
    assert!(
        !serde_json::to_string(&safe)
            .unwrap()
            .contains("unknown-secret")
    );
}
