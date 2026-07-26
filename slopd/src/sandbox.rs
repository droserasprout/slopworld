use crate::config::{expand, Config, ProjectCfg, SessionCfg, SessionKind};

/// A named bundle of binds and env vars, so a project can say "dbus" instead of
/// four paths nobody remembers correctly.
///
/// These are data, not policy: every path is bound only if it exists, so a
/// preset for something this host does not run costs nothing. The list is
/// compiled in rather than configurable because a preset the daemon does not
/// understand is one the GUI cannot draw a checkbox for either.
pub struct Preset {
    pub name: &'static str,
    pub description: &'static str,
    /// Read-only binds.
    pub ro: &'static [&'static str],
    /// Read-write binds. Sockets go here: a bus you cannot write to is a bus you
    /// cannot talk on.
    pub rw: &'static [&'static str],
    /// Device nodes, which need `--dev-bind` to survive the `--dev` tmpfs.
    pub dev: &'static [&'static str],
    /// Env vars passed through from slopd's own environment.
    pub env: &'static [&'static str],
    /// Env vars set to a literal value, which `env` cannot do: it forwards what
    /// slopd was started with, and some of what a sandboxed tool needs is true
    /// only *inside* the sandbox and so is set nowhere on the host. Applied
    /// after the forwarded ones, because this is the preset's deliberate answer
    /// and the environment's is an accident of how slopd was launched.
    pub setenv: &'static [(&'static str, &'static str)],
}

pub const PRESETS: &[Preset] = &[
    Preset {
        name: "claude",
        description: "Claude Code's own state and credentials",
        ro: &[],
        rw: &["~/.claude", "~/.claude.json"],
        dev: &[],
        env: &["ANTHROPIC_API_KEY", "ANTHROPIC_BASE_URL", "CLAUDE_CONFIG_DIR"],
        setenv: &[],
    },
    Preset {
        name: "dbus",
        description: "session and system message bus",
        ro: &["/run/dbus/system_bus_socket"],
        rw: &["$XDG_RUNTIME_DIR/bus"],
        dev: &[],
        env: &["DBUS_SESSION_BUS_ADDRESS", "XDG_RUNTIME_DIR"],
        setenv: &[],
    },
    Preset {
        name: "systemd",
        description: "systemctl --user, journalctl (needs dbus)",
        ro: &["/run/systemd", "/sys/fs/cgroup", "/var/log/journal", "/run/log/journal"],
        rw: &["$XDG_RUNTIME_DIR/systemd"],
        dev: &[],
        env: &["XDG_RUNTIME_DIR"],
        // systemctl talks to $XDG_RUNTIME_DIR/systemd/private first, and that
        // socket authenticates the peer in a way that does not survive bwrap's
        // user namespace: the handshake goes AUTHENTICATING -> CLOSED and the
        // error is the unhelpful "Failed to connect to user scope bus via local
        // transport". The session bus reaches the same manager and works, and
        // this is what makes systemctl take that road - which is also why this
        // preset is no use without `dbus`.
        setenv: &[("SYSTEMCTL_FORCE_BUS", "1")],
    },
    Preset {
        name: "x11",
        description: "X11 display, for anything that opens a window",
        ro: &["/tmp/.X11-unix", "~/.Xauthority"],
        rw: &[],
        dev: &[],
        env: &["DISPLAY", "XAUTHORITY"],
        setenv: &[],
    },
    Preset {
        name: "wayland",
        description: "Wayland display",
        ro: &[],
        rw: &["$XDG_RUNTIME_DIR/$WAYLAND_DISPLAY"],
        dev: &[],
        env: &["WAYLAND_DISPLAY", "XDG_RUNTIME_DIR"],
        setenv: &[],
    },
    Preset {
        name: "gpu",
        description: "/dev/dri, for rendering and compute",
        ro: &[],
        rw: &[],
        dev: &["/dev/dri"],
        env: &[],
        setenv: &[],
    },
    Preset {
        name: "audio",
        description: "PipeWire / PulseAudio",
        ro: &[],
        rw: &["$XDG_RUNTIME_DIR/pulse", "$XDG_RUNTIME_DIR/pipewire-0"],
        dev: &[],
        env: &["XDG_RUNTIME_DIR"],
        setenv: &[],
    },
    Preset {
        name: "docker",
        description: "the Docker daemon's socket",
        ro: &[],
        rw: &["/var/run/docker.sock"],
        dev: &[],
        env: &["DOCKER_HOST"],
        setenv: &[],
    },
    Preset {
        name: "podman",
        description: "rootless podman",
        ro: &[],
        rw: &["$XDG_RUNTIME_DIR/podman", "~/.local/share/containers"],
        dev: &[],
        env: &["XDG_RUNTIME_DIR", "CONTAINER_HOST"],
        setenv: &[],
    },
    Preset {
        name: "ssh",
        description: "SSH keys, known hosts and the agent socket",
        ro: &["~/.ssh"],
        rw: &["$SSH_AUTH_SOCK"],
        dev: &[],
        env: &["SSH_AUTH_SOCK"],
        setenv: &[],
    },
    Preset {
        name: "git",
        description: "global git identity and config",
        ro: &["~/.gitconfig", "~/.config/git"],
        rw: &[],
        dev: &[],
        env: &["GIT_AUTHOR_NAME", "GIT_AUTHOR_EMAIL", "EMAIL"],
        setenv: &[],
    },
    Preset {
        name: "rust",
        description: "cargo and rustup, with their shared registry cache",
        ro: &[],
        rw: &["~/.cargo", "~/.rustup"],
        dev: &[],
        env: &["CARGO_HOME", "RUSTUP_HOME"],
        setenv: &[],
    },
    Preset {
        name: "node",
        description: "npm, pnpm and nvm caches",
        ro: &[],
        rw: &["~/.npm", "~/.cache/node-gyp", "~/.nvm", "~/.local/share/pnpm"],
        dev: &[],
        env: &["NPM_CONFIG_PREFIX"],
        setenv: &[],
    },
    Preset {
        name: "python",
        description: "pip, uv and the user site-packages tree",
        ro: &[],
        rw: &["~/.cache/pip", "~/.cache/uv", "~/.local/lib", "~/.local/share/uv"],
        dev: &[],
        env: &["VIRTUAL_ENV", "UV_CACHE_DIR"],
        setenv: &[],
    },
];

pub fn preset(name: &str) -> Option<&'static Preset> {
    PRESETS.iter().find(|p| p.name == name)
}

/// The presets a session runs under: its project's, plus `claude` for a Claude
/// session whether the project asked or not. That last part is the whole reason
/// the kind is a kind rather than a command string - the agent that needs
/// `~/.claude` is the one thing about it we can know.
fn presets_for(s: &SessionCfg, p: &ProjectCfg) -> Vec<&'static Preset> {
    let mut names: Vec<&str> = p.presets.iter().map(String::as_str).collect();
    if s.kind == SessionKind::Claude && !names.contains(&"claude") {
        names.insert(0, "claude");
    }
    names
        .into_iter()
        .filter_map(|n| {
            let hit = preset(n);
            if hit.is_none() {
                tracing::warn!("project {:?} names unknown sandbox preset {n:?}, ignoring", p.name);
            }
            hit
        })
        .collect()
}

/// Builds the argv that tmux will exec. Returns the agent command unwrapped if
/// sandboxing is off for this session's project.
pub fn build_argv(cfg: &Config, s: &SessionCfg, p: &ProjectCfg) -> Vec<String> {
    let agent_argv: Vec<String> = shell_split(&cfg.command_of(s));
    let dir = expand(&p.dir);

    if !cfg.sandbox.enabled || !p.sandbox {
        return agent_argv;
    }
    let presets = presets_for(s, p);

    let home = dirs::home_dir()
        .map(|p| p.to_string_lossy().into_owned())
        .unwrap_or_else(|| "/root".into());

    // Global first, then the presets, then whatever the project spelled out, so
    // the most specific answer for a path is the last one bwrap sees. Each list
    // is expanded and filtered here, once: a path that does not exist is left
    // out rather than mounted, which is what makes a preset for something this
    // host does not run cost nothing.
    let ro = paths(&cfg.sandbox.ro_paths, &presets, |pr| pr.ro, &p.ro_paths);
    let rw = paths(&cfg.sandbox.rw_paths, &presets, |pr| pr.rw, &p.rw_paths);
    let dev = paths(&[], &presets, |pr| pr.dev, &[]);

    let mut a: Vec<String> = vec!["bwrap".into()];
    let mut push = |args: &[&str]| a.extend(args.iter().map(|x| x.to_string()));

    push(&["--die-with-parent", "--unshare-all"]);
    if p.net {
        push(&["--share-net"]);
    }

    for path in &ro {
        push(&["--ro-bind", path, path]);
    }

    // systemd-resolved / NetworkManager make /etc/resolv.conf a symlink into
    // /run, which the sandbox never mounts. With only /etc bound the symlink
    // dangles inside the namespace, so every lookup fails with ENOENT and the
    // agent reports it as an API connection error. Materialise a working
    // resolv.conf at whatever path the symlink points to.
    //
    // Prefer resolved's stub listener (127.0.0.53, reachable over the shared
    // loopback): it does the split-DNS routing - a Tailscale uplink, say - that a
    // resolver querying the raw server list gets wrong, where an upstream answers
    // NOTIMP and the agent then sees an ENOTIMP error. Fall back to the symlink's
    // own target elsewhere. Only relevant with the network shared; a resolv.conf
    // that is already a plain file under /etc needs nothing extra.
    let resolv = if p.net {
        std::fs::canonicalize("/etc/resolv.conf")
            .ok()
            .map(|p| p.to_string_lossy().into_owned())
            .filter(|target| target != "/etc/resolv.conf")
            .map(|target| {
                let stub = "/run/systemd/resolve/stub-resolv.conf";
                let src = if std::path::Path::new(stub).exists() {
                    stub.to_string()
                } else {
                    target.clone()
                };
                (src, target)
            })
    } else {
        None
    };
    if let Some((src, target)) = &resolv {
        push(&["--ro-bind", src.as_str(), target.as_str()]);
    }

    // Usr-merge symlinks, otherwise nothing resolves inside the namespace.
    push(&["--symlink", "usr/lib", "/lib"]);
    push(&["--symlink", "usr/lib", "/lib64"]);
    push(&["--symlink", "usr/bin", "/bin"]);
    push(&["--symlink", "usr/bin", "/sbin"]);

    push(&["--proc", "/proc"]);
    push(&["--dev", "/dev"]);
    push(&["--tmpfs", "/tmp"]);

    // The agent's own state must survive across sessions, so it is rw, not a tmpfs.
    for path in &rw {
        push(&["--bind", path, path]);
    }
    // After the /dev tmpfs, or it would be mounted over.
    for path in &dev {
        push(&["--dev-bind", path, path]);
    }

    push(&["--bind", &dir, &dir]);
    push(&["--setenv", "HOME", &home]);
    push(&["--setenv", "SLOPWORLD_SESSION", &s.name]);
    push(&["--setenv", "SLOPWORLD_PROJECT", &p.name]);
    push(&["--chdir", &dir]);

    let mut env: Vec<&str> = cfg.sandbox.pass_env.iter().map(String::as_str).collect();
    for pr in &presets {
        env.extend(pr.env.iter().copied());
    }
    env.extend(p.pass_env.iter().map(String::as_str));

    let mut passed: Vec<&str> = Vec::new();
    for k in env {
        if passed.contains(&k) {
            continue;
        }
        passed.push(k);
        if let Ok(v) = std::env::var(k) {
            push(&["--setenv", k, &v]);
        }
    }

    // Last, so a preset that knows what a value has to be inside the sandbox
    // beats whatever slopd happened to inherit for the same name.
    for pr in &presets {
        for (k, v) in pr.setenv {
            push(&["--setenv", k, v]);
        }
    }

    a.push("--".into());
    a.extend(agent_argv);
    a
}

/// Global, then preset, then project - expanded, dropped if they are not on this
/// host, and deduplicated. A path asked for twice is harmless to bwrap and
/// unreadable in the log line the daemon prints when it starts a session.
fn paths(
    global: &[String],
    presets: &[&'static Preset],
    pick: fn(&'static Preset) -> &'static [&'static str],
    project: &[String],
) -> Vec<String> {
    let from_presets: Vec<String> = presets
        .iter()
        .copied()
        .flat_map(|pr| pick(pr).iter().map(|s| s.to_string()))
        .collect();

    let mut out: Vec<String> = Vec::new();
    for path in global.iter().cloned().chain(from_presets).chain(project.iter().cloned()) {
        let path = expand(&path);
        if path.is_empty() || out.contains(&path) {
            continue;
        }
        if std::path::Path::new(&path).exists() {
            out.push(path);
        }
    }
    out
}

/// Splits on whitespace, honouring single and double quotes. No expansion, no
/// globbing - we are building an argv, not running a shell.
pub fn shell_split(s: &str) -> Vec<String> {
    let mut out = Vec::new();
    let mut cur = String::new();
    let mut quote: Option<char> = None;
    let mut any = false;

    for c in s.chars() {
        match quote {
            Some(q) if c == q => quote = None,
            Some(_) => cur.push(c),
            None if c == '\'' || c == '"' => {
                quote = Some(c);
                any = true;
            }
            None if c.is_whitespace() => {
                if !cur.is_empty() || any {
                    out.push(std::mem::take(&mut cur));
                    any = false;
                }
            }
            None => cur.push(c),
        }
    }
    if !cur.is_empty() || any {
        out.push(cur);
    }
    out
}

#[cfg(test)]
mod tests {
    use super::shell_split;

    #[test]
    fn splits_quoted_args() {
        assert_eq!(shell_split("claude"), vec!["claude"]);
        assert_eq!(
            shell_split(r#"claude --model opus "two words""#),
            vec!["claude", "--model", "opus", "two words"]
        );
        assert_eq!(shell_split("a  b"), vec!["a", "b"]);
        assert_eq!(shell_split(r#"x ''"#), vec!["x", ""]);
    }
}
