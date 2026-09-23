use super::*;
/// A host errand runs without bwrap. Its argument list ends with the shell command.
#[test]
fn a_host_errand_is_not_sandboxed() {
    let cfg = Config::default();
    let s = SessionCfg {
        name: "a".into(),
        project: "p".into(),
        command: "bash".into(),
        sandbox: vec!["x11".into()],
        ..Default::default()
    };
    let p = ProjectCfg {
        name: "p".into(),
        dir: "/tmp".into(),
        ..Default::default()
    };
    let a = host_argv(&cfg, &s, &p);

    assert_eq!(a[0], "env");
    assert!(!a.iter().any(|x| x == "bwrap" || x == "--clearenv"));
    assert!(a.contains(&format!("TERM={PANE_TERM}")));
    assert!(a.contains(&"SLOPWORLD_PROJECT=p".to_string()));
    // The selected shell command is last in the argument list.
    let want = shell_split(&host_command(&cfg, &s, host_shell().as_deref()));
    assert_eq!(a[a.len() - want.len()..], want[..]);
}

/// Sandbox errands use `[defaults] shell`. Host errands use `$SHELL` when available.
/// An explicit errand command overrides either default.
#[test]
fn a_host_errand_opens_the_login_shell_unless_it_named_one() {
    let cfg = Config::default();
    let bare = SessionCfg {
        command: cfg.defaults.shell.clone(),
        ..Default::default()
    };
    assert_eq!(
        host_command(&cfg, &bare, Some("/usr/bin/zsh")),
        "/usr/bin/zsh"
    );
    // Use the configured command when no host shell is available.
    assert_eq!(host_command(&cfg, &bare, None), cfg.command_of(&bare));

    // Preserve explicit command presets and custom command lines.
    let named = SessionCfg {
        command: "zsh".into(),
        ..Default::default()
    };
    assert_eq!(
        host_command(&cfg, &named, Some("/bin/bash")),
        cfg.command_of(&named)
    );
    let own = SessionCfg {
        command: cfg.defaults.shell.clone(),
        cmd: Some("htop".into()),
        ..Default::default()
    };
    assert_eq!(host_command(&cfg, &own, Some("/bin/bash")), "htop");
}

#[test]
fn foreground_shell_commands_mean_an_idle_host_prompt() {
    assert!(is_shell_command("/bin/bash"));
    assert!(is_shell_command("-zsh"));
    assert!(is_shell_command("fish"));
    assert!(!is_shell_command("python"));
    assert!(!is_shell_command("codex"));
}

/// The session name includes the project name followed by the shell name.
/// Different projects therefore use different terminal names.
#[test]
fn a_host_errand_is_named_for_its_project_and_its_shell() {
    assert_eq!(
        session_name_for("slopworld", Some("/usr/bin/zsh")),
        "slopworld-zsh"
    );
    assert_eq!(session_name_for("tmp", Some("/bin/bash")), "tmp-bash");
    assert_eq!(session_name_for("tmp", Some("fish")), "tmp-fish");
    assert!(!host_session_name("my.project").contains('.'));
    // Missing shell and project values use their respective fallback names.
    assert_eq!(session_name_for("tmp", None), "tmp-shell");
    assert_eq!(session_name_for("", Some("/usr/bin/zsh")), "zsh");
}
/// Each session has a separate private copy that preserves the source directory structure.
/// Paths with the same basename use different destinations.
#[test]
fn a_private_path_is_per_session_and_keeps_its_shape() {
    let a = private_path("one", "/home/u/.config/opencode").unwrap();
    let b = private_path("one", "/home/u/.local/share/opencode").unwrap();
    assert_ne!(a, b);

    assert_ne!(
        private_path("one", "/etc/x").unwrap(),
        private_path("two", "/etc/x").unwrap()
    );
    assert!(private_path("one", "/etc/x")
        .unwrap()
        .starts_with(state_root().join("one")));

    // Use the path relative to home for sources under home.
    // For other sources, preserve the path from the filesystem root.
    if let Some(home) = dirs::home_dir() {
        let mine = private_path("one", &home.join(".claude").to_string_lossy()).unwrap();
        assert_eq!(mine, state_root().join("one/home/.claude"));
    }
    assert_eq!(
        private_path("one", "/etc/x").unwrap(),
        state_root().join("one/root/etc/x")
    );
}

#[test]
fn private_state_paths_reject_traversal_and_absolute_identities() {
    for state_id in ["../escape", "one/two", "/tmp/escape", ".", ".trash"] {
        let s = SessionCfg {
            name: "bad-state".into(),
            state_id: state_id.into(),
            ..Default::default()
        };
        assert!(state_dir(&s).is_err(), "accepted state id {state_id:?}");
        assert!(
            private_path(state_id, "/etc/tool").is_err(),
            "accepted private path state id {state_id:?}"
        );
    }
}

#[test]
fn state_directory_follows_the_identity_not_the_agent_name() {
    let before = SessionCfg {
        name: "before".into(),
        state_id: "stable-agent-state".into(),
        ..Default::default()
    };
    let after = SessionCfg {
        name: "after".into(),
        ..before.clone()
    };

    assert_eq!(state_dir(&before).unwrap(), state_dir(&after).unwrap());
    assert_eq!(
        state_dir(&before).unwrap(),
        state_root().join("stable-agent-state")
    );
}

#[test]
fn persistent_tmp_is_created_under_the_agent_state() {
    let state_id = format!("persistent-tmp-{}", uuid::Uuid::new_v4());
    let s = SessionCfg {
        name: "tmp-agent".into(),
        state_id,
        persistent_tmp: true,
        ..Default::default()
    };
    let p = ProjectCfg {
        name: "p".into(),
        dir: "/tmp".into(),
        ..Default::default()
    };

    prepare_network(&Config::default(), &s, &p).expect("persistent tmp preparation");
    assert!(persistent_tmp_path(&s).unwrap().is_dir());
    std::fs::remove_dir_all(state_dir(&s).unwrap()).unwrap();
}

#[test]
fn preparation_uses_captured_presets_like_the_launch_plan() {
    let root = std::env::temp_dir().join(format!("slopd-snapshot-prep-{}", uuid::Uuid::new_v4()));
    let host = root.join("private");
    std::fs::create_dir_all(host.join("prompts")).unwrap();
    std::fs::write(host.join("auth.json"), "captured auth").unwrap();
    std::fs::write(host.join("prompts/one.md"), "prompt").unwrap();

    let state_id = uuid::Uuid::new_v4().to_string();
    let s = SessionCfg {
        name: "snapshot-agent".into(),
        state_id: state_id.clone(),
        project: "p".into(),
        sandbox: vec!["codex".into()],
        // This stands in for a pre-change Codex snapshot: it still seeds its private tree,
        // while the live builtin now describes a shared credential file.
        sandbox_snapshots: vec![SandboxPreset {
            name: "codex".into(),
            private: vec![host.to_string_lossy().into_owned()],
            seed: vec![host.join("prompts").to_string_lossy().into_owned()],
            ..Default::default()
        }],
        ..Default::default()
    };
    let p = ProjectCfg {
        name: "p".into(),
        dir: "/tmp".into(),
        ..Default::default()
    };

    let mut cfg = Config::default();
    cfg.defaults.agent = "codex".into();
    prepare_network(&cfg, &s, &p).expect("snapshot preparation");
    let copy = private_path(&state_id, host.to_string_lossy().as_ref()).unwrap();
    assert_eq!(
        std::fs::read_to_string(copy.join("auth.json")).unwrap(),
        "captured auth"
    );
    assert!(copy.join("prompts/one.md").is_file());

    std::fs::remove_dir_all(root).unwrap();
    std::fs::remove_dir_all(state_dir(&s).unwrap()).unwrap();
}

#[test]
fn stored_state_keys_cannot_escape_or_name_the_trash_root() {
    let root = Path::new("/tmp/state-root");
    assert_eq!(direct_child(root, "one").unwrap(), root.join("one"));
    for key in ["", ".", "..", "../one", "one/two", "/tmp/one", ".trash"] {
        assert!(direct_child(root, key).is_err(), "accepted {key:?}");
    }
}
/// Initialization copies top-level files and the subdirectories that the preset specifies.
/// It excludes other subdirectories. A second initialization preserves the agent's changes.
#[test]
fn a_private_tree_is_seeded_once_with_the_files_on_top_and_what_the_preset_names() {
    let root = std::env::temp_dir().join(format!("slopd-seed-{}", std::process::id()));
    let _ = std::fs::remove_dir_all(&root);
    let host = root.join("host");
    std::fs::create_dir_all(host.join("agents")).unwrap();
    std::fs::create_dir_all(host.join("projects")).unwrap();
    std::fs::write(host.join("auth.json"), "secret").unwrap();
    std::fs::write(host.join("agents/one.md"), "mine").unwrap();
    std::fs::write(host.join("projects/bulk"), "not this").unwrap();

    let pr = SandboxPreset {
        name: "t".into(),
        private: vec![host.to_string_lossy().into_owned()],
        seed: vec![host.join("agents").to_string_lossy().into_owned()],
        ..Default::default()
    };
    let copy = root.join("copy");
    seed_into(&pr, &host.to_string_lossy(), &copy).unwrap();

    // Copy top-level credentials without an explicit seed entry.
    assert_eq!(
        std::fs::read_to_string(copy.join("auth.json")).unwrap(),
        "secret"
    );
    // Copy only the subdirectories that the preset specifies.
    assert_eq!(
        std::fs::read_to_string(copy.join("agents/one.md")).unwrap(),
        "mine"
    );
    assert!(!copy.join("projects").exists(), "unnamed bulk came across");

    // Preserve the agent's changes when the host source changes after initialization.
    std::fs::write(copy.join("auth.json"), "the agent's own").unwrap();
    std::fs::write(host.join("auth.json"), "changed since").unwrap();
    seed_into(&pr, &host.to_string_lossy(), &copy).unwrap();
    assert_eq!(
        std::fs::read_to_string(copy.join("auth.json")).unwrap(),
        "the agent's own"
    );

    let _ = std::fs::remove_dir_all(&root);
}

/// Exclude top-level files that `skip` or `shared` specifies.
/// This keeps user prompt history out of the private copy.
/// Shared credentials use host bind mounts. Copying them could leave obsolete tokens in the session directory.
#[test]
fn the_files_on_top_are_cut_back_by_skip_and_by_what_is_shared() {
    let root = std::env::temp_dir().join(format!("slopd-top-{}", std::process::id()));
    let _ = std::fs::remove_dir_all(&root);
    let host = root.join("host");
    std::fs::create_dir_all(&host).unwrap();
    std::fs::write(host.join("settings.json"), "wanted").unwrap();
    std::fs::write(host.join("history.jsonl"), "every prompt ever typed").unwrap();
    std::fs::write(host.join(".credentials.json"), "rotates").unwrap();

    let pr = SandboxPreset {
        name: "t".into(),
        private: vec![host.to_string_lossy().into_owned()],
        skip: vec![host.join("history.jsonl").to_string_lossy().into_owned()],
        shared: vec![host
            .join(".credentials.json")
            .to_string_lossy()
            .into_owned()],
        ..Default::default()
    };
    let copy = root.join("copy");
    seed_into(&pr, &host.to_string_lossy(), &copy).unwrap();

    assert!(
        copy.join("settings.json").exists(),
        "the ordinary file went"
    );
    assert!(
        !copy.join("history.jsonl").exists(),
        "the user's prompt history came across"
    );
    assert!(
        !copy.join(".credentials.json").exists(),
        "a copy of the credential was left in the session directory"
    );

    let _ = std::fs::remove_dir_all(&root);
}

/// Copy a complete seed directory except the paths that `skip` specifies.
/// This permits model settings and nested configuration files without copying session transcripts.
#[test]
fn a_seeded_directory_comes_across_whole_bar_what_skip_names() {
    let root = std::env::temp_dir().join(format!("slopd-skip-{}", std::process::id()));
    let _ = std::fs::remove_dir_all(&root);
    let host = root.join("host");
    std::fs::create_dir_all(host.join("agent/sessions")).unwrap();
    std::fs::create_dir_all(host.join("agent/nested/deep")).unwrap();
    std::fs::write(host.join("agent/models-store.json"), "opus").unwrap();
    std::fs::write(host.join("agent/nested/deep/kept.json"), "kept").unwrap();
    std::fs::write(host.join("agent/sessions/big.jsonl"), "21MB of talk").unwrap();

    let pr = SandboxPreset {
        name: "t".into(),
        private: vec![host.to_string_lossy().into_owned()],
        seed: vec![host.join("agent").to_string_lossy().into_owned()],
        skip: vec![host.join("agent/sessions").to_string_lossy().into_owned()],
        ..Default::default()
    };
    let copy = root.join("copy");
    seed_into(&pr, &host.to_string_lossy(), &copy).unwrap();

    // Preserve settings and nested files outside excluded paths.
    assert_eq!(
        std::fs::read_to_string(copy.join("agent/models-store.json")).unwrap(),
        "opus"
    );
    assert!(copy.join("agent/nested/deep/kept.json").exists());
    // Exclude the transcript directory and its contents.
    assert!(
        !copy.join("agent/sessions").exists(),
        "the transcripts came across"
    );

    let _ = std::fs::remove_dir_all(&root);
}

#[test]
fn splits_quoted_args() {
    assert_eq!(shell_split("claude"), vec!["claude"]);
    assert_eq!(
        shell_split(r#"claude --model opus "two words""#),
        vec!["claude", "--model", "opus", "two words"]
    );
    assert_eq!(shell_split("a  b"), vec!["a", "b"]);
    assert_eq!(shell_split(r#"x ''"#), vec!["x", ""]);
    assert_eq!(shell_split(r#"'a'\''b'"#), vec!["a'b"]);
    assert_eq!(shell_split(r#"x\ y"#), vec!["x y"]);
    assert_eq!(
        shell_split(r#"bash -lc 'cd -- '\''a b'\'' && exec "${SHELL:-bash}"'"#),
        vec!["bash", "-lc", "cd -- 'a b' && exec \"${SHELL:-bash}\""]
    );
}

#[test]
fn agent_shell_presets_resolve_to_absolute_executables() {
    let mut cfg = Config::default();
    for name in ["bash", "sh"] {
        cfg.defaults.agent_shell = name.into();
        let path = agent_shell_path(&cfg).expect("installed shell preset");
        assert!(
            Path::new(&path).is_absolute(),
            "{name} resolved to {path:?}"
        );
        assert_eq!(Path::new(&path).file_name().unwrap(), name);
    }
}

/// Tests inspect the generated argument list. Production startup saves and executes the same launch plan.
pub(crate) fn build_argv(cfg: &Config, s: &SessionCfg, p: &ProjectCfg) -> Result<Vec<String>> {
    build_plan(cfg, s, p).map(|plan| plan.lower())
}
