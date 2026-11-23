//! Agent creation and lifecycle requests; the daemon owns template and project policy.

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

#[derive(Debug, PartialEq, Eq, clap::Args)]
pub(crate) struct AgentName {
    #[arg(value_parser = clap::builder::NonEmptyStringValueParser::new())]
    name: String,
}

#[derive(Debug, PartialEq, Eq, clap::Subcommand)]
pub(crate) enum AgentAction {
    /// Start an existing agent session
    Start(AgentName),
    /// Resume an agent from its saved state
    Resume(AgentName),
    /// Restart an agent session
    Restart(AgentName),
    /// Stop an agent session; one-shot workers are removed on stop
    Stop(AgentName),
    /// Remove an agent session
    Remove(AgentName),
}

impl AgentAction {
    pub(super) fn run(
        self,
        endpoint: &Endpoint,
        session: &str,
        json_output: bool,
    ) -> Result<(), String> {
        let (target, route, message) = match self {
            Self::Start(target) => (target, routes::SESSION_START, "Started"),
            Self::Resume(target) => (target, routes::SESSION_RESUME, "Resumed"),
            Self::Restart(target) => (target, routes::SESSION_RESTART, "Restarted"),
            Self::Stop(target) => (target, routes::SESSION_STOP, "Stopped"),
            Self::Remove(target) => (target, routes::SESSION, "Removed"),
        };
        let path = route.replace(":name", &encode_component(&target.name));
        let reply = if route == routes::SESSION {
            request(endpoint, session, "DELETE", &path, None)?
        } else {
            request(endpoint, session, "POST", &path, Some(json!({})))?
        };
        if json_output {
            print_json(&reply);
        } else {
            println!("{message} {}", target.name);
        }
        Ok(())
    }
}
