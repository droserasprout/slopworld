//! Structured sandbox launch plans and their safe, operator-facing projections.
//!
//! A plan owns the raw arguments only until the launch call has consumed it.  Anything that can
//! leave the daemon goes through `view`, which applies both known-secret and structural
//! redaction.  In particular, the structural rules still protect a worker token after a daemon
//! restart, when the value that was originally minted is no longer available to the process.

use std::path::Path;

use anyhow::{Context, Result};
use serde::{Deserialize, Serialize};

use crate::config::SessionCfg;

pub(crate) const REDACTED: &str = "<redacted>";
pub(crate) const UNKNOWN_ARG: &str = "<arg>";

/// The six diagnostic sections are deliberately separate from the flattened argv.  The latter
/// is produced once, immediately before tmux receives it, and is never used as a display string.
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

/// This is the on-disk and API shape.  It contains only sanitized strings; deserializing it on a
/// later daemon run cannot recover a worker credential that was used for the original launch.
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
    /// Lower the sectioned plan to the exact argv passed to `tmux new-session`.
    pub(crate) fn lower(&self) -> Vec<String> {
        let mut argv = Vec::new();
        argv.extend(self.limits.iter().cloned());
        argv.extend(self.pasta.iter().cloned());
        argv.extend(self.bwrap.iter().cloned());
        // bwrap's mount operations precede its environment operations.  They are kept as
        // separate diagnostic sections even though this is the execution order between them.
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

    /// Human output is intentionally made from the sanitized projection, never from raw argv.
    pub(crate) fn render_human(&self) -> String {
        render_view_human(&self.view())
    }

    /// Atomically save the sanitized projection beside the session's private state.  The plan is
    /// not below any configured private bind, and therefore is not mounted into the guest.
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
    // Treat even a hand-edited artifact as untrusted input.  This also makes the restart path
    // obey the same policy as a freshly lowered plan.
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

/// Used for subprocess errors that might have echoed the command line.  The useful operational
/// fact is that tmux rejected the operation; retaining an unparseable error line is not worth a
/// possible credential leak.
pub(crate) fn sanitize_diagnostic(_text: &str) -> String {
    // tmux does not provide a structured error channel and some versions echo the complete
    // `new-session -- argv` command.  There is no safe way to distinguish an echoed command from
    // an ordinary error after the fact, so retain only the operation-level diagnostic.
    "details omitted by launch redaction policy".into()
}

pub(crate) fn sanitize_process_argv(argv: &[String]) -> Vec<String> {
    // Process titles and argv[0] are writable by descendants, including in the ps fallback.
    // No part of a live argument is trusted as a name (even an environment/option key).
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
        if args[i] == "--setenv" {
            out.push(args[i].clone());
            if let Some(key) = args.get(i + 1) {
                out.push(redact_known(key, secrets));
            }
            if args.get(i + 2).is_some() {
                out.push(REDACTED.into());
            }
            i += 3.min(args.len() - i);
            continue;
        }
        out.push(sanitize_env_assignment(&args[i], secrets));
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
    let first = args[0]
        .split_once('=')
        .map(|_| sanitize_env_assignment(&args[0], secrets))
        .unwrap_or_else(|| redact_known(&args[0], secrets));
    let mut out = vec![first];
    let mut value_for_sensitive = false;
    for arg in &args[1..] {
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
mod tests {
    use super::*;

    #[test]
    fn lowering_preserves_wrappers_and_section_order() {
        let plan = LaunchPlan {
            session: "a".into(),
            limits: vec!["systemd-run".into(), "--".into()],
            pasta: vec!["pasta".into(), "--".into()],
            bwrap: vec!["bwrap".into(), "--clearenv".into()],
            environment: vec!["--setenv".into(), "EMPTY".into(), String::new()],
            mounts: vec!["--bind".into(), "/host".into(), "/guest".into()],
            command: vec!["agent".into(), "two words".into()],
            known_secrets: vec!["worker-secret".into()],
        };
        assert_eq!(
            plan.lower(),
            vec![
                "systemd-run",
                "--",
                "pasta",
                "--",
                "bwrap",
                "--clearenv",
                "--bind",
                "/host",
                "/guest",
                "--setenv",
                "EMPTY",
                "",
                "--",
                "agent",
                "two words"
            ]
        );
    }

    #[test]
    fn environment_and_sensitive_command_values_are_redacted_structurally() {
        let plan = LaunchPlan {
            session: "a".into(),
            limits: Vec::new(),
            pasta: Vec::new(),
            bwrap: Vec::new(),
            environment: vec![
                "--setenv".into(),
                "SLOPD_TOKEN".into(),
                "worker-secret".into(),
                "PATH=/secret/path".into(),
            ],
            mounts: Vec::new(),
            command: vec![
                "agent".into(),
                "--api-key".into(),
                "worker-secret".into(),
                "--model=opus".into(),
                "unknown positional".into(),
            ],
            known_secrets: vec!["worker-secret".into()],
        };
        let view = plan.view();
        assert_eq!(view.environment[2], REDACTED);
        assert_eq!(view.environment[3], "PATH=<redacted>");
        assert_eq!(view.command[1], "--api-key");
        assert_eq!(view.command[2], REDACTED);
        assert_eq!(view.command[3], UNKNOWN_ARG);
        assert_eq!(view.command[4], UNKNOWN_ARG);
        assert!(!serde_json::to_string(&view)
            .unwrap()
            .contains("worker-secret"));
    }

    #[test]
    fn restart_redaction_does_not_need_the_original_secret() {
        let view = PlanView {
            version: 1,
            session: "worker".into(),
            limits: Vec::new(),
            pasta: Vec::new(),
            bwrap: Vec::new(),
            environment: vec!["--setenv".into(), "SLOPD_TOKEN".into(), "old-secret".into()],
            mounts: Vec::new(),
            command: vec!["agent".into(), "--token=old-secret".into()],
            argv: Vec::new(),
        };
        let safe = sanitize_view(view);
        let text = serde_json::to_string(&safe).unwrap();
        assert!(!text.contains("old-secret"));
        assert!(text.contains(REDACTED));
    }

    #[test]
    fn live_process_and_tmux_diagnostics_use_the_same_safe_boundary() {
        let process = sanitize_process_argv(&[
            "agent".into(),
            "--worker-token".into(),
            "worker-secret".into(),
            "SLOPD_TOKEN=worker-secret".into(),
            "--model=opus".into(),
        ]);
        assert!(process.iter().all(|arg| arg == UNKNOWN_ARG));
        let hostile = sanitize_process_argv(&[
            "descendant-secret".into(),
            "https://user:secret@host/?q=value".into(),
            "SECRET=value".into(),
            "--secret-token=value".into(),
            "--help".into(),
        ]);
        assert_eq!(
            hostile,
            [UNKNOWN_ARG, UNKNOWN_ARG, UNKNOWN_ARG, UNKNOWN_ARG, "--help"]
        );
        assert_eq!(
            sanitize_diagnostic("tmux: worker-secret appeared in the command"),
            "details omitted by launch redaction policy"
        );
    }

    #[test]
    fn unknown_assignments_do_not_preserve_url_credentials() {
        let args = vec![
            "agent".into(),
            "https://user:secret@host/?q=value".into(),
            "https://user:secret@host/?token=value".into(),
        ];
        let safe = sanitize_command(&args, &[]);
        assert_eq!(safe, ["agent", UNKNOWN_ARG, UNKNOWN_ARG]);
    }

    #[test]
    fn shell_quote_keeps_argument_boundaries_for_special_values() {
        assert_eq!(shell_quote(""), "''");
        assert_eq!(shell_quote("a b"), "'a b'");
        assert_eq!(shell_quote("a'b"), "'a'\\''b'");
    }

    #[cfg(unix)]
    #[test]
    fn saved_projection_is_private_and_replaces_atomically() {
        use std::os::unix::fs::PermissionsExt;

        let dir = std::env::temp_dir().join(format!(
            "slopd-launch-plan-{}-{}",
            std::process::id(),
            uuid::Uuid::new_v4()
        ));
        std::fs::create_dir_all(&dir).unwrap();
        let path = dir.join("launch-plan.json");
        let plan = LaunchPlan {
            session: "worker".into(),
            limits: Vec::new(),
            pasta: Vec::new(),
            bwrap: Vec::new(),
            environment: vec![
                "--setenv".into(),
                "SLOPD_TOKEN".into(),
                "worker-secret".into(),
            ],
            mounts: Vec::new(),
            command: vec!["agent".into(), "--token".into(), "worker-secret".into()],
            known_secrets: vec!["worker-secret".into()],
        };

        plan.save_at(&path).unwrap();
        let first = std::fs::read_to_string(&path).unwrap();
        assert!(!first.contains("worker-secret"));
        assert_eq!(
            std::fs::metadata(&path).unwrap().permissions().mode() & 0o777,
            0o600
        );
        assert!(!dir.join("launch-plan.json.tmp").exists());

        let mut replacement = plan.clone();
        replacement.session = "replacement".into();
        replacement.save_at(&path).unwrap();
        assert_eq!(
            serde_json::from_str::<PlanView>(&std::fs::read_to_string(&path).unwrap())
                .unwrap()
                .session,
            "replacement"
        );

        std::fs::remove_dir_all(dir).unwrap();
    }
}
