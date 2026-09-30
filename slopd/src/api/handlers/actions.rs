//! Explicit terminal launches and file or desktop action HTTP boundaries.

use crate::api::protobuf::{Proto, domain, reply};
use crate::shared::wire;
use axum::extract::{Query, State};
use serde_json::json;

use crate::config::{LibraryItemCfg, LibraryItemKind};
use crate::session::RunWhere;

use super::super::types::{FileActionReq, OpenAppsQuery, RunReq};
use super::{ApiResult, Mgr, err};

pub(crate) async fn run(
    State(m): State<Mgr>,
    Proto(q): Proto<wire::RunReq>,
) -> ApiResult<wire::SessionResult> {
    let q: RunReq = domain(q)?;
    let plan = plan_run(&m, q).await?;
    let session = m
        .run_errand(
            plan.item,
            plan.destination,
            plan.host_action,
            plan.persistent_host,
            &plan.like,
        )
        .await
        .map_err(|e| err(axum::http::StatusCode::BAD_REQUEST, e))?;
    reply(json!({ "ok": true, "session": session }))
}

/// The API-specific launch decision is complete before the manager starts a session.
struct RunPlan {
    item: LibraryItemCfg,
    destination: RunWhere,
    host_action: bool,
    persistent_host: bool,
    like: String,
}

async fn plan_run(m: &Mgr, q: RunReq) -> Result<RunPlan, crate::api::protobuf::ApiError> {
    if !matches!(q.intent.as_str(), "" | "view" | "edit" | "diff" | "search") {
        return Err(err(
            axum::http::StatusCode::BAD_REQUEST,
            "Unknown terminal intent.",
        ));
    }
    if matches!(q.intent.as_str(), "view" | "edit" | "search") && q.reader.path.is_empty() {
        return Err(err(
            axum::http::StatusCode::BAD_REQUEST,
            "A file reader needs a path.",
        ));
    }
    let project = q.project.trim();
    let command = if q.path.trim().is_empty() {
        q.command.trim().to_string()
    } else {
        m.file_action_command(project, &q.worktree, &q.path, q.command.trim(), q.host)
            .await
            .map_err(|e| err(axum::http::StatusCode::BAD_REQUEST, e))?
    };
    let command = command.trim();
    let command = if q.hold && !q.path.trim().is_empty() {
        crate::session::hold_action_command(command)
    } else {
        command.to_string()
    };
    // A shell errand without a command opens the default shell.
    // Use `[defaults] shell` in the sandbox or `$SHELL` on the host.
    // Let the daemon resolve this default. Other errand types must specify a command.
    if command.is_empty() && q.kind != LibraryItemKind::Shell {
        return Err(err(
            axum::http::StatusCode::BAD_REQUEST,
            "Provide a command for this action.",
        ));
    }
    if project.is_empty() && !q.temp && !q.host {
        return Err(err(
            axum::http::StatusCode::BAD_REQUEST,
            "Choose a project or request a temporary project.",
        ));
    }

    let label = match q.label.trim() {
        // Name a host errand from its project and shell, for example `slopworld-zsh`.
        // The game sends no label because the daemon selects the shell.
        // The game reads the generated name from the response.
        "" if q.host => crate::sandbox::host_session_name(project),
        "" => "run".to_string(),
        l => l.to_string(),
    };
    let sc = LibraryItemCfg {
        name: label,
        kind: q.kind,
        link: crate::config::LibraryItemLink::Project,
        project: project.to_string(),
        text: q.text,
        // Use None to indicate no explicit command. `session_for` then uses the preset.
        command: (!command.is_empty()).then_some(command),
        mode: crate::config::FileActionMode::Ask,
        builtin: false,
        host: q.host,
        agent_template: q.agent_template,
    };

    // `run_errand` checks the project and creates temporary projects.
    // Pass `temp` as an override to use that same path.
    let want = RunWhere {
        worktree: q.worktree,
        intent: q.intent,
        reader_path: q.reader.path,
        reader_key: q.reader.key,
        reader_scope: q.reader.scope,
        reader_line: q.reader.line,
        reader_pinned: q.reader.pinned,
        cols: q.cols,
        rows: q.rows,
        project: None,
        // A host reader for private state has no associated project.
        // Give it a temporary project so the errand and session code can manage its working directory.
        // The command contains the selected absolute path.
        temp: q.temp || (q.host && project.is_empty()),
        random_tips: q.random_tips,
    };
    // Explicit commands (viewers, editors and diffs) are disposable even with a project.
    let persistent_host = q.host
        && q.kind == LibraryItemKind::Shell
        && !project.is_empty()
        && q.path.trim().is_empty()
        && q.command.trim().is_empty()
        && !q.temp;
    Ok(RunPlan {
        item: sc,
        destination: want,
        host_action: q.host || !q.path.trim().is_empty(),
        persistent_host,
        like: q.like.trim().to_string(),
    })
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
pub(crate) async fn open_apps(
    State(m): State<Mgr>,
    Query(q): Query<OpenAppsQuery>,
) -> ApiResult<wire::AppsReply> {
    let apps = m
        .open_apps(&q.path)
        .await
        .map_err(|e| err(axum::http::StatusCode::BAD_REQUEST, e))?;
    reply(json!({ "apps": apps }))
}

#[cfg(test)]
#[path = "actions_tests.rs"]
mod tests;
