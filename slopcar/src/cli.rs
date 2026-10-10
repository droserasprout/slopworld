//! CLI syntax only; filesystem and container validation belong to their owners.
use anyhow::{Context, Result, bail};
use std::{ffi::OsString, path::PathBuf};

pub(crate) const HELP: &str = "usage: slopcar COMMAND [OPTIONS]

commands:
  licenses              show embedded dependency notices
  build [--source PATH]  build the image from a checkout (default: current directory)
  doctor                check nested Bubblewrap, pasta and tmux
  start [OPTIONS]       create and start, or start the existing container
  stop                  stop without deleting persistent state
  restart               restart the existing container
  status                show container and health state
  logs [DOCKER OPTIONS] show daemon logs (e.g. -f or --tail 100)
  shell                 open an interactive shell
  rm                    remove only the container, preserving config and data

start options:
  --workspace PATH      mount an absolute workspace at the same path (repeatable)
  --credential-ro S=T   mount a credential read-only
  --credential-rw S=T   mount a credential read-write
  --port NUMBER         host and daemon port (default: 7718)
  --memory SIZE         memory ceiling (default: 8g)
  --cpus NUMBER         CPU ceiling (default: 4)
  --pids-limit NUMBER   process/thread ceiling (default: 4096)

Settings use SLOPCAR_IMAGE, SLOPCAR_CONTAINER, SLOPCAR_CONFIG_DIR,
SLOPCAR_DATA_DIR, SLOPCAR_PORT, SLOPCAR_MEMORY, SLOPCAR_CPUS, SLOPCAR_PIDS_LIMIT.
Config and data default to the XDG slopworld-car directories.
";

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
    Help,
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

pub(crate) fn parse(arguments: Vec<OsString>) -> Result<Command> {
    let mut args = arguments.into_iter();
    let name = args.next().unwrap_or_else(|| "help".into());
    let command = match name.to_str().context("command must be UTF-8")? {
        "help" | "-h" | "--help" => Command::Help,
        "licenses" => Command::Notices,
        "build" => {
            let source = match args.next() {
                None => std::env::current_dir()?,
                Some(flag) if flag == "--source" => {
                    PathBuf::from(args.next().context("--source needs a path")?)
                }
                Some(flag) => bail!("unknown build option: {}", flag.to_string_lossy()),
            };
            Command::Build(source)
        }
        "doctor" => Command::Doctor,
        "start" => {
            let mut start = Start::default();
            while let Some(flag) = args.next() {
                let flag = flag.to_str().context("option must be UTF-8")?;
                if !matches!(
                    flag,
                    "--workspace"
                        | "--credential-ro"
                        | "--credential-rw"
                        | "--port"
                        | "--memory"
                        | "--cpus"
                        | "--pids-limit"
                ) {
                    bail!("unknown start option: {flag}");
                }
                let value = args
                    .next()
                    .with_context(|| format!("{flag} needs a value"))?;
                match flag {
                    "--workspace" => start.workspaces.push(value.into()),
                    "--credential-ro" | "--credential-rw" => {
                        start.credentials.push((flag == "--credential-ro", value))
                    }
                    "--port" => {
                        start.port = Some(crate::settings::parse_port(
                            value.to_str().context("port must be UTF-8")?,
                        )?)
                    }
                    "--memory" => start.memory = Some(value),
                    "--cpus" => start.cpus = Some(value),
                    "--pids-limit" => start.pids = Some(value),
                    _ => unreachable!(),
                }
            }
            Command::Start(start)
        }
        "stop" => Command::Stop,
        "restart" => Command::Restart,
        "status" => Command::Status,
        "logs" => return Ok(Command::Logs(args.collect())),
        "shell" => Command::Shell,
        "rm" => Command::Remove,
        name => bail!("unknown command: {name} (try: slopcar help)"),
    };
    if args.next().is_some() {
        bail!("unexpected arguments");
    }
    Ok(command)
}

#[cfg(test)]
#[path = "cli_tests.rs"]
mod tests;
