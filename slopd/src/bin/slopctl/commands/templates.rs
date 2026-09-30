use super::super::format::print_json;
use super::super::http::{request, Endpoint};
use super::super::HOST;
use super::common::{arg, encode_component, only};
use super::Command;
use crate::shared::protocol::routes;
use serde_json::{json, Value};

pub(crate) const TEMPLATES_USAGE: &str = "usage:
  slopctl template list [--project PROJECT]

List agent templates. Root callers see the complete catalog. Agents see only
templates that worker policy enables for their project. For a root caller, --project
selects the worker project.
";
pub(crate) const TEMPLATE_USAGE: &str = "usage:
  slopctl template list [--project PROJECT]
  slopctl template show NAME [--project PROJECT]

discover and inspect agent templates. Use `template list` or `template show`.
";
pub(crate) const TEMPLATE_SHOW_USAGE: &str = "usage:
  slopctl template show NAME [--project PROJECT]

Show one agent template. Agent callers may inspect only templates enabled for
worker spawning. For a root caller, --project selects the worker project.
";
pub(super) fn parse_template_command(args: &[String]) -> Result<Command, String> {
    if matches!(
        args.get(1).map(String::as_str),
        Some("-h" | "--help" | "help")
    ) {
        return Ok(Command::Help {
            usage: TEMPLATE_USAGE,
        });
    }
    if args.get(1).map(String::as_str) != Some("show") {
        if args.get(1).map(String::as_str) == Some("list") {
            if matches!(
                args.get(2).map(String::as_str),
                Some("-h" | "--help" | "help")
            ) {
                return Ok(Command::Help {
                    usage: TEMPLATES_USAGE,
                });
            }
            let project = optional_project(args, 2, TEMPLATES_USAGE)?;
            return Ok(Command::Templates { project });
        }
        return Err(format!(
            "template needs the list or show subcommand\n\n{TEMPLATE_USAGE}"
        ));
    }
    if matches!(
        args.get(2).map(String::as_str),
        Some("-h" | "--help" | "help")
    ) {
        return Ok(Command::Help {
            usage: TEMPLATE_SHOW_USAGE,
        });
    }
    let name = arg(args, 2, "template show needs a template name")?.to_string();
    let project = optional_project(args, 3, TEMPLATE_SHOW_USAGE)?;
    Ok(Command::TemplateShow { name, project })
}

fn optional_project(
    args: &[String],
    at: usize,
    usage: &'static str,
) -> Result<Option<String>, String> {
    match args.get(at).map(String::as_str) {
        None => Ok(None),
        Some("--project") => {
            let project = args
                .get(at + 1)
                .ok_or_else(|| format!("--project needs a value\n\n{usage}"))?
                .clone();
            only(args, at + 2)?;
            Ok(Some(project))
        }
        Some(flag) => Err(format!("unexpected argument: {flag}\n\n{usage}")),
    }
}

fn template_catalog_path(session: &str, project: Option<&str>) -> String {
    let path = if session == HOST {
        routes::TEMPLATES.to_string()
    } else {
        routes::SPAWNABLE_TEMPLATES.to_string()
    };
    match project {
        Some(project) => format!("{path}?project={}", encode_component(project)),
        None => path,
    }
}

pub(super) fn run_templates(
    endpoint: &Endpoint,
    session: &str,
    json: bool,
    project: Option<&str>,
) -> Result<(), String> {
    let path = template_catalog_path(session, project);
    let v = request(endpoint, session, "GET", &path, None)?;
    if json {
        print_json(&v);
    } else {
        print_templates(&v);
    }
    Ok(())
}

pub(super) fn run_template_show(
    endpoint: &Endpoint,
    session: &str,
    json: bool,
    name: &str,
    project: Option<&str>,
) -> Result<(), String> {
    let path = template_catalog_path(session, project);
    let v = request(endpoint, session, "GET", &path, None)?;
    let template = v
        .get("templates")
        .and_then(Value::as_array)
        .and_then(|templates| templates.iter().find(|template| template["name"] == name))
        .ok_or_else(|| format!("no accessible agent template: {name}"))?;
    if json {
        print_json(&json!({ "template": template }));
    } else {
        print_template_details(template);
    }
    Ok(())
}

fn print_templates(v: &Value) {
    let Some(templates) = v.get("templates").and_then(Value::as_array) else {
        println!("no templates");
        return;
    };
    if templates.is_empty() {
        println!("no templates");
        return;
    }
    for template in templates {
        print_template(template);
    }
}

fn print_template(template: &Value) {
    let name = template["name"].as_str().unwrap_or("?");
    let description = template["description"].as_str().unwrap_or("");
    if description.is_empty() {
        println!("{name}");
    } else {
        println!("{name}  -  {description}");
    }
}

fn print_template_details(template: &Value) {
    print_template(template);
    println!("version  {}", template["version"].as_u64().unwrap_or(0));
    let defaults = &template["defaults"];
    let command = defaults
        .get("command")
        .and_then(|command| command.get("name"))
        .and_then(Value::as_str)
        .unwrap_or("");
    let cmd = defaults["cmd"].as_str().unwrap_or("");
    if !command.is_empty() {
        println!("command  {command}");
    }
    if !cmd.is_empty() {
        println!("cmd      {cmd}");
    }
    if let Some(sandbox) = defaults["sandbox"].as_array() {
        println!(
            "sandbox  {}",
            if sandbox.is_empty() {
                "(none)".into()
            } else {
                sandbox
                    .iter()
                    .filter_map(Value::as_str)
                    .collect::<Vec<_>>()
                    .join(", ")
            }
        );
    }
    println!(
        "network  {}",
        defaults["network"].as_str().unwrap_or("private")
    );
    println!(
        "autostart {}",
        defaults["autostart"].as_bool().unwrap_or(false)
    );
}
