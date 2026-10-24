//! CLI syntax and generated help; filesystem and container validation stay with their owners.
use anyhow::Result;
use clap::builder::StyledStr;
use clap::{CommandFactory, FromArgMatches, Parser, Subcommand};
use std::{ffi::OsString, path::PathBuf};

#[derive(Debug, Parser)]
#[command(
    name = "slopcar",
    styles = crate::cli_styles::styles(),
    about = "Manage the SlopWorld Linux sidecar",
    version,
    after_help = "Host paths follow SLOPD_CONFIG_ROOT, SLOPD_DATA, and SLOPD_CACHE under sidecar/.\nContainer settings use SLOPCAR_IMAGE, SLOPCAR_CONTAINER, SLOPCAR_PORT,\nSLOPCAR_MEMORY, SLOPCAR_CPUS, and SLOPCAR_PIDS_LIMIT."
)]
struct Cli {
    #[command(subcommand)]
    command: Option<CliCommand>,
}

#[derive(Debug, Subcommand)]
enum CliCommand {
    /// Print a shell completion script without Docker or container settings
    Completions {
        #[arg(value_enum)]
        shell: clap_complete::Shell,
    },
    /// Print an application path without Docker or container settings
    Paths {
        #[arg(value_parser = ["config", "data", "cache", "endpoint", "profile", "game-log"])]
        name: String,
    },
    /// Show embedded dependency notices
    Licenses,
    /// Build the image from a checkout (default: current directory)
    Build {
        #[arg(long, value_hint = clap::ValueHint::DirPath)]
        source: Option<PathBuf>,
    },
    /// Check nested Bubblewrap, pasta and tmux
    Doctor,
    /// Create and start, or start the existing container
    #[command(args_override_self = true)]
    Start(StartOptions),
    /// Stop without deleting persistent state
    Stop,
    /// Restart the existing container
    Restart,
    /// Show container and health state
    Status,
    /// Forward all following arguments to Docker logs; use `help logs` for this help
    #[command(disable_help_flag = true)]
    Logs,
    /// Open an interactive shell
    Shell,
    /// Remove only the container, preserving config and data
    Rm,
}

#[derive(Debug, clap::Args)]
struct StartOptions {
    /// Mount an absolute workspace at the same path (repeatable)
    #[arg(long = "workspace", help_heading = "Mounts", value_hint = clap::ValueHint::DirPath)]
    workspaces: Vec<PathBuf>,
    /// Mount a credential read-only (SOURCE=TARGET, repeatable)
    #[arg(long, help_heading = "Mounts")]
    credential_ro: Vec<OsString>,
    /// Mount a credential read-write (SOURCE=TARGET, repeatable)
    #[arg(long, help_heading = "Mounts")]
    credential_rw: Vec<OsString>,
    /// Host and daemon port (default: 7718)
    #[arg(long, value_parser = crate::settings::parse_port, help_heading = "Network")]
    port: Option<u16>,
    /// Memory ceiling (default: 8g)
    #[arg(long, help_heading = "Resources")]
    memory: Option<OsString>,
    /// CPU ceiling (default: 4)
    #[arg(long, help_heading = "Resources")]
    cpus: Option<OsString>,
    /// Process/thread ceiling (default: 4096)
    #[arg(long = "pids-limit", help_heading = "Resources")]
    pids: Option<OsString>,
}

#[derive(Default, Debug)]
pub(crate) struct Start {
    pub workspaces: Vec<PathBuf>,
    pub credentials: Vec<(bool, OsString)>,
    pub port: Option<u16>,
    pub memory: Option<OsString>,
    pub cpus: Option<OsString>,
    pub pids: Option<OsString>,
}
impl Start {
    pub fn has_options(&self) -> bool {
        !self.workspaces.is_empty()
            || !self.credentials.is_empty()
            || self.port.is_some()
            || self.memory.is_some()
            || self.cpus.is_some()
            || self.pids.is_some()
    }
}

#[derive(Debug)]
pub(crate) enum Command {
    Help(StyledStr),
    Completions(String),
    Paths(String),
    Notices,
    Build(PathBuf),
    Doctor,
    Start(Start),
    Stop,
    Restart,
    Status,
    Logs(Vec<OsString>),
    Shell,
    Remove,
}

/// Docker owns every word after `logs`, including help flags and separators.
/// Keep that opaque tail outside Clap so it cannot consume or rewrite any word.
pub(crate) fn parse(mut arguments: Vec<OsString>) -> Result<Command> {
    let log_options = if arguments.first().is_some_and(|word| word == "logs") {
        arguments.split_off(1)
    } else {
        Vec::new()
    };
    let matches = match Cli::command()
        .try_get_matches_from(std::iter::once(OsString::from("slopcar")).chain(arguments))
    {
        Ok(matches) => matches,
        Err(error)
            if matches!(
                error.kind(),
                clap::error::ErrorKind::DisplayHelp | clap::error::ErrorKind::DisplayVersion
            ) =>
        {
            return Ok(Command::Help(error.render()));
        }
        Err(error) => return Err(error.into()),
    };
    let cli = Cli::from_arg_matches(&matches)?;
    Ok(match cli.command {
        None => {
            let mut help = Cli::command().render_help();
            help.push_str("\n");
            Command::Help(help)
        }
        Some(CliCommand::Completions { shell }) => Command::Completions(
            crate::cli_completions::render(Cli::command(), shell).map_err(anyhow::Error::msg)?,
        ),
        Some(CliCommand::Paths { name }) => Command::Paths(name),
        Some(CliCommand::Licenses) => Command::Notices,
        Some(CliCommand::Build { source }) => Command::Build(match source {
            Some(path) => path,
            None => std::env::current_dir()?,
        }),
        Some(CliCommand::Doctor) => Command::Doctor,
        Some(CliCommand::Start(options)) => {
            // Clap stores repeatable RO/RW values separately. Merge by argv index
            // to retain the user's ordering across the two credential options.
            let start = matches
                .subcommand_matches("start")
                .ok_or_else(|| anyhow::anyhow!("missing start arguments"))?;
            let mut credentials = Vec::new();
            for (name, readonly, values) in [
                ("credential_ro", true, options.credential_ro),
                ("credential_rw", false, options.credential_rw),
            ] {
                if let Some(indices) = start.indices_of(name) {
                    credentials.extend(
                        indices
                            .zip(values)
                            .map(|(index, value)| (index, readonly, value)),
                    );
                }
            }
            credentials.sort_by_key(|(index, _, _)| *index);
            Command::Start(Start {
                workspaces: options.workspaces,
                credentials: credentials
                    .into_iter()
                    .map(|(_, readonly, value)| (readonly, value))
                    .collect(),
                port: options.port,
                memory: options.memory,
                cpus: options.cpus,
                pids: options.pids,
            })
        }
        Some(CliCommand::Stop) => Command::Stop,
        Some(CliCommand::Restart) => Command::Restart,
        Some(CliCommand::Status) => Command::Status,
        Some(CliCommand::Logs) => Command::Logs(log_options),
        Some(CliCommand::Shell) => Command::Shell,
        Some(CliCommand::Rm) => Command::Remove,
    })
}

#[cfg(test)]
#[path = "cli_tests.rs"]
mod tests;
