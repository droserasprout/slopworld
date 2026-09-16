use axum::extract::{Path, Query, State};
use axum::Json;
use serde_json::json;

use crate::config::{LibraryItemCfg, LibraryItemKind};
use crate::session::RunWhere;

use super::super::types::*;
use super::{err, ApiResult, Mgr};

pub(crate) async fn list_projects(State(m): State<Mgr>) -> ApiResult {
    Ok(Json(json!({ "projects": m.projects().await })))
}

pub(crate) async fn one_project(State(m): State<Mgr>, Path(name): Path<String>) -> ApiResult {
    m.projects()
        .await
        .into_iter()
        .find(|p| p.name == name)
        .map(|p| Json(json!(p)))
        .ok_or_else(|| {
            err(
                axum::http::StatusCode::NOT_FOUND,
                format!("no such project: {name}"),
            )
        })
}

pub(crate) async fn create_project(
    State(m): State<Mgr>,
    Json(value): Json<serde_json::Value>,
) -> ApiResult {
    let p = crate::api::parse_project(value)?;
    super::ok_json(m.add_project(p).await)
}

pub(crate) async fn project_preview(Json(req): Json<ProjectPreviewReq>) -> ApiResult {
    Ok(Json(json!({
        "name": req.name,
        "temp": req.temp,
        "dir": if req.temp { crate::config::temp_dir(&req.name) } else { String::new() },
    })))
}

pub(crate) async fn update_project(
    State(m): State<Mgr>,
    Path(name): Path<String>,
    Json(value): Json<serde_json::Value>,
) -> ApiResult {
    let p = crate::api::parse_project(value)?;
    super::ok_json(m.update_project(&name, p).await)
}

pub(crate) async fn destroy_project(State(m): State<Mgr>, Path(name): Path<String>) -> ApiResult {
    super::ok_json(m.remove_project(&name).await)
}

pub(crate) async fn list_library(State(m): State<Mgr>) -> ApiResult {
    Ok(Json(json!({ "library": m.library().await })))
}

pub(crate) async fn create_library_item(
    State(m): State<Mgr>,
    Json(sc): Json<LibraryItemCfg>,
) -> ApiResult {
    super::ok_json(m.add_library_item(sc).await)
}

pub(crate) async fn update_library_item(
    State(m): State<Mgr>,
    Path(name): Path<String>,
    Json(sc): Json<LibraryItemCfg>,
) -> ApiResult {
    super::ok_json(m.update_library_item(&name, sc).await)
}

pub(crate) async fn destroy_library_item(
    State(m): State<Mgr>,
    Path(name): Path<String>,
) -> ApiResult {
    super::ok_json(m.remove_library_item(&name).await)
}

/// Answers with the temporary agent's name as soon as it is up; the text lands well past the
/// point this client would have given up waiting. The body says where to run -
/// `{"project":"..."}` or `{"temp":true}` - and `Option<Json<_>>` is so a bodyless curl still
/// runs the errands that already know.
pub(crate) async fn run_library_item(
    State(m): State<Mgr>,
    Path(name): Path<String>,
    want: Option<Json<RunWhere>>,
) -> ApiResult {
    let session = m
        .run_library_item(&name, want.map(|Json(w)| w).unwrap_or_default())
        .await
        .map_err(|e| err(axum::http::StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({ "ok": true, "session": session })))
}

pub(crate) async fn run(State(m): State<Mgr>, Json(q): Json<RunReq>) -> ApiResult {
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
    // A shell errand with nothing to run is a shell - `[defaults] shell` inside the sandbox,
    // `$SHELL` on the host - and saying so again here would be the caller guessing at this
    // machine's answer. Every other kind has to say: a prompt with no command is an agent
    // nobody named.
    if command.is_empty() && q.kind != LibraryItemKind::Shell {
        return Err(err(
            axum::http::StatusCode::BAD_REQUEST,
            "an errand must say what to run",
        ));
    }
    if project.is_empty() && !q.temp && !q.host {
        return Err(err(
            axum::http::StatusCode::BAD_REQUEST,
            "an errand must name a project or ask for a temporary one",
        ));
    }

    let label = match q.label.trim() {
        // A host errand names itself for the project it opened on and the shell it opens -
        // `slopworld-zsh`. The game cannot know which shell that is, so it sends no label and
        // reads the name back off the answer, the same as it does for the session itself.
        "" if q.host => crate::sandbox::host_session_name(project),
        "" => "run".to_string(),
        l => l.to_string(),
    };
    let sc = LibraryItemCfg {
        source: String::new(),
        name: label,
        kind: q.kind,
        link: crate::config::LibraryItemLink::Project,
        project: project.to_string(),
        text: q.text,
        // None rather than an empty string: `session_for` reads "no command of its own" off
        // the Option, and that is what falls through to the preset.
        command: (!command.is_empty()).then_some(command),
        mode: crate::config::FileActionMode::Ask,
        builtin: false,
        host: q.host,
        agent_template: q.agent_template,
    };

    // The project is checked by `run_errand` itself, which is also where a temporary one is
    // coined - so `temp` rides over as the override it already is rather than a second road.
    let want = RunWhere {
        cols: q.cols,
        rows: q.rows,
        project: None,
        // A host reader for a private-state directory has no project to attach to. Give it a
        // disposable project only so the existing errand/session machinery can own its cwd;
        // the command itself carries the selected absolute path.
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
    Ok(Json(json!({ "ok": true, "session": session })))
}

/// Run a non-interactive Files or Git action and return a small result for a game message. Project
/// paths are validated against their project, but all file actions execute on the host.
/// Interactive actions use `/api/run`, since their terminal needs a tmux session and a persistent
/// screen.
pub(crate) async fn file_action(State(m): State<Mgr>, Json(q): Json<FileActionReq>) -> ApiResult {
    let output = m
        .file_action(&q.project, &q.path, &q.command, q.host)
        .await
        .map_err(|e| err(axum::http::StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({ "ok": true, "output": output })))
}

/// List the host desktop applications associated with a file. This is root-only because the
/// query runs outside every project sandbox, in the same desktop environment that will launch
/// the selected application.
pub(crate) async fn open_apps(State(m): State<Mgr>, Query(q): Query<OpenAppsQuery>) -> ApiResult {
    let apps = m
        .open_apps(&q.path)
        .await
        .map_err(|e| err(axum::http::StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({ "apps": apps })))
}
