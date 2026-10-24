//! Owns the command tree, generated help, and task identity resolution.
//! Command and log modules own reusable argument schemas and execution.

use super::commands::{
    ConfigArgs, InboxFilter, RemoteCommand, SpawnArgs, UpdateAction, WorktreeArgs,
};
use super::logs::{LogsArgs, LogsOptions};
use super::shared::application_paths::{self, Mode};
use clap::builder::StyledStr;
use clap::{Args, CommandFactory, Parser, Subcommand, ValueEnum};

#[derive(Debug, PartialEq, Eq)]
pub(crate) enum Command {
    Help { usage: StyledStr },
    Completions { script: String },
    Paths { name: String },
    Logs { options: LogsOptions },
    Remote(RemoteCommand),
}

#[derive(Debug, Parser)]
#[command(
    name = "slopctl",
    styles = crate::cli_styles::styles(),
    about = "Delegate work and inspect SlopWorld diagnostics",
    version = env!("SLOPWORLD_VERSION"),
    after_long_help = "Connection precedence: --endpoint, SLOPD_URL/SLOPD_TOKEN, SLOPD_ENDPOINT, local.\nManage peers with slopworld connect NAME [URL] and slopworld endpoints.\nSLOPWORLD_SESSION identifies the caller (default: host; requires the root token).\nSLOPWORLD_TASK_ID supplies the current task ID.\n\nKeep the task ID returned by task delegate or worker spawn, then run task wait ID.\nWait has no short timeout; do not poll task, inbox, or status while it waits."
)]
struct Cli {
    /// Print JSON instead of human-readable output
    #[arg(long, global = true, help_heading = "Output", action = clap::ArgAction::SetTrue)]
    json: bool,
    /// Select the sidecar endpoint and log sources
    #[arg(long, global = true, help_heading = "Connection")]
    sidecar: bool,
    /// Select endpoints/NAME.toml and its automatic game profile
    #[arg(long, global = true, value_parser = endpoint_name, help_heading = "Connection")]
    endpoint: Option<String>,
    #[command(subcommand)]
    command: CliCommand,
}

#[derive(Debug, Subcommand)]
enum CliCommand {
    /// Print a shell completion script without contacting the daemon
    Completions {
        #[arg(value_enum)]
        shell: clap_complete::Shell,
    },
    /// Create task-owned workers from agent templates
    #[command(subcommand)]
    Worker(WorkerCommand),
    /// Create agents from the daemon's template catalog
    #[command(subcommand)]
    Agent(AgentCommand),
    /// Discover and inspect agent templates
    #[command(subcommand)]
    Template(TemplateCommand),
    /// Manage project worktrees; rename and remove require the root token
    #[command(subcommand)]
    Worktree(WorktreeArgs),
    /// Manage delegated tasks; omit IDs when SLOPWORLD_TASK_ID is set
    #[command(subcommand)]
    Task(TaskCommand),
    /// List sessions visible to the caller, including host
    Peers,
    /// Show caller, endpoint, daemon reachability, and pending task counts
    Status,
    /// Inspect sanitized sandbox launch plans and live process trees
    #[command(subcommand)]
    Sandbox(SandboxCommand),
    /// Read or persist daemon settings (requires the root token)
    #[command(
        subcommand,
        after_help = "KEY uses dotted paths; escape dots in map keys as ~1 and tildes as ~0.\nString settings take literal text; other types take JSON.\nOnly individual values under daemon, defaults and commands can be set.\nToken reads are redacted. Changing daemon.token invalidates the old token; reconnect saved peers.\nChanging daemon.bind takes effect after restarting the daemon."
    )]
    Config(ConfigArgs),
    /// Read local game and daemon logs, even while the daemon is down
    Logs(LogsArgs),
    /// Print an absolute application path without contacting the daemon
    Paths {
        #[arg(value_enum)]
        name: PathName,
    },
}

#[derive(Debug, Clone, ValueEnum)]
enum PathName {
    Config,
    Data,
    Cache,
    Endpoint,
    Profile,
    GameLog,
}

impl PathName {
    fn name(&self) -> &'static str {
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
enum WorkerCommand {
    /// Spawn a worker and deliver TASK; its worktree remains until removed
    #[command(
        after_help = "Options end before TASK. Use -- before task text beginning with an option.\nWorkers are durable by default; --one-shot removes the session on exit.\nCreate checkouts with worktree create, then select one with --worktree.\nScoped callers may use only templates allowed by worker policy."
    )]
    Spawn(SpawnOptions),
}

#[derive(Debug, Args)]
struct SpawnOptions {
    #[command(flatten)]
    template: RequiredTemplate,
    /// Remove the worker session on exit
    #[arg(long)]
    one_shot: bool,
    /// Attach an existing worktree
    #[arg(long, value_parser = clap::builder::NonEmptyStringValueParser::new())]
    worktree: Option<String>,
    /// Task text, including all following words and flags
    #[arg(required = true, num_args = 1.., trailing_var_arg = true)]
    task: Vec<String>,
}

#[derive(Debug, Args)]
struct RequiredTemplate {
    /// Project that owns the agent
    #[arg(long, value_parser = clap::builder::NonEmptyStringValueParser::new())]
    project: String,
    /// Agent template name
    #[arg(long, value_parser = clap::builder::NonEmptyStringValueParser::new())]
    template: String,
}

#[derive(Debug, Subcommand)]
enum AgentCommand {
    /// Create an agent, stopped unless --start is supplied
    Create {
        name: String,
        #[command(flatten)]
        template: RequiredTemplate,
        #[arg(long)]
        start: bool,
    },
}

#[derive(Debug, Subcommand)]
enum TemplateCommand {
    /// List templates enabled for the caller; root sees the full catalog
    List {
        #[arg(long)]
        project: Option<String>,
    },
    /// Show one accessible template
    Show {
        name: String,
        #[arg(long)]
        project: Option<String>,
    },
}

#[derive(Debug, Subcommand)]
enum SandboxCommand {
    /// Show the saved sanitized plan and live process tree, when available
    Inspect { name: String },
}

#[derive(Debug, Subcommand)]
enum TaskCommand {
    /// Send TASK to AGENT
    Delegate {
        agent: String,
        #[arg(required = true, num_args = 1.., trailing_var_arg = true)]
        task: Vec<String>,
    },
    /// List newest tasks first; terminal tasks are hidden unless --all or --status is used
    List(InboxFilter),
    /// Show one task by its exact ID
    Show { id: Option<String> },
    /// Wait for a terminal result; report changes and a 30-second heartbeat to stderr
    #[command(
        after_help = "The final result goes to stdout. Do not use a status loop or a short timeout."
    )]
    Wait { id: Option<String> },
    /// Accept a queued task, with an optional note
    Accept(TaskUpdate),
    /// Set an accepted task to working, with an optional note
    Progress(TaskUpdate),
    /// Mark a task done, with an optional result
    Finish(TaskUpdate),
    /// Mark a task failed, with an optional reason
    Fail(TaskUpdate),
    /// Remove one terminal task
    Remove { id: Option<String> },
    /// Remove terminal tasks sent or received by the caller
    Prune {
        /// Also remove active tasks (requires the root token)
        #[arg(long)]
        include_active: bool,
    },
}

#[derive(Debug, Args)]
struct TaskUpdate {
    /// Task ID (default: SLOPWORLD_TASK_ID)
    #[arg(long, value_parser = clap::builder::NonEmptyStringValueParser::new())]
    id: Option<String>,
    /// Note text, including all following words and flags
    #[arg(num_args = 0.., trailing_var_arg = true)]
    note: Vec<String>,
}

impl TaskUpdate {
    fn resolve(
        self,
        action: UpdateAction,
        injected: Option<&str>,
    ) -> Result<RemoteCommand, String> {
        let id = self
            .id
            .or_else(|| injected.map(str::to_owned))
            .ok_or("task update needs --id ID or SLOPWORLD_TASK_ID")?;
        let note = (!self.note.is_empty()).then(|| self.note.join(" "));
        Ok(RemoteCommand::Update { action, id, note })
    }
}

pub(crate) struct Parsed {
    pub(crate) command: Command,
    pub(crate) mode: Mode,
    pub(crate) endpoint: Option<String>,
    pub(crate) json: bool,
}

fn endpoint_name(value: &str) -> Result<String, String> {
    application_paths::validate_endpoint_name(value)?;
    Ok(value.to_owned())
}

pub(crate) fn parse(args: Vec<String>, task_id: Option<&str>) -> Result<Parsed, StyledStr> {
    let no_args = args.is_empty();
    let cli = match Cli::try_parse_from(std::iter::once("slopctl".into()).chain(args)) {
        Ok(cli) => cli,
        Err(error)
            if !error.use_stderr()
                || (no_args
                    && error.kind()
                        == clap::error::ErrorKind::DisplayHelpOnMissingArgumentOrSubcommand) =>
        {
            return Ok(Parsed {
                command: Command::Help {
                    usage: error.render(),
                },
                mode: Mode::Native,
                endpoint: None,
                json: false,
            });
        }
        Err(error) => return Err(error.render()),
    };
    let mode = if cli.sidecar {
        Mode::Sidecar
    } else {
        Mode::Native
    };
    Ok(Parsed {
        command: cli.command.resolve(task_id, mode)?,
        mode,
        endpoint: cli.endpoint,
        json: cli.json,
    })
}

impl CliCommand {
    fn resolve(self, task_id: Option<&str>, mode: Mode) -> Result<Command, String> {
        let remote = match self {
            Self::Completions { shell } => {
                return Ok(Command::Completions {
                    script: crate::cli_completions::render(Cli::command(), shell)?,
                });
            }
            Self::Worker(WorkerCommand::Spawn(args)) => RemoteCommand::Spawn(SpawnArgs {
                project: args.template.project,
                template: args.template.template,
                durable: !args.one_shot,
                body: args.task.join(" "),
                worktree: args.worktree.unwrap_or_default(),
            }),
            Self::Agent(AgentCommand::Create {
                name,
                template,
                start,
            }) => RemoteCommand::AgentCreate {
                name,
                project: template.project,
                template: template.template,
                start,
            },
            Self::Template(TemplateCommand::List { project }) => {
                RemoteCommand::Templates { project }
            }
            Self::Template(TemplateCommand::Show { name, project }) => {
                RemoteCommand::TemplateShow { name, project }
            }
            Self::Sandbox(SandboxCommand::Inspect { name }) => {
                RemoteCommand::SandboxInspect { name }
            }
            Self::Worktree(args) => RemoteCommand::Worktree(args),
            Self::Config(args) => RemoteCommand::Config(args),
            Self::Peers => RemoteCommand::Peers,
            Self::Status => RemoteCommand::Status,
            Self::Paths { name } => {
                return Ok(Command::Paths {
                    name: name.name().into(),
                });
            }
            Self::Logs(args) => {
                return Ok(Command::Logs {
                    options: LogsOptions::from_args(args, mode),
                });
            }
            Self::Task(task) => task.resolve(task_id)?,
        };
        Ok(Command::Remote(remote))
    }
}

impl TaskCommand {
    fn resolve(self, injected: Option<&str>) -> Result<RemoteCommand, String> {
        let id = |value: Option<String>, name: &str| {
            value
                .or_else(|| injected.map(str::to_owned))
                .ok_or_else(|| format!("task {name} needs a task id"))
        };
        Ok(match self {
            Self::Delegate { agent, task } => RemoteCommand::Delegate {
                to: agent,
                body: task.join(" "),
            },
            Self::List(filter) => RemoteCommand::Inbox { filter },
            Self::Show { id: value } => RemoteCommand::Task {
                id: id(value, "show")?,
            },
            Self::Wait { id: value } => RemoteCommand::Wait {
                id: id(value, "wait")?,
            },
            Self::Remove { id: value } => RemoteCommand::Remove {
                id: id(value, "remove")?,
            },
            Self::Prune { include_active } => RemoteCommand::Prune {
                all: include_active,
            },
            Self::Accept(args) => return args.resolve(UpdateAction::Accept, injected),
            Self::Progress(args) => return args.resolve(UpdateAction::Progress, injected),
            Self::Finish(args) => return args.resolve(UpdateAction::Finish, injected),
            Self::Fail(args) => return args.resolve(UpdateAction::Fail, injected),
        })
    }
}

#[cfg(test)]
pub(crate) fn parse_command(args: &[String]) -> Result<Command, String> {
    parse_command_with_task_id(args, None)
}

#[cfg(test)]
pub(crate) fn parse_command_with_task_id(
    args: &[String],
    task_id: Option<&str>,
) -> Result<Command, String> {
    parse(args.to_vec(), task_id)
        .map(|parsed| parsed.command)
        .map_err(|error| error.to_string())
}

#[cfg(test)]
#[path = "cli_tests.rs"]
mod tests;
