use super::*;

#[test]
fn startup_keeps_valid_presets_while_reload_rejects_partial_catalogs() {
    let root = std::env::temp_dir().join(format!("slopd-presets-{}", uuid::Uuid::new_v4()));
    let dir = root.join("app_presets");
    std::fs::create_dir_all(&dir).unwrap();
    std::fs::write(dir.join("broken.toml"), "[invalid").unwrap();
    std::fs::write(
        dir.join("tool.toml"),
        "name = 'tool'\nkind = 'agent'\ncmd = 'tool'\n",
    )
    .unwrap();
    // Presets intentionally require lowercase extensions; jukebox accepts either case.
    std::fs::write(dir.join("ignored.TOML"), "[invalid").unwrap();
    let mut startup = Table::default();
    startup.merge_user_dirs(&root);
    assert_eq!(startup.commands.len(), 1);
    assert_eq!(startup.command("tool").unwrap().cmd, "tool");
    Table::try_load_from(&root).unwrap_err();
    std::fs::remove_file(dir.join("broken.toml")).unwrap();
    assert!(
        Table::try_load_from(&root)
            .unwrap()
            .command("tool")
            .is_some()
    );
    std::fs::remove_dir_all(root).unwrap();
}

#[test]
fn builtins_parse_and_name_themselves() {
    let table = load_builtin_table();
    assert_standard_commands(&table);
}

fn load_builtin_table() -> Table {
    let mut table = Table::default();
    for (name, text) in BUILTIN {
        let file: PresetFile = toml::from_str(text)
            .unwrap_or_else(|error| panic!("builtin preset {name} does not parse: {error}"));
        for preset in &file.sandbox {
            assert_eq!(
                &preset.name, name,
                "{name}.toml names a sandbox preset {:?}",
                preset.name
            );
        }
        for command in &file.command {
            assert_eq!(
                &command.name, name,
                "{name}.toml names a command {:?}",
                command.name
            );
            assert!(!command.cmd.is_empty(), "{name} has no command");
        }
        table.merge(file);
    }
    table
}

fn assert_standard_commands(table: &Table) {
    for name in [
        "claude", "opencode", "pi", "bash", "zsh", "fish", "nu", "pwsh", "sh",
    ] {
        assert!(table.command(name).is_some(), "no {name} command preset");
    }
    assert!(
        table.command("shell").is_none(),
        "old shell command preset remains"
    );
    assert_eq!(table.command("claude").unwrap().sandbox, vec!["claude"]);
    assert_eq!(table.command("codex").unwrap().sandbox, vec!["codex"]);
    for name in ["claude", "codex", "opencode", "pi"] {
        assert_eq!(table.command(name).unwrap().kind, CommandKind::Agent);
    }
    for name in ["bash", "zsh", "fish", "nu", "pwsh", "sh"] {
        assert_eq!(table.command(name).unwrap().kind, CommandKind::Shell);
    }
}

#[test]
fn unknown_preset_fields_are_rejected() {
    toml::from_str::<PresetFile>(
        r#"
            [[sandbox]]
            name = "sandbox"
            unexpected = "value"
            "#,
    )
    .unwrap_err();
}

#[test]
fn command_kind_is_required() {
    let file = toml::from_str::<PresetFile>(
        r#"
            [[command]]
            name = "missing-kind"
            cmd = "tool"
            "#,
    );

    file.unwrap_err();
}

#[test]
fn kind_parsing_and_typed_table_dispatch_cover_all_sources() {
    assert_eq!(
        "sandbox_presets".parse::<PresetKind>().unwrap(),
        PresetKind::SandboxPresets
    );
    assert_eq!(
        "app_presets".parse::<PresetKind>().unwrap(),
        PresetKind::AppPresets
    );
    "app".parse::<PresetKind>().unwrap_err();
    assert_eq!(
        "other".parse::<PresetKind>().unwrap_err(),
        "unknown preset kind: other"
    );

    let builtins = Table {
        sandbox: vec![SandboxPreset {
            name: "system".into(),
            ..Default::default()
        }],
        commands: vec![CommandPreset {
            name: "command".into(),
            cmd: "tool".into(),
            ..Default::default()
        }],
    };
    let users = Table {
        sandbox: vec![SandboxPreset {
            name: "user".into(),
            ..Default::default()
        }],
        commands: vec![CommandPreset {
            name: "command".into(),
            cmd: "replacement".into(),
            ..Default::default()
        }],
    };

    assert!(builtins.contains(PresetKind::SandboxPresets, "system"));
    assert!(!builtins.contains(PresetKind::AppPresets, "system"));
    assert!(matches!(
        builtins.definition(PresetKind::SandboxPresets, "system"),
        Some(PresetDefinitionRef::Sandbox(_))
    ));
    assert_eq!(
        builtins
            .source(PresetKind::SandboxPresets, "system", &users)
            .as_str(),
        "system"
    );
    assert_eq!(
        builtins
            .source(PresetKind::SandboxPresets, "user", &users)
            .as_str(),
        "user"
    );
    assert_eq!(
        builtins
            .source(PresetKind::AppPresets, "command", &users)
            .as_str(),
        "override"
    );
    assert_eq!(
        builtins
            .source(PresetKind::SandboxPresets, "missing", &users)
            .as_str(),
        "unknown"
    );
}

#[test]
fn copying_checks_user_and_target_conflicts_before_lookup() {
    let builtins = Table {
        sandbox: vec![
            SandboxPreset {
                name: "builtin".into(),
                ..Default::default()
            },
            SandboxPreset {
                name: "occupied".into(),
                ..Default::default()
            },
        ],
        commands: Vec::new(),
    };
    let users = Table {
        sandbox: vec![SandboxPreset {
            name: "missing".into(),
            ..Default::default()
        }],
        commands: Vec::new(),
    };

    let error = copy_definition(
        PresetKind::SandboxPresets,
        "missing",
        "copy",
        &builtins,
        &users,
    )
    .unwrap_err();
    assert_eq!(
        error.to_string(),
        "that preset already has a user definition"
    );

    let error = copy_definition(
        PresetKind::SandboxPresets,
        "missing",
        "occupied",
        &builtins,
        &Table::default(),
    )
    .unwrap_err();
    assert_eq!(error.to_string(), "the target name already exists");
}

#[test]
fn user_only_sandbox_delete_checks_command_and_sandbox_dependencies() {
    let builtins = Table::default();
    let command_users = Table {
        sandbox: vec![SandboxPreset {
            name: "user-only".into(),
            ..Default::default()
        }],
        commands: vec![CommandPreset {
            name: "dependent-command".into(),
            cmd: "tool".into(),
            sandbox: vec!["user-only".into()],
            ..Default::default()
        }],
    };
    let error = check_delete(
        PresetKind::SandboxPresets,
        "user-only",
        &builtins,
        &command_users,
    )
    .unwrap_err();
    assert!(error.to_string().contains("dependent-command"));

    let sandbox_users = Table {
        sandbox: vec![
            SandboxPreset {
                name: "user-only".into(),
                ..Default::default()
            },
            SandboxPreset {
                name: "dependent-sandbox".into(),
                requires: vec!["user-only".into()],
                ..Default::default()
            },
        ],
        commands: Vec::new(),
    };
    let error = check_delete(
        PresetKind::SandboxPresets,
        "user-only",
        &builtins,
        &sandbox_users,
    )
    .unwrap_err();
    assert!(error.to_string().contains("dependent-sandbox"));

    let override_users = Table {
        sandbox: vec![SandboxPreset {
            name: "builtin".into(),
            ..Default::default()
        }],
        commands: Vec::new(),
    };
    let builtin = Table {
        sandbox: vec![SandboxPreset {
            name: "builtin".into(),
            ..Default::default()
        }],
        commands: Vec::new(),
    };
    check_delete(
        PresetKind::SandboxPresets,
        "builtin",
        &builtin,
        &override_users,
    )
    .unwrap();
}

/// User entries replace built-in entries with the same name.
/// Entries with new names extend the preset table.
#[test]
fn user_files_override_by_name() {
    let mut t = Table::default();
    t.merge(
        toml::from_str(
            r#"
                [[sandbox]]
                name = "claude"
                rw = ["~/.claude"]
                [[command]]
                name = "claude"
                kind = "agent"
                cmd = "claude"
                "#,
        )
        .unwrap(),
    );
    t.merge(
        toml::from_str(
            r#"
                [[sandbox]]
                name = "claude"
                rw = ["~/overridden"]
                [[command]]
                name = "claude"
                kind = "agent"
                cmd = "claude --model opus"
                sandbox = ["claude", "docker"]
                [[command]]
                name = "codex"
                kind = "agent"
                cmd = "codex --yolo"
                "#,
        )
        .unwrap(),
    );

    assert_eq!(t.commands.len(), 2);
    assert_eq!(t.command("claude").unwrap().cmd, "claude --model opus");
    assert_eq!(t.command("codex").unwrap().cmd, "codex --yolo");
    assert_eq!(t.sandbox.len(), 1);
    assert_eq!(t.sandbox("claude").unwrap().rw, vec!["~/overridden"]);
}

#[test]
fn saving_one_preset_does_not_reserialize_an_unrelated_file() {
    let dir = std::env::temp_dir().join(format!("slopd-presets-{}", std::process::id()));
    drop(std::fs::remove_dir_all(&dir));
    std::fs::create_dir_all(dir.join("sandbox_presets")).unwrap();
    let untouched = dir.join("sandbox_presets/other.toml");
    let original = "# keep this comment\nname = \"other\"\ndescription = \"handwritten\"\n";
    std::fs::write(&untouched, original).unwrap();

    save_definition_in(
        &dir,
        &PresetDefinition::Sandbox(Box::new(SandboxPreset {
            name: "changed".into(),
            ..Default::default()
        })),
    )
    .unwrap();

    assert_eq!(std::fs::read_to_string(untouched).unwrap(), original);
    assert!(dir.join("sandbox_presets/changed.toml").exists());
    drop(std::fs::remove_dir_all(dir));
}

#[test]
fn user_files_are_direct_definitions_in_their_kind_directory() {
    let dir = std::env::temp_dir().join(format!("slopd-preset-direct-{}", std::process::id()));
    drop(std::fs::remove_dir_all(&dir));
    save_definition_in(
        &dir,
        &PresetDefinition::Command(Box::new(CommandPreset {
            name: "tool".into(),
            cmd: "tool".into(),
            ..Default::default()
        })),
    )
    .unwrap();

    let text = std::fs::read_to_string(dir.join("app_presets/tool.toml")).unwrap();
    assert!(text.contains("name = \"tool\""));
    assert!(!text.contains("[[command]]"));
    let loaded = Table::try_load_from(&dir).unwrap();
    assert_eq!(loaded.command("tool").unwrap().cmd, "tool");
    drop(std::fs::remove_dir_all(dir));
}

#[test]
fn generated_host_control_cannot_be_downgraded_and_legacy_warnings_survive() {
    let mut preset = SandboxPreset {
        escapes: "uses a credential".into(),
        ..Default::default()
    };
    assert_eq!(preset.warning_level(), WarningLevel::Sensitive);
    preset.escapes.clear();
    for (tmux, daemon_config) in [(true, false), (false, true)] {
        preset.tmux = tmux;
        preset.daemon_config = daemon_config;
        assert_eq!(preset.warning_level(), WarningLevel::HostControl);
    }
}

#[test]
fn warning_levels_round_trip_and_reject_unknown_values() {
    for level in [
        WarningLevel::None,
        WarningLevel::Sensitive,
        WarningLevel::HostControl,
    ] {
        let preset = SandboxPreset {
            name: "custom".into(),
            warning_level: level,
            ..Default::default()
        };
        let saved = toml::to_string(&preset).unwrap();
        let loaded: SandboxPreset = toml::from_str(&saved).unwrap();
        assert_eq!(loaded.warning_level, level);
    }
    toml::from_str::<SandboxPreset>("name='bad'\nwarning_level='safe'").unwrap_err();
}
