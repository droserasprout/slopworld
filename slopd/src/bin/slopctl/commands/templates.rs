//! Accessible template catalog requests and presentation; the daemon owns visibility.

use super::super::HOST;
use super::super::format::print_json;
use super::super::http::{Endpoint, request};
use super::common::encode_component;
use crate::shared::protocol::routes;
use serde_json::{Value, json};

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
