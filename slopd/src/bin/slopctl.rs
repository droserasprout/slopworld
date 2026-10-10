#[path = "slopctl/commands/mod.rs"]
mod commands;
#[path = "slopctl/format.rs"]
mod format;
#[path = "slopctl/http.rs"]
mod http;
#[path = "slopctl/logs/mod.rs"]
mod logs;
#[path = "../shared/mod.rs"]
mod shared;
#[cfg(test)]
#[path = "../test_http.rs"]
mod test_http;
#[cfg(test)]
#[path = "slopctl/tests.rs"]
mod tests;

use commands::{Command, USAGE, parse_command};
use http::load_endpoint;
use logs::{LOGS_USAGE, run_logs};
use shared::application_paths::{self, Mode};
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
    let mode = if application_paths::take_flag(&mut args, "--sidecar") {
        Mode::Sidecar
    } else {
        Mode::Native
    };
    let json = take_json_flag(&mut args);
    if let Some(path) = application_paths::path_query(&args, mode)? {
        if json {
            println!(
                "{}",
                serde_json::to_string(&path).map_err(|error| error.to_string())?
            );
        } else {
            println!("{}", path.display());
        }
        return Ok(());
    }
    let command = args.first().map(String::as_str);
    if matches!(command, None | Some("-h" | "--help" | "help")) {
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
        return run_logs(args, json, mode);
    }

    let endpoint = load_endpoint(mode)?;
    // Use the host identity when SLOPWORLD_SESSION is absent or empty.
    // This lets users read the host inbox without specifying an identity.
    let session = std::env::var("SLOPWORLD_SESSION")
        .ok()
        .filter(|s| !s.trim().is_empty())
        .unwrap_or_else(|| HOST.to_string());

    command.run(&endpoint, &session, json)
}

fn take_json_flag(args: &mut Vec<String>) -> bool {
    application_paths::take_flag(args, "--json")
}
