use super::*;
use crate::config::Limits;
use crate::sandbox::{
    build_argv, persistent_tmp_path, presets_for, private_path, private_resolver_path,
    validate_preset, PANE_TERM,
};

fn argv() -> Vec<String> {
    let cfg = Config::default();
    let s = SessionCfg {
        name: "a".into(),
        project: "p".into(),
        sandbox: vec!["x11".into()],
        ..Default::default()
    };
    let p = ProjectCfg {
        name: "p".into(),
        dir: "/tmp".into(),
        ..Default::default()
    };
    build_argv(&cfg, &s, &p).expect("sandbox argv")
}

fn at(a: &[String], needle: &str) -> usize {
    a.iter()
        .position(|x| x == needle)
        .unwrap_or_else(|| panic!("no {needle} in {a:?}"))
}

#[test]
fn arbitrary_mounts_keep_paths_modes_and_missing_sources_fail_launch() {
    use crate::config::{Mount, MountMode};
    let root = std::env::temp_dir().join(format!("slopd-path-mount-{}", uuid::Uuid::new_v4()));
    std::fs::create_dir_all(&root).unwrap();
    let file = root.join("input.txt");
    std::fs::write(&file, "input").unwrap();
    let mut project = ProjectCfg {
        name: "repo".into(),
        dir: root.to_string_lossy().into(),
        mounts: vec![Mount {
            from: file.to_string_lossy().into(),
            to: "/mnt/custom.txt".into(),
            mode: MountMode::Ro,
        }],
        ..Default::default()
    };
    let session = SessionCfg {
        name: "agent".into(),
        project: "repo".into(),
        ..Default::default()
    };
    let argv = build_argv(&Config::default(), &session, &project).unwrap();
    assert!(argv.windows(3).any(|w| w[0] == "--ro-bind"
        && w[1] == file.to_string_lossy()
        && w[2] == "/mnt/custom.txt"));
    project.mounts[0].from = root.join("missing").to_string_lossy().into();
    assert!(build_argv(&Config::default(), &session, &project)
        .unwrap_err()
        .to_string()
        .contains("does not exist"));
    std::fs::remove_dir_all(root).unwrap();
}

#[test]
fn path_mounts_cannot_recover_private_sources_and_private_overlays_win() {
    use crate::config::{Mount, MountMode};
    let root = std::env::temp_dir().join(format!("slopd-private-mount-{}", uuid::Uuid::new_v4()));
    let private = root.join("private");
    let public = root.join("public");
    std::fs::create_dir_all(&private).unwrap();
    std::fs::create_dir_all(&public).unwrap();
    let session = SessionCfg {
        name: "agent".into(),
        project: "repo".into(),
        cmd: Some("true".into()),
        sandbox: vec!["saved".into()],
        sandbox_snapshots: vec![crate::presets::SandboxPreset {
            name: "saved".into(),
            private: vec![private.to_string_lossy().into()],
            ..Default::default()
        }],
        ..Default::default()
    };
    let mut project = ProjectCfg {
        name: "repo".into(),
        dir: public.to_string_lossy().into(),
        mounts: vec![Mount {
            from: private.to_string_lossy().into(),
            to: "/mnt/leak".into(),
            mode: MountMode::Rw,
        }],
        ..Default::default()
    };
    assert!(build_argv(&Config::default(), &session, &project)
        .unwrap_err()
        .to_string()
        .contains("private preset state"));
    project.mounts[0].from = public.to_string_lossy().into();
    project.mounts[0].to = private.to_string_lossy().into();
    let argv = build_argv(&Config::default(), &session, &project).unwrap();
    let targets: Vec<_> = argv
        .windows(3)
        .filter(|w| w[0] == "--bind" && w[2] == private.to_string_lossy())
        .collect();
    assert_eq!(targets.len(), 2);
    assert_eq!(targets[0][1], public.to_string_lossy());
    assert_ne!(targets[1][1], public.to_string_lossy());
    std::fs::remove_dir_all(root).unwrap();
}

#[cfg(unix)]
#[test]
fn primary_aliases_cannot_expose_private_originals() {
    use std::os::unix::fs::symlink;

    let root = std::env::temp_dir().join(format!("slopd-primary-alias-{}", uuid::Uuid::new_v4()));
    let private = root.join("private");
    let alias = root.join("alias");
    std::fs::create_dir_all(private.join("child")).unwrap();
    symlink(&private, &alias).unwrap();
    let session = SessionCfg {
        name: "agent".into(),
        project: "repo".into(),
        cmd: Some("true".into()),
        sandbox: vec!["saved".into()],
        sandbox_snapshots: vec![crate::presets::SandboxPreset {
            name: "saved".into(),
            private: vec![private.to_string_lossy().into()],
            ..Default::default()
        }],
        ..Default::default()
    };
    let mut project = ProjectCfg {
        name: "repo".into(),
        ..Default::default()
    };
    for source in [&alias, &alias.join("child"), &private.join("child")] {
        project.dir = source.to_string_lossy().into();
        let error = build_argv(&Config::default(), &session, &project).unwrap_err();
        assert!(
            error.to_string().contains("private preset state"),
            "{error}"
        );
    }
    // The containing workspace remains usable. Private mounts hide the original files.
    project.dir = root.to_string_lossy().into();
    let argv = build_argv(&Config::default(), &session, &project).unwrap();
    assert!(argv.windows(3).any(|w| w[0] == "--bind"
        && w[2] == private.to_string_lossy()
        && w[1] != private.to_string_lossy()));
    std::fs::remove_dir_all(root).unwrap();
}

#[test]
fn network_mode_selects_the_expected_namespace() {
    let cfg = Config::default();
    let mut s = SessionCfg {
        name: "a".into(),
        project: "p".into(),
        ..Default::default()
    };

    let p = ProjectCfg {
        name: "p".into(),
        dir: "/tmp".into(),
        ..Default::default()
    };
    s.network = NetworkMode::None;
    let none = build_argv(&cfg, &s, &p).expect("no-network argv");
    assert_eq!(none[0], "bwrap");
    assert!(!none.contains(&"--share-net".into()));

    s.network = NetworkMode::Host;
    let host = build_argv(&cfg, &s, &p).expect("host-network argv");
    assert_eq!(host[0], "bwrap");
    assert!(host.contains(&"--share-net".into()));

    s.network = NetworkMode::Private;
    let private = build_argv(&cfg, &s, &p).expect("private-network argv");
    assert_eq!(private[0], "pasta");
    assert!(private.contains(&"--ipv4-only".into()));
    assert!(private.contains(&"--tcp-ports".into()));
    assert!(private.contains(&"none".into()));
    assert!(private.contains(&"--share-net".into()));
    assert!(private.contains(&PRIVATE_ADDRESS.into()));
    assert!(private
        .windows(2)
        .any(|w| w[0] == "--tcp-ns" && w[1] == "7717"));
    assert!(!private.contains(&"--map-host-loopback".into()));
    assert!(private
        .windows(2)
        .any(|w| w[0] == "--netmask" && w[1] == PRIVATE_NETMASK));
    let resolved: Vec<_> = private
        .windows(2)
        .filter(|w| w[0] == "--dns-host")
        .map(|w| w[1].as_str())
        .collect();
    assert_eq!(resolved, DnsConfig::Resolved.servers());

    s.dns = DnsConfig::Servers {
        servers: vec!["10.0.0.53".parse().unwrap(), "10.0.0.54".parse().unwrap()],
    };
    let explicit = build_argv(&cfg, &s, &p).expect("explicit DNS argv");
    let hosts: Vec<_> = explicit
        .windows(2)
        .filter(|w| w[0] == "--dns-host")
        .map(|w| w[1].as_str())
        .collect();
    assert_eq!(hosts, vec!["10.0.0.53", "10.0.0.54"]);
}

#[test]
fn private_network_forwards_only_a_local_daemon_port() {
    let mut cfg = Config::default();
    let s = SessionCfg {
        name: "a".into(),
        project: "p".into(),
        network: NetworkMode::Private,
        ..Default::default()
    };
    let p = ProjectCfg {
        name: "p".into(),
        dir: "/tmp".into(),
        ..Default::default()
    };
    cfg.daemon.bind = "127.0.0.1:8899".into();
    let local = build_argv(&cfg, &s, &p).unwrap();
    assert!(local
        .windows(2)
        .any(|w| w[0] == "--tcp-ns" && w[1] == "8899"));

    cfg.daemon.bind = "10.0.0.5:8899".into();
    let remote = build_argv(&cfg, &s, &p).unwrap();
    assert!(remote
        .windows(2)
        .any(|w| w[0] == "--tcp-ns" && w[1] == "none"));

    cfg.daemon.bind = "127.0.0.2:8899".into();
    let other_loopback = build_argv(&cfg, &s, &p).unwrap();
    assert!(other_loopback
        .windows(2)
        .any(|w| w[0] == "--tcp-ns" && w[1] == "none"));
}

#[test]
fn an_agent_can_widen_the_project_network_default() {
    let cfg = Config::default();
    let s = SessionCfg {
        name: "a".into(),
        project: "p".into(),
        network: NetworkMode::Host,
        ..Default::default()
    };
    let p = ProjectCfg {
        name: "p".into(),
        dir: "/tmp".into(),
        ..Default::default()
    };

    let argv = build_argv(&cfg, &s, &p).expect("widened host-network argv");
    assert_eq!(argv[0], "bwrap");
    assert!(argv.contains(&"--share-net".into()));
}

#[test]
fn resource_limits_wrap_the_agent_in_a_systemd_scope() {
    let cfg = Config::default();
    let s = SessionCfg {
        name: "a".into(),
        project: "p".into(),
        network: NetworkMode::None,
        limits: Limits {
            memory_mb: Some(512),
            pids: Some(64),
            nofile: None,
            cpu_pct: Some(150),
        },
        ..Default::default()
    };
    let p = ProjectCfg {
        name: "p".into(),
        dir: "/tmp".into(),
        ..Default::default()
    };
    let a = build_argv(&cfg, &s, &p).expect("scoped argv");

    // Outermost: the scope is the very first thing, ahead of bwrap.
    assert_eq!(a[0], "systemd-run");
    assert!(a.contains(&"--scope".into()));
    assert!(a
        .windows(2)
        .any(|w| w[0] == "--property" && w[1] == "MemoryMax=512M"));
    assert!(a
        .windows(2)
        .any(|w| w[0] == "--property" && w[1] == "TasksMax=64"));
    assert!(a
        .windows(2)
        .any(|w| w[0] == "--property" && w[1] == "CPUQuota=150%"));
    // An unset cap is absent, never zero.
    assert!(!a.iter().any(|x| x.starts_with("LimitNOFILE")));
    // bwrap follows the scope's own `--` separator.
    let sep = a.iter().position(|x| x == "--").expect("scope separator");
    assert_eq!(a[sep + 1], "bwrap");
}

#[test]
fn no_limits_leaves_the_argv_unwrapped() {
    let cfg = Config::default();
    let s = SessionCfg {
        name: "a".into(),
        project: "p".into(),
        ..Default::default()
    };
    let p = ProjectCfg {
        name: "p".into(),
        dir: "/tmp".into(),
        ..Default::default()
    };
    let a = build_argv(&cfg, &s, &p).expect("argv");
    assert!(!a.contains(&"systemd-run".into()));
}

#[test]
fn private_resolver_binds_to_the_canonical_target() {
    let Some(target) = resolver_target() else {
        return;
    };
    let cfg = Config::default();
    let s = SessionCfg {
        name: "resolver-test".into(),
        project: "p".into(),
        ..Default::default()
    };
    let p = ProjectCfg {
        name: "p".into(),
        dir: "/tmp".into(),
        ..Default::default()
    };
    let a = build_argv(&cfg, &s, &p).expect("private sandbox argv");
    let source = private_resolver_path(&s.state_id)
        .unwrap()
        .to_string_lossy()
        .into_owned();

    assert!(
        a.windows(3)
            .any(|w| w[0] == "--ro-bind" && w[1] == source && w[2] == target),
        "private resolver was not bound to {target}: {a:?}"
    );
    assert!(
        !a.windows(3)
            .any(|w| { w[0] == "--ro-bind" && w[1] == source && w[2] == "/etc/resolv.conf" }),
        "private resolver still targets the symlink: {a:?}"
    );
}
/// Apply bind mounts after tmpfs and device mounts so they remain accessible.
/// This prevents /tmp from hiding the X11 socket.
#[test]
fn binds_come_after_the_skeleton() {
    let a = argv();
    let tmp = at(&a, "--tmpfs");
    let first_bind = a
        .iter()
        .position(|x| x == "--ro-bind" || x == "--bind" || x == "--dev-bind")
        .expect("some bind");
    assert!(
        tmp < first_bind,
        "tmpfs at {tmp} buries the bind at {first_bind}"
    );
    assert!(at(&a, "--dev") < first_bind);
    assert!(at(&a, "--proc") < first_bind);
}

/// Include the command's sandbox presets even when the project selects another preset.
/// For example, the Claude Code command requires access to ~/.claude.
#[test]
fn a_command_brings_its_own_presets() {
    let cfg = Config::default();
    let s = SessionCfg {
        name: "a".into(),
        project: "p".into(),
        command: "claude".into(),
        sandbox: vec!["docker".into()],
        ..Default::default()
    };
    let p = ProjectCfg {
        name: "p".into(),
        dir: "/tmp".into(),
        ..Default::default()
    };
    let t = crate::presets::table();
    let names: Vec<&str> = presets_for(&cfg, &s, &p, &t)
        .iter()
        .map(|pr| pr.name.as_str())
        .collect();
    assert!(names.contains(&"claude"), "no claude preset in {names:?}");
    assert!(names.contains(&"docker"), "no docker preset in {names:?}");
}

#[test]
fn worker_identity_is_exported_to_the_sandbox() {
    let cfg = Config::default();
    let s = SessionCfg {
        name: "parent-worker".into(),
        project: "p".into(),
        command: "bash".into(),
        sandbox: Vec::new(),
        worker: true,
        task_id: "task-7".into(),
        worker_token: Some("worker-secret".into()),
        ..Default::default()
    };
    let p = ProjectCfg {
        name: "p".into(),
        dir: "/tmp".into(),
        ..Default::default()
    };
    let a = build_argv(&cfg, &s, &p).expect("worker sandbox argv");
    assert!(a
        .windows(3)
        .any(|w| { w[0] == "--setenv" && w[1] == "SLOPWORLD_TASK_ID" && w[2] == "task-7" }));
    assert!(a
        .windows(3)
        .any(|w| { w[0] == "--setenv" && w[1] == "SLOPD_TOKEN" && w[2] == "worker-secret" }));
    assert!(a
        .windows(3)
        .any(|w| { w[0] == "--setenv" && w[1] == "SLOPD_URL" && w[2] == "http://127.0.0.1:7717" }));
    assert!(a.windows(2).any(|w| w[0] == "--tcp-ns" && w[1] == "7717"));
    assert!(!a.contains(&"--map-host-loopback".into()));
    assert!(a.contains(&"--share-net".into()));
}

/// Check protected paths when resolving preset mounts as well as during validation.
#[test]
fn a_preset_asking_for_the_world_does_not_get_it() {
    let home = dirs::home_dir().map(|h| h.to_string_lossy().into_owned());
    let mut asked = vec!["/".to_string(), "/usr".to_string()];
    asked.extend(home.clone());
    asked.push(Config::path_in_use().to_string_lossy().into_owned());

    let preset = SandboxPreset {
        ro: asked,
        ..Default::default()
    };
    let out = paths(&[&preset], |pr| &pr.ro);
    assert_eq!(out, vec!["/usr".to_string()], "got {out:?}");
}
/// Mount the private copy after ordinary preset mounts at the same target.
/// bwrap applies mounts in order, so the sandbox uses the private copy.
#[test]
fn a_private_bind_is_last_and_beats_an_ordinary_preset_bind() {
    let Some(home) = dirs::home_dir() else { return };
    let claude = home.join(".claude").to_string_lossy().into_owned();
    if !std::path::Path::new(&claude).exists() {
        return; // nothing to keep private on a machine that has never run it
    }

    let cfg = Config::default();
    let s = SessionCfg {
        name: "a".into(),
        state_id: "a".into(),
        project: "p".into(),
        command: "claude".into(),
        ..Default::default()
    };
    let p = ProjectCfg {
        name: "p".into(),
        dir: "/tmp".into(),
        ..Default::default()
    };
    let a = build_argv(&cfg, &s, &p).expect("sandbox argv");

    let copy = private_path("a", &claude)
        .unwrap()
        .to_string_lossy()
        .into_owned();
    assert!(a.contains(&copy), "no private bind in {a:?}");
    // The last mount at the host path uses the private copy.
    let last = a.iter().rposition(|x| x == &claude).expect("the target");
    assert_eq!(a[last - 1], copy, "the copy is not what lands last");
}

/// Mount shared credentials after the private directory so they remain accessible.
/// Mounting them before private `~/.claude` would send credential updates to the session's copy instead of the host file.
#[test]
fn a_shared_file_lands_on_top_of_the_private_copy_it_sits_in() {
    let Some(home) = dirs::home_dir() else { return };
    let claude = home.join(".claude").to_string_lossy().into_owned();
    let creds = home.join(".claude/.credentials.json");
    if !creds.exists() {
        return; // nothing shared on a machine that has never logged in
    }
    let creds = creds.to_string_lossy().into_owned();

    let cfg = Config::default();
    let s = SessionCfg {
        name: "a".into(),
        state_id: "a".into(),
        project: "p".into(),
        command: "claude".into(),
        ..Default::default()
    };
    let p = ProjectCfg {
        name: "p".into(),
        dir: "/tmp".into(),
        ..Default::default()
    };
    let a = build_argv(&cfg, &s, &p).expect("sandbox argv");

    let copy = private_path("a", &claude)
        .unwrap()
        .to_string_lossy()
        .into_owned();
    let private_at = at(&a, &copy);
    let shared_at = a.iter().rposition(|x| x == &creds).expect("no shared bind");
    assert!(
        shared_at > private_at,
        "the copy is mounted over the credential it was supposed to expose"
    );
}

/// Share only files that pass the path guard.
/// A shared directory could let the sandbox change `settings.json`, including hooks that run commands on the host.
/// Enforce the file restriction for all shared mounts.
#[test]
fn only_a_file_is_ever_shared() {
    let root = std::env::temp_dir().join(format!("slopd-shared-{}", std::process::id()));
    let _ = std::fs::remove_dir_all(&root);
    std::fs::create_dir_all(root.join("dir")).unwrap();
    std::fs::write(root.join("creds.json"), "token").unwrap();

    let file = root.join("creds.json").to_string_lossy().into_owned();
    let t = Table {
        sandbox: vec![SandboxPreset {
            name: "t".into(),
            private: vec![root.to_string_lossy().into_owned()],
            shared: vec![file.clone()],
            ..Default::default()
        }],
        commands: Vec::new(),
    };

    let p = ProjectCfg {
        name: "p".into(),
        dir: "/tmp".into(),
        ..Default::default()
    };
    let s = SessionCfg {
        name: "a".into(),
        project: "p".into(),
        sandbox: vec!["t".into()],
        ..Default::default()
    };
    assert_eq!(
        shared_binds(&Config::default(), &s, &p, &t),
        vec![file],
        "the valid shared file did not get through"
    );

    let invalid = SandboxPreset {
        name: "invalid".into(),
        private: vec![root.to_string_lossy().into_owned()],
        shared: vec![root.join("dir").to_string_lossy().into_owned()],
        ..Default::default()
    };
    let error = validate_preset(&invalid, &Table::default())
        .unwrap_err()
        .to_string();
    assert!(error.contains("not a regular file"), "{error}");

    let _ = std::fs::remove_dir_all(&root);
}
/// The environment is built, not inherited.
#[test]
fn the_environment_is_declared() {
    let a = argv();
    assert!(a.contains(&"--clearenv".to_string()));

    let term = at(&a, "TERM");
    assert_eq!(a[term - 1], "--setenv");
    assert_eq!(a[term + 1], PANE_TERM);

    assert!(a.contains(&"PATH".to_string()));
}

#[test]
fn agent_shell_overrides_the_host_shell() {
    let mut cfg = Config::default();
    let s = SessionCfg {
        name: "a".into(),
        project: "p".into(),
        ..Default::default()
    };
    let p = ProjectCfg {
        name: "p".into(),
        dir: "/tmp".into(),
        ..Default::default()
    };

    for name in ["bash", "sh"] {
        cfg.defaults.agent_shell = name.into();
        let expected = crate::sandbox::agent_shell_path(&cfg).expect("agent shell path");
        let a = build_argv(&cfg, &s, &p).expect("sandbox argv");
        let shell = a
            .windows(3)
            .filter(|w| w[0] == "--setenv" && w[1] == "SHELL")
            .map(|w| w[2].as_str())
            .next_back();
        assert_eq!(shell, Some(expected.as_str()), "agent shell {name}");
    }
}

#[test]
fn pi_extension_is_disabled_when_daemon_owns_titles() {
    let mut cfg = Config::default();
    cfg.daemon.pi_titles = crate::config::TitlePolicy::Once;
    cfg.daemon.title_model = "test/title-model".into();
    let s = SessionCfg {
        name: "pi".into(),
        project: "p".into(),
        cmd: Some("pi --model test".into()),
        ..Default::default()
    };
    let p = ProjectCfg {
        name: "p".into(),
        dir: "/tmp".into(),
        ..Default::default()
    };
    let a = build_argv(&cfg, &s, &p).expect("pi sandbox argv");
    assert!(a
        .windows(3)
        .any(|w| { w[0] == "--setenv" && w[1] == "SLOPWORLD_PI_TITLES" && w[2] == "never" }));
}

#[test]
fn the_primary_project_keeps_its_configured_path() {
    let a = argv();
    assert!(
        a.windows(3)
            .any(|w| w[0] == "--bind" && w[1] == "/tmp" && w[2] == "/tmp"),
        "primary project not mounted at its configured path: {a:?}"
    );
    assert!(
        !a.iter().any(|arg| arg == "/mnt/p"),
        "removed project alias survived: {a:?}"
    );
    assert!(
        a.windows(2).any(|w| w[0] == "--chdir" && w[1] == "/tmp"),
        "cwd not set to the configured project path: {a:?}"
    );
}

#[test]
fn an_extra_mount_appears_in_the_argv() {
    use crate::config::{Mount, MountMode};

    let mut cfg = Config::default();
    cfg.projects.push(ProjectCfg {
        name: "main".into(),
        dir: "/tmp".into(),
        ..Default::default()
    });
    cfg.projects.push(ProjectCfg {
        name: "lib".into(),
        dir: "/tmp".into(),
        ..Default::default()
    });
    let s = SessionCfg {
        name: "a".into(),
        project: "main".into(),
        ..Default::default()
    };
    let p = ProjectCfg {
        name: "main".into(),
        dir: "/tmp".into(),
        mounts: vec![Mount {
            from: "/usr".into(),
            to: "/mnt/lib".into(),
            mode: MountMode::Ro,
        }],
        ..Default::default()
    };
    let a = build_argv(&cfg, &s, &p).expect("mounted sandbox argv");

    assert!(
        a.windows(3)
            .any(|w| w[0] == "--bind" && w[1] == "/tmp" && w[2] == "/tmp"),
        "primary project not at its configured path: {a:?}"
    );
    assert!(
        a.windows(3)
            .any(|w| w[0] == "--ro-bind" && w[1] == "/usr" && w[2] == "/mnt/lib"),
        "lib mount not at /mnt/lib: {a:?}"
    );
    assert!(
        a.windows(2).any(|w| w[0] == "--chdir" && w[1] == "/tmp"),
        "cwd not the configured project path: {a:?}"
    );
}

#[test]
fn a_relative_mount_destination_is_under_the_project_directory() {
    use crate::config::{Mount, MountMode};

    let cfg = Config::default();
    let s = SessionCfg {
        name: "a".into(),
        project: "main".into(),
        ..Default::default()
    };
    let p = ProjectCfg {
        name: "main".into(),
        dir: "/tmp".into(),
        mounts: vec![Mount {
            from: "/usr".into(),
            to: "vendor".into(),
            mode: MountMode::Ro,
        }],
        ..Default::default()
    };
    let a = build_argv(&cfg, &s, &p).expect("relative mount destination");

    assert!(
        a.windows(3)
            .any(|w| w[0] == "--ro-bind" && w[1] == "/usr" && w[2] == "/tmp/vendor"),
        "relative mount did not use the project directory: {a:?}"
    );
}

#[test]
fn sandbox_rejects_unsafe_project_names() {
    let cfg = Config::default();
    let s = SessionCfg {
        name: "a".into(),
        project: "../escape".into(),
        ..Default::default()
    };
    let p = ProjectCfg {
        name: "../escape".into(),
        dir: "/tmp".into(),
        ..Default::default()
    };

    let error = build_argv(&cfg, &s, &p).unwrap_err().to_string();
    assert!(error.contains("path component"), "{error}");
}

#[test]
fn mounting_the_primary_project_ro_overrides_its_mode() {
    use crate::config::{Mount, MountMode};

    let cfg = Config::default();
    let s = SessionCfg {
        name: "a".into(),
        project: "p".into(),
        ..Default::default()
    };
    let p = ProjectCfg {
        name: "p".into(),
        dir: "/tmp".into(),
        mounts: vec![Mount {
            from: "/tmp".into(),
            to: "/tmp".into(),
            mode: MountMode::Ro,
        }],
        ..Default::default()
    };
    let a = build_argv(&cfg, &s, &p).expect("ro primary argv");

    assert!(
        a.windows(3)
            .any(|w| w[0] == "--ro-bind" && w[1] == "/tmp" && w[2] == "/tmp"),
        "primary project should be ro at its configured path: {a:?}"
    );
}

#[test]
fn persistent_tmp_is_bound_from_the_agent_state_tree() {
    let cfg = Config::default();
    let s = SessionCfg {
        name: "a".into(),
        state_id: "persistent-tmp-test".into(),
        project: "p".into(),
        persistent_tmp: true,
        ..Default::default()
    };
    let p = ProjectCfg {
        name: "p".into(),
        dir: "/tmp".into(),
        ..Default::default()
    };
    let a = build_argv(&cfg, &s, &p).expect("persistent tmp sandbox argv");
    let source = persistent_tmp_path(&s)
        .unwrap()
        .to_string_lossy()
        .to_string();
    assert!(a
        .windows(3)
        .any(|w| w[0] == "--bind" && w[1] == source && w[2] == "/tmp"));
}
