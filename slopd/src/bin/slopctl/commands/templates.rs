//! Accessible template catalog requests and presentation; the daemon owns visibility.

use super::super::HOST;
use super::super::format::print_json;
use super::super::http::{Endpoint, request};
use super::common::encode_component;
use crate::shared::protocol::routes;
use serde_json::{Value, json};
use std::fmt::Write;

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
        print!("{}", render_templates(&v));
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
        print!("{}", render_template_details(template));
    }
    Ok(())
}

#[expect(clippy::expect_used, reason = "formatting into a String cannot fail")]
fn render_templates(v: &Value) -> String {
    let mut out = String::new();
    let Some(templates) = v.get("templates").and_then(Value::as_array) else {
        writeln!(out, "no templates").expect("writing to a String cannot fail");
        return out;
    };
    if templates.is_empty() {
        writeln!(out, "no templates").expect("writing to a String cannot fail");
        return out;
    }
    for template in templates {
        write_template(template, &mut out);
    }
    out
}

#[expect(clippy::expect_used, reason = "formatting into a String cannot fail")]
fn write_template(template: &Value, out: &mut String) {
    let name = template["name"].as_str().unwrap_or("?");
    let description = template["description"].as_str().unwrap_or("");
    if description.is_empty() {
        writeln!(out, "{name}").expect("writing to a String cannot fail");
    } else {
        writeln!(out, "{name}  -  {description}").expect("writing to a String cannot fail");
    }
}

#[expect(clippy::expect_used, reason = "formatting into a String cannot fail")]
fn render_template_details(template: &Value) -> String {
    let mut out = String::new();
    write_template(template, &mut out);
    writeln!(
        out,
        "version  {}",
        template["version"].as_u64().unwrap_or(0)
    )
    .expect("writing to a String cannot fail");
    let defaults = &template["defaults"];
    let command = defaults
        .get("command")
        .and_then(|command| command.get("name"))
        .and_then(Value::as_str)
        .unwrap_or("");
    let cmd = defaults["cmd"].as_str().unwrap_or("");
    if !command.is_empty() {
        writeln!(out, "command  {command}").expect("writing to a String cannot fail");
    }
    if !cmd.is_empty() {
        writeln!(out, "cmd      {cmd}").expect("writing to a String cannot fail");
    }
    if let Some(sandbox) = defaults["sandbox"].as_array() {
        writeln!(
            out,
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
        )
        .expect("writing to a String cannot fail");
    }
    writeln!(
        out,
        "network  {}",
        defaults["network"].as_str().unwrap_or("private")
    )
    .expect("writing to a String cannot fail");
    writeln!(
        out,
        "autostart {}",
        defaults["autostart"].as_bool().unwrap_or(false)
    )
    .expect("writing to a String cannot fail");
    out
}

#[cfg(test)]
#[path = "templates_tests.rs"]
mod tests;
