use std::path::Path;

use anyhow::Result;

use crate::config::{Config, DnsConfig, MountMode, NetworkMode, ProjectCfg, SessionCfg};
use crate::presets::{SandboxPreset, Table};

use super::LaunchPlan;
use super::ResolvedMount;

mod mounts;
mod policy;
#[cfg(test)]
use policy::{paths, resolver_target, shared_binds};

const BASE_ENV: &[&str] = &["PATH", "LANG", "USER", "LOGNAME", "SHELL"];

// Private networking gets an address space that cannot collide with the host's real LAN.
// pasta forwards DNS from this synthetic gateway to the host resolver, while the resolv.conf
// bind below keeps the guest from seeing a host-loopback stub address.
const PRIVATE_ADDRESS: &str = "192.0.2.2";
const PRIVATE_NETMASK: &str = "24";
const PRIVATE_GATEWAY: &str = "192.0.2.1";
pub(super) struct BuildArgs<'a> {
    pub(super) cfg: &'a Config,
    pub(super) s: &'a SessionCfg,
    pub(super) p: &'a ProjectCfg,
    pub(super) network: NetworkMode,
    pub(super) dns: &'a DnsConfig,
    pub(super) agent_argv: Vec<String>,
    pub(super) dir: &'a str,
    pub(super) table: &'a Table,
    pub(super) presets: &'a [&'a SandboxPreset],
    pub(super) home: &'a str,
    pub(super) mounts: &'a [ResolvedMount],
    pub(super) manifest: Option<&'a Path>,
    pub(super) manifest_mount_path: &'a str,
}

struct BindContext<'a> {
    cfg: &'a Config,
    s: &'a SessionCfg,
    p: &'a ProjectCfg,
    table: &'a Table,
    dir: &'a str,
    ro: &'a [String],
    rw: &'a [String],
    dev: &'a [String],
    network: NetworkMode,
    resolv: Option<&'a (String, String)>,
    tmux: bool,
    daemon_config: bool,
}

pub(super) fn assemble_plan(args: BuildArgs<'_>) -> Result<LaunchPlan> {
    let BuildArgs {
        cfg,
        s,
        p,
        network,
        dns,
        agent_argv,
        dir,
        table,
        presets,
        home,
        mounts,
        manifest,
        manifest_mount_path,
    } = args;
    // Global first, then the resolved presets, so the most specific answer for a path is the
    // last one bwrap sees.
    let ro = policy::paths(presets, |pr| &pr.ro);
    let rw = policy::paths(presets, |pr| &pr.rw);
    let dev = policy::paths(presets, |pr| &pr.dev);
    let tmux = presets.iter().any(|pr| pr.tmux);
    // A worker may inherit a broad parent preset such as slopworld-debug. Its task credential
    // is intentionally the only daemon access it receives, so no worker may mount the root
    // config/endpoint exception through any preset.
    let daemon_config = !s.worker && presets.iter().any(|pr| pr.daemon_config);

    let resolv = mounts::resolver_bind(network, dns, &s.state_id);
    let bind = BindContext {
        cfg,
        s,
        p,
        table,
        dir,
        ro: &ro,
        rw: &rw,
        dev: &dev,
        network,
        resolv: resolv.as_ref(),
        tmux,
        daemon_config,
    };

    let mut bwrap = Vec::new();
    let mut mounts_args = Vec::new();
    let mut environment = Vec::new();
    mounts::push_skeleton(&mut bwrap, network);
    mounts::push_ro_binds(&mut mounts_args, &bind);
    let primary_mode = mounts
        .first()
        .map(|mount| mount.mode)
        .unwrap_or(MountMode::Rw);
    mounts::push_private_binds(&mut mounts_args, &bind, primary_mode);
    mounts::push_mounts(
        &mut mounts_args,
        mounts,
        &p.name,
        manifest,
        manifest_mount_path,
    );
    mounts::push_env(
        &mut environment,
        EnvArgs {
            cfg,
            home,
            s,
            p,
            mounts,
            presets,
            agent_argv: &agent_argv,
        },
    );

    let limits = cfg.limits_of(s, p);
    let pasta = mounts::pasta_prefix(dns, s.worker, network);
    let limits = mounts::scope_prefix(&limits);
    Ok(LaunchPlan {
        session: s.name.clone(),
        limits,
        pasta,
        bwrap,
        environment,
        mounts: mounts_args,
        command: agent_argv,
        known_secrets: [
            cfg.daemon.token.clone(),
            s.worker_token.clone().unwrap_or_default(),
        ]
        .into_iter()
        .filter(|secret| !secret.is_empty())
        .collect(),
    })
}

/// Declares the complete environment after all mounts. The sandbox starts with --clearenv, so
/// machine basics are selected explicitly and preset literals are the final word.
struct EnvArgs<'a> {
    cfg: &'a Config,
    home: &'a str,
    s: &'a SessionCfg,
    p: &'a ProjectCfg,
    mounts: &'a [ResolvedMount],
    presets: &'a [&'a SandboxPreset],
    agent_argv: &'a [String],
}

#[cfg(test)]
mod tests {
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
            ..Default::default()
        };
        let p = ProjectCfg {
            name: "p".into(),
            dir: "/tmp".into(),
            sandbox: vec!["x11".into()],
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
    fn network_mode_selects_the_expected_namespace() {
        let cfg = Config::default();
        let s = SessionCfg {
            name: "a".into(),
            project: "p".into(),
            ..Default::default()
        };

        let mut p = ProjectCfg {
            name: "p".into(),
            dir: "/tmp".into(),
            ..Default::default()
        };
        p.network = NetworkMode::None;
        let none = build_argv(&cfg, &s, &p).expect("no-network argv");
        assert_eq!(none[0], "bwrap");
        assert!(!none.contains(&"--share-net".into()));

        p.network = NetworkMode::Host;
        let host = build_argv(&cfg, &s, &p).expect("host-network argv");
        assert_eq!(host[0], "bwrap");
        assert!(host.contains(&"--share-net".into()));

        p.network = NetworkMode::Private;
        let private = build_argv(&cfg, &s, &p).expect("private-network argv");
        assert_eq!(private[0], "pasta");
        assert!(private.contains(&"--ipv4-only".into()));
        assert!(private.contains(&"--tcp-ports".into()));
        assert!(private.contains(&"none".into()));
        assert!(private.contains(&"--share-net".into()));
        assert!(private.contains(&PRIVATE_ADDRESS.into()));
        assert!(private
            .windows(2)
            .any(|w| w[0] == "--netmask" && w[1] == PRIVATE_NETMASK));
        let resolved: Vec<_> = private
            .windows(2)
            .filter(|w| w[0] == "--dns-host")
            .map(|w| w[1].as_str())
            .collect();
        assert_eq!(resolved, DnsConfig::Resolved.servers());

        p.dns = Some(DnsConfig::Servers {
            servers: vec!["10.0.0.53".parse().unwrap(), "10.0.0.54".parse().unwrap()],
        });
        let explicit = build_argv(&cfg, &s, &p).expect("explicit DNS argv");
        let hosts: Vec<_> = explicit
            .windows(2)
            .filter(|w| w[0] == "--dns-host")
            .map(|w| w[1].as_str())
            .collect();
        assert_eq!(hosts, vec!["10.0.0.53", "10.0.0.54"]);
    }

    #[test]
    fn an_agent_can_widen_the_project_network_default() {
        let cfg = Config::default();
        let s = SessionCfg {
            name: "a".into(),
            project: "p".into(),
            network: Some(NetworkMode::Host),
            ..Default::default()
        };
        let p = ProjectCfg {
            name: "p".into(),
            dir: "/tmp".into(),
            network: NetworkMode::Private,
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
            network: NetworkMode::None,
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
    /// Every bind lands after the tmpfs and devices that would otherwise be mounted
    /// over it - the bug that made the x11 preset a no-op.
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

    /// The command preset's own binds, whether or not the project selected another preset:
    /// knowing a session is Claude Code is what lets the sandbox hand it ~/.claude.
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
            sandbox: vec!["slopworld-worker".into()],
            worker: true,
            task_id: "task-7".into(),
            worker_token: Some("worker-secret".into()),
            ..Default::default()
        };
        let p = ProjectCfg {
            name: "p".into(),
            dir: "/tmp".into(),
            network: NetworkMode::Private,
            ..Default::default()
        };
        let a = build_argv(&cfg, &s, &p).expect("worker sandbox argv");
        assert!(a
            .windows(3)
            .any(|w| { w[0] == "--setenv" && w[1] == "SLOPWORLD_TASK_ID" && w[2] == "task-7" }));
        assert!(a
            .windows(3)
            .any(|w| { w[0] == "--setenv" && w[1] == "SLOPD_TOKEN" && w[2] == "worker-secret" }));
        assert!(a.windows(3).any(|w| {
            w[0] == "--setenv" && w[1] == "SLOPD_URL" && w[2] == "http://127.0.0.1:7717"
        }));
        assert!(a
            .windows(2)
            .any(|w| w[0] == "--map-host-loopback" && w[1] == "127.0.0.1"));
        assert!(a.contains(&"--share-net".into()));
    }

    /// The guard is reached from the effective preset list, not only from the validator.
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
    /// The point of a private path: whatever else asked for that path, the copy is what the
    /// sandbox gets, because it is bound last and bwrap mounts in order.
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
        // Every mention of the host path is before the private one that lands on top.
        let last = a.iter().rposition(|x| x == &claude).expect("the target");
        assert_eq!(a[last - 1], copy, "the copy is not what lands last");
    }

    /// A shared file is the hole in the copy, so it has to be mounted *after* the copy that
    /// would otherwise bury it. The credential is the case: bound over the private `~/.claude`
    /// rather than under it, or a refresh lands in the session directory and expires there.
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

    /// Files only, and the guard every other bind list passes through. A shared *directory* is
    /// a sandbox that can write `settings.json`, and hooks are command lines the host runs -
    /// the narrowness is the whole of what makes this safe, so it is enforced and not asked for.
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
            sandbox: vec!["t".into()],
            ..Default::default()
        };
        let s = SessionCfg {
            name: "a".into(),
            project: "p".into(),
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
        cfg.defaults.agent_shell = "bash".into();
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

        let a = build_argv(&cfg, &s, &p).expect("sandbox argv");
        let shell = a
            .windows(3)
            .filter(|w| w[0] == "--setenv" && w[1] == "SHELL")
            .map(|w| w[2].as_str())
            .next_back();
        assert_eq!(shell, Some("bash"));
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
    fn the_primary_project_keeps_its_path_and_gets_a_mnt_alias() {
        let a = argv();
        assert!(
            a.windows(3)
                .any(|w| w[0] == "--bind" && w[1] == "/tmp" && w[2] == "/tmp"),
            "primary project not mounted at its configured path: {a:?}"
        );
        assert!(
            a.windows(3)
                .any(|w| w[0] == "--symlink" && w[1] == "/tmp" && w[2] == "/mnt/p"),
            "primary project has no /mnt/p alias: {a:?}"
        );
        assert!(
            !a.windows(3)
                .any(|w| w[0] == "--bind" && w[1] == "/tmp" && w[2] == "/mnt/p"),
            "primary project was mounted a second time: {a:?}"
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
            mounts: vec![Mount {
                project: "lib".into(),
                mode: MountMode::Ro,
            }],
            ..Default::default()
        };
        let p = cfg.project("main").unwrap().clone();
        let a = build_argv(&cfg, &s, &p).expect("mounted sandbox argv");

        assert!(
            a.windows(3)
                .any(|w| w[0] == "--bind" && w[1] == "/tmp" && w[2] == "/tmp"),
            "primary project not at its configured path: {a:?}"
        );
        assert!(
            a.windows(3)
                .any(|w| w[0] == "--symlink" && w[1] == "/tmp" && w[2] == "/mnt/main"),
            "primary project has no /mnt/main alias: {a:?}"
        );
        assert!(
            a.windows(3)
                .any(|w| w[0] == "--ro-bind" && w[1] == "/tmp" && w[2] == "/mnt/lib"),
            "lib mount not at /mnt/lib: {a:?}"
        );
        assert!(
            a.windows(2).any(|w| w[0] == "--chdir" && w[1] == "/tmp"),
            "cwd not the configured project path: {a:?}"
        );
    }

    #[test]
    fn sandbox_rejects_unsafe_project_aliases() {
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
            mounts: vec![Mount {
                project: "p".into(),
                mode: MountMode::Ro,
            }],
            ..Default::default()
        };
        let p = ProjectCfg {
            name: "p".into(),
            dir: "/tmp".into(),
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
    fn generated_manifest_is_read_only_through_the_primary_alias() {
        let dir =
            std::env::temp_dir().join(format!("slopd-manifest-bind-{}", uuid::Uuid::new_v4()));
        std::fs::create_dir_all(&dir).unwrap();
        let manifest = dir.join(crate::manifest::FILE_NAME);
        std::fs::write(
            &manifest,
            "<!-- Generated by SlopWorld; do not edit or commit. -->\n",
        )
        .unwrap();

        let cfg = Config::default();
        let s = SessionCfg {
            name: "a".into(),
            project: "p".into(),
            slopworld_md: true,
            ..Default::default()
        };
        let p = ProjectCfg {
            name: "p".into(),
            dir: dir.to_string_lossy().into_owned(),
            ..Default::default()
        };
        let gated = build_argv(&cfg, &s, &p).expect("disabled manifest sandbox argv");
        assert!(!gated.iter().any(|arg| arg == &manifest.to_string_lossy()));
        let mut cfg = cfg;
        cfg.daemon.experimental_instructions = true;
        let a = build_argv(&cfg, &s, &p).expect("manifest sandbox argv");
        let source = manifest.to_string_lossy().to_string();
        let target = manifest.to_string_lossy().to_string();
        assert!(a
            .windows(3)
            .any(|w| { w[0] == "--ro-bind" && w[1] == source && w[2] == target }));
        assert!(a
            .windows(3)
            .any(|w| { w[0] == "--symlink" && w[1] == dir.to_string_lossy() && w[2] == "/mnt/p" }));

        std::fs::remove_dir_all(dir).unwrap();
    }

    #[test]
    fn generated_manifest_is_also_read_only_at_the_configured_mount_path() {
        let dir = std::env::temp_dir().join(format!(
            "slopd-manifest-custom-bind-{}",
            uuid::Uuid::new_v4()
        ));
        std::fs::create_dir_all(&dir).unwrap();
        let manifest = dir.join(crate::manifest::FILE_NAME);
        std::fs::write(
            &manifest,
            "<!-- Generated by SlopWorld; do not edit or commit. -->\n",
        )
        .unwrap();

        let mut cfg = Config::default();
        cfg.daemon.instructions.mount_path = "docs/SLOPWORLD.md".into();
        cfg.daemon.experimental_instructions = true;
        let s = SessionCfg {
            name: "a".into(),
            project: "p".into(),
            slopworld_md: true,
            ..Default::default()
        };
        let p = ProjectCfg {
            name: "p".into(),
            dir: dir.to_string_lossy().into_owned(),
            ..Default::default()
        };
        let a = build_argv(&cfg, &s, &p).expect("custom manifest sandbox argv");
        let source = manifest.to_string_lossy().to_string();
        let root_target = dir
            .join(crate::manifest::FILE_NAME)
            .to_string_lossy()
            .to_string();
        let custom_target = dir.join("docs/SLOPWORLD.md").to_string_lossy().to_string();
        assert!(a
            .windows(3)
            .any(|w| { w[0] == "--ro-bind" && w[1] == source && w[2] == root_target }));
        assert!(a
            .windows(3)
            .any(|w| { w[0] == "--ro-bind" && w[1] == source && w[2] == custom_target }));

        std::fs::remove_dir_all(dir).unwrap();
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
}
