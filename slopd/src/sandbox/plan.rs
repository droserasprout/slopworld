//! Build sandbox launch plans and redacted operator views.
//!
//! Keep raw arguments in the plan until the daemon starts the session.
//! Logs, API responses, and saved files use the redacted view.
//! The view removes known secrets and sensitive values.
//! After a restart, structural rules still hide worker tokens that the daemon no longer knows.

use std::path::Path;

use anyhow::{Context, Result};
use serde::{Deserialize, Serialize};

use crate::config::SessionCfg;

pub(crate) const REDACTED: &str = "<redacted>";
pub(crate) const UNKNOWN_ARG: &str = "<arg>";

/// Keep the six diagnostic sections separate from the argument list.
/// Combine them immediately before sending them to tmux. Never display raw arguments.
#[derive(Debug, Clone)]
pub(crate) struct LaunchPlan {
    pub(crate) session: String,
    pub(crate) limits: Vec<String>,
    pub(crate) pasta: Vec<String>,
    pub(crate) bwrap: Vec<String>,
    pub(crate) environment: Vec<String>,
    pub(crate) mounts: Vec<String>,
    pub(crate) command: Vec<String>,
    pub(crate) known_secrets: Vec<String>,
}

/// Define the fields for saved plans and API responses.
/// The strings are sanitized before storage. A saved file cannot reveal a worker token after restart.
#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
pub(crate) struct PlanView {
    pub(crate) version: u8,
    pub(crate) session: String,
    pub(crate) limits: Vec<String>,
    pub(crate) pasta: Vec<String>,
    pub(crate) bwrap: Vec<String>,
    pub(crate) environment: Vec<String>,
    pub(crate) mounts: Vec<String>,
    pub(crate) command: Vec<String>,
    pub(crate) argv: Vec<String>,
}

impl LaunchPlan {
    /// Convert the plan sections to the argument list for `tmux new-session`.
    pub(crate) fn lower(&self) -> Vec<String> {
        let mut argv = Vec::new();
        argv.extend(self.limits.iter().cloned());
        argv.extend(self.pasta.iter().cloned());
        argv.extend(self.bwrap.iter().cloned());
        // bwrap runs mount operations before environment operations.
        // Keep these operations in separate diagnostic sections.
        argv.extend(self.mounts.iter().cloned());
        argv.extend(self.environment.iter().cloned());
        argv.push("--".into());
        argv.extend(self.command.iter().cloned());
        argv
    }

    pub(crate) fn view(&self) -> PlanView {
        let limits = sanitize_section(&self.limits, &self.known_secrets);
        let pasta = sanitize_section(&self.pasta, &self.known_secrets);
        let bwrap = sanitize_section(&self.bwrap, &self.known_secrets);
        let environment = sanitize_environment(&self.environment, &self.known_secrets);
        let mounts = sanitize_section(&self.mounts, &self.known_secrets);
        let command = sanitize_command(&self.command, &self.known_secrets);
        let argv = lower_view(&limits, &pasta, &bwrap, &mounts, &environment, &command);
        PlanView {
            version: 1,
            session: self.session.clone(),
            limits,
            pasta,
            bwrap,
            environment,
            mounts,
            command,
            argv,
        }
    }

    /// Format operator output from the redacted view, never from raw arguments.
    pub(crate) fn render_human(&self) -> String {
        render_view_human(&self.view())
    }

    /// Save the redacted view beside the session's private state.
    /// Configured private mounts do not expose this file to the guest.
    pub(crate) fn save(&self, session: &SessionCfg) -> Result<()> {
        let dir = super::state_dir(session)?;
        std::fs::create_dir_all(&dir).with_context(|| format!("making {}", dir.display()))?;
        self.save_at(&dir.join("launch-plan.json"))
    }

    fn save_at(&self, path: &Path) -> Result<()> {
        let dir = path
            .parent()
            .context("launch plan path has no parent directory")?;
        let tmp = dir.join("launch-plan.json.tmp");
        let text = serde_json::to_string_pretty(&self.view())? + "\n";
        std::fs::write(&tmp, text).with_context(|| format!("writing {}", tmp.display()))?;
        set_private_mode(&tmp)?;
        std::fs::rename(&tmp, path)
            .with_context(|| format!("installing launch plan {}", path.display()))?;
        Ok(())
    }
}

pub(crate) fn read(session: &SessionCfg) -> Result<Option<PlanView>> {
    let path = super::state_dir(session)?.join("launch-plan.json");
    let text = match std::fs::read_to_string(&path) {
        Ok(text) => text,
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => return Ok(None),
        Err(error) => return Err(error).with_context(|| format!("reading {}", path.display())),
    };
    let view: PlanView = serde_json::from_str(&text)
        .with_context(|| format!("parsing saved launch plan {}", path.display()))?;
    // Treat saved files as untrusted input, including files with manual changes.
    // Apply the same redaction policy after a restart as for a new plan.
    Ok(Some(sanitize_view(view)))
}

pub(crate) fn render_view_human(view: &PlanView) -> String {
    let mut out = format!("session: {}\n", view.session);
    for (name, args) in [
        ("limits", &view.limits),
        ("pasta", &view.pasta),
        ("bwrap", &view.bwrap),
        ("environment", &view.environment),
        ("mounts", &view.mounts),
        ("command", &view.command),
    ] {
        out.push_str(name);
        out.push_str(":\n");
        if args.is_empty() {
            out.push_str("  (none)\n");
        } else {
            for arg in args {
                out.push_str("  ");
                out.push_str(&shell_quote(arg));
                out.push('\n');
            }
        }
    }
    out
}

pub(crate) fn shell_quote(value: &str) -> String {
    if !value.is_empty()
        && value
            .bytes()
            .all(|byte| byte.is_ascii_alphanumeric() || b"_./:@%+=,-".contains(&byte))
    {
        return value.to_string();
    }
    format!("'{}'", value.replace('\'', "'\\''"))
}

/// Remove subprocess details that can include command-line credentials.
/// The caller can still report that tmux rejected the operation.
pub(crate) fn sanitize_diagnostic(_text: &str) -> String {
    // Some tmux versions include the full `new-session -- argv` command in errors.
    // The error format does not separate arguments from other details.
    // Keep only a fixed message about the failed operation.
    "details omitted by launch redaction policy".into()
}

pub(crate) fn sanitize_process_argv(argv: &[String]) -> Vec<String> {
    // Child processes can change their titles and `argv[0]`.
    // Treat live arguments as untrusted, including environment names and option keys.
    argv.iter()
        .map(|arg| {
            if safe_command_flag(arg) {
                arg.clone()
            } else {
                UNKNOWN_ARG.into()
            }
        })
        .collect()
}

fn lower_view(
    limits: &[String],
    pasta: &[String],
    bwrap: &[String],
    mounts: &[String],
    environment: &[String],
    command: &[String],
) -> Vec<String> {
    let mut argv = Vec::new();
    argv.extend(limits.iter().cloned());
    argv.extend(pasta.iter().cloned());
    argv.extend(bwrap.iter().cloned());
    argv.extend(mounts.iter().cloned());
    argv.extend(environment.iter().cloned());
    argv.push("--".into());
    argv.extend(command.iter().cloned());
    argv
}

fn sanitize_view(view: PlanView) -> PlanView {
    let limits = sanitize_section(&view.limits, &[]);
    let pasta = sanitize_section(&view.pasta, &[]);
    let bwrap = sanitize_section(&view.bwrap, &[]);
    let environment = sanitize_environment(&view.environment, &[]);
    let mounts = sanitize_section(&view.mounts, &[]);
    let command = sanitize_command(&view.command, &[]);
    let argv = lower_view(&limits, &pasta, &bwrap, &mounts, &environment, &command);
    PlanView {
        version: view.version,
        session: redact_known(&view.session, &[]),
        limits,
        pasta,
        bwrap,
        environment,
        mounts,
        command,
        argv,
    }
}

fn sanitize_section(args: &[String], secrets: &[String]) -> Vec<String> {
    args.iter().map(|arg| redact_known(arg, secrets)).collect()
}

fn sanitize_environment(args: &[String], secrets: &[String]) -> Vec<String> {
    let mut out = Vec::with_capacity(args.len());
    let mut i = 0;
    while i < args.len() {
        let Some(arg) = args.get(i) else {
            break;
        };
        if arg == "--setenv" {
            out.push(arg.clone());
            if let Some(key) = args.get(i + 1) {
                out.push(redact_known(key, secrets));
            }
            if args.get(i + 2).is_some() {
                out.push(REDACTED.into());
            }
            i += 3.min(args.len() - i);
            continue;
        }
        out.push(sanitize_env_assignment(arg, secrets));
        i += 1;
    }
    out
}

fn sanitize_env_assignment(arg: &str, secrets: &[String]) -> String {
    let Some((key, _)) = arg.split_once('=') else {
        return redact_known(arg, secrets);
    };
    format!("{key}={REDACTED}")
}

fn sanitize_command(args: &[String], secrets: &[String]) -> Vec<String> {
    if args.is_empty() {
        return Vec::new();
    }
    let Some(first_arg) = args.first() else {
        return Vec::new();
    };
    let first = first_arg
        .split_once('=')
        .map(|_| sanitize_env_assignment(first_arg, secrets))
        .unwrap_or_else(|| redact_known(first_arg, secrets));
    let mut out = vec![first];
    let mut value_for_sensitive = false;
    for arg in args.iter().skip(1) {
        if value_for_sensitive {
            out.push(REDACTED.into());
            value_for_sensitive = false;
            continue;
        }
        if let Some((key, _value)) = arg.split_once('=') {
            if is_env_key(key) {
                out.push(sanitize_env_assignment(arg, secrets));
                continue;
            }
            if is_sensitive_key(key) {
                out.push(format!("{}={REDACTED}", redact_known(key, secrets)));
            } else {
                out.push(UNKNOWN_ARG.into());
            }
            continue;
        }
        if let Some(key) = arg.strip_prefix('-') {
            if is_sensitive_key(key) {
                out.push(redact_known(arg, secrets));
                value_for_sensitive = true;
            } else if safe_command_flag(arg) {
                out.push(redact_known(arg, secrets));
            } else {
                out.push(UNKNOWN_ARG.into());
            }
            continue;
        }
        if arg.contains('=') && is_env_key(arg.split('=').next().unwrap_or_default()) {
            out.push(sanitize_env_assignment(arg, secrets));
        } else {
            out.push(UNKNOWN_ARG.into());
        }
    }
    out
}

fn safe_command_flag(arg: &str) -> bool {
    matches!(
        arg,
        "-h" | "--help"
            | "-q"
            | "--quiet"
            | "-v"
            | "--verbose"
            | "-y"
            | "--yes"
            | "--full-auto"
            | "--dangerously-bypass-approvals-and-sandbox"
            | "--no-alt-screen"
            | "--json"
            | "--version"
    )
}

fn is_env_key(key: &str) -> bool {
    !key.is_empty()
        && key.bytes().enumerate().all(|(i, byte)| {
            byte == b'_' || byte.is_ascii_uppercase() || (i > 0 && byte.is_ascii_digit())
        })
}

fn is_sensitive_key(key: &str) -> bool {
    let key = key.trim_start_matches('-').to_ascii_lowercase();
    [
        "token",
        "key",
        "api-key",
        "apikey",
        "secret",
        "password",
        "passwd",
        "credential",
        "auth",
        "cookie",
        "private-key",
        "refresh-token",
        "access-token",
    ]
    .iter()
    .any(|needle| key == *needle || key == format!("worker-{needle}"))
}

fn redact_known(value: &str, secrets: &[String]) -> String {
    let mut out = value.to_string();
    for secret in secrets.iter().filter(|secret| !secret.is_empty()) {
        out = out.replace(secret, REDACTED);
    }
    out
}

#[cfg(unix)]
fn set_private_mode(path: &Path) -> Result<()> {
    use std::os::unix::fs::PermissionsExt;
    std::fs::set_permissions(path, std::fs::Permissions::from_mode(0o600))?;
    Ok(())
}

#[cfg(not(unix))]
fn set_private_mode(_path: &Path) -> Result<()> {
    Ok(())
}

#[cfg(test)]
#[path = "plan_tests.rs"]
mod tests;
