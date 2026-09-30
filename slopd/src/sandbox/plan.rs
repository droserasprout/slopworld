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

mod redaction;
pub(crate) use redaction::sanitize_process_argv;
use redaction::{
    redact_known, sanitize_command, sanitize_environment, sanitize_process_arg, sanitize_section,
};

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
        lower_view(
            &self.limits,
            &self.pasta,
            &self.bwrap,
            &self.mounts,
            &self.environment,
            &self.command,
        )
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
            session: redact_known(&self.session, &self.known_secrets),
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
        let text = serde_json::to_string_pretty(&self.view())? + "\n";
        crate::paths::write_atomic(path, &text, Some(0o600))
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
    // After restart only fixed diagnostics and canonical scope identity survive;
    // unknown credentials cannot be recovered from the original launch.
    let mut view = sanitize_view(view);
    view.session = session.name.clone();
    Ok(Some(view))
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
    // Only fixed diagnostics and a validated launch-scope identity survive an
    // untrusted saved file. Paths, keys, executable names and wrapper values may
    // contain credentials that are no longer known after a daemon restart.
    let limits = view
        .limits
        .iter()
        .map(|arg| {
            if launch_scope(arg).is_some() {
                arg.clone()
            } else {
                sanitize_process_arg(arg)
            }
        })
        .collect::<Vec<_>>();
    let pasta = sanitize_process_argv(&view.pasta);
    let bwrap = sanitize_process_argv(&view.bwrap);
    let environment = sanitize_process_argv(&view.environment);
    let mounts = sanitize_process_argv(&view.mounts);
    let command = sanitize_process_argv(&view.command);
    let argv = lower_view(&limits, &pasta, &bwrap, &mounts, &environment, &command);
    PlanView {
        version: 1,
        session: REDACTED.into(),
        limits,
        pasta,
        bwrap,
        environment,
        mounts,
        command,
        argv,
    }
}

/// A fixed prefix and canonical UUID keep scope identity safe for persisted views.
pub(super) fn launch_scope(arg: &str) -> Option<&str> {
    let unit = arg.strip_prefix("--unit=")?;
    let id = unit.strip_prefix("slopworld-")?.strip_suffix(".scope")?;
    let parsed = uuid::Uuid::parse_str(id).ok()?;
    (parsed.to_string() == id).then_some(unit)
}

#[cfg(test)]
#[path = "plan_tests.rs"]
mod tests;
