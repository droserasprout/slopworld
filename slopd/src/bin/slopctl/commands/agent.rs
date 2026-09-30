use super::super::format::print_json;
use super::super::http::Endpoint;
use super::super::http::request;
use super::Command;
use super::common::encode_component;
use super::common::{arg, option_value, value_option};
use crate::shared::protocol::routes;
use serde_json::json;

pub(crate) const AGENT_USAGE: &str = "usage:
  slopctl agent create NAME --project PROJECT --template TEMPLATE [--start]

Create and manage agents from the daemon's template catalog.
";
pub(crate) const AGENT_CREATE_USAGE: &str = "usage:
  slopctl agent create NAME --project PROJECT --template TEMPLATE [--start]

Create an agent from a daemon catalog template. The daemon does not start the
agent unless you use --start. The daemon returns the new identity.
Use --option=VALUE for option values beginning with a dash.
";
pub(super) fn parse_agent_command(args: &[String]) -> Result<Command, String> {
    if matches!(
        args.get(1).map(String::as_str),
        Some("-h" | "--help" | "help")
    ) {
        return Ok(Command::Help { usage: AGENT_USAGE });
    }
    if args.get(1).map(String::as_str) != Some("create") {
        return Err(format!(
            "agent needs the create subcommand\n\n{AGENT_USAGE}"
        ));
    }
    if matches!(
        args.get(2).map(String::as_str),
        Some("-h" | "--help" | "help")
    ) {
        return Ok(Command::Help {
            usage: AGENT_CREATE_USAGE,
        });
    }
    let name = arg(args, 2, "agent create needs a name")?.to_string();
    let mut project = None;
    let mut template = None;
    let mut start = false;
    let mut i = 3;
    while i < args.len() {
        let Some(option) = args.get(i) else {
            break;
        };
        let (flag, inline) = value_option(option);
        match flag {
            "--project" => project = Some(option_value(args, &mut i, flag, inline)?),
            "--template" => template = Some(option_value(args, &mut i, flag, inline)?),
            "--start" if inline.is_none() => start = true,
            flag => {
                return Err(format!(
                    "unknown agent create option: {flag}\n\n{AGENT_CREATE_USAGE}"
                ));
            }
        }
        i += 1;
    }
    let project = project
        .ok_or_else(|| format!("agent create needs --project PROJECT\n\n{AGENT_CREATE_USAGE}"))?;
    let template = template
        .ok_or_else(|| format!("agent create needs --template TEMPLATE\n\n{AGENT_CREATE_USAGE}"))?;
    Ok(Command::AgentCreate {
        name,
        project,
        template,
        start,
    })
}

pub(crate) fn run_agent_create(
    endpoint: &Endpoint,
    session: &str,
    json_output: bool,
    name: &str,
    project: &str,
    template: &str,
    start: bool,
) -> Result<(), String> {
    let mut v = request(
        endpoint,
        session,
        "POST",
        &format!(
            "{}/{}/create",
            routes::TEMPLATES,
            encode_component(template)
        ),
        Some(json!({ "name": name, "project": project, "start": start })),
    )?;
    v.as_object_mut()
        .ok_or_else(|| "agent create response must be an object".to_string())?
        .insert("started".to_string(), json!(start));
    if json_output {
        print_json(&v);
    } else {
        println!(
            "agent    {}\nproject  {}\ntemplate {}\nstarted  {}",
            name,
            project,
            template,
            if start { "yes" } else { "no" }
        );
    }
    Ok(())
}
