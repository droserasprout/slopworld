use super::super::format::emit;
use super::super::http::{request, Endpoint};
use super::common::{option_value, value_option};
use super::task::print_wait_hint;
use super::Command;
use crate::shared::protocol::routes;
use serde_json::json;

pub(crate) const SPAWN_USAGE: &str = "usage:
  slopctl worker spawn [--one-shot] [--worktree ID | --new-worktree] [--base REV] --project PROJECT --template TEMPLATE [--] TASK...

Create a task-owned worker from an agent template. Scoped callers may use only
templates that worker policy allows. The worker receives the task body and its exact task ID.
By default, the daemon keeps worker sessions after they exit.
Use --one-shot to remove a session on exit.
The worktree stays until you remove it.
Use --worktree-name to name a new worktree.
Use --option=VALUE for dash-leading option values.
Options end before TASK. Put -- before task text that starts with an option, such as --durable.

The project and template are required. The old parent/clone syntax is unsupported.
";
pub(crate) const WORKER_USAGE: &str = "usage:
  slopctl worker spawn [--one-shot] [--worktree ID | --new-worktree] [--base REV] --project PROJECT --template TEMPLATE [--] TASK...

Create a task-owned worker from an agent template.
";
#[derive(Debug, Default, PartialEq, Eq)]
pub(crate) struct WorktreeChoice {
    pub worktree: String,
    pub new_worktree: bool,
    pub base: String,
    pub worktree_name: String,
}
pub(super) fn parse_worker_command(args: &[String]) -> Result<Command, String> {
    if matches!(
        args.get(1).map(String::as_str),
        Some("-h" | "--help" | "help")
    ) {
        return Ok(Command::Help {
            usage: WORKER_USAGE,
        });
    }
    if args.get(1).map(String::as_str) == Some("spawn") {
        if matches!(
            args.get(2).map(String::as_str),
            Some("-h" | "--help" | "help")
        ) {
            return Ok(Command::Help { usage: SPAWN_USAGE });
        }
        return parse_spawn(args, 2);
    }
    Err(format!(
        "worker needs the spawn subcommand\n\n{WORKER_USAGE}"
    ))
}

fn parse_spawn(args: &[String], options_at: usize) -> Result<Command, String> {
    let mut durable = true;
    let mut worktree = WorktreeChoice::default();
    let mut project = None;
    let mut template = None;
    let mut i = options_at;
    while i < args.len() {
        let Some(option) = args.get(i) else {
            break;
        };
        let (flag, inline) = value_option(option);
        match flag {
            "--" if inline.is_none() => {
                i += 1;
                break;
            }
            "--durable" if inline.is_none() => durable = true,
            "--one-shot" if inline.is_none() => durable = false,
            "--new-worktree" if inline.is_none() => worktree.new_worktree = true,
            "--worktree" | "--base" | "--worktree-name" | "--project" | "--template" => {
                let value = option_value(args, &mut i, flag, inline)?;
                match flag {
                    "--worktree" => worktree.worktree = value,
                    "--base" => worktree.base = value,
                    "--worktree-name" => worktree.worktree_name = value,
                    "--project" => project = Some(value),
                    _ => template = Some(value),
                }
            }
            // The first argument that is not an option starts the task body.
            // Treat all subsequent arguments as task text, including words that resemble flags.
            _ => break,
        }
        i += 1;
    }
    let Some(project) = project else {
        return Err(format!(
            "spawn requires --project PROJECT and --template TEMPLATE.\n\n{SPAWN_USAGE}"
        ));
    };
    let Some(template) = template else {
        return Err(format!(
            "spawn requires --project PROJECT and --template TEMPLATE.\n\n{SPAWN_USAGE}"
        ));
    };
    if args.len() <= i {
        return Err(format!("spawn needs a task body\n\n{SPAWN_USAGE}"));
    }
    if worktree.new_worktree && !worktree.worktree.is_empty() {
        return Err("choose --worktree or --new-worktree".into());
    }
    if !worktree.new_worktree && (!worktree.base.is_empty() || !worktree.worktree_name.is_empty()) {
        return Err("--base and --worktree-name require --new-worktree".into());
    }
    Ok(Command::Spawn {
        project,
        template,
        durable,
        worktree,
        body: args.get(i..).unwrap_or_default().join(" "),
    })
}

pub(crate) struct SpawnArgs<'a> {
    pub(crate) project: &'a str,
    pub(crate) template: &'a str,
    pub(crate) durable: bool,
    pub(crate) worktree: &'a WorktreeChoice,
    pub(crate) body: &'a str,
}

pub(crate) fn run_spawn(
    endpoint: &Endpoint,
    session: &str,
    json: bool,
    args: SpawnArgs<'_>,
) -> Result<(), String> {
    let v = request(
        endpoint,
        session,
        "POST",
        routes::WORKERS,
        Some(json!({
            "project": args.project,
            "template": args.template,
            "body": args.body,
            "durable": args.durable,
            "worktree": args.worktree.worktree,
            "new_worktree": args.worktree.new_worktree,
            "base": args.worktree.base,
            "worktree_name": args.worktree.worktree_name,
        })),
    )?;
    emit(&v, json);
    print_wait_hint(&v, json);
    Ok(())
}
