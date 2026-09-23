use crate::api::protobuf::{domain, reply, Proto};
use crate::shared::wire;
use axum::extract::{Path, Query, State};

use serde_json::json;

use crate::config::{LibraryItemCfg, LibraryItemKind};
use crate::session::RunWhere;

use super::super::types::*;
use super::{err, ApiResult, Mgr};

pub(crate) async fn list_projects(State(m): State<Mgr>) -> ApiResult<wire::ProjectsReply> {
    reply(json!({ "projects": m.projects().await }))
}

pub(crate) async fn one_project(
    State(m): State<Mgr>,
    Path(name): Path<String>,
) -> ApiResult<wire::Project> {
    m.projects()
        .await
        .into_iter()
        .find(|p| p.config.name == name)
        .map(|p| reply(json!(p)))
        .ok_or_else(|| {
            err(
                axum::http::StatusCode::NOT_FOUND,
                format!("Project {name:?} does not exist."),
            )
        })?
}

pub(crate) async fn create_project(
    State(m): State<Mgr>,
    Proto(value): Proto<wire::Project>,
) -> ApiResult<wire::Ack> {
    let p = crate::api::parse_project(domain(value)?)?;
    super::ok_json(m.add_project(p).await)
}

pub(crate) async fn project_preview(
    Proto(req): Proto<wire::ProjectPreviewReq>,
) -> ApiResult<wire::ProjectPreviewResult> {
    let req: ProjectPreviewReq = domain(req)?;
    reply(json!({
        "name": req.name,
        "temp": req.temp,
        "dir": if req.temp { crate::config::temp_dir(&req.name) } else { String::new() },
    }))
}

pub(crate) async fn update_project(
    State(m): State<Mgr>,
    Path(name): Path<String>,
    Proto(value): Proto<wire::Project>,
) -> ApiResult<wire::Ack> {
    let p = crate::api::parse_project(domain(value)?)?;
    super::ok_json(m.update_project(&name, p).await)
}

pub(crate) async fn destroy_project(
    State(m): State<Mgr>,
    Path(name): Path<String>,
) -> ApiResult<wire::Ack> {
    super::ok_json(m.remove_project(&name).await)
}

pub(crate) async fn list_library(State(m): State<Mgr>) -> ApiResult<wire::LibraryReply> {
    reply(json!({ "library": m.library().await }))
}

pub(crate) async fn create_library_item(
    State(m): State<Mgr>,
    Proto(sc): Proto<wire::LibraryItem>,
) -> ApiResult<wire::Ack> {
    let sc: LibraryItemCfg = domain(sc)?;
    super::ok_json(m.add_library_item(sc).await)
}

pub(crate) async fn update_library_item(
    State(m): State<Mgr>,
    Path(name): Path<String>,
    Proto(sc): Proto<wire::LibraryItem>,
) -> ApiResult<wire::Ack> {
    let sc: LibraryItemCfg = domain(sc)?;
    super::ok_json(m.update_library_item(&name, sc).await)
}

pub(crate) async fn destroy_library_item(
    State(m): State<Mgr>,
    Path(name): Path<String>,
) -> ApiResult<wire::Ack> {
    super::ok_json(m.remove_library_item(&name).await)
}

/// Return the temporary agent's name after startup, before delayed text delivery.
/// This prevents the client from waiting for the text and reaching its timeout.
/// The body selects the destination with `{"project":"..."}` or `{"temp":true}`.
/// `Option<Json<_>>` permits requests without a body when the errand already specifies its destination.
pub(crate) async fn run_library_item(
    State(m): State<Mgr>,
    Path(name): Path<String>,
    want: Option<Proto<wire::RunWhere>>,
) -> ApiResult<wire::SessionResult> {
    let session = m
        .run_library_item(
            &name,
            want.map(|Proto(w)| domain::<RunWhere>(w))
                .transpose()?
                .unwrap_or_default(),
        )
        .await
        .map_err(|e| err(axum::http::StatusCode::BAD_REQUEST, e))?;
    reply(json!({ "ok": true, "session": session }))
}

pub(crate) async fn run(
    State(m): State<Mgr>,
    Proto(q): Proto<wire::RunReq>,
) -> ApiResult<wire::SessionResult> {
    let mut q: RunReq = domain(q)?;
    if q.worktree.is_empty() && !q.path.is_empty() {
        q.worktree = m
            .worktree_for_path(&q.project, &q.path)
            .await
            .map_err(|e| err(axum::http::StatusCode::BAD_REQUEST, e))?;
    }
    let project = q.project.trim();
    let command = if q.path.trim().is_empty() {
        q.command.trim().to_string()
    } else {
        m.file_action_command(project, &q.path, q.command.trim(), q.host)
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
    let like = q.like.trim().to_string();
    let session = m
        .run_errand(
            sc,
            want,
            q.host || !q.path.trim().is_empty(),
            persistent_host,
            &like,
        )
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
        .file_action(&q.project, &q.path, &q.command, q.host)
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
#[path = "handlers_library_tests.rs"]
mod tests;
