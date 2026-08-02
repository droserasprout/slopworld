use crate::config::{env_pairs, expand, Config, ProjectCfg, SessionCfg};

/// Every path is bound only if it exists, so a preset for something this host does not run
/// costs nothing. Compiled in: a preset the daemon does not understand is one the GUI cannot
/// draw a checkbox for.
pub struct Preset {
    pub name: &'static str,
    pub description: &'static str,
    pub ro: &'static [&'static str],
    /// Sockets go here: a bus you cannot write to is a bus you cannot talk on.
    pub rw: &'static [&'static str],
    /// Device nodes, which need `--dev-bind` to survive the `--dev` tmpfs.
    pub dev: &'static [&'static str],
    /// Forwarded out of slopd's own environment.
    pub env: &'static [&'static str],
    /// Set to a literal value, for what is true only *inside* the sandbox. Applied
    /// after the forwarded ones, so the preset's answer beats how slopd was launched.
    pub setenv: &'static [(&'static str, &'static str)],
}

/// Forwarded whatever the config says, plus anything named `LC_*`. What belongs here says
/// something about *this machine* and nothing about what the sandbox can reach; anything
/// naming a socket, a token or a service is a preset.
const BASE_ENV: &[&str] = &["PATH", "LANG", "USER", "LOGNAME", "SHELL"];

const PANE_TERM: &str = "tmux-256color";

pub const PRESETS: &[Preset] = &[
    Preset {
        name: "claude",
        description: "Claude Code's own state and credentials",
        ro: &[],
        rw: &["~/.claude", "~/.claude.json"],
        dev: &[],
        env: &[
            "ANTHROPIC_API_KEY",
            "ANTHROPIC_BASE_URL",
            "CLAUDE_CONFIG_DIR",
        ],
        setenv: &[],
    },
    Preset {
        name: "pi",
        description: "pi coding agent's own state, config and credentials",
        ro: &[],
        rw: &["~/.pi"],
        dev: &[],
        env: &[
            "PI_CODING_AGENT_DIR",
            "ANTHROPIC_API_KEY",
            "ANTHROPIC_BASE_URL",
            "OPENAI_API_KEY",
            "OPENROUTER_API_KEY",
            "GEMINI_API_KEY",
            "DEEPSEEK_API_KEY",
            "XAI_API_KEY",
            "MISTRAL_API_KEY",
        ],
        setenv: &[],
    },
    Preset {
        name: "opencode",
        description: "OpenCode's own state, config and credentials",
        ro: &[],
        rw: &[
            "~/.local/share/opencode",
            "~/.local/state/opencode",
            "~/.config/opencode",
            "~/.cache/opencode",
        ],
        dev: &[],
        env: &[
            "OPENCODE_CONFIG",
            "OPENCODE_CONFIG_DIR",
            "ANTHROPIC_API_KEY",
            "ANTHROPIC_BASE_URL",
            "OPENAI_API_KEY",
            "OPENROUTER_API_KEY",
        ],
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
        ro: &[
            "/run/systemd",
            "/sys/fs/cgroup",
            "/var/log/journal",
            "/run/log/journal",
        ],
        rw: &["$XDG_RUNTIME_DIR/systemd"],
        dev: &[],
        env: &["XDG_RUNTIME_DIR"],
        // systemctl talks to $XDG_RUNTIME_DIR/systemd/private first, and that handshake does
        // not survive bwrap's user namespace (AUTHENTICATING -> CLOSED). The session bus
        // reaches the same manager, which is why this preset is no use without `dbus`.
        setenv: &[("SYSTEMCTL_FORCE_BUS", "1")],
    },
    Preset {
        name: "x11",
        description: "X11 display, for anything that opens or reads a window",
        // $XAUTHORITY as well as the classic path: Xwayland writes the cookie under
        // $XDG_RUNTIME_DIR under a name of its own and leaves ~/.Xauthority absent. Made once
        // per login, so bound by name rather than by directory.
        ro: &["/tmp/.X11-unix", "~/.Xauthority", "$XAUTHORITY"],
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
        name: "1password",
        description: "the 1Password agent, for SSH auth and signed commits",
        ro: &[],
        // The directory, not the socket in it: a bind of `agent.sock` pins the inode that was
        // there at exec, and the app unlinks and recreates it on restart or relock, leaving
        // the sandbox a socket with nothing listening.
        rw: &["~/.1password"],
        dev: &[],
        // op-ssh-sign finds the socket under $HOME rather than being told, and HOME
        // inside the sandbox is the real one.
        env: &[],
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
        rw: &[
            "~/.npm",
            "~/.cache/node-gyp",
            "~/.nvm",
            "~/.local/share/pnpm",
        ],
        dev: &[],
        env: &["NPM_CONFIG_PREFIX"],
        setenv: &[],
    },
    Preset {
        name: "python",
        description: "pip, uv and the user site-packages tree",
        ro: &[],
        rw: &[
            "~/.cache/pip",
            "~/.cache/uv",
            "~/.local/lib",
            "~/.local/share/uv",
        ],
        dev: &[],
        env: &["VIRTUAL_ENV", "UV_CACHE_DIR"],
        setenv: &[],
    },
];

pub fn preset(name: &str) -> Option<&'static Preset> {
    PRESETS.iter().find(|p| p.name == name)
}

/// Plus the kind's own preset whether the project asked or not: the state directory
/// an agent of a known kind needs is the one thing about it we can know.
fn presets_for(s: &SessionCfg, p: &ProjectCfg) -> Vec<&'static Preset> {
    let mut names: Vec<&str> = p.presets.iter().map(String::as_str).collect();
    if let Some(own) = s.kind.preset() {
        if !names.contains(&own) {
            names.insert(0, own);
        }
    }
    names
        .into_iter()
        .filter_map(|n| {
            let hit = preset(n);
            if hit.is_none() {
                tracing::warn!(
                    "project {:?} names unknown sandbox preset {n:?}, ignoring",
                    p.name
                );
            }
            hit
        })
        .collect()
}

pub fn build_argv(cfg: &Config, s: &SessionCfg, p: &ProjectCfg) -> Vec<String> {
    let agent_argv: Vec<String> = shell_split(&cfg.command_of(s));
    let dir = expand(&p.dir);

    let overrides = env_pairs(&s.env);

    if !p.sandbox {
        if overrides.is_empty() {
            return agent_argv;
        }
        let mut a: Vec<String> = vec!["env".into()];
        a.extend(overrides.into_iter().map(|(k, v)| format!("{k}={v}")));
        a.extend(agent_argv);
        return a;
    }
    let presets = presets_for(s, p);

    let home = dirs::home_dir()
        .map(|p| p.to_string_lossy().into_owned())
        .unwrap_or_else(|| "/root".into());

    // Global, then presets, then the project, so the most specific answer for a path
    // is the last one bwrap sees.
    let ro = paths(&cfg.sandbox.ro_paths, &presets, |pr| pr.ro, &p.ro_paths);
    let rw = paths(&cfg.sandbox.rw_paths, &presets, |pr| pr.rw, &p.rw_paths);
    let dev = paths(&[], &presets, |pr| pr.dev, &[]);

    let mut a: Vec<String> = vec!["bwrap".into()];
    let mut push = |args: &[&str]| a.extend(args.iter().map(|x| x.to_string()));

    // Built back up rather than inherited: a variable naming a socket that is not there is
    // worse than its absence, a program reading it as "this host has one" and failing at the
    // far end of a connect().
    push(&["--die-with-parent", "--unshare-all", "--clearenv"]);
    if p.net {
        push(&["--share-net"]);
    }

    // systemd-resolved / NetworkManager make /etc/resolv.conf a symlink into /run, which the
    // sandbox never mounts, so with only /etc bound every lookup fails with ENOENT. Prefer
    // resolved's stub listener (127.0.0.53, over the shared loopback): it does the split-DNS
    // routing - a Tailscale uplink, say - that a resolver querying the raw server list gets
    // wrong, where an upstream answers NOTIMP and the agent sees ENOTIMP.
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
    // Usr-merge symlinks, otherwise nothing resolves inside the namespace.
    push(&["--symlink", "usr/lib", "/lib"]);
    push(&["--symlink", "usr/lib", "/lib64"]);
    push(&["--symlink", "usr/bin", "/bin"]);
    push(&["--symlink", "usr/bin", "/sbin"]);

    // The skeleton goes down before any bind: each of these covers what is under it and bwrap
    // mounts in the order given. `x11` binds /tmp/.X11-unix, this tmpfs buried it, and the
    // preset went on handing out a DISPLAY with no socket behind it.
    push(&["--proc", "/proc"]);
    push(&["--dev", "/dev"]);
    push(&["--tmpfs", "/tmp"]);

    for path in &ro {
        push(&["--ro-bind", path, path]);
    }
    // After the ro list: the target can sit under a path a preset binds (the stub is in
    // /run/systemd/resolve and `systemd` binds /run/systemd), and this file has to be on top.
    if let Some((src, target)) = &resolv {
        push(&["--ro-bind", src.as_str(), target.as_str()]);
    }

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

    // Stated rather than forwarded: the terminal is one slopd built, and a daemon has
    // none of its own to inherit.
    push(&["--setenv", "TERM", PANE_TERM]);
    push(&["--setenv", "COLORTERM", "truecolor"]);

    // Compiled in: a config written before --clearenv lists none of them, and an agent with
    // no PATH is a session that starts and dies.
    let mut passed: Vec<String> = Vec::new();
    for (k, v) in std::env::vars() {
        if BASE_ENV.contains(&k.as_str()) || k.starts_with("LC_") {
            push(&["--setenv", k.as_str(), v.as_str()]);
            passed.push(k);
        }
    }

    let mut env: Vec<&str> = cfg.sandbox.pass_env.iter().map(String::as_str).collect();
    for pr in &presets {
        env.extend(pr.env.iter().copied());
    }
    env.extend(p.pass_env.iter().map(String::as_str));

    for k in env {
        if passed.iter().any(|seen| seen == k) {
            continue;
        }
        passed.push(k.to_string());
        if let Ok(v) = std::env::var(k) {
            push(&["--setenv", k, &v]);
        }
    }

    // Last, so a preset that knows what a value must be inside the sandbox beats
    // whatever slopd inherited for the same name.
    for pr in &presets {
        for (k, v) in pr.setenv {
            push(&["--setenv", k, v]);
        }
    }

    for (k, v) in &overrides {
        push(&["--setenv", k.as_str(), v.as_str()]);
    }

    a.push("--".into());
    a.extend(agent_argv);
    a
}

/// Expanded, dropped if they are not on this host, and deduplicated.
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
    for path in global
        .iter()
        .cloned()
        .chain(from_presets)
        .chain(project.iter().cloned())
    {
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

/// No expansion, no globbing - we are building an argv, not running a shell.
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
    use super::*;

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
            presets: vec!["x11".into()],
            ..Default::default()
        };
        build_argv(&cfg, &s, &p)
    }

    fn at(a: &[String], needle: &str) -> usize {
        a.iter()
            .position(|x| x == needle)
            .unwrap_or_else(|| panic!("no {needle} in {a:?}"))
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
