use super::super::format::emit;
use super::super::http::{request, Endpoint};
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
        match option.as_str() {
            "--" => {
                i += 1;
                break;
            }
            "--durable" => durable = true,
            "--one-shot" => durable = false,
            "--new-worktree" => worktree.new_worktree = true,
            "--worktree" | "--base" | "--worktree-name" => {
                let flag = option.clone();
                i += 1;
                let value = args
                    .get(i)
                    .ok_or_else(|| format!("{flag} needs a value"))?
                    .clone();
                match flag.as_str() {
                    "--worktree" => worktree.worktree = value,
                    "--base" => worktree.base = value,
                    _ => worktree.worktree_name = value,
                }
            }
            "--project" => {
                i += 1;
                project = Some(
                    args.get(i)
                        .ok_or_else(|| format!("spawn --project needs a value\n\n{SPAWN_USAGE}"))?
                        .clone(),
                );
            }
            "--template" => {
                i += 1;
                template = Some(
                    args.get(i)
                        .ok_or_else(|| format!("spawn --template needs a value\n\n{SPAWN_USAGE}"))?
                        .clone(),
                );
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
