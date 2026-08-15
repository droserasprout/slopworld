use std::io::{self, BufRead, BufReader, LineWriter, Read, Write};
use std::path::PathBuf;
use std::process::{Command, ExitCode, Stdio};
use std::sync::{mpsc, Arc, Mutex};
use std::thread;
use std::time::{SystemTime, UNIX_EPOCH};

use serde::Deserialize;
use serde_json::{json, Value};

/// Mirrors `tasks::HOST` in the daemon. `src/bin` is its own crate root and this crate has no
/// library target, so the name is restated rather than imported; it is the whole of the contract.
const HOST: &str = "host";
const DEFAULT_LOG_LINES: usize = 200;
const MAX_LOG_LINES: usize = 100_000;
const GAME_LOG_ENV: &str = "SLOPWORLD_GAME_LOG";
const DAEMON_UNIT_ENV: &str = "SLOPWORLD_DAEMON_UNIT";

const LOGS_USAGE: &str = "usage:
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

const USAGE: &str = "slopctl - delegate work and inspect SlopWorld diagnostics

usage:
  slopctl delegate AGENT TASK...
  slopctl inbox [--all] [--sent] [--received] [--status STATUS]
  slopctl task ID
  slopctl accept ID [NOTE...]
  slopctl progress ID [NOTE...]
  slopctl finish ID [RESULT...]
  slopctl fail ID [ERROR...]
  slopctl rm ID
  slopctl prune [--all]
  slopctl peers
  slopctl status
  slopctl logs [game|daemon|all] [--lines N] [--follow]

inbox shows unfinished work in both directions, newest first; --all adds what is
done and failed. rm takes a finished task, prune takes all of them. --json is
accepted anywhere and prints the answer as JSON instead of for a reader.

SLOPWORLD_SESSION identifies the caller, and defaults to `host` - the user at the
keyboard - which the daemon accepts only from the root token. SLOPD_ENDPOINT
selects endpoint.toml; SLOPD_URL and SLOPD_TOKEN override it.
";

#[derive(Deserialize)]
struct Endpoint {
    url: String,
    token: String,
}

fn main() -> ExitCode {
    match run() {
        Ok(()) => ExitCode::SUCCESS,
        Err(e) => {
            eprintln!("slopctl: {e}");
            ExitCode::FAILURE
        }
    }
}

fn run() -> Result<(), String> {
    let mut args: Vec<String> = std::env::args().skip(1).collect();
    if args.is_empty() || matches!(args[0].as_str(), "-h" | "--help" | "help") {
        print!("{USAGE}");
        return Ok(());
    }
    // A global: it changes how an answer is rendered, never which answer is asked for, so it is
    // taken out of the argument list before any command has to think about it.
    let json = args.iter().any(|a| a == "--json");
    args.retain(|a| a != "--json");

    // Logs are deliberately local: they remain useful when slopd is down and do not need the
    // endpoint token. Dispatch before loading endpoint.toml for that reason.
    if args[0] == "logs" {
        if args[1..]
            .iter()
            .any(|a| matches!(a.as_str(), "-h" | "--help" | "help"))
        {
            print!("{LOGS_USAGE}");
            return Ok(());
        }
        return run_logs(&args[1..], json);
    }

    let endpoint = load_endpoint()?;
    // Unset means the host: running `slopctl` by hand is the common case, and asking the user to
    // name themselves before they can read their own inbox buys nothing.
    let session = std::env::var("SLOPWORLD_SESSION")
        .ok()
        .filter(|s| !s.trim().is_empty())
        .unwrap_or_else(|| HOST.to_string());

    match args[0].as_str() {
        "delegate" => {
            let to = arg(&args, 1, "delegate needs the agent to send to")?;
            let body = rest(&args, 2, "delegate needs a task body")?;
            let v = request(
                &endpoint,
                &session,
                "POST",
                "/api/tasks",
                Some(json!({"to": to, "body": body})),
            )?;
            emit(&v, json);
        }
        "inbox" => {
            let filter = InboxFilter::parse(&args[1..])?;
            let v = request(&endpoint, &session, "GET", "/api/tasks", None)?;
            let tasks = filter.apply(&v, &session);
            if json {
                print_json(&Value::Array(tasks.into_iter().cloned().collect()));
            } else if tasks.is_empty() {
                // stderr, so an empty inbox stays an empty stdout for whatever is reading it.
                eprintln!("no tasks");
            } else {
                tasks.iter().for_each(|t| print_task(t));
            }
        }
        "task" => {
            let id = arg(&args, 1, "task needs a task id")?;
            only(&args, 2)?;
            let v = request(
                &endpoint,
                &session,
                "GET",
                &format!("/api/tasks/{id}"),
                None,
            )?;
            emit(&v, json);
        }
        command @ ("accept" | "progress" | "finish" | "fail") => {
            let id = arg(&args, 1, &format!("{command} needs a task id"))?;
            let status = match command {
                "accept" => "accepted",
                "progress" => "working",
                "finish" => "done",
                _ => "failed",
            };
            let note = (args.len() > 2).then(|| args[2..].join(" "));
            let v = request(
                &endpoint,
                &session,
                "POST",
                &format!("/api/tasks/{id}"),
                Some(json!({"status": status, "note": note})),
            )?;
            emit(&v, json);
        }
        "rm" => {
            let id = arg(&args, 1, "rm needs a task id")?;
            only(&args, 2)?;
            let v = request(
                &endpoint,
                &session,
                "DELETE",
                &format!("/api/tasks/{id}"),
                None,
            )?;
            emit(&v, json);
        }
        "prune" => {
            let path = match args.get(1).map(String::as_str) {
                None => "/api/tasks",
                Some("--all") => "/api/tasks?all=true",
                Some(flag) => return Err(format!("unknown flag: {flag}\n\n{USAGE}")),
            };
            only(&args, 2)?;
            let v = request(&endpoint, &session, "DELETE", path, None)?;
            if json {
                print_json(&v);
            } else {
                println!("removed {}", v["removed"].as_u64().unwrap_or(0));
            }
        }
        "peers" => {
            only(&args, 1)?;
            let v = request(&endpoint, &session, "GET", "/api/sessions", None)?;
            let names = peer_names(&v);
            if json {
                print_json(&json!(names));
            } else {
                for name in names {
                    let mark = if name == session { " (you)" } else { "" };
                    println!("{name}{mark}");
                }
            }
        }
        "status" => {
            only(&args, 1)?;
            let v = status_value(&endpoint, &session);
            if json {
                print_json(&v);
            } else {
                print_status(&v);
            }
        }
        command => return Err(format!("unknown command: {command}\n\n{USAGE}")),
    }
    Ok(())
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
enum LogSource {
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
enum LogSelection {
    One(LogSource),
    All,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq)]
struct LogsOptions {
    selection: LogSelection,
    lines: usize,
    follow: bool,
}

fn run_logs(args: &[String], json: bool) -> Result<(), String> {
    let options = parse_logs_args(args)?;
    match options.selection {
        LogSelection::One(source) => run_one_log(source, options, json),
        LogSelection::All => run_all_logs(options, json),
    }
}

fn parse_logs_args(args: &[String]) -> Result<LogsOptions, String> {
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
                // Handled by run(), but retaining this arm makes the parser safe to call in
                // isolation and keeps its error surface unsurprising in tests.
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

fn expand_home(value: &str) -> Result<PathBuf, String> {
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

fn source_command(source: LogSource, options: LogsOptions) -> Result<Command, String> {
    let mut command = match source {
        LogSource::Game => {
            let mut command = Command::new("tail");
            command.arg("--lines").arg(options.lines.to_string());
            if options.follow {
                // -F follows the name, so restarting RimWorld or replacing Player.log does not
                // strand the diagnostic terminal on the old inode.
                command.arg("--follow=name");
            }
            command.arg("--").arg(game_log_path()?);
            command
        }
        LogSource::Daemon => {
            let mut command = Command::new("journalctl");
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

fn clean_log_line(mut line: String) -> String {
    if line.ends_with('\n') {
        line.pop();
    }
    if line.ends_with('\r') {
        line.pop();
    }
    line
}

fn write_log_line<W: Write>(
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

    // Breaking from the receiver loop drops the channel before joining. Kill the children
    // explicitly as well: a worker may already have sent its last line and be blocked waiting for
    // a follow process that has no more output yet.
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
        Err(errors.join("; "))
    }
}

/// The argument at `at`, or the sentence naming what was left out. A usage dump answers "what may
/// I type"; a caller who typed most of a command is asking the narrower question instead.
fn arg<'a>(args: &'a [String], at: usize, missing: &str) -> Result<&'a str, String> {
    args.get(at)
        .map(String::as_str)
        .ok_or_else(|| missing.to_string())
}

/// Everything from `from` on, joined - a task body or a note, which are written as prose and so
/// arrive as however many words the shell split them into.
fn rest(args: &[String], from: usize, missing: &str) -> Result<String, String> {
    if args.len() <= from {
        return Err(missing.to_string());
    }
    Ok(args[from..].join(" "))
}

/// Refuse what follows a command that takes nothing more, rather than silently ignoring it: a
/// stray word is usually a flag that was meant to do something.
fn only(args: &[String], at: usize) -> Result<(), String> {
    match args.get(at) {
        None => Ok(()),
        Some(extra) => Err(format!("unexpected argument: {extra}\n\n{USAGE}")),
    }
}

fn load_endpoint() -> Result<Endpoint, String> {
    if let Ok(url) = std::env::var("SLOPD_URL") {
        // An empty root token is the daemon's "no auth" contract, so `SLOPD_TOKEN=` is a real
        // answer and kept. Unset is not one - it is the variable that was forgotten - and sending
        // an empty token on its behalf only turns the mistake into a 401 raised somewhere else.
        let token = std::env::var("SLOPD_TOKEN").map_err(|_| {
            "SLOPD_URL is set but SLOPD_TOKEN is not; \
             set SLOPD_TOKEN= for a daemon with no token"
                .to_string()
        })?;
        return Ok(Endpoint { url, token });
    }
    let path = std::env::var("SLOPD_ENDPOINT")
        .map(PathBuf::from)
        .unwrap_or_else(|_| {
            dirs::config_dir()
                .unwrap_or_else(|| PathBuf::from("."))
                .join("slopworld/endpoint.toml")
        });
    let text =
        std::fs::read_to_string(&path).map_err(|e| format!("reading {}: {e}", path.display()))?;
    toml::from_str(&text).map_err(|e| format!("parsing {}: {e}", path.display()))
}

fn request(
    endpoint: &Endpoint,
    session: &str,
    method: &str,
    path: &str,
    body: Option<Value>,
) -> Result<Value, String> {
    let url = format!("{}{}", endpoint.url.trim_end_matches('/'), path);
    // A refusal carries the daemon's reason in its body, and that sentence is the whole value of
    // the reply - "a task still in flight cannot be removed" tells the user what to do next, where
    // a bare 400 does not. So a status is not an error here; it is read below, body first.
    let mut res = match (method, body) {
        ("GET", None) => ureq::get(&url)
            .config()
            .http_status_as_error(false)
            .build()
            .header("x-slop-token", &endpoint.token)
            .header("x-slop-session", session)
            .call(),
        ("DELETE", None) => ureq::delete(&url)
            .config()
            .http_status_as_error(false)
            .build()
            .header("x-slop-token", &endpoint.token)
            .header("x-slop-session", session)
            .call(),
        ("POST", Some(value)) => ureq::post(&url)
            .config()
            .http_status_as_error(false)
            .build()
            .header("x-slop-token", &endpoint.token)
            .header("x-slop-session", session)
            .send_json(value),
        _ => return Err(format!("unsupported request: {method}")),
    }
    .map_err(|e| format!("request: {e}"))?;
    let status = res.status();
    let mut text = String::new();
    res.body_mut()
        .as_reader()
        .read_to_string(&mut text)
        .map_err(|e| format!("response: {e}"))?;
    let value: Value = match serde_json::from_str(&text) {
        Ok(value) => value,
        Err(e) if status.is_success() => return Err(format!("response {status}: {e}")),
        // A refusal from the layer above the handlers - `auth`, which answers 401 bare - has no
        // JSON body to quote, so the status is the whole of what happened.
        Err(_) if text.trim().is_empty() => return Err(format!("refused: {status}")),
        Err(_) => return Err(format!("refused: {status}: {}", text.trim())),
    };
    if !status.is_success() {
        return Err(value
            .get("error")
            .and_then(Value::as_str)
            .unwrap_or(&text)
            .to_string());
    }
    Ok(value)
}

/// What `inbox` was asked to leave out. The store hands back everything the caller is party to,
/// in the order it was written; the shaping is here, where the person reading it is.
#[derive(Default)]
struct InboxFilter {
    all: bool,
    sent: bool,
    received: bool,
    status: Option<String>,
}

impl InboxFilter {
    fn parse(args: &[String]) -> Result<Self, String> {
        let mut f = Self::default();
        let mut rest = args.iter();
        while let Some(arg) = rest.next() {
            match arg.as_str() {
                "--all" => f.all = true,
                "--sent" => f.sent = true,
                "--received" => f.received = true,
                "--status" => {
                    let s = rest.next().ok_or("--status needs a value")?;
                    f.status = Some(s.to_string());
                }
                flag => return Err(format!("unknown flag: {flag}\n\n{USAGE}")),
            }
        }
        Ok(f)
    }

    fn keeps(&self, task: &Value, me: &str) -> bool {
        let status = task["status"].as_str().unwrap_or("");
        let from_me = task["from"].as_str() == Some(me);
        let to_me = task["to"].as_str() == Some(me);
        // Neither direction flag, or both, means both - there is no third direction to ask for.
        if self.sent != self.received && ((self.sent && !from_me) || (self.received && !to_me)) {
            return false;
        }
        match &self.status {
            Some(want) => status == want,
            // Finished work is still readable by id; it just stops crowding the list.
            None => self.all || !matches!(status, "done" | "failed"),
        }
    }

    /// Newest first: what arrived while you were away is what the list is opened to find.
    fn apply<'a>(&self, v: &'a Value, me: &str) -> Vec<&'a Value> {
        let mut tasks: Vec<&Value> = v
            .get("tasks")
            .and_then(Value::as_array)
            .map(|t| t.iter().filter(|t| self.keeps(t, me)).collect())
            .unwrap_or_default();
        tasks.sort_by_key(|t| std::cmp::Reverse(t["created_ms"].as_u64().unwrap_or(0)));
        tasks
    }
}

fn peer_names(v: &Value) -> Vec<&str> {
    let mut names: Vec<&str> = v
        .get("sessions")
        .and_then(Value::as_array)
        .map(|s| s.iter().filter_map(|s| s["name"].as_str()).collect())
        .unwrap_or_default();
    // The host is a mailbox without being a session, so it is named here or it is never found.
    names.push(HOST);
    names.sort_unstable();
    names.dedup();
    names
}

/// Identity, reachability and what is waiting - the three things to check before believing any
/// other answer this CLI gives. Unreachable is reported, not raised: that *is* the status.
fn status_value(endpoint: &Endpoint, session: &str) -> Value {
    let mut out = json!({ "session": session, "endpoint": endpoint.url });
    match request(endpoint, session, "GET", "/api/health", None) {
        Ok(v) => {
            out["daemon"] = json!("ok");
            out["version"] = v["version"].clone();
        }
        Err(e) => {
            out["daemon"] = json!("unreachable");
            out["error"] = json!(e);
            return out;
        }
    }
    match request(endpoint, session, "GET", "/api/tasks", None) {
        Ok(v) => {
            let tasks = v
                .get("tasks")
                .and_then(Value::as_array)
                .cloned()
                .unwrap_or_default();
            let open = |t: &Value| !matches!(t["status"].as_str().unwrap_or(""), "done" | "failed");
            out["waiting"] = json!(tasks
                .iter()
                .filter(|t| t["to"].as_str() == Some(session) && open(t))
                .count());
            out["sent"] = json!(tasks
                .iter()
                .filter(|t| t["from"].as_str() == Some(session) && open(t))
                .count());
        }
        Err(e) => out["error"] = json!(e),
    }
    out
}

fn print_status(v: &Value) {
    println!("session   {}", v["session"].as_str().unwrap_or("?"));
    println!("endpoint  {}", v["endpoint"].as_str().unwrap_or("?"));
    if v["daemon"] != json!("ok") {
        println!(
            "daemon    unreachable: {}",
            v["error"].as_str().unwrap_or("?")
        );
        return;
    }
    println!(
        "daemon    ok, slopd {}",
        v["version"].as_str().unwrap_or("?")
    );
    match (v["waiting"].as_u64(), v["sent"].as_u64()) {
        (Some(waiting), Some(sent)) => println!("pending   {waiting} for you, {sent} you sent"),
        _ => println!("pending   unknown: {}", v["error"].as_str().unwrap_or("?")),
    }
}

fn emit(v: &Value, json: bool) {
    if json {
        print_json(v);
    } else {
        print_value(v);
    }
}

fn print_json(v: &Value) {
    println!("{}", serde_json::to_string_pretty(v).unwrap_or_default());
}

fn print_value(v: &Value) {
    if let Some(tasks) = v.get("tasks").and_then(Value::as_array) {
        for t in tasks {
            print_task(t);
        }
    } else if let Some(task) = v.get("task") {
        print_task(task);
    } else {
        print_json(v);
    }
}

fn print_task(t: &Value) {
    let created = t["created_ms"].as_u64().unwrap_or(0);
    let updated = t["updated_ms"].as_u64().unwrap_or(created);
    let mut when = age(created);
    if updated > created {
        when.push_str(&format!(", moved {}", age(updated)));
    }
    println!(
        "{}  {} -> {}  [{}]  {}\n  {}",
        t["id"].as_str().unwrap_or("?"),
        t["from"].as_str().unwrap_or("?"),
        t["to"].as_str().unwrap_or("?"),
        t["status"].as_str().unwrap_or("?"),
        when,
        t["body"].as_str().unwrap_or("")
    );
    if let Some(note) = t["note"].as_str() {
        println!("  {note}");
    }
}

/// How long ago, in the coarsest unit that still says something. An inbox is read to find what
/// has been sitting, so hours and days are the answer; the exact second never is.
fn age(ms: u64) -> String {
    let now = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_millis() as u64;
    let secs = now.saturating_sub(ms) / 1000;
    match secs {
        0..=59 => format!("{secs}s ago"),
        60..=3599 => format!("{}m ago", secs / 60),
        3600..=86399 => format!("{}h ago", secs / 3600),
        _ => format!("{}d ago", secs / 86400),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn words(text: &str) -> Vec<String> {
        text.split_whitespace().map(str::to_string).collect()
    }

    #[test]
    fn logs_defaults_to_both_and_a_bounded_tail() {
        assert_eq!(
            parse_logs_args(&[]).unwrap(),
            LogsOptions {
                selection: LogSelection::All,
                lines: DEFAULT_LOG_LINES,
                follow: false,
            }
        );
    }

    #[test]
    fn logs_accepts_source_flags_and_follow() {
        assert_eq!(
            parse_logs_args(&words("--daemon -f --lines=37")).unwrap(),
            LogsOptions {
                selection: LogSelection::One(LogSource::Daemon),
                lines: 37,
                follow: true,
            }
        );
    }

    #[test]
    fn logs_accepts_positional_source() {
        assert_eq!(
            parse_logs_args(&words("game -n 9")).unwrap(),
            LogsOptions {
                selection: LogSelection::One(LogSource::Game),
                lines: 9,
                follow: false,
            }
        );
    }

    #[test]
    fn logs_rejects_duplicate_source_and_invalid_line_count() {
        assert!(parse_logs_args(&words("game --daemon")).is_err());
        assert!(parse_logs_args(&words("--lines 0")).is_err());
        assert!(parse_logs_args(&words("--lines 100001")).is_err());
    }
}
