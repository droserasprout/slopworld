use super::{
    Config, DEFAULT_SUMMARY_PROMPT, DEFAULT_WORKER_PROMPT, DnsConfig, FileActionMode,
    HostTerminalCfg, LibraryItemCfg, LibraryItemKind, LibraryItemLink, Limits, NetworkMode,
    ProjectCfg, SessionCfg, TOKEN_REDACTED, TitlePolicy, expand, mount_target, redact_token_text,
};
use crate::paths::temp_dir;
use crate::storage::workspace::{Record, Store};

#[test]
fn dns_defaults_to_resolved_and_round_trips_on_the_agent() {
    let session: SessionCfg = toml::from_str(
        "name = 'agent'\n[dns]\nmode = 'servers'\nservers = ['10.0.0.53', '10.0.0.54']",
    )
    .unwrap();
    assert_eq!(
        toml::from_str::<SessionCfg>("name = 'agent'").unwrap().dns,
        DnsConfig::Resolved
    );
    assert_eq!(
        Config::default().dns_of(&session, &ProjectCfg::default()),
        session.dns
    );
    assert_eq!(
        crate::sandbox::dns_servers(&session.dns),
        vec!["10.0.0.53", "10.0.0.54"]
    );
    let back: SessionCfg = toml::from_str(&toml::to_string_pretty(&session).unwrap()).unwrap();
    assert_eq!(back.dns, session.dns);
}

#[test]
fn mount_destinations_accept_absolute_and_project_relative_paths() {
    let project = ProjectCfg {
        dir: "/work/repo".into(),
        ..Default::default()
    };
    assert_eq!(mount_target(&project, "/mnt/shared"), "/mnt/shared");
    assert_eq!(mount_target(&project, "vendor"), "/work/repo/vendor");
    assert_eq!(mount_target(&project, "."), "/work/repo");
}

#[test]
fn dns_validation_rejects_empty_duplicate_and_too_many_servers() {
    for dns in [
        DnsConfig::Servers {
            servers: Vec::new(),
        },
        DnsConfig::Servers {
            servers: vec!["10.0.0.53".parse().unwrap(), "10.0.0.53".parse().unwrap()],
        },
        DnsConfig::Servers {
            servers: vec![
                "10.0.0.51".parse().unwrap(),
                "10.0.0.52".parse().unwrap(),
                "10.0.0.53".parse().unwrap(),
            ],
        },
    ] {
        assert!(dns.validate("project repo").is_err());
    }
    DnsConfig::Resolved.validate("project repo").unwrap();
}

#[test]
fn resource_limits_are_agent_owned_and_reject_zero() {
    let session = Limits {
        memory_mb: Some(2048),
        pids: None,
        nofile: Some(1024),
        cpu_pct: None,
    };

    assert!(Limits::default().is_empty());
    assert!(!session.is_empty());
    session.validate().unwrap();

    for invalid in [
        Limits {
            memory_mb: Some(0),
            ..Default::default()
        },
        Limits {
            pids: Some(0),
            ..Default::default()
        },
        Limits {
            nofile: Some(0),
            ..Default::default()
        },
        Limits {
            cpu_pct: Some(0),
            ..Default::default()
        },
    ] {
        assert!(invalid.validate().is_err());
    }
}

#[test]
fn automatic_titles_are_opt_in_and_once_parses() {
    assert_eq!(Config::default().daemon.agent_titles, TitlePolicy::Never);
    assert_eq!(Config::default().daemon.pi_titles, TitlePolicy::Always);
    assert_eq!(Config::default().daemon.task_summaries, TitlePolicy::Never);
    assert_eq!(Config::default().daemon.title_min_chars, 0);
    let mut cfg = Config::default();
    cfg.daemon.agent_titles = TitlePolicy::Once;
    cfg.daemon.pi_titles = TitlePolicy::Never;
    cfg.daemon.task_summaries = TitlePolicy::Once;
    cfg.daemon.title_min_chars = 42;
    let back = super::settings::document::replace(
        &Config::default(),
        &toml::to_string_pretty(&cfg.settings).unwrap(),
    )
    .unwrap()
    .candidate;
    assert_eq!(back.daemon.agent_titles, TitlePolicy::Once);
    assert_eq!(back.daemon.pi_titles, TitlePolicy::Never);
    assert_eq!(back.daemon.task_summaries, TitlePolicy::Once);
    assert_eq!(back.daemon.title_model, cfg.daemon.title_model);
    assert_eq!(back.daemon.summary_prompt, cfg.daemon.summary_prompt);
    assert_eq!(back.daemon.title_min_chars, 42);
}

#[test]
fn worker_prompt_defaults() {
    let instructions = &Config::default().daemon.instructions;
    assert_eq!(instructions.worker_prompt, DEFAULT_WORKER_PROMPT);
    assert!(!instructions.worker_prompt.contains("SLOPWORLD_TASK_ID"));
    assert_eq!(
        Config::default().daemon.summary_prompt,
        DEFAULT_SUMMARY_PROMPT
    );
}

/// Redact nonempty tokens from client responses.
/// Preserve an empty token so the client can identify a daemon without authentication.
#[test]
fn a_set_token_is_redacted_and_an_empty_one_is_left() {
    let mut cfg = Config::default();
    cfg.daemon.token = "s3cr3t".into();
    assert_eq!(cfg.redacted().daemon.token, TOKEN_REDACTED);

    cfg.daemon.token = String::new();
    assert_eq!(cfg.redacted().daemon.token, "");
}

/// Redact only the nonempty daemon token in the raw editor's text.
/// Preserve other token keys, empty values, and comments.
#[test]
fn redacting_the_text_touches_only_the_daemon_token() {
    let text = "\
# keep me
[daemon]
bind = \"127.0.0.1:7717\"
token = \"s3cr3t\"

[[library]]
name = \"x\"
token = \"not-a-daemon-token\"
";
    let out = redact_token_text(text).unwrap();
    assert!(out.contains(&format!("token = \"{TOKEN_REDACTED}\"")));
    assert!(!out.contains("s3cr3t"));
    assert!(out.contains("# keep me"));
    // Preserve a `token` key in another table.
    assert!(out.contains("token = \"not-a-daemon-token\""));

    // Preserve an empty token instead of inserting the redaction sentinel.
    let empty = "[daemon]\ntoken = \"\"\n";
    assert_eq!(redact_token_text(empty).unwrap(), empty);
}

#[test]
fn redacting_quoted_and_multiline_tokens_preserves_parseable_text() {
    for text in [
        "[daemon]\n\"token\" = 'secret' # keep\n",
        "[daemon]\ntoken = \"\"\"first\nsecond\"\"\"\n",
        "daemon = { token = 'secret', bind = '127.0.0.1:7717' }\n",
    ] {
        let redacted = redact_token_text(text).unwrap();
        assert!(
            !redacted.contains("secret")
                && !redacted.contains("first")
                && !redacted.contains("second")
        );
        let value: toml::Value = toml::from_str(&redacted).unwrap();
        assert_eq!(value["daemon"]["token"].as_str(), Some(TOKEN_REDACTED));
        if text.contains("# keep") {
            assert!(redacted.contains("# keep"));
        }
    }
}

/// Preserve the client's redaction sentinel during parsing.
/// The manager uses this sentinel to restore the saved token.
#[test]
fn the_sentinel_round_trips_as_itself() {
    let cfg = toml::from_str::<super::Settings>(&format!(
        "[daemon]\nbind = \"127.0.0.1:7717\"\ntoken = \"{TOKEN_REDACTED}\"\n"
    ))
    .expect("config with the sentinel should parse");
    assert_eq!(cfg.daemon.token, TOKEN_REDACTED);
}

/// Unset variables expand to empty rather than leaving separators that could name `/`.
#[test]
fn a_path_naming_a_variable_this_machine_lacks_is_nothing() {
    assert_eq!(expand("$SLOPD_NO_SUCH_VAR_A/thing"), "");
    assert_eq!(expand("$SLOPD_NO_SUCH_VAR_A/$SLOPD_NO_SUCH_VAR_B"), "");
    assert_eq!(expand("${SLOPD_NO_SUCH_VAR_A}/thing"), "");

    // Preserve literal paths. Expand variables that have values.
    assert_eq!(expand("/usr/lib"), "/usr/lib");
    assert!(expand("$PATH/bin").ends_with("/bin"));
    assert_ne!(expand("$PATH/bin"), "/bin");
    if let Some(home) = dirs::home_dir() {
        assert_eq!(expand("~/x"), home.join("x").to_string_lossy());
    }
}

#[test]
fn library_become_sessions() {
    let mut cfg = Config {
        library: vec![
            toml::from_str(
                r#"
                name = "review diff"
                link = "project"
                project = "slopworld"
                text = "review the working diff"
                "#,
            )
            .unwrap(),
            toml::from_str(
                r#"
                name = "tests"
                link = "project"
                kind = "shell"
                project = "slopworld"
                text = "make test"
                "#,
            )
            .unwrap(),
            toml::from_str(
                r#"
                name = "codex"
                link = "project"
                project = "slopworld"
                text = "have a look"
                command = "codex --yolo"
                "#,
            )
            .unwrap(),
        ],
        ..Default::default()
    };

    cfg.defaults.agent = "pi".into();
    cfg.defaults.shell = "bash".into();
    let sc = cfg.library_item("review diff").unwrap();
    let prompt = cfg.session_for(&sc, "review-diff".into(), sc.project.clone());
    // Store the default preset name instead of its command line.
    assert_eq!(prompt.command, "pi");
    assert_eq!(prompt.cmd, None);
    assert_eq!(cfg.command_of(&prompt), "pi");
    assert_eq!(
        cfg.sandbox_of(&prompt, &Default::default()),
        vec!["global", "pi"]
    );
    assert_eq!(prompt.project, "slopworld");

    let shell = cfg.session_for(
        &cfg.library_item("tests").unwrap(),
        "tests".into(),
        "x".into(),
    );
    assert_eq!(shell.command, "bash");
    assert_eq!(cfg.command_of(&shell), "bash");

    // Preserve a custom command line without selecting an agent preset.
    // Apply only the implicit global sandbox preset.
    let custom = cfg.session_for(
        &cfg.library_item("codex").unwrap(),
        "codex".into(),
        "x".into(),
    );
    assert_eq!(custom.command, "");
    assert_eq!(cfg.command_of(&custom), "codex --yolo");
    assert_eq!(cfg.sandbox_of(&custom, &Default::default()), vec!["global"]);

    // Use the caller's project selection instead of the library item's project.
    let anywhere = cfg.session_for(&sc, "review-diff-2".into(), "elsewhere".into());
    assert_eq!(anywhere.project, "elsewhere");
}

#[test]
fn preset_dependencies_arrive_before_the_preset_that_needs_them() {
    let cfg = Config::default();
    let session = SessionCfg {
        command: "bash".into(),
        sandbox: vec!["systemd".into()],
        ..Default::default()
    };
    assert_eq!(
        cfg.sandbox_of(&session, &Default::default()),
        vec!["global", "dbus", "systemd"]
    );
}

#[test]
fn library_item_links_round_trip() {
    for (link, expected) in [
        ("project", LibraryItemLink::Project),
        ("temp", LibraryItemLink::Temp),
        ("ask", LibraryItemLink::Ask),
    ] {
        let item: LibraryItemCfg = toml::from_str(&format!(
            "name = 'entry'\nlink = '{link}'\nproject = 'slopworld'\ntext = 'carry on'"
        ))
        .unwrap();
        assert_eq!(item.link, expected);
        let back: LibraryItemCfg = toml::from_str(&toml::to_string_pretty(&item).unwrap()).unwrap();
        assert_eq!(back.link, expected);
    }
}

#[test]
fn file_action_modes_round_trip_and_default_to_the_menu() {
    for (mode, expected) in [
        ("", FileActionMode::Ask),
        ("show_result", FileActionMode::ShowResult),
        ("open_terminal", FileActionMode::OpenTerminal),
        ("nothing", FileActionMode::Nothing),
    ] {
        let mut text =
            "name = 'report'\nlink = 'project'\nkind = 'fa'\ncommand = 'file'\n".to_owned();
        if !mode.is_empty() {
            text.push_str(&format!("mode = '{mode}'"));
        }
        let item: LibraryItemCfg = toml::from_str(&text).unwrap();
        assert_eq!(item.mode, expected);
        let saved = toml::to_string_pretty(&item).unwrap();
        if !mode.is_empty() {
            assert!(saved.contains(&format!("mode = \"{mode}\"")));
        }
        let back: LibraryItemCfg = toml::from_str(&saved).unwrap();
        assert_eq!(back.mode, expected);
    }
}

/// Generate a temporary project's directory from its name.
#[test]
fn temp_projects_name_their_own_directory() {
    assert_eq!(temp_dir("scratch"), "/tmp/slopworld/scratch");
    let project: ProjectCfg =
        toml::from_str("name = 'scratch'\ndir = '/tmp/slopworld/scratch'\ntemp = true").unwrap();
    assert!(project.temp);
    let plain: ProjectCfg = toml::from_str("name = 'repo'\ndir = '/home/you/git/repo'").unwrap();
    assert!(!plain.temp);
}

#[test]
fn agent_network_is_independent_of_project() {
    let cfg = Config::default();
    let project = ProjectCfg::default();
    for (mode, expected) in [("none", NetworkMode::None), ("host", NetworkMode::Host)] {
        let session: SessionCfg =
            toml::from_str(&format!("name = 'agent'\nnetwork = '{mode}'")).unwrap();
        assert_eq!(cfg.network_of(&session, &project), expected);
    }
}

/// Preserve the library item's type during TOML serialization and parsing.
/// A shell item must not become a prompt.
#[test]
fn library_round_trip_through_toml() {
    let mut cfg = Config::default();
    cfg.library.push(LibraryItemCfg {
        name: "tests".into(),
        kind: LibraryItemKind::Shell,
        link: LibraryItemLink::Project,
        project: "slopworld".into(),
        text: "make test".into(),
        command: None,
        mode: FileActionMode::Ask,
        builtin: false,
        host: true,
        agent_template: String::new(),
    });

    let sc: LibraryItemCfg =
        toml::from_str(&toml::to_string_pretty(&cfg.library[0]).unwrap()).unwrap();
    assert_eq!(sc.kind, LibraryItemKind::Shell);
    assert_eq!(sc.text, "make test");
    assert!(sc.command.is_none());
}

#[test]
fn auto_resume_is_an_opt_in_session_setting() {
    let old: SessionCfg = toml::from_str("name = 'Ada'").unwrap();
    assert!(!old.auto_resume);

    let enabled = SessionCfg {
        auto_resume: true,
        ..Default::default()
    };
    let text = toml::to_string(&enabled).unwrap();
    assert!(text.contains("auto_resume = true"));
    assert!(toml::from_str::<SessionCfg>(&text).unwrap().auto_resume);
}

#[test]
fn persistent_tmp_is_an_opt_in_session_setting() {
    let old: SessionCfg = toml::from_str("name = 'Ada'").unwrap();
    assert!(!old.persistent_tmp);
    assert!(!toml::to_string(&old).unwrap().contains("persistent_tmp"));

    let enabled = SessionCfg {
        persistent_tmp: true,
        ..Default::default()
    };
    let text = toml::to_string(&enabled).unwrap();
    assert!(text.contains("persistent_tmp = true"));
    assert!(toml::from_str::<SessionCfg>(&text).unwrap().persistent_tmp);
}

/// Include the built-in breadcrumb in the library without saving it in user configuration.
/// Saving it as a user entry would prevent later daemon versions from updating the built-in content.
#[test]
fn the_shipped_breadcrumb_is_offered_but_never_written_down() {
    let cfg = Config::default();
    assert!(cfg.library.is_empty());

    let sc = cfg
        .library_item("Useful tips")
        .expect("shipped with the daemon");
    assert_eq!(sc.kind, LibraryItemKind::Breadcrumb);
    assert!(sc.builtin);
    assert_eq!(sc.text.matches("{{ random_tip }}").count(), 5);
    assert!(cfg.is_builtin_library_item("Useful tips"));
    assert!(
        cfg.library_items_all()
            .iter()
            .any(|s| s.name == "Useful tips" && s.builtin)
    );

    // Omit the built-in entry from saved configuration. Loading must not duplicate it.
    let text = toml::to_string_pretty(&cfg.settings).unwrap();
    assert!(!text.contains("Useful tips"));
    let back = super::settings::document::replace(&cfg, &text)
        .unwrap()
        .candidate;
    assert_eq!(
        back.library_items_all()
            .iter()
            .filter(|s| s.name == "Useful tips")
            .count(),
        1
    );
}

/// A user entry overrides a built-in entry with the same name.
/// Treat the replacement as a user entry that permits editing and deletion.
#[test]
fn a_written_entry_shadows_the_builtin_it_is_named_after() {
    let mut cfg = Config::default();
    cfg.library.push(LibraryItemCfg {
        name: "Useful tips".into(),
        kind: LibraryItemKind::Breadcrumb,
        text: "mine".into(),
        ..Default::default()
    });

    assert_eq!(cfg.library_item("Useful tips").unwrap().text, "mine");
    assert!(!cfg.is_builtin_library_item("Useful tips"));
    assert_eq!(cfg.library_items_all().len(), 1);
}

#[test]
fn assembled_config_view_round_trips_through_toml() {
    let mut cfg = Config::default();
    cfg.projects.push(ProjectCfg {
        name: "repo".into(),
        dir: "/home/you/repo".into(),
        ..Default::default()
    });
    cfg.sessions.push(SessionCfg {
        name: "quiet".into(),
        project: "repo".into(),
        label: Some("manual title".into()),
        network: NetworkMode::None,
        ..Default::default()
    });

    let text = toml::to_string_pretty(&cfg).unwrap();
    let back: Config = toml::from_str(&text).unwrap();

    assert_eq!(back.session("quiet").unwrap().network, NetworkMode::None);
    assert_eq!(
        back.session("quiet").unwrap().label.as_deref(),
        Some("manual title")
    );
    assert_eq!(back.commands.pager, "auto");
    assert_eq!(back.commands.editor, "micro");
}

#[test]
fn host_terminal_records_round_trip_and_default_to_autostart() {
    let tab = HostTerminalCfg {
        name: "repo-bash".into(),
        label: Some("Repository shell".into()),
        project: "repo".into(),
        path: "/home/you/repo/src".into(),
        ..Default::default()
    };
    let text = toml::to_string_pretty(&tab).unwrap();
    assert!(!text.contains("autostart"));
    let back: HostTerminalCfg = toml::from_str(&text).unwrap();
    assert_eq!(back.name, tab.name);
    assert_eq!(back.label, tab.label);
    assert_eq!(back.project, tab.project);
    assert_eq!(back.path, tab.path);
    assert!(back.autostart);
    assert!(back.id.is_empty());
    assert!(!text.contains("id ="));
}

#[test]
fn host_shell_ids_are_valid_unique_and_preserved() {
    let tab: HostTerminalCfg = toml::from_str("name = 'one'\nid = '0123456789abcdef'").unwrap();
    let saved = tab.document(0, None).unwrap();
    assert_eq!(HostTerminalCfg::decode(&saved).unwrap().id, tab.id);
    let store = Store::<HostTerminalCfg>::empty();
    let mut duplicate = tab.clone();
    duplicate.name = "two".into();
    assert!(store.prepare(vec![tab.clone(), duplicate]).is_err());
    for bad in ["../escape", "0123456789abcdeF", "short"] {
        let invalid = HostTerminalCfg {
            id: bad.into(),
            ..tab.clone()
        };
        assert!(store.prepare(vec![invalid]).is_err());
    }
}

#[test]
fn config_rejects_sessions_without_a_valid_state_identity() {
    let session: SessionCfg = toml::from_str("name = 'agent'\nproject = 'repo'").unwrap();
    assert!(Store::<SessionCfg>::empty().prepare(vec![session]).is_err());
    // New in-memory sessions, including short-lived errands, always have an identity.
    assert!(!SessionCfg::default().state_id.is_empty());
}

#[test]
fn records_reject_state_id_path_traversal_absolute_paths_and_invalid_ids() {
    for state_id in ["../escape", "one/two", "/tmp/escape", ".", "safe-state"] {
        let session = SessionCfg {
            name: "agent".into(),
            state_id: state_id.into(),
            ..Default::default()
        };
        assert!(
            Store::<SessionCfg>::empty().prepare(vec![session]).is_err(),
            "{state_id}"
        );
    }
}

#[test]
fn config_rejects_unsafe_or_duplicate_project_names() {
    let project = ProjectCfg {
        id: crate::storage_id::draft_identity(),
        name: "repo.v2".into(),
        dir: "/tmp".into(),
        ..Default::default()
    };
    let store = Store::<ProjectCfg>::empty();
    for name in ["../escape", "one/two", "/tmp/escape", ".", "..", r"one\two"] {
        assert!(
            store
                .prepare(vec![ProjectCfg {
                    name: name.into(),
                    ..project.clone()
                }])
                .is_err(),
            "{name}"
        );
    }
    let duplicate = ProjectCfg {
        id: crate::storage_id::draft_identity(),
        dir: "/tmp/two".into(),
        ..project.clone()
    };
    let error = store
        .prepare(vec![project.clone(), duplicate])
        .err()
        .unwrap()
        .to_string();
    assert!(error.contains("already exists"), "{error}");
    store.prepare(vec![project]).unwrap();
}

#[test]
fn config_rejects_duplicate_state_ids() {
    let first = SessionCfg {
        name: "one".into(),
        ..Default::default()
    };
    let second = SessionCfg {
        name: "two".into(),
        ..first.clone()
    };
    let cfg = Config {
        sessions: vec![first, second],
        ..Default::default()
    };
    let error = super::validate_loaded(&cfg).unwrap_err().to_string();
    assert!(
        error.contains("Two sessions use the same private-state ID"),
        "{error}"
    );
    assert!(Store::<SessionCfg>::empty().prepare(cfg.sessions).is_err());
}

#[test]
fn diagnostic_formatting_redacts_credentials_without_changing_serialization() {
    let mut cfg = Config::default();
    cfg.daemon.token = "root-secret".into();
    cfg.sessions.push(SessionCfg {
        worker_token: Some("worker-secret".into()),
        ..Default::default()
    });
    for text in [
        format!("{cfg:?}"),
        format!("{cfg:#?}"),
        format!("{:?}", cfg.daemon),
        format!("{:?}", cfg.sessions[0]),
    ] {
        assert!(!text.contains("root-secret") && !text.contains("worker-secret"));
        assert!(text.contains(TOKEN_REDACTED));
    }
    assert_eq!(cfg.clone().daemon.token, "root-secret");
    let serialized = toml::to_string(&cfg).unwrap();
    assert!(serialized.contains("root-secret"));
    assert!(!serialized.contains("worker-secret"));
}

#[test]
fn safety_policy_ingestion_rejects_unknown_and_contradictory_fields() {
    for text in [
        "mode = 'resolved'\nservers = ['10.0.0.53']",
        "mode = 'resolved'\nserver = []",
        "mode = 'servers'",
    ] {
        assert!(toml::from_str::<DnsConfig>(text).is_err(), "{text}");
    }
    assert_eq!(
        serde_json::from_value::<DnsConfig>(serde_json::json!({"mode":"resolved", "servers":[]}))
            .unwrap(),
        DnsConfig::Resolved
    );
    toml::from_str::<Limits>("memroy_mb = 512").unwrap_err();
    serde_json::from_value::<Limits>(serde_json::json!({"memroy_mb":512})).unwrap_err();
    serde_json::from_value::<DnsConfig>(
        serde_json::json!({"mode":"resolved", "servers":["10.0.0.53"]}),
    )
    .unwrap_err();
}

#[test]
fn in_memory_state_ids_reject_control_characters() {
    for id in ["a\0b", "a\nb", "a\u{7f}b"] {
        super::state_id_component(id).unwrap_err();
    }
}
