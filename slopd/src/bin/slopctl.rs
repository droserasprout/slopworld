#[path = "slopctl/commands.rs"]
mod commands;
#[path = "slopctl/format.rs"]
mod format;
#[path = "slopctl/http.rs"]
mod http;
#[path = "slopctl/logs.rs"]
mod logs;
#[path = "../shared/mod.rs"]
mod shared;
#[cfg(test)]
#[path = "slopctl/tests.rs"]
mod tests;

use commands::{parse_command, Command, USAGE};
use http::load_endpoint;
use logs::{run_logs, LOGS_USAGE};
use std::process::ExitCode;
use std::time::Duration;

const HOST: &str = shared::protocol::HOST_IDENTITY;
const TASK_WAIT_INTERVAL: Duration = Duration::from_secs(1);

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
    let command = args.first().map(String::as_str);
    if matches!(command, None | Some("-h" | "--help" | "help")) {
        print!("{USAGE}");
        return Ok(());
    }
    // The global JSON flag changes output formatting without changing the request.
    // Remove it before parsing the command.
    let json = take_json_flag(&mut args);
    if args.is_empty() {
        print!("{USAGE}");
        return Ok(());
    }
    if matches!(
        args.first().map(String::as_str),
        Some("-h" | "--help" | "help")
    ) {
        print!("{USAGE}");
        return Ok(());
    }

    let command = parse_command(&args)?;
    if let Command::Help { usage } = &command {
        print!("{usage}");
        return Ok(());
    }
    // Read local log files so they remain available while slopd is down.
    // Handle this command before loading endpoint.toml because it does not need the endpoint token.
    if let Command::Logs { ref args } = command {
        if args
            .iter()
            .any(|a| matches!(a.as_str(), "-h" | "--help" | "help"))
        {
            print!("{LOGS_USAGE}");
            return Ok(());
        }
        return run_logs(args, json);
    }

    let endpoint = load_endpoint()?;
    // Use the host identity when SLOPWORLD_SESSION is absent or empty.
    // This lets users read the host inbox without specifying an identity.
    let session = std::env::var("SLOPWORLD_SESSION")
        .ok()
        .filter(|s| !s.trim().is_empty())
        .unwrap_or_else(|| HOST.to_string());

    command.run(&endpoint, &session, json)
}

fn take_json_flag(args: &mut Vec<String>) -> bool {
    let mut json = false;
    let mut options = true;
    args.retain(|arg| {
        if arg == "--" {
            options = false;
        }
        if options && arg == "--json" {
            json = true;
            false
        } else {
            true
        }
    });
    json
}
