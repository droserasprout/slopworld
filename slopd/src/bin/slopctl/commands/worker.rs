//! Worker spawn requests and output; `cli` constructs the owned spawn request.

use super::super::format::emit;
use super::super::http::{Endpoint, request};
use super::task::print_wait_hint;
use crate::shared::protocol::routes;
use serde_json::json;

#[derive(Debug, PartialEq, Eq)]
pub(crate) struct SpawnArgs {
    pub(crate) project: String,
    pub(crate) template: String,
    pub(crate) durable: bool,
    pub(crate) worktree: String,
    pub(crate) body: String,
}

pub(crate) fn run_spawn(
    endpoint: &Endpoint,
    session: &str,
    json: bool,
    args: &SpawnArgs,
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
            "worktree": args.worktree,
        })),
    )?;
    emit(&v, json);
    print_wait_hint(&v, json);
    Ok(())
}
