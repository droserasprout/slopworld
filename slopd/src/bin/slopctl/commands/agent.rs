//! Agent creation requests and output; the daemon owns template and project policy.

use super::super::format::print_json;
use super::super::http::Endpoint;
use super::super::http::request;
use super::common::encode_component;
use crate::shared::protocol::routes;
use serde_json::json;

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
