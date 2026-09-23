use serde_json::json;
use std::io::{self, BufRead, BufReader, LineWriter, Write};
use std::path::PathBuf;
use std::process::{Command as ProcessCommand, Stdio};
use std::sync::{mpsc, Arc, Mutex};
use std::thread;

pub(crate) const DEFAULT_LOG_LINES: usize = 200;
pub(crate) const MAX_LOG_LINES: usize = 100_000;
const GAME_LOG_ENV: &str = "SLOPWORLD_GAME_LOG";
const DAEMON_UNIT_ENV: &str = "SLOPWORLD_DAEMON_UNIT";

pub(crate) const LOGS_USAGE: &str = "usage:
  slopctl logs [game|daemon|all] [--lines N] [--follow]

logs defaults to the last 200 lines from both the game and daemon. Output is
plain text and can be piped to grep, head or other host tools. --json emits
newline-delimited objects with source and line fields.

  game    tails Player.log (default path, or $SLOPWORLD_GAME_LOG)
  daemon  reads the slopd user journal (default unit, or $SLOPWORLD_DAEMON_UNIT)
  all     reads both sources and prefixes plain-text lines with their source

options:
  -n, --lines N   maximum recent lines per source (default 200)
  -f, --follow    keep reading appended lines
";

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
    pub(crate) selection: LogSelection,
    pub(crate) lines: usize,
    pub(crate) follow: bool,
}

pub(crate) fn run_logs(args: &[String], json: bool) -> Result<(), String> {
    let options = parse_logs_args(args)?;
    match options.selection {
        LogSelection::One(source) => run_one_log(source, options, json),
        LogSelection::All => run_all_logs(options, json),
    }
}

pub(crate) fn parse_logs_args(args: &[String]) -> Result<LogsOptions, String> {
    let mut selection = None;
    let mut lines = DEFAULT_LOG_LINES;
    let mut follow = false;
    let mut it = args.iter();

    while let Some(arg) = it.next() {
        let (flag, inline_value) = match arg.split_once('=') {
            Some((flag, value)) if flag == "--lines" => (flag, Some(value)),
            _ => (arg.as_str(), None),
        };

        match flag {
            "game" | "--game" => {
                set_log_selection(&mut selection, LogSelection::One(LogSource::Game), "game")?
            }
            "daemon" | "--daemon" => set_log_selection(
                &mut selection,
                LogSelection::One(LogSource::Daemon),
                "daemon",
            )?,
            "all" | "--all" => set_log_selection(&mut selection, LogSelection::All, "all")?,
            "-f" | "--follow" => follow = true,
            "-n" | "--lines" => {
                let value = inline_value
                    .map(str::to_string)
                    .or_else(|| it.next().cloned())
                    .ok_or_else(|| "--lines needs a number".to_string())?;
                lines = parse_log_lines(&value)?;
            }
            "--help" | "-h" | "help" => {
                // run() handles help before parsing. Also handle it here for independent parser calls and tests.
                return Err(LOGS_USAGE.to_string());
            }
            unknown if unknown.starts_with('-') => {
                return Err(format!("unknown logs option: {unknown}"));
            }
            unknown => return Err(format!("unknown logs source: {unknown}")),
        }
    }

    Ok(LogsOptions {
        selection: selection.unwrap_or(LogSelection::All),
        lines,
        follow,
    })
}

fn set_log_selection(
    selected: &mut Option<LogSelection>,
    next: LogSelection,
    name: &str,
) -> Result<(), String> {
    if selected.replace(next).is_some() {
        return Err(format!("logs source selected more than once (got {name})"));
    }
    Ok(())
}

fn parse_log_lines(value: &str) -> Result<usize, String> {
    let lines = value
        .parse::<usize>()
        .map_err(|_| format!("--lines is not a positive number: {value}"))?;
    if lines == 0 || lines > MAX_LOG_LINES {
        return Err(format!(
            "--lines must be between 1 and {MAX_LOG_LINES}: {value}"
        ));
    }
    Ok(lines)
}

fn game_log_path() -> Result<PathBuf, String> {
    let raw = std::env::var(GAME_LOG_ENV)
        .ok()
        .filter(|value| !value.trim().is_empty())
        .unwrap_or_else(|| {
            "~/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios/Player.log".to_string()
        });
    expand_home(&raw)
}

pub(crate) fn expand_home(value: &str) -> Result<PathBuf, String> {
    if value == "~" || value.starts_with("~/") {
        let home = dirs::home_dir().ok_or("cannot resolve ~: home directory is unknown")?;
        return Ok(if value == "~" {
            home
        } else {
            home.join(&value[2..])
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
            command.arg("--").arg(game_log_path()?);
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

fn run_one_log(source: LogSource, options: LogsOptions, json: bool) -> Result<(), String> {
    let mut child = source_command(source, options)
        .map_err(|e| format!("preparing {} logs: {e}", source.name()))?
        .spawn()
        .map_err(|e| format!("starting {} logs: {e}", source.name()))?;
    let stdout = match child.stdout.take() {
        Some(stdout) => stdout,
        None => {
            let _ = child.kill();
            let _ = child.wait();
            return Err(format!("{} logs produced no output pipe", source.name()));
        }
    };
    let mut reader = BufReader::new(stdout);
    let stdout = io::stdout();
    let mut output = LineWriter::new(stdout.lock());
    let mut line = String::new();

    loop {
        line.clear();
        match reader.read_line(&mut line) {
            Ok(0) => break,
            Ok(_) => {
                let line = clean_log_line(std::mem::take(&mut line));
                if let Err(e) = write_log_line(&mut output, source, &line, false, json) {
                    let _ = child.kill();
                    let _ = child.wait();
                    if e.kind() == io::ErrorKind::BrokenPipe {
                        return Ok(());
                    }
                    return Err(format!("writing {} logs: {e}", source.name()));
                }
            }
            Err(e) => {
                let _ = child.kill();
                let _ = child.wait();
                return Err(format!("reading {} logs: {e}", source.name()));
            }
        }
    }

    if let Err(e) = output.flush() {
        let _ = child.kill();
        let _ = child.wait();
        if e.kind() == io::ErrorKind::BrokenPipe {
            return Ok(());
        }
        return Err(format!("writing {} logs: {e}", source.name()));
    }

    let status = child
        .wait()
        .map_err(|e| format!("waiting for {} logs: {e}", source.name()))?;
    if status.success() {
        Ok(())
    } else {
        Err(format!(
            "{} log command exited with {status}",
            source.name()
        ))
    }
}

enum LogEvent {
    Line(LogSource, String),
    Finished(LogSource, Result<(), String>),
}

type SharedChild = Arc<Mutex<std::process::Child>>;

fn spawn_log_source(
    source: LogSource,
    options: LogsOptions,
    sender: mpsc::Sender<LogEvent>,
) -> Result<(SharedChild, thread::JoinHandle<()>), String> {
    let mut command = source_command(source, options)?;
    let mut child = command
        .spawn()
        .map_err(|e| format!("starting {} logs: {e}", source.name()))?;
    let stdout = match child.stdout.take() {
        Some(stdout) => stdout,
        None => {
            let _ = child.kill();
            let _ = child.wait();
            return Err(format!("{} logs produced no output pipe", source.name()));
        }
    };
    let child = Arc::new(Mutex::new(child));
    let worker_child = Arc::clone(&child);
    let worker = thread::spawn(move || {
        let mut reader = BufReader::new(stdout);
        let mut line = String::new();
        loop {
            line.clear();
            match reader.read_line(&mut line) {
                Ok(0) => break,
                Ok(_) => {
                    let line = clean_log_line(std::mem::take(&mut line));
                    if sender.send(LogEvent::Line(source, line)).is_err() {
                        kill_child(&worker_child);
                        let _ = wait_child(&worker_child, source);
                        return;
                    }
                }
                Err(e) => {
                    kill_child(&worker_child);
                    let _ = wait_child(&worker_child, source);
                    let _ = sender.send(LogEvent::Finished(
                        source,
                        Err(format!("reading {} logs: {e}", source.name())),
                    ));
                    return;
                }
            }
        }

        let result = wait_child(&worker_child, source);
        let _ = sender.send(LogEvent::Finished(source, result));
    });
    Ok((child, worker))
}

fn kill_child(child: &SharedChild) {
    if let Ok(mut child) = child.lock() {
        let _ = child.kill();
    }
}

fn wait_child(child: &SharedChild, source: LogSource) -> Result<(), String> {
    let status = child
        .lock()
        .map_err(|_| format!("waiting for {} logs: child lock poisoned", source.name()))?
        .wait()
        .map_err(|e| format!("waiting for {} logs: {e}", source.name()))?;
    if status.success() {
        Ok(())
    } else {
        Err(format!(
            "{} log command exited with {status}",
            source.name()
        ))
    }
}

fn run_all_logs(options: LogsOptions, json: bool) -> Result<(), String> {
    let (sender, receiver) = mpsc::channel();
    let mut children = Vec::new();
    let mut workers = Vec::new();
    let mut errors = Vec::new();
    for source in [LogSource::Game, LogSource::Daemon] {
        match spawn_log_source(source, options, sender.clone()) {
            Ok((child, worker)) => {
                children.push(child);
                workers.push(worker);
            }
            Err(error) => errors.push(format!("{}: {error}", source.name())),
        }
    }
    drop(sender);

    let stdout = io::stdout();
    let mut output = LineWriter::new(stdout.lock());
    let mut output_error = None;

    for event in receiver {
        match event {
            LogEvent::Line(source, line) => {
                if let Err(e) = write_log_line(&mut output, source, &line, true, json) {
                    output_error = Some(e);
                    break;
                }
            }
            LogEvent::Finished(source, result) => {
                if let Err(error) = result {
                    errors.push(format!("{}: {error}", source.name()));
                }
            }
        }
    }

    // Leaving the receiver loop drops the channel before the worker joins.
    // Also kill child processes after an output error.
    // A worker can still be waiting for a process that follows logs but has no new output.
    if output_error.is_some() {
        for child in &children {
            kill_child(child);
        }
    }
    for worker in workers {
        let _ = worker.join();
    }

    if let Some(error) = output_error {
        if error.kind() == io::ErrorKind::BrokenPipe {
            return Ok(());
        }
        return Err(format!("writing logs: {error}"));
    }
    output.flush().map_err(|e| format!("writing logs: {e}"))?;
    if errors.is_empty() {
        Ok(())
    } else {
        Err(errors.join(". "))
    }
}
