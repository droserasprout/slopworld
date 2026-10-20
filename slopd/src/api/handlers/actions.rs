//! Explicit terminal launches and file or desktop action HTTP boundaries.

use crate::api::protobuf::{Proto, domain, reply};
use crate::shared::wire;
use axum::extract::{Query, State};
use serde_json::json;

use super::super::types::{FileActionReq, OpenAppsQuery, RunReq};
use super::{ApiResult, Mgr, err};

pub(crate) async fn run(
    State(m): State<Mgr>,
    Proto(q): Proto<wire::RunReq>,
) -> ApiResult<wire::SessionResult> {
    let q: RunReq = domain(q)?;
    let plan = m
        .plan_explicit_launch(q)
        .await
        .map_err(|e| err(axum::http::StatusCode::BAD_REQUEST, e))?;
    let session = m
        .run_launch(plan)
        .await
        .map_err(|e| err(axum::http::StatusCode::BAD_REQUEST, e))?;
    reply(json!({ "ok": true, "session": session }))
}

/// Run a non-interactive Files or Git action. Return a short result for a game message.
/// Validate project paths against their project. All file actions execute on the host.
/// Interactive actions use `/api/run` because their terminals need tmux sessions and persistent screens.
pub(crate) async fn file_action(
    State(m): State<Mgr>,
    Proto(q): Proto<wire::FileActionReq>,
) -> ApiResult<wire::OutputResult> {
    let q: FileActionReq = domain(q)?;
    let output = m
        .file_action(&q.project, &q.worktree, &q.path, &q.command, q.host)
        .await
        .map_err(|e| err(axum::http::StatusCode::BAD_REQUEST, e))?;
    reply(json!({ "ok": true, "output": output }))
}

/// List host desktop applications associated with a file. Only root can run this query.
/// It runs outside project sandboxes, in the desktop environment that launches the selected application.
pub(crate) async fn open_apps(Query(q): Query<OpenAppsQuery>) -> ApiResult<wire::AppsReply> {
    let apps = crate::files::desktop::open_apps(&q.path)
        .await
        .map_err(|e| err(axum::http::StatusCode::BAD_REQUEST, e))?;
    reply(json!({ "apps": apps }))
}

#[cfg(test)]
#[path = "actions_tests.rs"]
mod tests;
