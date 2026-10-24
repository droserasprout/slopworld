//! Log selection, local source commands, and line rendering.
//! `stream` owns readers, bounded fan-in, and child cancellation/reaping.

use crate::shared::application_paths::Mode;
use serde_json::json;
use std::io::{self, Write};
use std::path::PathBuf;
use std::process::{Command as ProcessCommand, Stdio};

mod stream;
use stream::{run_all_logs, run_one_log};

pub(crate) const DEFAULT_LOG_LINES: usize = 200;
pub(crate) const MAX_LOG_LINES: usize = 100_000;
const DAEMON_UNIT_ENV: &str = "SLOPWORLD_DAEMON_UNIT";

#[derive(Clone, Copy, Debug, clap::Args)]
#[group(id = "log-options", multiple = true)]
#[command(
    after_help = "Defaults to the last 200 lines from both sources. --json emits one object per line.\nGame: Player.log or SLOPWORLD_GAME_LOG.\nDaemon: user journal (SLOPWORLD_DAEMON_UNIT), or Docker logs in sidecar mode (SLOPCAR_CONTAINER)."
)]
pub(crate) struct LogsArgs {
    /// Log source (defaults to all)
    #[arg(value_enum)]
    source: Option<LogName>,
    /// Maximum recent lines per source
    #[arg(short = 'n', long, default_value_t = DEFAULT_LOG_LINES, value_parser = parse_log_lines)]
    lines: usize,
    /// Keep reading appended lines
    #[arg(short = 'f', long)]
    follow: bool,
}

#[derive(Clone, Copy, Debug, clap::ValueEnum)]
enum LogName {
    Game,
    Daemon,
    All,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub(crate) enum LogSource {
    Game,
    Daemon,
}

impl LogSource {
    fn name(self) -> &'static str {
        match self {
            Self::Game => "game",
            Self::Daemon => "daemon",
        }
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub(crate) enum LogSelection {
    One(LogSource),
    All,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub(crate) struct LogsOptions {
    pub(crate) mode: Mode,
    pub(crate) selection: LogSelection,
    pub(crate) lines: usize,
    pub(crate) follow: bool,
}

pub(crate) fn run_logs(options: LogsOptions, json: bool) -> Result<(), String> {
    match options.selection {
        LogSelection::One(source) => run_one_log(source, options, json),
        LogSelection::All => run_all_logs(options, json),
    }
}

impl LogsOptions {
    pub(crate) fn from_args(args: LogsArgs, mode: Mode) -> Self {
        let source = args.source.unwrap_or(LogName::All);
        Self {
            mode,
            lines: args.lines,
            follow: args.follow,
            selection: match source {
                LogName::Game => LogSelection::One(LogSource::Game),
                LogName::Daemon => LogSelection::One(LogSource::Daemon),
                LogName::All => LogSelection::All,
            },
        }
    }
}

#[cfg(test)]
pub(crate) fn parse_logs_args(args: &[String]) -> Result<LogsOptions, String> {
    let mut command = vec!["logs".into()];
    command.extend_from_slice(args);
    match super::cli::parse_command(&command)? {
        super::cli::Command::Logs { options } => Ok(options),
        _ => Err("expected logs options".into()),
    }
}

fn parse_log_lines(value: &str) -> Result<usize, String> {
    let lines = value
        .parse::<usize>()
        .map_err(|_error| format!("--lines is not a positive number: {value}"))?;
    if lines == 0 || lines > MAX_LOG_LINES {
        return Err(format!(
            "--lines must be between 1 and {MAX_LOG_LINES}: {value}"
        ));
    }
    Ok(lines)
}

fn game_log_path(mode: Mode) -> Result<PathBuf, String> {
    let path = mode.game_log_path();
    expand_home(&path.to_string_lossy())
}

pub(crate) fn expand_home(value: &str) -> Result<PathBuf, String> {
    if value == "~" || value.starts_with("~/") {
        let home = dirs::home_dir().ok_or("cannot resolve ~: home directory is unknown")?;
        return Ok(if value == "~" {
            home
        } else {
            home.join(value.strip_prefix("~/").unwrap_or_default())
        });
    }
    Ok(PathBuf::from(value))
}

fn daemon_unit() -> String {
    std::env::var(DAEMON_UNIT_ENV)
        .ok()
        .filter(|value| !value.trim().is_empty())
        .unwrap_or_else(|| "slopd.service".to_string())
}

pub(crate) fn source_command(
    source: LogSource,
    options: LogsOptions,
) -> Result<ProcessCommand, String> {
    let mut command = match source {
        LogSource::Game => {
            let mut command = ProcessCommand::new("tail");
            command.arg("--lines").arg(options.lines.to_string());
            if options.follow {
                // -F follows the file name so output continues after a RimWorld restart or Player.log replacement.
                command.arg("--follow=name");
            }
            command.arg("--").arg(game_log_path(options.mode)?);
            command
        }
        LogSource::Daemon if options.mode == Mode::Sidecar => {
            let mut command = ProcessCommand::new("docker");
            command
                .args(["logs", "--tail"])
                .arg(options.lines.to_string());
            if options.follow {
                command.arg("--follow");
            }
            command.arg(
                std::env::var("SLOPCAR_CONTAINER")
                    .ok()
                    .filter(|value| !value.is_empty())
                    .unwrap_or_else(|| "slopcar".into()),
            );
            command
        }
        LogSource::Daemon => {
            let mut command = ProcessCommand::new("journalctl");
            command
                .args(["--user", "--no-pager", "--output=short-iso", "--lines"])
                .arg(options.lines.to_string())
                .arg("--unit")
                .arg(daemon_unit());
            if options.follow {
                command.arg("--follow");
            }
            command
        }
    };
    command
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::inherit());
    Ok(command)
}

pub(crate) fn clean_log_line(mut line: String) -> String {
    if line.ends_with('\n') {
        line.pop();
    }
    if line.ends_with('\r') {
        line.pop();
    }
    line
}

pub(crate) fn write_log_line<W: Write>(
    output: &mut W,
    source: LogSource,
    line: &str,
    prefix: bool,
    json: bool,
) -> io::Result<()> {
    if json {
        let value = json!({ "source": source.name(), "line": line });
        let encoded = serde_json::to_string(&value).map_err(|e| io::Error::other(e.to_string()))?;
        writeln!(output, "{encoded}")
    } else if prefix {
        writeln!(output, "[{}] {line}", source.name())
    } else {
        writeln!(output, "{line}")
    }
}
