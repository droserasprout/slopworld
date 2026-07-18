use crate::config::{expand, Config, SessionCfg};

/// Builds the argv that tmux will exec. Returns the agent command unwrapped if
/// sandboxing is off for this session.
pub fn build_argv(cfg: &Config, s: &SessionCfg) -> Vec<String> {
    let agent = s.agent.clone().unwrap_or_else(|| cfg.defaults.agent.clone());
    let agent_argv: Vec<String> = shell_split(&agent);
    let dir = expand(&s.dir);

    if !cfg.sandbox.enabled || !s.sandbox {
        return agent_argv;
    }

    let home = dirs::home_dir()
        .map(|p| p.to_string_lossy().into_owned())
        .unwrap_or_else(|| "/root".into());

    let mut a: Vec<String> = vec!["bwrap".into()];
    let mut push = |args: &[&str]| a.extend(args.iter().map(|x| x.to_string()));

    push(&["--die-with-parent", "--unshare-all"]);
    if s.net {
        push(&["--share-net"]);
    }

    for p in &cfg.sandbox.ro_paths {
        let p = expand(p);
        if std::path::Path::new(&p).exists() {
            push(&["--ro-bind", &p, &p]);
        }
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
    for p in cfg.sandbox.rw_paths.iter().chain(s.rw_paths.iter()) {
        let p = expand(p);
        if std::path::Path::new(&p).exists() {
            push(&["--bind", &p, &p]);
        }
    }

    push(&["--bind", &dir, &dir]);
    push(&["--setenv", "HOME", &home]);
    push(&["--setenv", "SLOPWORLD_SESSION", &s.name]);
    push(&["--chdir", &dir]);

    for k in &cfg.sandbox.pass_env {
        if let Ok(v) = std::env::var(k) {
            push(&["--setenv", k, &v]);
        }
    }

    a.push("--".into());
    a.extend(agent_argv);
    a
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
