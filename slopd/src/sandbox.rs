use crate::config::{env_pairs, expand, Config, ProjectCfg, SessionCfg};
use crate::presets::{SandboxPreset, Table};

/// Forwarded whatever the config says, plus anything named `LC_*`. What belongs here says
/// something about *this machine* and nothing about what the sandbox can reach; anything
/// naming a socket, a token or a service is a preset.
const BASE_ENV: &[&str] = &["PATH", "LANG", "USER", "LOGNAME", "SHELL"];

const PANE_TERM: &str = "tmux-256color";

/// A name this build has no preset for is dropped with a warning rather than refused: the
/// files outlive the binary, and one bad name is not grounds for an agent that will not
/// start.
fn presets_for<'a>(
    cfg: &Config,
    s: &SessionCfg,
    p: &ProjectCfg,
    t: &'a Table,
) -> Vec<&'a SandboxPreset> {
    cfg.sandbox_of(s, p)
        .into_iter()
        .filter_map(|n| {
            let hit = t.sandbox(&n);
            if hit.is_none() {
                tracing::warn!(
                    "session {:?} in project {:?} names unknown sandbox preset {n:?}, ignoring",
                    s.name,
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
    let table = crate::presets::table();
    let presets = presets_for(cfg, s, p, &table);

    let home = dirs::home_dir()
        .map(|p| p.to_string_lossy().into_owned())
        .unwrap_or_else(|| "/root".into());

    // Global, then presets, then the project, so the most specific answer for a path
    // is the last one bwrap sees.
    let ro = paths(&cfg.sandbox.ro_paths, &presets, |pr| &pr.ro, &p.ro_paths);
    let rw = paths(&cfg.sandbox.rw_paths, &presets, |pr| &pr.rw, &p.rw_paths);
    let dev = paths(&[], &presets, |pr| &pr.dev, &[]);

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
        env.extend(pr.env.iter().map(String::as_str));
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
        for (k, v) in &pr.setenv {
            push(&["--setenv", k.as_str(), v.as_str()]);
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
    presets: &[&SandboxPreset],
    pick: fn(&SandboxPreset) -> &[String],
    project: &[String],
) -> Vec<String> {
    let from_presets: Vec<String> = presets
        .iter()
        .flat_map(|pr| pick(pr).iter().cloned())
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
            sandbox: vec!["x11".into()],
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

    /// The command preset's own binds, whether or not the project asked: knowing a session
    /// is Claude Code is what lets the sandbox hand it ~/.claude.
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
