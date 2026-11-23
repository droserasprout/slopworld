//! Project discovery and detail requests for the selected daemon.

use super::super::format::print_json;
use super::super::http::{Endpoint, request};
use super::common::encode_component;
use crate::shared::protocol::routes;

#[derive(Debug, PartialEq, Eq, clap::Subcommand)]
pub(crate) enum ProjectArgs {
    /// List configured project names
    List,
    /// Show a project's full configuration
    Show {
        #[arg(value_parser = clap::builder::NonEmptyStringValueParser::new())]
        name: String,
    },
}

pub(super) fn run(
    endpoint: &Endpoint,
    session: &str,
    json_output: bool,
    args: ProjectArgs,
) -> Result<(), String> {
    match args {
        ProjectArgs::List => {
            let reply = request(endpoint, session, "GET", routes::PROJECTS, None)?;
            if json_output {
                print_json(&reply);
            } else if let Some(projects) = reply["projects"].as_array() {
                for project in projects {
                    if let Some(name) = project["name"].as_str() {
                        println!("{name}");
                    }
                }
            }
        }
        ProjectArgs::Show { name } => {
            let path = routes::PROJECT.replace(":name", &encode_component(&name));
            print_json(&request(endpoint, session, "GET", &path, None)?);
        }
    }
    Ok(())
}
