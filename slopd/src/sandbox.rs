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

/// The same session, spawned *beside* the sandbox rather than inside one: what the sidebar's
/// "Terminal (host)" runs. There is no bwrap, so there is no `--clearenv` either and nothing
/// to build an environment back up from - the pane inherits the tmux server's, which is
/// slopd's, which is the login's. Only the four the sandbox states for its own reasons are
/// stated here too: the two the emulator at the far end assumes about its terminal, and the
/// two an agent reads to learn what it is.
///
/// The directory is tmux's `-c`, the same as for a sandboxed session, so nothing is chdir'd
/// here. Deliberately not reachable from `config.toml`: an entry that could ask for this
/// would be an agent outside the sandbox written down as an ordinary one.
pub fn host_argv(cfg: &Config, s: &SessionCfg, p: &ProjectCfg) -> Vec<String> {
    let mut a: Vec<String> = vec![
        "env".into(),
        format!("TERM={PANE_TERM}"),
        "COLORTERM=truecolor".into(),
        format!("SLOPWORLD_SESSION={}", s.name),
        format!("SLOPWORLD_PROJECT={}", p.name),
    ];
    for (k, v) in env_pairs(&s.env) {
        a.push(format!("{k}={v}"));
    }
    a.extend(shell_split(&host_command(cfg, s, host_shell().as_deref())));
    a
}

/// `$SHELL`, the login shell of whoever slopd runs as, which is the shell a terminal on this
/// machine opens. `None` when the environment does not say - a daemon started without one -
/// and the preset answers instead.
pub fn host_shell() -> Option<String> {
    std::env::var("SHELL")
        .ok()
        .map(|s| s.trim().to_string())
        .filter(|s| !s.is_empty())
}

/// What a host errand actually runs. `$SHELL` when it named nothing: `[defaults] shell` is
/// the answer for a shell *inside* a sandbox, where a login shell's own rc files are mostly
/// not reachable anyway, and out here the machine's own answer is the better one. An errand
/// that did name something - a preset, a command line - is taken at its word and run as
/// asked, which is what keeps `/api/run` with `host` from being a shell and nothing else.
fn host_command(cfg: &Config, s: &SessionCfg, shell: Option<&str>) -> String {
    let asked = s.cmd.is_some() || s.command.trim() != cfg.defaults.shell.trim();
    match shell {
        Some(sh) if !asked => sh.to_string(),
        _ => cfg.command_of(s),
    }
}

/// The word such a session goes by: the project it opened on, then the shell's own basename
/// off `$SHELL` - `slopworld-zsh` in this project, `tmp-bash` in one called `tmp`. Both
/// halves are what a reader wants to know about it and neither is on any other entry, an
/// ordinary agent being named for the errand instead. An errand that named no project - a
/// temporary one, coined after this - is the shell alone. `session_name_for` is the pure half
/// so a test does not have to own the environment.
pub fn host_session_name(project: &str) -> String {
    session_name_for(project, host_shell().as_deref())
}

fn session_name_for(project: &str, shell: Option<&str>) -> String {
    let base = shell.unwrap_or("").rsplit('/').next().unwrap_or("").trim();
    let base = if base.is_empty() { "shell" } else { base };
    match project.trim() {
        "" => base.to_string(),
        p => format!("{p}-{base}"),
    }
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

    /// The point of the host errand: no bwrap anywhere in it, and the shell at the end of it
    /// rather than behind a `--`.
    #[test]
    fn a_host_errand_is_not_sandboxed() {
        let cfg = Config::default();
        let s = SessionCfg {
            name: "a".into(),
            project: "p".into(),
            command: "shell".into(),
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
        // Whichever shell this machine answers with, it is the tail and nothing follows it.
        let want = shell_split(&host_command(&cfg, &s, host_shell().as_deref()));
        assert_eq!(a[a.len() - want.len()..], want[..]);
    }

    /// `[defaults] shell` answers for a shell inside bwrap and `$SHELL` for one beside it -
    /// unless the errand named a shell itself, which is taken at its word either side.
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
        // Nothing in the environment to read: the preset is still there to answer.
        assert_eq!(host_command(&cfg, &bare, None), cfg.command_of(&bare));

        // A preset by name, and a command line of its own: both are run as asked.
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

    /// The name the sidebar shows, and the tmux target behind it: the project, then the
    /// shell. Not a constant either side - two projects open two differently named terminals.
    #[test]
    fn a_host_errand_is_named_for_its_project_and_its_shell() {
        assert_eq!(
            session_name_for("slopworld", Some("/usr/bin/zsh")),
            "slopworld-zsh"
        );
        assert_eq!(session_name_for("tmp", Some("/bin/bash")), "tmp-bash");
        assert_eq!(session_name_for("tmp", Some("fish")), "tmp-fish");
        // No shell to read, and no project to open on: both fall back on their own.
        assert_eq!(session_name_for("tmp", None), "tmp-shell");
        assert_eq!(session_name_for("", Some("/usr/bin/zsh")), "zsh");
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
