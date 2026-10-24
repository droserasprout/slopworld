//! Parse CLI requests, handle local commands, then load credentials for remote execution.

#[path = "../shared/cli_completions.rs"]
mod cli_completions;

#[path = "../shared/cli_styles.rs"]
mod cli_styles;

#[path = "slopctl/cli.rs"]
mod cli;
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

use clap::builder::StyledStr;
use cli::Command;
use logs::run_logs;
use std::process::ExitCode;
use std::time::Duration;

const HOST: &str = shared::protocol::HOST_IDENTITY;
const TASK_WAIT_INTERVAL: Duration = Duration::from_secs(1);

fn main() -> ExitCode {
    match run() {
        Ok(()) => ExitCode::SUCCESS,
        Err(e) => {
            anstream::eprintln!("slopctl: {}", e.ansi());
            ExitCode::FAILURE
        }
    }
}

fn run() -> Result<(), StyledStr> {
    let cli::Parsed {
        command,
        mode,
        endpoint: named_endpoint,
        json,
    } = cli::parse(
        std::env::args().skip(1).collect(),
        commands::task_id_from_env().as_deref(),
    )?;
    let command = match command {
        Command::Help { usage } => {
            anstream::print!("{}", usage.ansi());
            return Ok(());
        }
        Command::Completions { script } => {
            print!("{script}");
            return Ok(());
        }
        Command::Paths { name } => {
            let path = mode.query(&name, named_endpoint.as_deref())?;
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
        // Local logs remain available without daemon credentials or connectivity.
        Command::Logs { options } => return run_logs(options, json).map_err(Into::into),
        Command::Remote(command) => command,
    };

    let endpoint = http::load_endpoint(mode, named_endpoint.as_deref())?;
    // Use the host identity when SLOPWORLD_SESSION is absent or empty.
    // This lets users read the host inbox without specifying an identity.
    let session = std::env::var("SLOPWORLD_SESSION")
        .ok()
        .filter(|s| !s.trim().is_empty())
        .unwrap_or_else(|| HOST.to_string());

    command.run(&endpoint, &session, json).map_err(Into::into)
}
