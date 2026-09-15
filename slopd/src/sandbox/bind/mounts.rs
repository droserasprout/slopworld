//! Ordered bwrap, environment, network and resource-limit argument emission.

use std::path::Path;

use crate::config::{DnsConfig, Limits, MountMode, NetworkMode};
use crate::sandbox::{
    persistent_tmp_path, private_resolver_path, ResolvedMount, PANE_TERM, PRIVATE_RESOLVER,
};

use super::policy::{
    daemon_config_binds, private_bind_paths, resolver_target, shared_binds, tmux_socket_bind,
};
use super::{BindContext, EnvArgs, BASE_ENV, PRIVATE_ADDRESS, PRIVATE_GATEWAY, PRIVATE_NETMASK};

fn push_args(a: &mut Vec<String>, args: &[&str]) {
    a.extend(args.iter().map(|arg| (*arg).to_string()));
}

/// Builds the bwrap namespace before any bind. Each later mount covers what is under it, so
/// usr-merge symlinks and the proc/dev/tmpfs skeleton have to be in place first.
pub(super) fn push_skeleton(a: &mut Vec<String>, network: NetworkMode) {
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
    // The primary project is exposed below /mnt through a symlink. Create its parent before
    // the alias is installed; ordinary bind mounts would create this parent implicitly.
    push_args(a, &["--dir", "/mnt"]);
}

/// Adds ordinary preset and host capability binds. These all precede private state, while the
/// host resolver deliberately remains above ordinary read-only binds in host network mode.
pub(super) fn push_ro_binds(a: &mut Vec<String>, bind: &BindContext<'_>) {
    for path in bind.ro {
        push_args(a, &["--ro-bind", path, path]);
    }
    // Host mode keeps the old resolver ordering: it sits above the ordinary ro binds, while
    // a preset that explicitly binds a larger tree still gets the final say.
    if bind.network == NetworkMode::Host {
        if let Some((src, target)) = bind.resolv {
            push_args(a, &["--ro-bind", src, target]);
        }
    }

    for path in bind.rw {
        push_args(a, &["--bind", path, path]);
    }
    // After the /dev tmpfs, or it would be mounted over.
    for path in bind.dev {
        push_args(a, &["--dev-bind", path, path]);
    }

    // The debug preset asks for this after the ordinary binds so a broad `/tmp` bind from
    // another preset cannot bury the socket. Inside bwrap the guest uid is 0, hence the target.
    if bind.tmux {
        if let Some((source, target)) = tmux_socket_bind(crate::config::tmux_socket()) {
            push_args(a, &["--ro-bind", &source, &target]);
        }
    }
    if bind.daemon_config {
        for path in daemon_config_binds() {
            push_args(a, &["--ro-bind", &path, &path]);
        }
    }
}

/// Adds mounts that must win over ordinary binds: private copies, shared files, the project,
/// and finally private DNS. A shared file is a hole cut in private state, so it lands on top.
pub(super) fn push_private_binds(
    a: &mut Vec<String>,
    bind: &BindContext<'_>,
    project_mode: MountMode,
) {
    // Replace the skeleton tmpfs before mounting private preset subdirectories, so paths such
    // as Claude's `/tmp/claude-0` can still overlay their own private copies on this tree.
    if bind.s.persistent_tmp {
        if let Ok(path) = persistent_tmp_path(bind.s) {
            let path = path.to_string_lossy().into_owned();
            push_args(a, &["--bind", &path, "/tmp"]);
        }
    }

    // Last of the binds under $HOME, so the private copy wins over an ordinary preset bind:
    // the point of a private path is that there is no way to ask for the original, and an
    // earlier bind of the same target is one bwrap mounts over.
    for (copy, host) in private_bind_paths(bind.cfg, bind.s, bind.p, bind.table) {
        push_args(a, &["--bind", &copy, &host]);
    }

    // After the private binds, and only ever inside one: a shared file is a hole cut in a
    // copy, so it has to be mounted over the copy rather than under it. bwrap makes the
    // mount point, which is why nothing seeds one.
    for path in shared_binds(bind.cfg, bind.s, bind.p, bind.table) {
        push_args(a, &["--bind", &path, &path]);
    }

    match project_mode {
        MountMode::Ro => push_args(a, &["--ro-bind", bind.dir, bind.dir]),
        MountMode::Rw => push_args(a, &["--bind", bind.dir, bind.dir]),
    }
    // Private mode's resolver must be the last bind at this target. A user preset or project
    // directory may bind /etc or a file below it, but neither should restore a host-loopback
    // resolver.
    if bind.network == NetworkMode::Private {
        if let Some((src, target)) = bind.resolv {
            push_args(a, &["--ro-bind", src, target]);
        }
    }
}

/// Exposes the primary project at its configured path and adds additional projects under
/// `/mnt/<project-name>`. The primary project's `/mnt` entry is only a symlink, so the project
/// directory itself is mounted exactly once.
pub(super) fn push_mounts(
    a: &mut Vec<String>,
    mounts: &[ResolvedMount],
    primary_name: &str,
    manifest: Option<&Path>,
    manifest_mount_path: &str,
) {
    if let Some(primary) = mounts.first() {
        let alias = format!("/mnt/{primary_name}");
        push_args(a, &["--symlink", &primary.guest_dir, &alias]);
    }
    for m in mounts.iter().skip(1) {
        match m.mode {
            MountMode::Ro => push_args(a, &["--ro-bind", &m.host_dir, &m.guest_dir]),
            MountMode::Rw => push_args(a, &["--bind", &m.host_dir, &m.guest_dir]),
        }
    }
    if let Some(manifest) = manifest {
        if let Some(primary) = mounts.first() {
            let source = manifest.to_string_lossy();
            // Keep the generated source protected at its project-root location even when the
            // user chooses another guest path. The second bind is the configured destination;
            // both spellings resolve to the same generated bytes and neither is writable.
            let root_target = Path::new(&primary.guest_dir).join(crate::manifest::FILE_NAME);
            let root_target = root_target.to_string_lossy();
            push_args(a, &["--ro-bind", &source, &root_target]);

            let target = Path::new(&primary.guest_dir).join(manifest_mount_path);
            let target = target.to_string_lossy();
            if target != root_target {
                push_args(a, &["--ro-bind", &source, &target]);
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

    // Agents must not inherit the daemon's login shell: zsh's terminal behavior and startup
    // files are not a reliable default for agent CLIs. The setting comes last so it wins over
    // both the host environment and a preset literal; shell errands still execute the command
    // selected by `[defaults] shell`.
    push_args(a, &["--setenv", "SHELL", cfg.defaults.agent_shell.trim()]);

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

    if let Some(token) = s.worker_token.as_deref() {
        // Keep the worker credential last so an inherited preset cannot replace it with a root
        // token. This also avoids mounting endpoint.toml, whose token is root-scoped.
        let url = crate::endpoint::url_for(&cfg.daemon.bind);
        push_args(a, &["--setenv", "SLOPD_URL", &url]);
        push_args(a, &["--setenv", "SLOPD_TOKEN", token]);
    }
}

/// `/etc/resolv.conf` often points into unmounted /run; use resolved's stub when available so
/// split-DNS routing is preserved.
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

/// Pasta wraps bwrap for private networking; its prefix must be the outer command.
pub(super) fn pasta_prefix(dns: &DnsConfig, worker: bool, network: NetworkMode) -> Vec<String> {
    if network != NetworkMode::Private {
        return Vec::new();
    }
    pasta_prefix_args(dns, worker)
}

/// The scope goes outermost, so pasta, bwrap and the agent all count against the caps.
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

fn pasta_prefix_args(dns: &DnsConfig, worker: bool) -> Vec<String> {
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
    if worker {
        // Private sandboxes deliberately do not inherit host loopback. Workers still need the
        // daemon API, whose default listener is on 127.0.0.1, so map only that address back to
        // the host without restoring general host networking.
        out.push("--map-host-loopback".into());
        out.push("127.0.0.1".into());
    }
    for server in dns.servers() {
        out.push("--dns-host".into());
        out.push(server.to_string());
    }
    out.push("--".into());
    out
}
