//! Build ordered arguments for Bubblewrap mounts, environment, network, and resource limits.

use std::path::Path;

use crate::config::{DnsConfig, Limits, MountMode, NetworkMode};
use crate::sandbox::{
    pane_less_utfchardef, persistent_tmp_path, private_resolver_path, ResolvedMount, PANE_TERM,
    PRIVATE_RESOLVER,
};

use super::policy::{
    daemon_config_binds, private_bind_paths, resolver_target, shared_binds, tmux_socket_bind,
};
use super::{BindContext, EnvArgs, BASE_ENV, PRIVATE_ADDRESS, PRIVATE_GATEWAY, PRIVATE_NETMASK};

fn push_args(a: &mut Vec<String>, args: &[&str]) {
    a.extend(args.iter().map(|arg| (*arg).to_string()));
}

/// Create the Bubblewrap namespace before bind mounts.
/// Create usr-merge symlinks and the `/proc`, `/dev`, and `/tmp` mounts first.
pub(super) fn push_skeleton(a: &mut Vec<String>, network: NetworkMode) {
    // Clear inherited environment variables before adding sandbox values.
    // Some socket variables point to paths that the sandbox cannot access.
    push_args(
        a,
        &["bwrap", "--die-with-parent", "--unshare-all", "--clearenv"],
    );
    if network != NetworkMode::None {
        push_args(a, &["--share-net"]);
    }

    // Recreate the host's library and executable paths inside the namespace.
    // Read the paths from the host because usr-merge layouts differ between systems.
    // The sandbox runs on the same system as slopd and needs the same layout.
    for link in ["/lib", "/lib64", "/bin", "/sbin"] {
        match std::fs::symlink_metadata(link) {
            Ok(meta) if meta.file_type().is_symlink() => {
                if let Ok(target) = std::fs::read_link(link) {
                    a.push("--symlink".to_string());
                    a.push(target.to_string_lossy().into_owned());
                    a.push(link.to_string());
                }
            }
            // If this system does not use usr-merge, bind the real directory.
            Ok(meta) if meta.is_dir() => push_args(a, &["--ro-bind", link, link]),
            // Skip paths that do not exist, such as `/lib64` on arm64.
            _ => {}
        }
    }

    // Bubblewrap applies mounts in argument order. Create these mounts before user binds.
    // A later /tmp mount could hide /tmp/.X11-unix and block X11 access.
    push_args(a, &["--proc", "/proc"]);
    push_args(a, &["--dev", "/dev"]);
    push_args(a, &["--tmpfs", "/tmp"]);
    // Create /mnt before applying user mounts that need this directory.
    push_args(a, &["--dir", "/mnt"]);
}

/// Add preset and host capability mounts before private state mounts.
/// In host network mode, mount the resolver after ordinary read-only mounts.
pub(super) fn push_ro_binds(a: &mut Vec<String>, bind: &BindContext<'_>) {
    for path in bind.ro {
        push_args(a, &["--ro-bind", path, path]);
    }
    // Mount the host resolver after ordinary read-only mounts.
    // Later preset mounts can still replace it.
    if bind.network == NetworkMode::Host {
        if let Some((src, target)) = bind.resolv {
            push_args(a, &["--ro-bind", src, target]);
        }
    }

    for path in bind.rw {
        push_args(a, &["--bind", path, path]);
    }
    // Mount devices after /dev so they remain accessible.
    for path in bind.dev {
        push_args(a, &["--dev-bind", path, path]);
    }

    // Mount the debug socket after ordinary mounts so another preset's /tmp mount cannot hide it.
    // The target path uses guest UID 0.
    if bind.tmux {
        if let Some((source, target)) = tmux_socket_bind(crate::tmux::tmux_socket()) {
            push_args(a, &["--ro-bind", &source, &target]);
        }
    }
    if bind.daemon_config {
        for path in daemon_config_binds() {
            push_args(a, &["--ro-bind", &path, &path]);
        }
    }
}

/// Mount persistent `/tmp` before project and private-preset mounts.
pub(super) fn push_persistent_tmp(a: &mut Vec<String>, bind: &BindContext<'_>) {
    if bind.s.persistent_tmp {
        if let Ok(path) = persistent_tmp_path(bind.s) {
            push_args(a, &["--bind", &path.to_string_lossy(), "/tmp"]);
        }
    }
}

/// Mount private state, shared credentials, and DNS files after ordinary paths.
pub(super) fn push_private_binds(
    a: &mut Vec<String>,
    bind: &BindContext<'_>,
) -> anyhow::Result<()> {
    // Mount private copies after preset and project mounts.
    // Each copy hides earlier mounts at the same target and blocks access to the original.
    for (copy, host) in private_bind_paths(bind.cfg, bind.s, bind.p, bind.table)? {
        push_args(a, &["--bind", &copy, &host]);
    }

    // Mount shared files inside private copies.
    // Bubblewrap creates the mount point without copying the host file first.
    for path in shared_binds(bind.cfg, bind.s, bind.p, bind.table)? {
        push_args(a, &["--bind", &path, &path]);
    }

    // In private network mode, mount the resolver last at this target.
    // Preset or project mounts under /etc must not restore a resolver that uses host loopback.
    if bind.network == NetworkMode::Private {
        if let Some((src, target)) = bind.resolv {
            push_args(a, &["--ro-bind", src, target]);
        }
    }
    Ok(())
}

/// Mount the primary project at its configured path.
/// Apply the resolved project mounts.
pub(super) fn push_mounts(a: &mut Vec<String>, mounts: &[ResolvedMount]) {
    for m in mounts {
        match m.mode {
            MountMode::Ro => push_args(a, &["--ro-bind", &m.host_dir, &m.guest_dir]),
            MountMode::Rw | MountMode::Cache => {
                push_args(a, &["--bind", &m.host_dir, &m.guest_dir])
            }
        }
    }
}

pub(super) fn push_env(a: &mut Vec<String>, args: EnvArgs<'_>) {
    let EnvArgs {
        cfg,
        home,
        s,
        p,
        mounts,
        presets,
        agent_shell,
        agent_argv,
    } = args;
    push_args(a, &["--setenv", "HOME", home]);
    push_args(a, &["--setenv", "SLOPWORLD_SESSION", &s.name]);
    push_args(a, &["--setenv", "SLOPWORLD_PROJECT", &p.name]);
    if !s.task_id.trim().is_empty() {
        push_args(a, &["--setenv", "SLOPWORLD_TASK_ID", &s.task_id]);
    }
    let cwd = mounts.first().map(|m| m.guest_dir.as_str()).unwrap_or("/");
    push_args(a, &["--chdir", cwd]);

    // Set terminal values explicitly because slopd does not inherit them from a terminal.
    push_args(a, &["--setenv", "TERM", PANE_TERM]);
    push_args(a, &["--setenv", "COLORTERM", "truecolor"]);
    push_args(a, &["--setenv", "LESSUTFCHARDEF", &pane_less_utfchardef()]);

    // Keep the standard host variables needed by existing configurations after --clearenv.
    // Agents need PATH to find executables.
    let mut passed: Vec<String> = vec!["LESSUTFCHARDEF".into()];
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

    // Apply preset values after inherited values so presets take precedence.
    for pr in presets {
        for (k, v) in &pr.setenv {
            push_args(a, &["--setenv", k.as_str(), v.as_str()]);
        }
    }

    // Give agent CLIs their configured shell, not the daemon's login shell.
    // Apply it after host and preset values so it takes precedence.
    // Use an absolute path because Codex rejects a bare name and falls back to the account shell.
    // Shell errands still use `[defaults] shell`.
    push_args(a, &["--setenv", "SHELL", agent_shell]);

    // slopd captures Pi and Codex prompts before they enter tmux.
    // Disable the project's Pi title extension in managed sessions.
    // This prevents duplicate title updates and removes the extension's project-trust and OpenRouter-key requirements.
    if agent_argv.first().is_some_and(|command| {
        Path::new(command)
            .file_name()
            .is_some_and(|name| name == "pi")
    }) {
        push_args(a, &["--setenv", "SLOPWORLD_PI_TITLES", "never"]);
    }

    if let Some(token) = s.worker_token.as_deref() {
        // Set the worker token after preset values so a preset cannot replace it with the root token.
        // This also removes the need to mount endpoint.toml, which contains the root token.
        let url = crate::endpoint::url_for(&cfg.daemon.bind);
        push_args(a, &["--setenv", "SLOPD_URL", &url]);
        push_args(a, &["--setenv", "SLOPD_TOKEN", token]);
    }
}

/// The host's `/etc/resolv.conf` often points to a file in `/run`, which the sandbox does not mount.
/// Use the systemd-resolved stub when available so split-DNS routing works.
pub(super) fn resolver_bind(
    network: NetworkMode,
    dns: &DnsConfig,
    state_id: &str,
) -> Option<(String, String)> {
    if network == NetworkMode::Host {
        let target = resolver_target()?;
        Some(match dns {
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
                    .ok()?
                    .to_string_lossy()
                    .into_owned(),
                target,
            ),
        })
    } else if network == NetworkMode::Private {
        Some((
            private_resolver_path(state_id)
                .ok()?
                .to_string_lossy()
                .into_owned(),
            resolver_target().unwrap_or_else(|| "/etc/resolv.conf".into()),
        ))
    } else {
        None
    }
}

/// For private networking, run `pasta` before `bwrap`.
pub(super) fn pasta_prefix(dns: &DnsConfig, bind: &str, network: NetworkMode) -> Vec<String> {
    if network != NetworkMode::Private {
        return Vec::new();
    }
    pasta_prefix_args(dns, bind)
}

/// Start the scope before pasta, bwrap, and the agent so resource limits apply to all three.
pub(super) fn scope_prefix(limits: &Limits) -> Vec<String> {
    if limits.is_empty() {
        return Vec::new();
    }
    scope_prefix_args(limits)
}

fn scope_prefix_args(limits: &Limits) -> Vec<String> {
    let mut out = vec![
        "systemd-run".into(),
        "--user".into(),
        "--scope".into(),
        // A per-launch identity lets inspection distinguish this scope from tmux's cgroup.
        format!("--unit=slopworld-{}.scope", uuid::Uuid::new_v4()),
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

fn pasta_prefix_args(dns: &DnsConfig, bind: &str) -> Vec<String> {
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
    // Forward only the daemon TCP port into the private namespace when the
    // daemon listens on 127.0.0.1 (or all IPv4 interfaces). A broad host
    // loopback mapping would also expose unrelated host services.
    let daemon_port = bind
        .parse::<std::net::SocketAddr>()
        .ok()
        .and_then(|addr| match addr {
            std::net::SocketAddr::V4(addr)
                if *addr.ip() == std::net::Ipv4Addr::LOCALHOST || addr.ip().is_unspecified() =>
            {
                Some(addr.port().to_string())
            }
            _ => None,
        })
        .unwrap_or_else(|| "none".into());
    out.push("--tcp-ns".into());
    out.push(daemon_port);
    for server in crate::sandbox::dns_servers(dns) {
        out.push("--dns-host".into());
        out.push(server.to_string());
    }
    out.push("--".into());
    out
}
