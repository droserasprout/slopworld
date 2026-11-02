use super::*;

#[test]
fn launch_trust_is_scoped_to_workers() {
    use crate::config::{Config, ProjectCfg, SessionCfg};

    let cfg = Config::default();
    let project = ProjectCfg {
        name: "p".into(),
        dir: "/tmp".into(),
        ..Default::default()
    };
    for worker in [false, true] {
        let session = SessionCfg {
            name: "codex-trust-test".into(),
            state_id: uuid::Uuid::new_v4().to_string(),
            cmd: Some("codex --no-daemon --yolo".into()),
            worker,
            ..Default::default()
        };
        let plan = super::super::build_plan(
            &cfg,
            &session,
            &project,
            &[],
            &crate::desktop::Environment::inherited(),
        )
        .unwrap();
        assert_eq!(
            plan.command.iter().any(|arg| arg.starts_with("projects=")),
            worker
        );
    }
}

#[test]
fn trust_override_preserves_literal_checkout_paths_and_command_arguments() {
    for directory in ["/work/repo", "/work/a.b/quote\" slash\\ space ' 日本語"] {
        let mut argv = vec!["/usr/bin/codex".into(), "resume".into(), "--last".into()];
        trust_workspace(&mut argv, directory);
        assert_eq!(argv[1], "-c");
        let config: toml::Value = toml::from_str(&argv[2]).unwrap();
        let projects = config["projects"].as_table().unwrap();
        assert_eq!(projects.len(), 1);
        assert_eq!(projects[directory]["trust_level"].as_str(), Some("trusted"));
        assert_eq!(&argv[3..], &["resume", "--last"]);
    }
}

#[test]
fn other_executables_and_wrappers_do_not_receive_codex_flags() {
    for command in ["claude", "codex-wrapper", "bash", "env"] {
        let original = vec![command.to_owned(), "codex".into()];
        let mut argv = original.clone();
        trust_workspace(&mut argv, "/work/repo");
        assert_eq!(argv, original);
    }
}
