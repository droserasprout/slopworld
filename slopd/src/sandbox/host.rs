//! Host-session commands and shell-like argument parsing without expansion.

use std::path::Path;

use crate::config::{Config, ProjectCfg, SessionCfg};

use super::{PANE_TERM, pane_less_utfchardef};

/// Build the unsandboxed Terminal (host) command. Inherit the slopd environment.
/// Use the same tmux working directory as sandboxed sessions.
/// Library errands must explicitly select host execution. Ordinary agents remain sandboxed.
pub fn host_argv(cfg: &Config, s: &SessionCfg, p: &ProjectCfg) -> Vec<String> {
    let mut a: Vec<String> = vec![
        "env".into(),
        format!("TERM={PANE_TERM}"),
        "COLORTERM=truecolor".into(),
        format!("LESSUTFCHARDEF={}", pane_less_utfchardef()),
        format!("SLOPWORLD_SESSION={}", s.name),
        format!("SLOPWORLD_PROJECT={}", p.name),
    ];
    a.extend(shell_split(&host_command(cfg, s, host_shell().as_deref())));
    a
}

/// Read SHELL from the slopd environment to select the host user's shell.
/// Return None if the variable is absent or empty. The caller then uses the preset.
pub fn host_shell() -> Option<String> {
    std::env::var("SHELL")
        .ok()
        .map(|s| s.trim().to_string())
        .filter(|s| !s.is_empty())
}

/// tmux reports the foreground command name.
/// Treat the login shell and common replacement shells as idle prompts.
/// Treat other foreground commands as active work, even without recent output.
pub fn is_shell_command(command: &str) -> bool {
    let name = Path::new(command)
        .file_name()
        .and_then(|name| name.to_str())
        .unwrap_or(command);
    let name = name.strip_prefix('-').unwrap_or(name);
    if host_shell()
        .as_deref()
        .and_then(|shell| Path::new(shell).file_name())
        .and_then(|shell| shell.to_str())
        .is_some_and(|shell| shell == name)
    {
        return true;
    }
    matches!(
        name,
        "ash"
            | "bash"
            | "csh"
            | "dash"
            | "fish"
            | "ksh"
            | "mksh"
            | "nu"
            | "pwsh"
            | "sh"
            | "tcsh"
            | "xonsh"
            | "zsh"
    )
}

/// An unnamed host errand uses SHELL. An explicit preset or command line runs as configured.
pub(super) fn host_command(cfg: &Config, s: &SessionCfg, shell: Option<&str>) -> String {
    let asked = s.cmd.is_some() || s.command.trim() != cfg.defaults.shell.trim();
    match shell {
        Some(sh) if !asked => sh.to_string(),
        _ => cfg.command_of(s),
    }
}

/// Names a host session `<project>-<shell>`, or just the shell when no project is set.
pub fn host_session_name(project: &str) -> String {
    let raw = session_name_for(project, host_shell().as_deref());
    let mut out = String::with_capacity(raw.len());
    for ch in raw.chars() {
        if ch.is_whitespace() || ch == ':' || ch == '.' || ch == '/' {
            if !out.ends_with('-') {
                out.push('-');
            }
        } else {
            out.push(ch);
        }
    }
    let out = out.trim_matches('-').to_string();
    if out.is_empty() { "shell".into() } else { out }
}

pub(super) fn session_name_for(project: &str, shell: Option<&str>) -> String {
    let base = shell.unwrap_or("").rsplit('/').next().unwrap_or("").trim();
    let base = if base.is_empty() { "shell" } else { base };
    match project.trim() {
        "" => base.to_string(),
        p => format!("{p}-{base}"),
    }
}

/// Preserve argv boundaries without expansion. Inside double quotes, backslash
/// escapes only `$`, backtick, quote, backslash and newline, as in a POSIX shell.
/// Incomplete quotes are accepted; a trailing backslash stays literal.
pub fn shell_split(s: &str) -> Vec<String> {
    let mut out = Vec::new();
    let mut cur = String::new();
    let mut quote: Option<char> = None;
    let mut any = false;
    let mut escaped = false;

    for c in s.chars() {
        if escaped {
            if quote == Some('"') && !matches!(c, '$' | '`' | '"' | '\\' | '\n') {
                cur.push('\\');
            }
            if c != '\n' {
                cur.push(c);
            }
            any = true;
            escaped = false;
            continue;
        }

        match quote {
            Some('\'') if c == '\'' => quote = None,
            Some('"') if c == '"' => quote = None,
            Some('"') if c == '\\' => escaped = true,
            None if c == '\\' => {
                escaped = true;
                any = true;
            }
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
            _ => cur.push(c),
        }
    }
    if escaped {
        cur.push('\\');
    }
    if !cur.is_empty() || any {
        out.push(cur);
    }
    out
}

#[cfg(test)]
#[path = "host_tests.rs"]
mod tests;
