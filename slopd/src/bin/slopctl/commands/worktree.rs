//! Worktree argument definitions and requests; the daemon owns checkout mutation policy.

use super::super::format::print_json;
use super::super::http::{Endpoint, request};
use super::common::encode_component;
use crate::shared::protocol::routes;
use serde_json::{Value, json};

#[derive(Debug, PartialEq, Eq, clap::Subcommand)]
pub(crate) enum WorktreeArgs {
    /// List registered worktrees
    List {
        #[arg(long, value_parser = clap::builder::NonEmptyStringValueParser::new())]
        project: String,
    },
    /// Allocate a worktree or register an existing checkout
    Create {
        #[arg(long, value_parser = clap::builder::NonEmptyStringValueParser::new())]
        project: String,
        #[arg(long, value_parser = clap::builder::NonEmptyStringValueParser::new())]
        name: Option<String>,
        #[arg(long, value_parser = clap::builder::NonEmptyStringValueParser::new())]
        base: Option<String>,
        #[arg(long, value_parser = clap::builder::NonEmptyStringValueParser::new())]
        path: Option<String>,
    },
    /// Rename a registered worktree (requires the root token)
    Rename {
        #[arg(long, value_parser = clap::builder::NonEmptyStringValueParser::new())]
        project: String,
        id: String,
        #[arg(long, value_parser = clap::builder::NonEmptyStringValueParser::new())]
        name: String,
    },
    /// Remove a managed worktree or unregister an external checkout, keeping its files
    Remove {
        #[arg(long, value_parser = clap::builder::NonEmptyStringValueParser::new())]
        project: String,
        id: String,
    },
}

pub(super) fn run(
    endpoint: &Endpoint,
    session: &str,
    json_output: bool,
    args: WorktreeArgs,
) -> Result<(), String> {
    let removed = matches!(&args, WorktreeArgs::Remove { .. });
    let (project, id) = match &args {
        WorktreeArgs::List { project } | WorktreeArgs::Create { project, .. } => (project, None),
        WorktreeArgs::Rename { project, id, .. } | WorktreeArgs::Remove { project, id } => {
            (project, Some(id))
        }
    };
    let resource = match id {
        Some(id) => format!("{}/{}", routes::WORKTREES, encode_component(id)),
        None => routes::WORKTREES.to_string(),
    };
    let url = format!("{resource}?project={}", encode_component(project));
    let (method, body) = match args {
        WorktreeArgs::List { .. } => ("GET", None),
        WorktreeArgs::Create {
            project,
            name,
            base,
            path,
        } => (
            "POST",
            Some(
                json!({"project":project,"name":name.unwrap_or_default(),"base":base.unwrap_or_default(),"path":path.unwrap_or_default()}),
            ),
        ),
        WorktreeArgs::Remove { .. } => ("DELETE", None),
        WorktreeArgs::Rename { name, .. } => ("PUT", Some(json!({"name":name}))),
    };
    let value = request(endpoint, session, method, &url, body)?;
    if json_output {
        print_json(&value);
    } else if removed {
        println!("Worktree removed. Branches retained.");
    } else {
        let rows: Vec<&Value> = value
            .get("worktrees")
            .and_then(Value::as_array)
            .map(|v| v.iter().collect())
            .unwrap_or_else(|| vec![&value]);
        for w in rows {
            println!(
                "{}  {}  {}\n  {}",
                w.get("id").and_then(Value::as_str).unwrap_or(""),
                w.get("name").and_then(Value::as_str).unwrap_or(""),
                w.get("branch").and_then(Value::as_str).unwrap_or(""),
                w.get("path").and_then(Value::as_str).unwrap_or("")
            );
            if let Some(error) = w
                .get("error")
                .and_then(Value::as_str)
                .filter(|s| !s.is_empty())
            {
                println!("  {error}");
            }
            if let Some(attachments) = w
                .get("attachments")
                .and_then(Value::as_array)
                .filter(|v| !v.is_empty())
            {
                println!(
                    "  attached: {}",
                    attachments
                        .iter()
                        .filter_map(Value::as_str)
                        .collect::<Vec<_>>()
                        .join(", ")
                );
            }
        }
    }
    Ok(())
}
