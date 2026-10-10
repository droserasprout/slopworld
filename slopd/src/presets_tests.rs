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
    assert_new_sandbox_presets(&table);
    assert_debug_preset(&table);
    assert_builtin_cache_presets(&table);
    assert_shell_userdata_presets(&table);
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
    // Desktop access is opt-in for clipboard image paste.
    assert!(table.sandbox("codex").unwrap().requires.is_empty());
    assert_eq!(
        table.sandbox("systemd").unwrap().setenv["SYSTEMCTL_FORCE_BUS"],
        "1"
    );
    assert_eq!(
        table.sandbox("gpu").unwrap().dev,
        vec!["/dev/dri", "/dev/kfd"]
    );
}

fn assert_new_sandbox_presets(table: &Table) {
    for name in [
        "go",
        "gh",
        "aws",
        "kube",
        "android-dev",
        "android-debug",
        "ios-dev",
        "ios-debug",
        "slopworld-debug",
    ] {
        let preset = table
            .sandbox(name)
            .unwrap_or_else(|| panic!("no {name} sandbox preset"));
        assert!(!preset.description.is_empty(), "{name} has no description");
    }
    assert_eq!(
        table.sandbox("android-debug").unwrap().requires,
        vec!["android-dev", "gpu", "x11", "wayland"]
    );
    assert_eq!(
        table.sandbox("ios-debug").unwrap().requires,
        vec!["ios-dev"]
    );
    for name in ["android-debug", "ios-debug"] {
        let preset = table.sandbox(name).unwrap();
        assert!(preset.dev.iter().any(|path| path == "/dev/bus/usb"));
        assert!(!preset.escapes.is_empty());
    }
}

fn assert_debug_preset(table: &Table) {
    let debug = table.sandbox("slopworld-debug").unwrap();
    assert!(debug.tmux);
    assert!(debug.daemon_config);
    assert_eq!(
        debug.requires,
        vec![
            "ccache",
            "docker",
            "git",
            "nuget-cache",
            "python",
            "rust-sccache",
            "systemd",
            "wayland",
            "x11",
        ]
    );
    assert_eq!(debug.setenv["RUST_BACKTRACE"], "1");
    for path in [
        "$SLOPWORLD_GAME",
        "~/GOG Games/RimWorld/game",
        "~/.local/share/slopworld/profile",
        "~/.local/share/slopworld-car",
        "~/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios",
        "~/.local/bin",
        "~/.config/systemd/user",
    ] {
        assert!(
            debug.rw.iter().any(|seen| seen == path),
            "debug lacks {path}"
        );
    }
    assert!(
        debug.env.iter().any(|seen| seen == "SLOPCAR_PROFILE"),
        "debug does not forward SLOPCAR_PROFILE"
    );
    for path in ["/sys", "/run/udev", "~/.local/share/applications"] {
        assert!(
            debug.ro.iter().any(|seen| seen == path),
            "debug lacks {path}"
        );
    }
    // The launcher mounts procfs for the private PID namespace.
    assert!(
        !debug
            .ro
            .iter()
            .chain(&debug.rw)
            .chain(&debug.dev)
            .any(|path| path == "/proc")
    );
}

fn assert_builtin_cache_presets(table: &Table) {
    assert_eq!(
        table.sandbox("global").unwrap().ro,
        vec!["/usr", "/etc", "/opt", "~/.local/bin"]
    );
    assert!(table.sandbox("global").unwrap().rw.is_empty());
    assert_eq!(
        table.sandbox("go-cache").unwrap().cache,
        vec!["~/go/pkg/mod", "~/.cache/go-build"]
    );
    assert_eq!(
        table.sandbox("rust-cache").unwrap().rw,
        vec!["~/.rustup/toolchains", "~/.rustup/update-hashes"]
    );
    assert_eq!(
        table
            .sandbox("rust-sccache")
            .unwrap()
            .setenv
            .get("RUSTC_WRAPPER")
            .map(String::as_str),
        Some("sccache")
    );
    assert_eq!(
        table.sandbox("rust-cache").unwrap().cache,
        vec!["~/.cargo/registry", "~/.cargo/git"]
    );
    for name in ["go-cache", "node-cache", "nuget-cache", "ruby-cache"] {
        let preset = table.sandbox(name).unwrap();
        assert!(!preset.cache.is_empty(), "{name} must create its caches");
        assert!(preset.rw.is_empty(), "{name} has only cache directories");
    }
    assert_eq!(
        table.sandbox("python-cache").unwrap().cache,
        vec!["~/.cache/pip", "~/.cache/uv"]
    );
    assert_eq!(
        table.sandbox("python-cache").unwrap().rw,
        vec!["~/.local/share/uv"]
    );
    assert_eq!(
        table.sandbox("ccache").unwrap().cache,
        vec!["~/.cache/ccache"]
    );
    assert_eq!(table.sandbox("ccache").unwrap().rw, vec!["~/.ccache"]);
    assert!(table.sandbox("rust-cache").unwrap().setenv.is_empty());
    assert_eq!(
        table.sandbox("rust-sccache").unwrap().cache,
        vec!["~/.cache/sccache"]
    );
    assert_eq!(
        table.sandbox("rust-sccache").unwrap().requires,
        vec!["rust-cache"]
    );
    assert_eq!(table.sandbox("kube").unwrap().ro, vec!["~/.kube"]);
    for (cache, tool) in [
        ("rust-cache", "rust"),
        ("node-cache", "node"),
        ("python-cache", "python"),
        ("go-cache", "go"),
        ("nuget-cache", "dotnet"),
        ("ruby-cache", "ruby"),
    ] {
        assert_eq!(table.sandbox(cache).unwrap().requires, vec![tool]);
    }
}

fn assert_shell_userdata_presets(table: &Table) {
    // Shell commands select only the executable; matching presets opt in to host dotfiles.
    for name in ["bash", "zsh", "fish", "nu", "pwsh"] {
        assert!(
            table.command(name).unwrap().sandbox.is_empty(),
            "{name} command unexpectedly shares userdata by default"
        );
        let userdata = format!("{name}-userdata");
        let preset = table
            .sandbox(&userdata)
            .unwrap_or_else(|| panic!("no {userdata} sandbox preset"));
        assert!(!preset.ro.is_empty(), "{userdata} has no config paths");
        assert!(!preset.rw.is_empty(), "{userdata} has no userdata paths");
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

/// Check that agent presets keep private state and host-access presets include warnings.
/// These requirements depend on preset data. Test the supplied presets so the UI can show the correct warnings.
#[test]
fn the_agents_keep_their_state_and_the_ways_out_are_marked() {
    let t = Table::load();

    // Agent configuration can include commands that later run on the host.
    // Use private copies instead of writable host configuration directories.
    for name in ["claude", "codex", "pi", "opencode"] {
        let p = t
            .sandbox(name)
            .unwrap_or_else(|| panic!("no {name} preset"));
        assert!(
            !p.private.is_empty(),
            "{name} shares its state with the host"
        );
        assert!(
            p.rw.is_empty(),
            "{name} still binds {:?} read-write on the host's own copy",
            p.rw
        );
        // Permit shared credentials only inside private paths.
        // A shared path outside these directories would provide unrestricted writable access to its host source.
        for s in &p.shared {
            assert!(
                p.private
                    .iter()
                    .any(|priv_| Path::new(s).starts_with(Path::new(priv_))),
                "{name} shares {s}, which is under nothing it keeps private"
            );
        }
    }

    assert_eq!(
        t.sandbox("codex").unwrap().shared,
        vec!["~/.codex/auth.json"],
        "Codex auth must remain shared so a host refresh reaches every session"
    );

    // Require warnings for access to host services or the shared display.
    for name in [
        "docker",
        "podman",
        "dbus",
        "systemd",
        "x11",
        "ssh-agent",
        "1password",
        "gpg-agent",
    ] {
        let p = t
            .sandbox(name)
            .unwrap_or_else(|| panic!("no {name} preset"));
        assert!(
            !p.escapes.is_empty(),
            "{name} is a way out and does not say so"
        );
    }

    // These presets must not report host-service access.
    for name in [
        "rust",
        "rust-cache",
        "go",
        "go-cache",
        "python",
        "python-cache",
        "node",
        "node-cache",
        "dotnet",
        "nuget-cache",
        "ruby",
        "ruby-cache",
        "ccache",
        "git",
        "hg",
        "android-dev",
        "ios-dev",
        "ollama",
        "gpg",
    ] {
        let p = t
            .sandbox(name)
            .unwrap_or_else(|| panic!("no {name} preset"));
        assert!(p.escapes.is_empty(), "{name} is marked as a way out");
    }

    // Keep SSH configuration separate from access to the SSH agent socket.
    // The socket lets a sandbox request signatures from the host.
    assert!(t.sandbox("ssh").unwrap().rw.is_empty());
    assert_eq!(
        t.sandbox("ssh").unwrap().private,
        vec!["/etc/ssh/ssh_config.d"]
    );
    assert_eq!(t.sandbox("ssh-agent").unwrap().rw, vec!["$SSH_AUTH_SOCK"]);
    assert_eq!(t.sandbox("systemd").unwrap().requires, vec!["dbus"]);

    // Configuration-root variables could bypass private mounts.
    // Agent presets use default paths under HOME instead of forwarding these variables.
    for (name, forbidden) in [
        ("claude", "CLAUDE_CONFIG_DIR"),
        ("codex", "CODEX_HOME"),
        ("pi", "PI_CODING_AGENT_DIR"),
        ("opencode", "OPENCODE_CONFIG"),
        ("opencode", "OPENCODE_CONFIG_DIR"),
    ] {
        assert!(
            !t.sandbox(name)
                .unwrap()
                .env
                .iter()
                .any(|env| env == forbidden),
            "{name} forwards {forbidden}, bypassing its private state"
        );
    }
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
