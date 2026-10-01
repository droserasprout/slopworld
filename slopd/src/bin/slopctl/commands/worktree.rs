//! Worktree command parsing and execution share one owned argument record.

use super::super::format::print_json;
use super::super::http::{Endpoint, request};
use super::Command;
use super::common::{encode_component, option_value, value_option};
use crate::shared::protocol::routes;
use serde_json::{Value, json};

pub(crate) const WORKTREE_USAGE: &str = "usage:\n  slopctl worktree list --project PROJECT\n  slopctl worktree create --project PROJECT [--name NAME] [--base REV] [--path EXISTING_CHECKOUT]\n  slopctl worktree rename ID --project PROJECT --name NAME\n  slopctl worktree remove ID --project PROJECT\n\nYou need the root token to rename or remove a worktree.\nThe daemon unregisters external checkouts and keeps their files.\nUse --option=VALUE for option values beginning with a dash.\n";
pub(super) fn parse_worktree(args: &[String]) -> Result<Command, String> {
    let action = args.get(1).ok_or(WORKTREE_USAGE)?.clone();
    if !["list", "create", "remove", "rename"].contains(&action.as_str()) {
        return Err(WORKTREE_USAGE.into());
    }
    let mut project = String::new();
    let mut name = String::new();
    let mut base = String::new();
    let mut path = String::new();
    let mut id = String::new();
    let mut i = 2;
    if action == "remove" || action == "rename" {
        id = args
            .get(i)
            .filter(|id| !id.starts_with('-') && !id.is_empty())
            .ok_or(WORKTREE_USAGE)?
            .clone();
        i += 1;
    }
    while let Some(option) = args.get(i) {
        let (flag, inline) = value_option(option);
        let allowed = flag == "--project"
            || (flag == "--name" && matches!(action.as_str(), "create" | "rename"))
            || (matches!(flag, "--base" | "--path") && action == "create");
        if !allowed {
            return Err(WORKTREE_USAGE.into());
        }
        let value = option_value(args, &mut i, flag, inline)?;
        i += 1;
        match flag {
            "--project" => project = value,
            "--name" => name = value,
            "--base" => base = value,
            _ => path = value,
        }
    }
    if project.is_empty() || (action == "rename" && name.is_empty()) {
        return Err(WORKTREE_USAGE.into());
    }
    Ok(Command::Worktree(WorktreeArgs {
        action,
        project,
        name,
        base,
        path,
        id,
    }))
}

#[derive(Debug, PartialEq, Eq)]
pub(crate) struct WorktreeArgs {
    pub(crate) action: String,
    pub(crate) project: String,
    pub(crate) name: String,
    pub(crate) base: String,
    pub(crate) path: String,
    pub(crate) id: String,
}

pub(super) fn run(
    endpoint: &Endpoint,
    session: &str,
    json_output: bool,
    args: WorktreeArgs,
) -> Result<(), String> {
    let WorktreeArgs {
        action,
        project,
        name,
        base,
        path,
        id,
    } = args;
    let url = if action == "remove" || action == "rename" {
        format!(
            "{}/{}?project={}",
            routes::WORKTREES,
            encode_component(&id),
            encode_component(&project)
        )
    } else {
        format!(
            "{}?project={}",
            routes::WORKTREES,
            encode_component(&project)
        )
    };
    let (method, body) = match action.as_str() {
        "create" => (
            "POST",
            Some(json!({"project":project,"name":name,"base":base,"path":path})),
        ),
        "remove" => ("DELETE", None),
        "rename" => ("PUT", Some(json!({"name":name}))),
        _ => ("GET", None),
    };
    let value = request(endpoint, session, method, &url, body)?;
    if json_output {
        print_json(&value);
    } else if action == "remove" {
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
