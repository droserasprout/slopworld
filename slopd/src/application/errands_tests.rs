use super::*;

fn template() -> AgentTemplate {
    serde_json::from_value(serde_json::json!({
        "name": "reviewer",
        "defaults": {
            "cmd": "review-agent", "network": "none", "persistent_tmp": true,
            "limits": {"memory_mb": 256}, "autostart": true, "auto_resume": true,
            "sandbox": ["captured"],
            "sandbox_presets": [{"name": "captured", "ro": ["/usr"]}]
        }
    }))
    .unwrap()
}

#[test]
fn explicit_executable_overrides_template_without_losing_portable_settings() {
    let cfg = Config::default();
    for (kind, command) in [
        (LibraryItemKind::Prompt, None),
        (LibraryItemKind::Prompt, Some("  ")),
        (LibraryItemKind::Prompt, Some("custom-agent --flag")),
        (LibraryItemKind::Shell, None),
    ] {
        let item = LaunchContent {
            name: "review".into(),
            kind,
            command: command.map(str::to_owned),
            ..Default::default()
        };
        let (session, _) = build_session(
            &cfg,
            &item,
            &LaunchOptions::default(),
            "allocated",
            "destination".into(),
            Some(template()),
            None,
        );
        let expected =
            if kind == LibraryItemKind::Shell || command.is_some_and(|c| !c.trim().is_empty()) {
                cfg.command_of(&cfg.session_for_command(
                    kind,
                    command,
                    "allocated".into(),
                    "destination".into(),
                ))
            } else {
                "review-agent".into()
            };
        assert_eq!(cfg.command_of(&session), expected);
        assert_eq!(session.name, "allocated");
        assert_eq!(session.project, "destination");
        assert_eq!(session.network, crate::config::NetworkMode::None);
        assert_eq!(session.limits.memory_mb, Some(256));
        assert_eq!(session.sandbox_snapshots[0].ro, ["/usr"]);
        assert!(session.persistent_tmp);
        assert!(!session.autostart && !session.auto_resume);
    }
}

#[test]
fn reader_metadata_uses_display_label_without_changing_execution_identity() {
    for (label, intent, expected) in [
        ("edit-README.md", "edit", Some("README.md")),
        ("README.md", "view", Some("README.md")),
        ("edit-README.md", "", Some("edit-README.md")),
        ("review", "", None),
    ] {
        let options = LaunchOptions {
            context: LaunchContext {
                intent: intent.into(),
                reader: crate::session::ReaderContext {
                    path: "src/README.md".into(),
                    ..Default::default()
                },
                ..Default::default()
            },
            ..Default::default()
        };
        let (session, context) = build_session(
            &Config::default(),
            &LaunchContent {
                name: label.into(),
                ..Default::default()
            },
            &options,
            "opaque-handle",
            "repo".into(),
            None,
            None,
        );
        assert_eq!(session.name, "opaque-handle");
        assert_eq!(session.label.as_deref(), expected);
        if !intent.is_empty() {
            assert_eq!(context.original_label, expected.unwrap());
            assert_eq!(context.reader.path, "src/README.md");
        } else {
            assert!(context.reader.path.is_empty());
        }
    }
}

#[test]
fn cloned_settings_are_owned_and_require_a_resolvable_source_project() {
    let mut source = SessionCfg {
        project: "source".into(),
        persistent_tmp: true,
        network: crate::config::NetworkMode::None,
        ..Default::default()
    };
    source.limits.memory_mb = Some(128);
    assert!(capture_source_settings(&Config::default(), Some(&source)).is_err());
    assert!(
        capture_source_settings(&Config::default(), None)
            .unwrap()
            .is_none()
    );
    let cfg = Config {
        projects: vec![ProjectCfg {
            name: "source".into(),
            ..Default::default()
        }],
        ..Default::default()
    };
    let settings = capture_source_settings(&cfg, Some(&source)).unwrap();
    source.limits.memory_mb = Some(512);
    let (session, _) = build_session(
        &cfg,
        &LaunchContent {
            command: Some("explicit-command".into()),
            ..Default::default()
        },
        &LaunchOptions::default(),
        "clone",
        "destination".into(),
        None,
        settings,
    );
    assert_eq!(session.limits.memory_mb, Some(128));
    assert_eq!(session.network, crate::config::NetworkMode::None);
    assert!(session.persistent_tmp);
    assert_eq!(cfg.command_of(&session), "explicit-command");
    assert_eq!(session.project, "destination");
}

#[test]
fn selection_uses_explicit_checkout_then_clone_default_except_for_temporary_projects() {
    let cfg = Config {
        sessions: vec![SessionCfg {
            name: "source".into(),
            worktree: "inherited".into(),
            project: "repo".into(),
            ..Default::default()
        }],
        ..Default::default()
    };
    let mut request = LaunchRequest {
        content: LaunchContent::default(),
        execution: ExecutionSource::CloneAgent("source".into()),
        destination: LaunchDestination::Project("repo".into()),
        options: LaunchOptions::default(),
    };
    assert_eq!(
        select(&cfg, &request, &[]).unwrap().selected_worktree,
        "inherited"
    );
    request.destination = LaunchDestination::Project("other".into());
    assert!(
        select(&cfg, &request, &[])
            .unwrap()
            .selected_worktree
            .is_empty()
    );
    request.options.worktree = "explicit".into();
    assert_eq!(
        select(&cfg, &request, &[]).unwrap().selected_worktree,
        "explicit"
    );
    request.destination = LaunchDestination::Temporary;
    assert!(
        select(&cfg, &request, &[])
            .err()
            .unwrap()
            .to_string()
            .contains("cannot select a worktree")
    );
    request.options.worktree.clear();
    let selected = select(&cfg, &request, &[]).unwrap();
    assert!(selected.fresh);
    assert!(selected.project_name.is_empty());
    assert!(selected.selected_worktree.is_empty());
    assert_eq!(selected.source.unwrap().name, "source");
    request.execution = ExecutionSource::Host(crate::session::HostLifetime::Disposable);
    let selected = select(&cfg, &request, &[]).unwrap();
    assert!(selected.source.is_none() && selected.template.is_none());
}

#[test]
fn selection_rejects_missing_sources_and_copies_template_settings() {
    let cfg = Config::default();
    let mut request = LaunchRequest {
        content: LaunchContent::default(),
        execution: ExecutionSource::Template("reviewer".into()),
        destination: LaunchDestination::Project("repo".into()),
        options: LaunchOptions::default(),
    };
    assert!(
        select(&cfg, &request, &[])
            .err()
            .unwrap()
            .to_string()
            .contains("no such agent template")
    );
    let mut templates = vec![template()];
    let selected = select(&cfg, &request, &templates).unwrap();
    templates[0].defaults.cmd = Some("changed".into());
    assert_eq!(
        selected.template.unwrap().defaults.cmd.as_deref(),
        Some("review-agent")
    );
    assert!(selected.source.is_none());
    request.execution = ExecutionSource::CloneAgent("missing".into());
    assert!(
        select(&cfg, &request, &templates)
            .err()
            .unwrap()
            .to_string()
            .contains("no such session to clone")
    );
}
