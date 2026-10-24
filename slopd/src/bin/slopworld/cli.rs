//! Owns launcher syntax, generated help, and shell completions.
//! Execution and filesystem validation belong to the launcher, endpoints, and mod installer.
use super::application_paths::{Mode, validate_endpoint_name};
use clap::builder::StyledStr;
use clap::{CommandFactory, Parser, Subcommand, ValueEnum};
use std::path::PathBuf;

#[derive(Debug, Parser)]
#[command(
    name = "slopworld",
    styles = crate::cli_styles::styles(),
    about = "Launch RimWorld into the SlopWorld profile",
    version = env!("SLOPWORLD_VERSION"),
    disable_help_subcommand = true
)]
struct Cli {
    #[command(flatten)]
    launch: LaunchArgs,
    #[command(subcommand)]
    command: Option<LocalCommand>,
}

#[derive(Debug, Default, PartialEq, clap::Args)]
pub(super) struct LaunchArgs {
    /// RimWorld install (default: SLOPWORLD_GAME, game.toml, usual locations)
    #[arg(long, help_heading = "Game", value_hint = clap::ValueHint::DirPath)]
    pub game: Option<String>,
    /// Explicit executable for native macOS app bundles
    #[arg(long, help_heading = "Game", value_hint = clap::ValueHint::FilePath)]
    pub game_exe: Option<String>,
    /// Working directory for the game process
    #[arg(long, help_heading = "Game", value_hint = clap::ValueHint::DirPath)]
    pub working_dir: Option<String>,
    /// Mod directory to validate for a non-Linux game layout
    #[arg(long, help_heading = "Game", value_hint = clap::ValueHint::DirPath)]
    pub mods: Option<String>,
    /// Save data folder (default: SLOPWORLD_PROFILE, selected connection profile)
    #[arg(long, help_heading = "Profile", value_hint = clap::ValueHint::DirPath, long_help = "Save data folder. Uses SLOPWORLD_PROFILE when set, otherwise the selected connection profile. Existing mod lists are preserved; use profile reset to replace them.")]
    pub profile: Option<String>,
    /// Saved connection (default: local)
    #[arg(long, global = true, value_parser = endpoint_name, help_heading = "Connection")]
    pub endpoint: Option<String>,
    /// Use sidecar paths and UI defaults
    #[arg(long, global = true, help_heading = "Connection")]
    pub sidecar: bool,
    /// Print game argv without launching or creating files
    #[arg(long, help_heading = "Game")]
    pub print: bool,
    /// Omit default X11/OpenGL window arguments
    #[arg(long, help_heading = "Game")]
    pub no_window_fix: bool,
}

#[derive(Debug, Subcommand)]
pub(super) enum LocalCommand {
    /// Print a shell completion script without loading game or profile state
    Completions {
        #[arg(value_enum)]
        shell: clap_complete::Shell,
    },
    /// Verify and save a remote peer; prompt for its key if needed
    Connect {
        #[arg(value_parser = endpoint_name)]
        name: String,
        url: Option<String>,
    },
    /// List saved endpoint names and URLs
    Endpoints,
    /// Print an application path without contacting the daemon
    Paths {
        #[arg(value_enum)]
        name: PathName,
    },
    /// Initialize or reset a profile without starting the game
    Profile {
        #[command(subcommand)]
        command: ProfileCommand,
    },
    /// Install or remove the SlopWorld mod
    Mod {
        #[command(subcommand)]
        command: ModCommand,
    },
}

#[derive(Debug, Subcommand)]
pub(super) enum ProfileCommand {
    /// Create missing profile files, preserving existing settings and mod choices
    Init(ProfileOptions),
    /// Replace the mod list, preserving saves and other settings
    Reset(ProfileOptions),
}

#[derive(Debug, Default, clap::Args)]
pub(super) struct ProfileOptions {
    /// Save data folder (default: SLOPWORLD_PROFILE, selected connection profile)
    #[arg(long, value_hint = clap::ValueHint::DirPath)]
    pub profile: Option<String>,
}

#[derive(Debug, Clone, ValueEnum)]
pub(super) enum PathName {
    Config,
    Data,
    Cache,
    Endpoint,
    Profile,
    GameLog,
}
impl PathName {
    pub(super) fn as_str(&self) -> &'static str {
        match self {
            Self::Config => "config",
            Self::Data => "data",
            Self::Cache => "cache",
            Self::Endpoint => "endpoint",
            Self::Profile => "profile",
            Self::GameLog => "game-log",
        }
    }
}

#[derive(Debug, Subcommand)]
pub(super) enum ModCommand {
    /// Atomically replace the mod after copying to a temporary sibling directory
    #[command(
        after_help = "Default source: /usr/share/slopworld/SlopWorld. Destination defaults to RIMWORLD, then MAC_RIMWORLD or ~/Documents/RimWorld.app on macOS; launcher defaults on Linux."
    )]
    Install {
        #[arg(long, value_hint = clap::ValueHint::DirPath)]
        source: Option<PathBuf>,
        #[command(flatten)]
        destination: ModOptions,
    },
    /// Remove only the SlopWorld directory inside Mods
    Uninstall(ModOptions),
}

#[derive(Debug, Default, PartialEq, Eq, clap::Args)]
pub(super) struct ModOptions {
    #[arg(skip)]
    pub source: Option<PathBuf>,
    /// Directory containing Mods
    #[arg(long, conflicts_with = "mods", value_hint = clap::ValueHint::DirPath)]
    pub game: Option<PathBuf>,
    /// Exact Mods directory
    #[arg(long, value_hint = clap::ValueHint::DirPath)]
    pub mods: Option<PathBuf>,
}

#[derive(Debug)]
pub(super) enum Command {
    Display(StyledStr),
    Launch(LaunchArgs),
    Local {
        command: LocalCommand,
        mode: Mode,
        endpoint: Option<String>,
    },
}

fn endpoint_name(value: &str) -> Result<String, String> {
    validate_endpoint_name(value)?;
    Ok(value.to_owned())
}

pub(super) fn parse(args: Vec<String>) -> Result<Command, StyledStr> {
    let cli = match Cli::try_parse_from(std::iter::once("slopworld".into()).chain(args)) {
        Ok(cli) => cli,
        Err(error)
            if matches!(
                error.kind(),
                clap::error::ErrorKind::DisplayHelp
                    | clap::error::ErrorKind::DisplayVersion
                    | clap::error::ErrorKind::DisplayHelpOnMissingArgumentOrSubcommand
            ) =>
        {
            return Ok(Command::Display(error.render()));
        }
        Err(error) => return Err(error.render()),
    };
    let Some(command) = cli.command else {
        return Ok(Command::Launch(cli.launch));
    };
    let launch = &cli.launch;
    if launch.game.is_some()
        || launch.game_exe.is_some()
        || launch.working_dir.is_some()
        || launch.mods.is_some()
        || launch.profile.is_some()
        || launch.print
        || launch.no_window_fix
    {
        return Err("launch options cannot be combined with a local command".into());
    }
    if cli.launch.endpoint.is_some()
        && matches!(
            command,
            LocalCommand::Connect { .. } | LocalCommand::Endpoints
        )
    {
        return Err(
            "--endpoint applies to launches and path queries, not peer registration or listing"
                .into(),
        );
    }
    let mode = if cli.launch.sidecar {
        Mode::Sidecar
    } else {
        Mode::Native
    };
    Ok(Command::Local {
        command,
        mode,
        endpoint: cli.launch.endpoint,
    })
}

#[cfg(test)]
#[path = "cli_tests.rs"]
mod tests;

pub(super) fn completion_script(shell: clap_complete::Shell) -> Result<String, String> {
    crate::cli_completions::render(Cli::command(), shell)
}
