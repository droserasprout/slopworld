use std::path::{Path, PathBuf};

use anyhow::Result;

use crate::config::{expand, Config, DnsConfig, Limits, NetworkMode, ProjectCfg, SessionCfg};
use crate::presets::{SandboxPreset, Table};

use super::{
    presets_for, private_path, private_resolver_path, refused, ResolvedMount, PANE_TERM,
    PRIVATE_RESOLVER,
};

const BASE_ENV: &[&str] = &["PATH", "LANG", "USER", "LOGNAME", "SHELL"];

// Private networking gets an address space that cannot collide with the host's real LAN.
// pasta forwards DNS from this synthetic gateway to the host resolver, while the resolv.conf
// bind below keeps the guest from seeing a host-loopback stub address.
const PRIVATE_ADDRESS: &str = "192.0.2.2";
const PRIVATE_NETMASK: &str = "24";
const PRIVATE_GATEWAY: &str = "192.0.2.1";
/// Returns existing `(private_copy, host_path)` pairs used to shadow private state under
/// `$HOME`. Disk preparation happens in `prepare_network`.
fn private_bind_paths(
    cfg: &Config,
    s: &SessionCfg,
    p: &ProjectCfg,
    t: &Table,
) -> Vec<(String, String)> {
    let mut out: Vec<(String, String)> = Vec::new();
    for pr in presets_for(cfg, s, p, t) {
        for path in &pr.private {
            let host = expand(path);
            if host.is_empty() {
                continue;
            }
            let copy = private_path(&s.state_id, &host);
            // The host path or the prepared session-state copy must exist. Paths under /tmp/
            // never exist on the host (the skeleton mounts its own tmpfs there), but
            // prepare_network creates the copy beforehand.
            if !Path::new(&host).exists() && !copy.exists() {
                continue;
            }
            if out.iter().any(|(_, seen)| seen == &host) {
                continue;
            }
            out.push((copy.to_string_lossy().into_owned(), host));
        }
    }
    out
}

/// Returns existing regular files that presets may share read-write into private state.
/// Refused paths and directories are excluded so shared state cannot grant execution or reach
/// another session's secrets.
fn shared_binds(cfg: &Config, s: &SessionCfg, p: &ProjectCfg, t: &Table) -> Vec<String> {
    let mut out: Vec<String> = Vec::new();
    for pr in presets_for(cfg, s, p, t) {
        for path in &pr.shared {
            let host = expand(path);
            if host.is_empty() || out.contains(&host) {
                continue;
            }
            if let Some(what) = refused(&host) {
                tracing::warn!("not sharing {host}: it reaches {what}");
                continue;
            }
            if !Path::new(&host).exists() {
                continue;
            }
            if !Path::new(&host).is_file() {
                tracing::warn!("not sharing {host}: shared paths are files, and this is not one");
                continue;
            }
            out.push(host);
        }
    }
    out
}

/// The tmux server is outside bwrap and uses the host uid in its socket directory. A bwrap
/// session is uid 0 in its user namespace, so a debug preset gets the host directory mounted at
/// the path tmux will calculate inside the session. The bind is read-only: clients talk through
/// the socket, while an absent or stale server costs the preset nothing.
fn tmux_socket_bind(socket: &str) -> Option<(String, String)> {
    if socket.trim().is_empty() {
        return None;
    }
    let root = std::env::var_os("TMUX_TMPDIR")
        .map(PathBuf::from)
        .unwrap_or_else(|| PathBuf::from("/tmp"));
    let host_dir = root.join(format!("tmux-{}", nix::unistd::getuid().as_raw()));
    if !host_dir.is_dir() || !host_dir.join(socket).exists() {
        return None;
    }
    let host_dir = host_dir.to_string_lossy().into_owned();
    if let Some(what) = refused(&host_dir) {
        tracing::warn!("not exposing tmux socket directory {host_dir}: it reaches {what}");
        return None;
    }
    Some((host_dir, "/tmp/tmux-0".into()))
}

/// The debug capability names these files structurally instead of putting their parent in an
/// ordinary path list. That keeps presets and future config-directory contents out of reach.
fn daemon_config_binds() -> Vec<String> {
    [Config::path_in_use(), crate::endpoint::path()]
        .into_iter()
        .filter(|path| path.is_file())
        .map(|path| path.to_string_lossy().into_owned())
        .collect()
}

/// `/etc/resolv.conf` is commonly a symlink into `/run`, which is not mounted in the sandbox.
/// Bind the replacement onto the real target so the symlink still resolves inside bwrap.
fn resolver_target() -> Option<String> {
    std::fs::canonicalize("/etc/resolv.conf")
        .ok()
        .map(|p| p.to_string_lossy().into_owned())
        .filter(|target| target != "/etc/resolv.conf")
}

/// Prefix limited sessions with a transient systemd scope; requested caps are enforced or startup fails, with no inline fallback.
fn scope_prefix(limits: &Limits) -> Vec<String> {
    let mut out = vec![
        "systemd-run".into(),
        "--user".into(),
        "--scope".into(),
        "--quiet".into(),
        "--collect".into(),
    ];
    let mut prop = |k: &str, v: String| {
        out.push("--property".into());
        out.push(format!("{k}={v}"));
    };
    if let Some(mb) = limits.memory_mb {
        prop("MemoryMax", format!("{mb}M"));
    }
    if let Some(pids) = limits.pids {
        prop("TasksMax", pids.to_string());
    }
    if let Some(nofile) = limits.nofile {
        prop("LimitNOFILE", nofile.to_string());
    }
    if let Some(cpu) = limits.cpu_pct {
        prop("CPUQuota", format!("{cpu}%"));
    }
    out.push("--".into());
    out
}

fn pasta_prefix(dns: &DnsConfig) -> Vec<String> {
    let mut out = vec![
        "pasta".into(),
        "--foreground".into(),
        "--quiet".into(),
        "--config-net".into(),
        "--no-map-gw".into(),
        "--ipv4-only".into(),
        "--tcp-ports".into(),
        "none".into(),
        "--udp-ports".into(),
        "none".into(),
        "--tcp-ns".into(),
        "none".into(),
        "--udp-ns".into(),
        "none".into(),
        "--address".into(),
        PRIVATE_ADDRESS.into(),
        "--netmask".into(),
        PRIVATE_NETMASK.into(),
        "--gateway".into(),
        PRIVATE_GATEWAY.into(),
        "--dns-forward".into(),
        PRIVATE_RESOLVER.into(),
    ];
    for server in dns.servers() {
        out.push("--dns-host".into());
        out.push(server.to_string());
    }
    out.push("--".into());
    out
}

pub(super) fn assemble_argv(
    cfg: &Config,
    s: &SessionCfg,
    p: &ProjectCfg,
    network: NetworkMode,
    dns: &DnsConfig,
    agent_argv: Vec<String>,
    dir: &str,
    table: &Table,
    presets: &[&SandboxPreset],
    home: &str,
    mounts: &[ResolvedMount],
) -> Result<Vec<String>> {
    // Global first, then the resolved presets, so the most specific answer for a path is the
    // last one bwrap sees.
    let ro = paths(&presets, |pr| &pr.ro);
    let rw = paths(&presets, |pr| &pr.rw);
    let dev = paths(&presets, |pr| &pr.dev);
    let tmux = presets.iter().any(|pr| pr.tmux);
    let daemon_config = presets.iter().any(|pr| pr.daemon_config);

    let resolv = resolver_bind(network, dns, &s.state_id);

    let mut a = Vec::new();
    push_skeleton(&mut a, network);
    push_ro_binds(
        &mut a,
        &ro,
        &rw,
        &dev,
        network,
        resolv.as_ref(),
        tmux,
        daemon_config,
    );
    push_private_binds(&mut a, cfg, s, p, &table, &dir, network, resolv.as_ref());
    push_mounts(&mut a, mounts);
    push_env(&mut a, &home, s, p, mounts, &presets, &agent_argv);

    a.push("--".into());
    a.extend(agent_argv);

    wrap_pasta(&mut a, network, dns);
    let limits = cfg.limits_of(s, p);
    wrap_scope(&mut a, &limits);
    Ok(a)
}

fn push_args(a: &mut Vec<String>, args: &[&str]) {
    a.extend(args.iter().map(|arg| (*arg).to_string()));
}

/// Builds the bwrap namespace before any bind. Each later mount covers what is under it, so
/// usr-merge symlinks and the proc/dev/tmpfs skeleton have to be in place first.
fn push_skeleton(a: &mut Vec<String>, network: NetworkMode) {
    // Built back up rather than inherited: a variable naming a socket that is not there is
    // worse than its absence, a program reading it as "this host has one" and failing at the
    // far end of a connect().
    push_args(
        a,
        &["bwrap", "--die-with-parent", "--unshare-all", "--clearenv"],
    );
    if network != NetworkMode::None {
        push_args(a, &["--share-net"]);
    }

    // Recreate the running system's own top-level lib/bin entries, or nothing resolves inside
    // the namespace. This is read off the host rather than hardcoded because usr-merge layouts
    // differ: Arch keeps every library flat in /usr/lib, while Debian's multiarch tree reaches
    // the loader through /lib64 -> /usr/lib64 on amd64 and has no /lib64 at all on arm64. The
    // sandbox always runs on the same system as slopd, so mirroring these keeps it correct on any
    // of them.
    for link in ["/lib", "/lib64", "/bin", "/sbin"] {
        match std::fs::symlink_metadata(link) {
            Ok(meta) if meta.file_type().is_symlink() => {
                if let Ok(target) = std::fs::read_link(link) {
                    a.push("--symlink".to_string());
                    a.push(target.to_string_lossy().into_owned());
                    a.push(link.to_string());
                }
            }
            // A real directory on a system that never merged: bind it in as-is.
            Ok(meta) if meta.is_dir() => push_args(a, &["--ro-bind", link, link]),
            // Absent (e.g. no /lib64 on arm64): nothing to recreate.
            _ => {}
        }
    }

    // The skeleton goes down before any bind: each of these covers what is under it and bwrap
    // mounts in the order given. `x11` binds /tmp/.X11-unix, this tmpfs buried it, and the
    // preset went on handing out a DISPLAY with no socket behind it.
    push_args(a, &["--proc", "/proc"]);
    push_args(a, &["--dev", "/dev"]);
    push_args(a, &["--tmpfs", "/tmp"]);
}

/// Adds ordinary preset and host capability binds. These all precede private state, while the
/// host resolver deliberately remains above ordinary read-only binds in host network mode.
fn push_ro_binds(
    a: &mut Vec<String>,
    ro: &[String],
    rw: &[String],
    dev: &[String],
    network: NetworkMode,
    resolv: Option<&(String, String)>,
    tmux: bool,
    daemon_config: bool,
) {
    for path in ro {
        push_args(a, &["--ro-bind", path, path]);
    }
    // Host mode keeps the old resolver ordering: it sits above the ordinary ro binds, while
    // a preset that explicitly binds a larger tree still gets the final say.
    if network == NetworkMode::Host {
        if let Some((src, target)) = resolv {
            push_args(a, &["--ro-bind", src, target]);
        }
    }

    for path in rw {
        push_args(a, &["--bind", path, path]);
    }
    // After the /dev tmpfs, or it would be mounted over.
    for path in dev {
        push_args(a, &["--dev-bind", path, path]);
    }

    // The debug preset asks for this after the ordinary binds so a broad `/tmp` bind from
    // another preset cannot bury the socket. Inside bwrap the guest uid is 0, hence the target.
    if tmux {
        if let Some((source, target)) = tmux_socket_bind(crate::config::tmux_socket()) {
            push_args(a, &["--ro-bind", &source, &target]);
        }
    }
    if daemon_config {
        for path in daemon_config_binds() {
            push_args(a, &["--ro-bind", &path, &path]);
        }
    }
}

/// Adds mounts that must win over ordinary binds: private copies, shared files, the project,
/// and finally private DNS. A shared file is a hole cut in private state, so it lands on top.
fn push_private_binds(
    a: &mut Vec<String>,
    cfg: &Config,
    s: &SessionCfg,
    p: &ProjectCfg,
    table: &Table,
    dir: &str,
    network: NetworkMode,
    resolv: Option<&(String, String)>,
) {
    // Last of the binds under $HOME, so the private copy wins over an ordinary preset bind:
    // the point of a private path is that there is no way to ask for the original, and an
    // earlier bind of the same target is one bwrap mounts over.
    for (copy, host) in private_bind_paths(cfg, s, p, table) {
        push_args(a, &["--bind", &copy, &host]);
    }

    // After the private binds, and only ever inside one: a shared file is a hole cut in a
    // copy, so it has to be mounted over the copy rather than under it. bwrap makes the
    // mount point, which is why nothing seeds one.
    for path in shared_binds(cfg, s, p, table) {
        push_args(a, &["--bind", &path, &path]);
    }

    push_args(a, &["--bind", dir, dir]);
    // Private mode's resolver must be the last bind at this target. A user preset or project
    // directory may bind /etc or a file below it, but neither should restore a host-loopback
    // resolver.
    if network == NetworkMode::Private {
        if let Some((src, target)) = resolv {
            push_args(a, &["--ro-bind", src, target]);
        }
    }
}

/// Binds project directories into the `/mnt/<project-name>` namespace. The primary project's
/// host-path bind in `push_private_binds` stays for private-state overlays; this adds the
/// uniform guest-side path the agent works from.
fn push_mounts(a: &mut Vec<String>, mounts: &[ResolvedMount]) {
    use crate::config::MountMode;
    for m in mounts {
        match m.mode {
            MountMode::Ro => push_args(a, &["--ro-bind", &m.host_dir, &m.guest_dir]),
            MountMode::Rw => push_args(a, &["--bind", &m.host_dir, &m.guest_dir]),
        }
    }
}

/// Declares the complete environment after all mounts. The sandbox starts with --clearenv, so
/// machine basics are selected explicitly and preset literals are the final word.
fn push_env(
    a: &mut Vec<String>,
    home: &str,
    s: &SessionCfg,
    p: &ProjectCfg,
    mounts: &[ResolvedMount],
    presets: &[&SandboxPreset],
    agent_argv: &[String],
) {
    push_args(a, &["--setenv", "HOME", home]);
    push_args(a, &["--setenv", "SLOPWORLD_SESSION", &s.name]);
    push_args(a, &["--setenv", "SLOPWORLD_PROJECT", &p.name]);
    let cwd = mounts
        .first()
        .map(|m| m.guest_dir.as_str())
        .unwrap_or("/");
    push_args(a, &["--chdir", cwd]);

    // Stated rather than forwarded: the terminal is one slopd built, and a daemon has
    // none of its own to inherit.
    push_args(a, &["--setenv", "TERM", PANE_TERM]);
    push_args(a, &["--setenv", "COLORTERM", "truecolor"]);

    // Compiled in: a config written before --clearenv lists none of them, and an agent with
    // no PATH is a session that starts and dies.
    let mut passed: Vec<String> = Vec::new();
    for (k, v) in std::env::vars() {
        if BASE_ENV.contains(&k.as_str()) || k.starts_with("LC_") {
            push_args(a, &["--setenv", k.as_str(), v.as_str()]);
            passed.push(k);
        }
    }

    let mut env: Vec<&str> = Vec::new();
    for pr in presets {
        env.extend(pr.env.iter().map(String::as_str));
    }

    for k in env {
        if passed.iter().any(|seen| seen == k) {
            continue;
        }
        passed.push(k.to_string());
        if let Ok(v) = std::env::var(k) {
            push_args(a, &["--setenv", k, &v]);
        }
    }

    // Last, so a preset that knows what a value must be inside the sandbox beats
    // whatever slopd inherited for the same name.
    for pr in presets {
        for (k, v) in &pr.setenv {
            push_args(a, &["--setenv", k.as_str(), v.as_str()]);
        }
    }

    // slopd captures Pi prompts before tmux, just as it does Codex prompts. Disable the
    // project-local extension in managed sessions so it cannot race the daemon or require
    // project trust and a sandbox-visible OpenRouter key.
    if agent_argv.first().is_some_and(|command| {
        Path::new(command)
            .file_name()
            .is_some_and(|name| name == "pi")
    }) {
        push_args(a, &["--setenv", "SLOPWORLD_PI_TITLES", "never"]);
    }
}

/// `/etc/resolv.conf` often points into unmounted /run; use resolved's stub when available so
/// split-DNS routing is preserved.
fn resolver_bind(
    network: NetworkMode,
    dns: &DnsConfig,
    state_id: &str,
) -> Option<(String, String)> {
    if network == NetworkMode::Host {
        resolver_target().map(|target| match dns {
            DnsConfig::Resolved => {
                let stub = "/run/systemd/resolve/stub-resolv.conf";
                let src = if Path::new(stub).exists() {
                    stub.to_string()
                } else {
                    target.clone()
                };
                (src, target)
            }
            DnsConfig::Servers { .. } => (
                private_resolver_path(state_id)
                    .to_string_lossy()
                    .into_owned(),
                target,
            ),
        })
    } else if network == NetworkMode::Private {
        Some((
            private_resolver_path(state_id)
                .to_string_lossy()
                .into_owned(),
            resolver_target().unwrap_or_else(|| "/etc/resolv.conf".into()),
        ))
    } else {
        None
    }
}

/// Pasta wraps bwrap for private networking; its prefix must be the outer command.
fn wrap_pasta(a: &mut Vec<String>, network: NetworkMode, dns: &DnsConfig) {
    if network != NetworkMode::Private {
        return;
    }
    let mut pasta = pasta_prefix(dns);
    pasta.append(a);
    *a = pasta;
}

/// The scope goes outermost, so pasta, bwrap and the agent all count against the caps.
fn wrap_scope(a: &mut Vec<String>, limits: &Limits) {
    if limits.is_empty() {
        return;
    }
    let mut scoped = scope_prefix(limits);
    scoped.append(a);
    *a = scoped;
}

/// Expanded, dropped if they are not on this host, and deduplicated.
fn paths(presets: &[&SandboxPreset], pick: fn(&SandboxPreset) -> &[String]) -> Vec<String> {
    let from_presets: Vec<String> = presets
        .iter()
        .flat_map(|pr| pick(pr).iter().cloned())
        .collect();

    let mut out: Vec<String> = Vec::new();
    for path in from_presets {
        let path = expand(&path);
        if path.is_empty() || out.contains(&path) {
            continue;
        }
        // Warned about and dropped rather than refused, the way an unknown preset name is:
        // the files outlive the binary, and a line somebody wrote a year ago is not grounds
        // for an agent that will not start. It is still never bound.
        if let Some(what) = refused(&path) {
            tracing::warn!("not binding {path}: it reaches {what}");
            continue;
        }
        if Path::new(&path).exists() {
            out.push(path);
        }
    }
    out
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::sandbox::{build_argv, private_resolver_path, validate_preset};

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

        let copy = private_path("a", &claude).to_string_lossy().into_owned();
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

        let copy = private_path("a", &claude).to_string_lossy().into_owned();
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
    fn the_primary_project_is_mounted_at_mnt() {
        let a = argv();
        assert!(
            a.windows(3)
                .any(|w| w[0] == "--bind" && w[1] == "/tmp" && w[2] == "/mnt/p"),
            "primary project not mounted at /mnt/p: {a:?}"
        );
        assert!(
            a.windows(2).any(|w| w[0] == "--chdir" && w[1] == "/mnt/p"),
            "cwd not set to /mnt/p: {a:?}"
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
                .any(|w| w[0] == "--bind" && w[1] == "/tmp" && w[2] == "/mnt/main"),
            "primary project not at /mnt/main: {a:?}"
        );
        assert!(
            a.windows(3)
                .any(|w| w[0] == "--ro-bind" && w[1] == "/tmp" && w[2] == "/mnt/lib"),
            "lib mount not at /mnt/lib: {a:?}"
        );
        assert!(
            a.windows(2)
                .any(|w| w[0] == "--chdir" && w[1] == "/mnt/main"),
            "cwd not /mnt/main: {a:?}"
        );
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
                .any(|w| w[0] == "--ro-bind" && w[1] == "/tmp" && w[2] == "/mnt/p"),
            "primary mount should be ro: {a:?}"
        );
    }
}
