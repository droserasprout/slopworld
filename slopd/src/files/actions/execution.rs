//! Non-interactive host file actions; callers supply an accepted checkout scope.
use super::{absolute_path, project_action_path};
use crate::config::{Config, ProjectCfg, SessionCfg, expand};
use crate::process::{self, CaptureLimits};
use anyhow::{Result, anyhow, bail};
use std::time::Duration;
use tokio::process::Command;
const FILE_ACTION_TIMEOUT: Duration = Duration::from_secs(15);
const FILE_ACTION_STREAM_LIMIT: usize = 4096;
pub(crate) fn resolve_file_action(
    cfg: &Config,
    project: &str,
    raw_path: &str,
    command: &str,
    host: bool,
) -> Result<(ProjectCfg, SessionCfg)> {
    let p = if host && project.trim().is_empty() {
        absolute_path(raw_path)?;
        ProjectCfg::default()
    } else {
        let p = cfg
            .project(project.trim())
            .cloned()
            .ok_or_else(|| anyhow!("Project {project:?} does not exist."))?;
        project_action_path(&p, raw_path)?;
        p
    };
    if command.trim().is_empty() {
        bail!("file action has no command");
    }
    // The client already substituted and quoted paths. Placeholder text can now be
    // literal filename data, so never process this command as a template again.
    let s = SessionCfg {
        name: "file-action".into(),
        project: p.name.clone(),
        command: cfg.defaults.shell.clone(),
        cmd: Some(command.to_owned()),
        ..Default::default()
    };
    Ok((p, s))
}

async fn run_file_action_command(
    argv: &[String],
    directory: &str,
) -> Result<process::BoundedOutput> {
    let (program, arguments) = argv
        .split_first()
        .ok_or_else(|| anyhow!("command has no executable"))?;
    let mut command = Command::new(program);
    command.args(arguments);
    if !directory.is_empty() {
        command.current_dir(directory);
    }
    process::run_bounded(
        &mut command,
        FILE_ACTION_TIMEOUT,
        CaptureLimits {
            stdout: FILE_ACTION_STREAM_LIMIT,
            stderr: FILE_ACTION_STREAM_LIMIT,
        },
    )
    .await
    .map_err(|error| {
        if error.downcast_ref::<process::TimedOut>().is_some() {
            anyhow!("file action timed out")
        } else {
            error.context("starting file action")
        }
    })
}

fn format_file_action_result(
    stdout: &[u8],
    stderr: &[u8],
    stdout_truncated: bool,
    stderr_truncated: bool,
    status: std::process::ExitStatus,
) -> Result<String> {
    let mut text = String::from_utf8_lossy(stdout).into_owned();
    if !stderr.is_empty() {
        if !text.is_empty() && !text.ends_with('\n') {
            text.push('\n');
        }
        text.push_str(&String::from_utf8_lossy(stderr));
    }
    if stdout_truncated || stderr_truncated {
        text.push_str("\n[output truncated]");
    }
    if !status.success() {
        bail!("file action failed: {}", text.trim());
    }
    Ok(if text.trim().is_empty() {
        "(no output)".into()
    } else {
        text.trim_end().into()
    })
}

pub(crate) async fn execute_file_action(
    cfg: &Config,
    s: &SessionCfg,
    p: &ProjectCfg,
) -> Result<String> {
    // File actions execute on the host as user operations.
    // Project scope validates their paths without restricting execution to the project sandbox.
    let argv = crate::sandbox::host_argv(cfg, s, p)?;
    let output = run_file_action_command(&argv, &expand(&p.dir)).await?;
    format_file_action_result(
        &output.stdout,
        &output.stderr,
        output.stdout_truncated,
        output.stderr_truncated,
        output.status,
    )
}

#[cfg(test)]
#[path = "execution_tests.rs"]
mod tests;
