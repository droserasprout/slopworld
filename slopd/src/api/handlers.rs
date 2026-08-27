use anyhow::{bail, Context};
use axum::extract::{Path, Query, State};
use axum::http::{HeaderMap, StatusCode};
use axum::{Extension, Json};
use base64::{engine::general_purpose::STANDARD as BASE64, Engine as _};
use serde_json::{json, Value};
use std::process::Stdio;
use std::time::Duration;
use tokio::io::AsyncReadExt;
use tokio::process::Command;

use crate::config::{ProjectCfg, SessionCfg, ShortcutCfg};
use crate::grant::{Cap, Level};
use crate::session::RunWhere;

use super::types::*;
use super::{err, ApiResult, Mgr};

fn ok_json(r: anyhow::Result<()>) -> ApiResult {
    r.map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({ "ok": true })))
}

macro_rules! rw_action {
    ($($handler:ident => $method:ident),+ $(,)?) => {
        $(
            pub(super) async fn $handler(
                State(m): State<Mgr>,
                Extension(cap): Extension<Cap>,
                Path(name): Path<String>,
            ) -> ApiResult {
                guard(&m, &cap, &name, Level::Rw).await?;
                ok_json(m.$method(&name).await)
            }
        )+
    };
}

macro_rules! root_action {
    ($($handler:ident => $method:ident),+ $(,)?) => {
        $(
            pub(super) async fn $handler(State(m): State<Mgr>, Path(name): Path<String>) -> ApiResult {
                ok_json(m.$method(&name).await)
            }
        )+
    };
}

/// 403 unless `cap` may touch `name` at `need` - the check every per-session route makes. Root
/// passes everything; a grant passes only a session it names, and never the host. 403 rather
/// than 404, since a scoped caller has no business learning whether the name it cannot touch
/// exists (a missing name and a forbidden one read the same to it).
pub(super) async fn guard(
    m: &Mgr,
    cap: &Cap,
    name: &str,
    need: Level,
) -> Result<(), (StatusCode, Json<serde_json::Value>)> {
    if m.cap_ok(cap, name, need).await {
        Ok(())
    } else {
        Err(err(StatusCode::FORBIDDEN, format!("not allowed: {name}")))
    }
}

/// 403 unless `cap` is the root: creating a session - a new agent, an errand - is the mod's,
/// never a grant's. A grant is a handle on what already exists.
pub(super) fn guard_create(cap: &Cap) -> Result<(), (StatusCode, Json<serde_json::Value>)> {
    if cap.may_create() {
        Ok(())
    } else {
        Err(err(
            StatusCode::FORBIDDEN,
            "only the daemon's own token may create sessions",
        ))
    }
}

pub(super) async fn health(State(_m): State<Mgr>) -> ApiResult {
    Ok(Json(json!({
        "ok": true,
        "version": env!("CARGO_PKG_VERSION"),
        "hostname": crate::runtime::hostname(),
        "tmux_socket": crate::config::tmux_socket(),
    })))
}

pub(super) fn task_principal(
    cap: &Cap,
    headers: &HeaderMap,
) -> Result<String, (StatusCode, Json<Value>)> {
    let who = cap
        .principal()
        .map(str::to_string)
        .or_else(|| {
            headers
                .get("x-slop-session")
                .and_then(|v| v.to_str().ok())
                .map(str::to_string)
        })
        .filter(|s| !s.trim().is_empty())
        .ok_or_else(|| {
            err(
                StatusCode::BAD_REQUEST,
                "root task requests need x-slop-session",
            )
        })?;
    // The root token is the only thing that speaks for the user at the keyboard. A grant resolves
    // to its grantor's name, so this covers a session that happens to be called `host` too: it may
    // hold a grant, but it cannot wear the host's identity with it.
    if who == crate::tasks::HOST && !cap.may_create() {
        return Err(err(
            StatusCode::FORBIDDEN,
            "only the daemon's own token speaks as the host",
        ));
    }
    Ok(who)
}

/// A task endpoint is a live session or the host. The host is checked here rather than through
/// `guard`, because it is not a session: it has no host-ness flag to look up, appears in no
/// grant's scope, and names nothing a scoped caller could learn from reaching it. An agent
/// reporting back to the user is what the mailbox is for.
pub(super) async fn task_endpoint_known(m: &Mgr, who: &str) -> bool {
    who == crate::tasks::HOST || m.session_known(who).await
}

pub(super) async fn create_task(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Json(q): Json<CreateTaskReq>,
) -> ApiResult {
    let from = task_principal(&cap, &headers)?;
    if !task_endpoint_known(&m, &from).await {
        return Err(err(
            StatusCode::BAD_REQUEST,
            format!("no such session: {from}"),
        ));
    }
    if !task_endpoint_known(&m, &q.to).await {
        return Err(err(
            StatusCode::BAD_REQUEST,
            format!("no such session: {}", q.to),
        ));
    }
    if q.to != crate::tasks::HOST {
        guard(&m, &cap, &q.to, Level::Ro).await?;
    }
    let task = m
        .create_task(from, q.to, q.body)
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({ "task": task })))
}

pub(super) async fn list_tasks(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
) -> ApiResult {
    let who = task_principal(&cap, &headers)?;
    Ok(Json(json!({ "tasks": m.tasks_for(&who) })))
}

pub(super) async fn one_task(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Path(id): Path<String>,
) -> ApiResult {
    let who = task_principal(&cap, &headers)?;
    m.task_for(&who, &id)
        .map(|task| Json(json!({ "task": task })))
        .ok_or_else(|| err(StatusCode::NOT_FOUND, format!("no such task: {id}")))
}

pub(super) async fn update_task(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Path(id): Path<String>,
    Json(q): Json<UpdateTaskReq>,
) -> ApiResult {
    let who = task_principal(&cap, &headers)?;
    let task = m
        .update_task(&who, &id, q.status, q.note)
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({ "task": task })))
}

pub(super) async fn prune_tasks(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Query(q): Query<PruneTasksQuery>,
) -> ApiResult {
    let who = task_principal(&cap, &headers)?;
    if q.all && !cap.may_create() {
        return Err(err(
            StatusCode::FORBIDDEN,
            "only the daemon's own token prunes every mailbox",
        ));
    }
    let removed = m
        .prune_tasks(&who, q.all)
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({ "removed": removed })))
}

pub(super) async fn remove_task(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
    Path(id): Path<String>,
) -> ApiResult {
    let who = task_principal(&cap, &headers)?;
    let task = m
        .remove_task(&who, &id, cap.may_create())
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({ "task": task })))
}

pub(super) async fn list(State(m): State<Mgr>, Extension(cap): Extension<Cap>) -> ApiResult {
    // Filtered to what the caller may see: a grant lists the sessions it names and no others, so
    // a name it cannot touch is a name it never learns. Host-ness is not carried on a view and
    // is not needed here - a grant never names a host session, so `can_see` refuses it by
    // membership alone.
    let sessions: Vec<_> = m
        .views()
        .await
        .into_iter()
        .filter(|s| cap.can_see(&s.name, s.host))
        .collect();
    Ok(Json(json!({ "sessions": sessions })))
}

pub(super) async fn one(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    Path(name): Path<String>,
) -> ApiResult {
    guard(&m, &cap, &name, Level::Ro).await?;
    m.views()
        .await
        .into_iter()
        .find(|s| s.name == name)
        .map(|s| Json(json!(s)))
        .ok_or_else(|| err(StatusCode::NOT_FOUND, format!("no such session: {name}")))
}

pub(super) async fn cwd(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    Path(name): Path<String>,
) -> ApiResult {
    guard(&m, &cap, &name, Level::Ro).await?;
    let path = m
        .tmux
        .current_path(&name)
        .await
        .ok_or_else(|| err(StatusCode::NOT_FOUND, format!("no such session: {name}")))?;
    Ok(Json(json!({ "path": path })))
}

pub(super) async fn create(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    Json(s): Json<SessionCfg>,
) -> ApiResult {
    guard_create(&cap)?;
    ok_json(m.add(s).await)
}

pub(super) async fn update(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    Path(name): Path<String>,
    Json(s): Json<SessionCfg>,
) -> ApiResult {
    guard(&m, &cap, &name, Level::Rw).await?;
    ok_json(m.update(&name, s).await)
}

rw_action!(
    destroy => remove,
    start => start,
    stop => stop,
    reset_state => reset_state,
    restart => restart,
);

pub(super) async fn stored_states(State(m): State<Mgr>) -> ApiResult {
    m.stored_states()
        .await
        .map(|entries| Json(json!({ "entries": entries })))
        .map_err(|e| err(StatusCode::INTERNAL_SERVER_ERROR, e))
}

pub(super) async fn delete_stored_state(
    State(m): State<Mgr>,
    Path((kind, key)): Path<(String, String)>,
) -> ApiResult {
    ok_json(m.delete_stored_state(&kind, &key).await)
}

pub(super) async fn restore_stored_state(
    State(m): State<Mgr>,
    Path(key): Path<String>,
) -> ApiResult {
    m.restore_stored_state(&key)
        .await
        .map(|session| Json(json!({ "ok": true, "session": session })))
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))
}

pub(super) async fn set_label(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    Path(name): Path<String>,
    Json(q): Json<LabelReq>,
) -> ApiResult {
    guard(&m, &cap, &name, Level::Rw).await?;
    ok_json(m.set_label(&name, q.label).await)
}

pub(super) async fn list_projects(State(m): State<Mgr>) -> ApiResult {
    Ok(Json(json!({ "projects": m.projects().await })))
}

pub(super) async fn one_project(State(m): State<Mgr>, Path(name): Path<String>) -> ApiResult {
    m.projects()
        .await
        .into_iter()
        .find(|p| p.name == name)
        .map(|p| Json(json!(p)))
        .ok_or_else(|| err(StatusCode::NOT_FOUND, format!("no such project: {name}")))
}

pub(super) async fn create_project(State(m): State<Mgr>, Json(p): Json<ProjectCfg>) -> ApiResult {
    ok_json(m.add_project(p).await)
}

pub(super) async fn update_project(
    State(m): State<Mgr>,
    Path(name): Path<String>,
    Json(p): Json<ProjectCfg>,
) -> ApiResult {
    ok_json(m.update_project(&name, p).await)
}

root_action!(destroy_project => remove_project, destroy_shortcut => remove_shortcut);

pub(super) async fn list_shortcuts(State(m): State<Mgr>) -> ApiResult {
    Ok(Json(json!({ "shortcuts": m.shortcuts().await })))
}

pub(super) async fn create_shortcut(
    State(m): State<Mgr>,
    Json(sc): Json<ShortcutCfg>,
) -> ApiResult {
    ok_json(m.add_shortcut(sc).await)
}

pub(super) async fn update_shortcut(
    State(m): State<Mgr>,
    Path(name): Path<String>,
    Json(sc): Json<ShortcutCfg>,
) -> ApiResult {
    ok_json(m.update_shortcut(&name, sc).await)
}

/// Answers with the temporary agent's name as soon as it is up; the text lands well past the
/// point this client would have given up waiting. The body says where to run -
/// `{"project":"..."}` or `{"temp":true}` - and `Option<Json<_>>` is so a bodyless curl still
/// runs the errands that already know.
pub(super) async fn run_shortcut(
    State(m): State<Mgr>,
    Path(name): Path<String>,
    want: Option<Json<RunWhere>>,
) -> ApiResult {
    let session = m
        .run_shortcut(&name, want.map(|Json(w)| w).unwrap_or_default())
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({ "ok": true, "session": session })))
}

pub(super) async fn run(State(m): State<Mgr>, Json(q): Json<RunReq>) -> ApiResult {
    let project = q.project.trim();
    let command = if q.path.trim().is_empty() {
        q.command.trim().to_string()
    } else {
        m.file_action_command(project, &q.path, q.command.trim(), q.host)
            .await
            .map_err(|e| err(StatusCode::BAD_REQUEST, e))?
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
    if command.is_empty() && q.kind != crate::config::ShortcutKind::Shell {
        return Err(err(
            StatusCode::BAD_REQUEST,
            "an errand must say what to run",
        ));
    }
    if project.is_empty() && !q.temp && !q.host {
        return Err(err(
            StatusCode::BAD_REQUEST,
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
    let sc = ShortcutCfg {
        name: label,
        kind: q.kind,
        link: crate::config::ShortcutLink::Project,
        project: project.to_string(),
        text: q.text,
        // None rather than an empty string: `session_for` reads "no command of its own" off
        // the Option, and that is what falls through to the preset.
        command: (!command.is_empty()).then_some(command),
        builtin: false,
    };

    // The project is checked by `run_errand` itself, which is also where a temporary one is
    // coined - so `temp` rides over as the override it already is rather than a second road.
    let want = RunWhere {
        project: None,
        // A host reader for a private-state directory has no project to attach to. Give it a
        // disposable project only so the existing errand/session machinery can own its cwd;
        // the command itself carries the selected absolute path.
        temp: q.temp || (q.host && project.is_empty()),
        random_tips: q.random_tips,
    };
    let persistent_host = q.host
        && q.kind == crate::config::ShortcutKind::Shell
        && !project.is_empty()
        && q.path.trim().is_empty()
        && !q.temp;
    let session = m
        .run_errand(sc, want, q.host, persistent_host)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({ "ok": true, "session": session })))
}

/// Run a non-interactive Files or Git action and return a small result for a game message. Project
/// paths use their normal sandbox; private-state and Git paths explicitly ask for the host.
/// Interactive actions use `/api/run`, since their terminal needs a tmux session and a persistent
/// screen.
pub(super) async fn file_action(State(m): State<Mgr>, Json(q): Json<FileActionReq>) -> ApiResult {
    let output = m
        .file_action(&q.project, &q.path, &q.command, q.host)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({ "ok": true, "output": output })))
}

/// List the host desktop applications associated with a file. This is root-only because the
/// query runs outside every project sandbox, in the same desktop environment that will launch
/// the selected application.
pub(super) async fn open_apps(State(m): State<Mgr>, Query(q): Query<OpenAppsQuery>) -> ApiResult {
    let apps = m
        .open_apps(&q.path)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({ "apps": apps })))
}

/// Mint a grant: let `grantor` watch or drive the named `sessions`. Root-only by the router,
/// so only the mod asks. The daemon refuses a host session in the scope - the one line the
/// whole scheme is for - and refuses a grantor or target that is not there. The token comes
/// back once and is never stored on the file; losing it means minting another.
pub(super) async fn mint_grant(State(m): State<Mgr>, Json(q): Json<GrantReq>) -> ApiResult {
    let level = match q.level.trim() {
        "ro" => Level::Ro,
        "rw" => Level::Rw,
        other => {
            return Err(err(
                StatusCode::BAD_REQUEST,
                format!("level must be \"ro\" or \"rw\", not {other:?}"),
            ))
        }
    };
    let token = m
        .mint_grant(q.grantor, q.sessions, level)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({ "ok": true, "token": token })))
}

/// How many grants are live, for the mod's readout. Not the tokens themselves: those are
/// bearer secrets and leave the daemon once, at the mint.
pub(super) async fn list_grants(State(m): State<Mgr>) -> ApiResult {
    Ok(Json(json!({ "grants": m.grant_count().await })))
}

/// Drop every grant a session minted, by hand rather than by its exit. The mod's revoke.
pub(super) async fn revoke_grants(State(m): State<Mgr>, Path(grantor): Path<String>) -> ApiResult {
    m.revoke_grants(&grantor).await;
    Ok(Json(json!({ "ok": true })))
}

/// So the GUI draws a checkbox per preset and a row per command rather than a list
/// somebody keeps in step by hand. Both tables are files, so this is also how the mod
/// learns about one that was added while it was running. `source` is explicit because a
/// merged table alone cannot tell a built-in from a user override.
pub(super) async fn presets(State(_m): State<Mgr>) -> ApiResult {
    let effective = crate::presets::table();
    let builtins = crate::presets::Table::builtins();
    let users = crate::presets::Table::users();

    let sandbox: Vec<serde_json::Value> = effective
        .sandbox
        .iter()
        .map(|p| sandbox_json(p, &builtins, &users))
        .collect();
    let commands: Vec<serde_json::Value> = effective
        .commands
        .iter()
        .map(|c| command_json(c, &builtins, &users))
        .collect();

    Ok(Json(json!({
        "presets": sandbox,
        "commands": commands,
        "dir": crate::presets::Table::dir(),
    })))
}

pub(super) fn source(has_builtin: bool, has_user: bool) -> &'static str {
    match (has_builtin, has_user) {
        (true, true) => "override",
        (true, false) => "system",
        (false, true) => "user",
        (false, false) => "unknown",
    }
}

pub(super) fn sandbox_json(
    p: &crate::presets::SandboxPreset,
    builtins: &crate::presets::Table,
    users: &crate::presets::Table,
) -> serde_json::Value {
    json!({
        "name": p.name,
        "source": source(builtins.sandbox(&p.name).is_some(), users.sandbox(&p.name).is_some()),
        "description": p.description,
        "requires": p.requires,
        "ro": p.ro,
        "rw": p.rw,
        "dev": p.dev,
        "private": p.private,
        "seed": p.seed,
        "skip": p.skip,
        "shared": p.shared,
        "escapes": p.escapes,
        "env": p.env,
        "setenv": p.setenv,
        "tmux": p.tmux,
        "daemon_config": p.daemon_config,
    })
}

pub(super) fn command_json(
    c: &crate::presets::CommandPreset,
    builtins: &crate::presets::Table,
    users: &crate::presets::Table,
) -> serde_json::Value {
    json!({
        "name": c.name,
        "source": source(builtins.command(&c.name).is_some(), users.command(&c.name).is_some()),
        "description": c.description,
        "cmd": c.cmd,
        "sandbox": c.sandbox,
    })
}

pub(super) fn valid_kind(kind: &str) -> Result<(), (StatusCode, Json<serde_json::Value>)> {
    if kind == "sandbox" || kind == "command" {
        Ok(())
    } else {
        Err(err(
            StatusCode::BAD_REQUEST,
            format!("unknown preset kind: {kind}"),
        ))
    }
}

pub(super) async fn copy_preset(
    State(m): State<Mgr>,
    Path((kind, old_name)): Path<(String, String)>,
    body: Option<Json<CopyPresetReq>>,
) -> ApiResult {
    valid_kind(&kind)?;
    let target = body.map(|Json(b)| b.name).unwrap_or_default();
    let name = if target.trim().is_empty() {
        old_name.clone()
    } else {
        target
    };
    let builtins = crate::presets::Table::builtins();
    let users = crate::presets::Table::users();
    let already_user = if kind == "sandbox" {
        users.sandbox(&old_name).is_some()
    } else {
        users.command(&old_name).is_some()
    };
    if already_user {
        return Err(err(
            StatusCode::BAD_REQUEST,
            "that preset already has a user definition",
        ));
    }
    let target_exists = if kind == "sandbox" {
        users.sandbox(&name).is_some() || (name != old_name && builtins.sandbox(&name).is_some())
    } else {
        users.command(&name).is_some() || (name != old_name && builtins.command(&name).is_some())
    };
    if target_exists {
        return Err(err(
            StatusCode::BAD_REQUEST,
            "the target name already exists",
        ));
    }

    if kind == "sandbox" {
        let Some(mut p) = builtins.sandbox(&old_name).cloned() else {
            return Err(err(
                StatusCode::NOT_FOUND,
                format!("unknown sandbox preset: {old_name}"),
            ));
        };
        p.name = name;
        crate::presets::save_sandbox(p).map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    } else {
        let Some(mut c) = builtins.command(&old_name).cloned() else {
            return Err(err(
                StatusCode::NOT_FOUND,
                format!("unknown command preset: {old_name}"),
            ));
        };
        c.name = name;
        crate::presets::save_command(c).map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    }
    m.reload_presets_if_changed().await;
    Ok(Json(json!({ "ok": true })))
}

pub(super) async fn update_preset(
    State(m): State<Mgr>,
    Path((kind, name)): Path<(String, String)>,
    body: axum::body::Bytes,
) -> ApiResult {
    valid_kind(&kind)?;
    if name.trim().is_empty() {
        return Err(err(StatusCode::BAD_REQUEST, "preset name is empty"));
    }
    if kind == "sandbox" {
        let mut p: crate::presets::SandboxPreset =
            serde_json::from_slice(&body).map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
        p.name = name;
        let current = crate::presets::table();
        let mut candidate = (*current).clone();
        candidate.sandbox.retain(|existing| existing.name != p.name);
        candidate.sandbox.push(p.clone());
        crate::sandbox::validate_preset(&p, &candidate)
            .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
        crate::presets::save_sandbox(p).map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    } else {
        let mut c: crate::presets::CommandPreset =
            serde_json::from_slice(&body).map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
        c.name = name;
        if c.cmd.trim().is_empty() {
            return Err(err(StatusCode::BAD_REQUEST, "command line is empty"));
        }
        let table = crate::presets::table();
        for dep in &c.sandbox {
            crate::sandbox::validate_preset_name(dep, &table).map_err(|e| {
                err(
                    StatusCode::BAD_REQUEST,
                    format!("invalid sandbox dependency {dep:?}: {e}"),
                )
            })?;
        }
        crate::presets::save_command(c).map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    }
    m.reload_presets_if_changed().await;
    Ok(Json(json!({ "ok": true })))
}

pub(super) async fn delete_preset(
    State(m): State<Mgr>,
    Path((kind, name)): Path<(String, String)>,
) -> ApiResult {
    valid_kind(&kind)?;
    let builtins = crate::presets::Table::builtins();
    let users = crate::presets::Table::users();
    let has_user = if kind == "sandbox" {
        users.sandbox(&name).is_some()
    } else {
        users.command(&name).is_some()
    };
    if !has_user {
        return Err(err(
            StatusCode::NOT_FOUND,
            "no user definition exists for that preset",
        ));
    }
    if kind == "sandbox" {
        if builtins.sandbox(&name).is_none() {
            // A user-only sandbox cannot disappear while a command still requires it. Check
            // before writing, or a rejected delete would have already changed the directory.
            let remaining = crate::presets::Table::users();
            for c in remaining.commands.iter().chain(builtins.commands.iter()) {
                if c.sandbox.iter().any(|d| d == &name) {
                    return Err(err(
                        StatusCode::BAD_REQUEST,
                        format!("sandbox {name:?} is required by command {:?}", c.name),
                    ));
                }
            }
            for p in remaining.sandbox.iter().chain(builtins.sandbox.iter()) {
                if p.requires.iter().any(|d| d == &name) {
                    return Err(err(
                        StatusCode::BAD_REQUEST,
                        format!("sandbox {name:?} is required by sandbox {:?}", p.name),
                    ));
                }
            }
        }
        crate::presets::remove_sandbox(&name).map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    } else {
        crate::presets::remove_command(&name).map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    }
    m.reload_presets_if_changed().await;
    Ok(Json(json!({ "ok": true })))
}

/// Text for the raw editor, parsed for the settings GUI, so a mod can offer either
/// without parsing TOML.
pub(super) async fn get_config(State(m): State<Mgr>) -> ApiResult {
    // Keep the raw text and parsed values from different snapshots when a user edits the file
    // outside the daemon.
    m.reload_if_changed().await;
    let text = std::fs::read_to_string(&m.cfg_path)
        .map_err(|e| err(StatusCode::INTERNAL_SERVER_ERROR, e))?;
    // The token never leaves the daemon as written: the raw text and the parsed values both
    // carry the sentinel, and a write that sends it back is read as "unchanged". The endpoint
    // is behind the token itself, so this guards the one case that is not - the raw editor,
    // and any future client that reaches the config without holding the secret first.
    Ok(Json(json!({
        "path": m.cfg_path,
        "text": crate::config::redact_token_text(&text),
        "values": m.config().await.redacted(),
    })))
}

pub(super) async fn capabilities() -> ApiResult {
    Ok(Json(json!(crate::runtime::capabilities())))
}

pub(super) async fn put_config(State(m): State<Mgr>, Json(req): Json<ConfigReq>) -> ApiResult {
    ok_json(m.replace_config(&req.text).await)
}

/// Apply only the fields named by the client, leaving unmentioned fields untouched.
pub(super) async fn put_config_patch(State(m): State<Mgr>, Json(req): Json<Value>) -> ApiResult {
    ok_json(m.patch_config(req).await)
}

pub(super) async fn usage(State(m): State<Mgr>) -> ApiResult {
    Ok(Json(json!(m.usage().await)))
}

/// What the jukebox is doing, for anything that would rather ask than listen - which in
/// practice means a person with `curl` and a suspicion. The mod hears the same thing as an
/// event; nothing needs this route to work.
pub(super) async fn audio(State(m): State<Mgr>) -> ApiResult {
    Ok(Json(json!(m.audio.state())))
}

/// The station catalog for clients that draw the jukebox. URLs stay daemon-side; this carries
/// only ids, stream keys, rates and the metadata a UI may render.
pub(super) async fn jukebox() -> ApiResult {
    Ok(Json(json!(crate::jukebox::catalog())))
}

/// A tool that is missing or wedged is a 502 rather than a 400, because nothing
/// about the request was wrong.
pub(super) async fn clip_read() -> ApiResult {
    let text = crate::clipboard::read()
        .await
        .map_err(|e| err(StatusCode::BAD_GATEWAY, e))?;
    Ok(Json(json!({ "text": text })))
}

pub(super) async fn clip_read_text() -> ApiResult {
    let text = crate::clipboard::read_text()
        .await
        .map_err(|e| err(StatusCode::BAD_GATEWAY, e))?;
    Ok(Json(json!({ "text": text })))
}

pub(super) async fn clip_read_primary() -> ApiResult {
    let text = crate::clipboard::read_primary()
        .await
        .map_err(|e| err(StatusCode::BAD_GATEWAY, e))?;
    Ok(Json(json!({ "text": text })))
}

pub(super) async fn clip_read_primary_text() -> ApiResult {
    let text = crate::clipboard::read_primary_text()
        .await
        .map_err(|e| err(StatusCode::BAD_GATEWAY, e))?;
    Ok(Json(json!({ "text": text })))
}

pub(super) async fn clip_write(Json(q): Json<ClipReq>) -> ApiResult {
    crate::clipboard::write(&q.text)
        .await
        .map_err(|e| err(StatusCode::BAD_GATEWAY, e))?;
    Ok(Json(json!({ "ok": true })))
}

/// A preview is a UI document, not a file transfer. Keep the response bounded so a generated
/// README cannot turn one click into an unbounded JSON allocation in the daemon or the game.
const READ_LIMIT: u64 = 512 * 1024;

/// Images are a bounded preview transfer, not a general file download. This leaves room for
/// screenshots while keeping one Markdown page from allocating an unbounded JSON response.
const IMAGE_LIMIT: u64 = 8 * 1024 * 1024;

/// Enough to fill a column several screens deep, and short of the answer to
/// `read_dir` on `.git` or `node_modules` being a reply nobody reads.
const BROWSE_LIMIT: usize = 500;

pub(super) fn browse_limit(requested: Option<usize>) -> usize {
    requested.unwrap_or(BROWSE_LIMIT).clamp(1, BROWSE_LIMIT)
}

/// What one directory holds, as far as this endpoint is concerned. Split out from the
/// handler so the rules below can be tested against a real directory without standing a
/// manager and a router up around them.
struct Listing {
    dirs: Vec<String>,
    files: Vec<String>,
    truncated: bool,
}

async fn list_dir(
    base: &std::path::Path,
    want_files: bool,
    hidden: bool,
    limit: usize,
) -> std::io::Result<Listing> {
    let mut out = Listing {
        dirs: Vec::new(),
        files: Vec::new(),
        truncated: false,
    };

    let mut rd = tokio::fs::read_dir(base).await?;
    while let Some(e) = rd.next_entry().await? {
        let name = e.file_name().to_string_lossy().into_owned();
        if !hidden && name.starts_with('.') {
            continue;
        }

        let t = e.file_type().await?;
        // `DirEntry::file_type` is an lstat: a symlink is a symlink and never the thing it
        // points at, so a symlinked directory would come out neither a dir nor a file and
        // read in the tree as one that had gone. One stat per link, and a broken one falls
        // out of both lists rather than being guessed at.
        let is_dir = if t.is_symlink() {
            match tokio::fs::metadata(e.path()).await {
                Ok(m) => m.is_dir(),
                Err(_) => continue,
            }
        } else {
            t.is_dir()
        };

        if !is_dir && !want_files {
            // Not asked for, so not counted either: a directory holding three folders and
            // ten thousand files is three rows, not a truncated answer.
            continue;
        }

        let count = out.dirs.len() + out.files.len();
        if count >= limit {
            // This qualifying entry is the evidence that the answer really was cut short.
            // Merely reaching the limit is not: a directory may contain exactly that many.
            out.truncated = true;
            break;
        }

        if is_dir {
            out.dirs.push(name);
        } else {
            out.files.push(name);
        }
    }

    out.dirs.sort();
    out.files.sort();
    Ok(out)
}

/// So the dialog can pick a project dir, and the files view can draw a tree, without
/// the mod touching the host filesystem itself. `dirs` is what it always was; `files`
/// is asked for.
pub(super) async fn browse(State(_m): State<Mgr>, Query(q): Query<BrowseReq>) -> ApiResult {
    let base = if q.path.is_empty() {
        dirs::home_dir().unwrap_or_else(|| "/".into())
    } else {
        std::path::PathBuf::from(crate::config::expand(&q.path))
    };

    let limit = browse_limit(q.limit);
    let out = list_dir(&base, q.files, q.hidden, limit)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;

    Ok(Json(json!({
        "path": base,
        "parent": base.parent(),
        "dirs": out.dirs,
        "files": out.files,
        "truncated": out.truncated,
    })))
}

pub(super) async fn read_file(State(_m): State<Mgr>, Query(q): Query<ReadReq>) -> ApiResult {
    let path = q.path.trim();
    if path.is_empty() {
        return Err(err(StatusCode::BAD_REQUEST, "no path"));
    }

    let path = std::path::PathBuf::from(crate::config::expand(path));
    let text = read_preview(&path)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({
        "path": path,
        "text": text,
        "bytes": text.len(),
    })))
}

const HIGHLIGHT_LIMIT: usize = READ_LIMIT as usize * 4;
const HIGHLIGHT_TIMEOUT: Duration = Duration::from_secs(3);

pub(super) async fn highlight(State(m): State<Mgr>, Json(q): Json<HighlightReq>) -> ApiResult {
    if q.text.len() as u64 > READ_LIMIT {
        return Err(err(
            StatusCode::BAD_REQUEST,
            "code is larger than the preview limit",
        ));
    }
    let command = m.config().await.commands.highlighter;
    let text = highlight_text(&command, &q.language, &q.text)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({ "text": text, "bytes": text.len() })))
}

async fn highlight_text(command: &str, language: &str, text: &str) -> anyhow::Result<String> {
    if command.trim().is_empty() {
        bail!("syntax highlighter is disabled");
    }

    let dir = std::path::PathBuf::from(crate::config::temp_dir("highlight"));
    tokio::fs::create_dir_all(&dir).await?;
    let extension: String = language
        .trim()
        .trim_start_matches('.')
        .chars()
        .take_while(|c| c.is_ascii_alphanumeric() || matches!(c, '+' | '-' | '_'))
        .take(24)
        .collect();
    let extension = if extension.is_empty() {
        "txt"
    } else {
        &extension
    };
    let path = dir.join(format!("{}.{}", uuid::Uuid::new_v4(), extension));
    tokio::fs::write(&path, text).await?;

    let result = run_highlighter(command, &path).await;
    let _ = tokio::fs::remove_file(&path).await;
    result
}

fn highlighter_argv(command: &str, path: &std::path::Path) -> anyhow::Result<Vec<String>> {
    let mut argv = crate::sandbox::shell_split(command);
    if argv.is_empty() {
        bail!("syntax highlighter is disabled");
    }
    let path = path
        .to_str()
        .ok_or_else(|| anyhow::anyhow!("highlight path is not valid UTF-8"))?;
    let mut replaced = false;
    for arg in &mut argv {
        if arg.contains("%s") {
            *arg = arg.replace("%s", path);
            replaced = true;
        }
    }
    if !replaced {
        argv.push(path.to_string());
    }
    Ok(argv)
}

async fn run_highlighter(command: &str, path: &std::path::Path) -> anyhow::Result<String> {
    let argv = highlighter_argv(command, path)?;
    let mut child = Command::new(&argv[0])
        .args(&argv[1..])
        .stdin(Stdio::null())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .kill_on_drop(true)
        .spawn()
        .context("starting syntax highlighter")?;
    let stdout = child
        .stdout
        .take()
        .context("capturing highlighter stdout")?;
    let stderr = child
        .stderr
        .take()
        .context("capturing highlighter stderr")?;

    let collected = tokio::time::timeout(HIGHLIGHT_TIMEOUT, async {
        let out = read_highlight_output(stdout, HIGHLIGHT_LIMIT);
        let err = read_highlight_output(stderr, 16 * 1024);
        let status = child.wait();
        let (out, err, status) = tokio::join!(out, err, status);
        Ok::<_, anyhow::Error>((out?, err?, status?))
    })
    .await;
    let (stdout, stderr, status) = match collected {
        Ok(result) => result?,
        Err(_) => {
            let _ = child.kill().await;
            let _ = child.wait().await;
            bail!("syntax highlighter timed out");
        }
    };
    if stdout.1 {
        bail!("syntax highlighter output exceeded the preview limit");
    }
    if !status.success() {
        bail!(
            "syntax highlighter failed: {}",
            String::from_utf8_lossy(&stderr.0).trim()
        );
    }
    String::from_utf8(stdout.0).context("syntax highlighter output is not valid UTF-8")
}

async fn read_highlight_output<R: tokio::io::AsyncRead + Unpin>(
    reader: R,
    limit: usize,
) -> anyhow::Result<(Vec<u8>, bool)> {
    let mut bytes = Vec::new();
    reader
        .take(limit as u64 + 1)
        .read_to_end(&mut bytes)
        .await?;
    let truncated = bytes.len() > limit;
    if truncated {
        bytes.truncate(limit);
    }
    Ok((bytes, truncated))
}

pub(super) async fn read_image(State(_m): State<Mgr>, Query(q): Query<ReadReq>) -> ApiResult {
    let path = q.path.trim();
    if path.is_empty() {
        return Err(err(StatusCode::BAD_REQUEST, "no path"));
    }

    let path = std::path::PathBuf::from(crate::config::expand(path));
    let bytes = read_image_bytes(&path)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({
        "path": path,
        "data": BASE64.encode(&bytes),
        "bytes": bytes.len(),
    })))
}

pub(super) async fn read_image_bytes(path: &std::path::Path) -> anyhow::Result<Vec<u8>> {
    let metadata = tokio::fs::metadata(path).await?;
    if !metadata.is_file() {
        bail!("path is not a file");
    }
    if metadata.len() > IMAGE_LIMIT {
        bail!(
            "image is larger than the {} MiB preview limit",
            IMAGE_LIMIT / (1024 * 1024)
        );
    }

    let bytes = tokio::fs::read(path).await?;
    if bytes.len() as u64 > IMAGE_LIMIT {
        bail!(
            "image grew beyond the {} MiB preview limit",
            IMAGE_LIMIT / (1024 * 1024)
        );
    }
    Ok(bytes)
}

pub(super) async fn read_preview(path: &std::path::Path) -> anyhow::Result<String> {
    let metadata = tokio::fs::metadata(path).await?;
    if !metadata.is_file() {
        bail!("path is not a file");
    }
    if metadata.len() > READ_LIMIT {
        bail!(
            "file is larger than the {} KiB preview limit",
            READ_LIMIT / 1024
        );
    }

    let mut file = tokio::fs::File::open(path).await?;
    let mut bytes = Vec::with_capacity(metadata.len() as usize);
    file.read_to_end(&mut bytes).await?;
    if bytes.len() as u64 > READ_LIMIT {
        bail!(
            "file grew beyond the {} KiB preview limit",
            READ_LIMIT / 1024
        );
    }

    String::from_utf8(bytes).context("file is not valid UTF-8")
}

pub(super) fn file_path(path: &str) -> anyhow::Result<std::path::PathBuf> {
    let path = crate::config::expand(path);
    if path.trim().is_empty() {
        bail!("no path");
    }

    let path = std::path::PathBuf::from(path);
    if !path.is_absolute() {
        bail!("path must be absolute");
    }
    Ok(path)
}

pub(super) fn entry_name(name: &str) -> anyhow::Result<&str> {
    let name = name.trim();
    if name.is_empty() || name == "." || name == ".." {
        bail!("name is empty");
    }
    if name.contains('/') || name.contains('\\') || name.contains('\0') {
        bail!("name must be one path component");
    }
    Ok(name)
}

pub(super) async fn create_file(Json(q): Json<FileReq>) -> ApiResult {
    let parent = file_path(&q.path).map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    let name = entry_name(&q.name).map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    let meta = tokio::fs::metadata(&parent)
        .await
        .with_context(|| format!("reading parent directory {}", parent.display()))
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    if !meta.is_dir() {
        return Err(err(
            StatusCode::BAD_REQUEST,
            format!("not a directory: {}", parent.display()),
        ));
    }

    let target = parent.join(name);
    if tokio::fs::symlink_metadata(&target).await.is_ok() {
        return Err(err(
            StatusCode::BAD_REQUEST,
            format!("already exists: {}", target.display()),
        ));
    }

    let result = if q.kind.trim() == "file" {
        tokio::fs::OpenOptions::new()
            .write(true)
            .create_new(true)
            .open(&target)
            .await
            .map(|_| ())
            .with_context(|| format!("creating {}", target.display()))
    } else if q.kind.trim() == "folder" {
        tokio::fs::create_dir(&target)
            .await
            .with_context(|| format!("creating {}", target.display()))
    } else {
        return Err(err(StatusCode::BAD_REQUEST, "kind must be file or folder"));
    };
    result.map_err(|e| err(StatusCode::BAD_REQUEST, e))?;

    Ok(Json(json!({ "ok": true })))
}

pub(super) async fn rename_file(Json(q): Json<FileReq>) -> ApiResult {
    let source = file_path(&q.path).map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    let name = entry_name(&q.name).map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    let parent = source
        .parent()
        .filter(|p| !p.as_os_str().is_empty())
        .ok_or_else(|| err(StatusCode::BAD_REQUEST, "cannot rename this path"))?;
    tokio::fs::symlink_metadata(&source)
        .await
        .with_context(|| format!("reading {}", source.display()))
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;

    let target = parent.join(name);
    if tokio::fs::symlink_metadata(&target).await.is_ok() {
        return Err(err(
            StatusCode::BAD_REQUEST,
            format!("already exists: {}", target.display()),
        ));
    }
    tokio::fs::rename(&source, &target)
        .await
        .with_context(|| format!("renaming {}", source.display()))
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({ "ok": true })))
}

pub(super) async fn remove_file(Json(q): Json<FileReq>) -> ApiResult {
    let path = file_path(&q.path).map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    let meta = tokio::fs::symlink_metadata(&path)
        .await
        .with_context(|| format!("reading {}", path.display()))
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    if path.parent().is_none() {
        return Err(err(StatusCode::BAD_REQUEST, "cannot remove this path"));
    }

    if meta.is_dir() {
        tokio::fs::remove_dir_all(&path)
            .await
            .with_context(|| format!("removing {}", path.display()))
            .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    } else {
        tokio::fs::remove_file(&path)
            .await
            .with_context(|| format!("removing {}", path.display()))
            .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    }
    Ok(Json(json!({ "ok": true })))
}

const SEARCH_LIMIT: usize = 200;
// `rg --max-columns` controls its human output, but JSON match records still carry the whole
// line. A generated or minified asset can therefore turn one sidebar row into tens of thousands
// of glyphs. Keep the answer a preview, centred near the first match when there is room.
const SEARCH_TEXT_LIMIT: usize = 1_000;

pub(super) fn search_preview(text: &str, column: usize) -> String {
    let text = text.trim_end_matches(['\r', '\n']);
    if text.len() <= SEARCH_TEXT_LIMIT {
        return text.to_string();
    }

    // `column` is a byte offset from ripgrep. Do not slice through a UTF-8 codepoint even when
    // a match began at a non-ASCII character.
    let boundary = |at: usize| {
        let mut at = at.min(text.len());
        while at > 0 && !text.is_char_boundary(at) {
            at -= 1;
        }
        at
    };
    let match_at = boundary(column.saturating_sub(1));
    let begin = boundary(match_at.saturating_sub(SEARCH_TEXT_LIMIT / 2));
    let end = boundary((begin + SEARCH_TEXT_LIMIT).min(text.len()));
    let mut out = String::with_capacity(end - begin + 6);
    if begin > 0 {
        out.push('…');
    }
    out.push_str(&text[begin..end]);
    if end < text.len() {
        out.push('…');
    }
    out
}

pub(super) fn build_rg_command(path: &std::path::Path, q: &SearchReq) -> tokio::process::Command {
    let mut cmd = tokio::process::Command::new("rg");
    cmd.current_dir(path)
        .arg("--json")
        .arg("--line-number")
        .arg("--color=never")
        .arg("--max-columns=1000")
        .arg("--max-columns-preview")
        .arg("--no-messages");
    if !q.regex {
        cmd.arg("--fixed-strings");
    }
    if q.case {
        cmd.arg("--case-sensitive");
    } else {
        cmd.arg("--ignore-case");
    }
    if q.word {
        cmd.arg("--word-regexp");
    }
    if !q.gitignore {
        cmd.arg("--no-ignore");
    }
    if q.hidden {
        cmd.arg("--hidden");
    }
    cmd.arg("--glob=!.git/**")
        .arg("--")
        .arg(&q.q)
        .arg(".")
        .stdout(std::process::Stdio::piped())
        .stderr(std::process::Stdio::null())
        .kill_on_drop(true);
    cmd
}

/// Search one project without putting a pattern or path through a shell. `rg --json` keeps
/// filenames and matching text unambiguous; reading it a line at a time lets the endpoint
/// stop the process once the UI-sized answer is full rather than collecting an unbounded
/// repository search in memory.
pub(super) async fn search(State(_m): State<Mgr>, Query(q): Query<SearchReq>) -> ApiResult {
    use tokio::io::{AsyncBufReadExt, BufReader};

    if q.path.is_empty() {
        return Err(err(StatusCode::BAD_REQUEST, "no path"));
    }
    if q.q.is_empty() {
        return Err(err(StatusCode::BAD_REQUEST, "no query"));
    }

    let dir = std::path::PathBuf::from(crate::config::expand(&q.path));
    let limit = q.limit.unwrap_or(SEARCH_LIMIT).clamp(1, SEARCH_LIMIT);

    let mut child = build_rg_command(&dir, &q)
        .spawn()
        .map_err(|e| err(StatusCode::BAD_REQUEST, format!("could not run rg: {e}")))?;
    let stdout = child.stdout.take().ok_or_else(|| {
        err(
            StatusCode::INTERNAL_SERVER_ERROR,
            "could not read rg output",
        )
    })?;
    let mut lines = BufReader::new(stdout).lines();
    let mut matches = Vec::new();
    let mut truncated = false;

    while let Some(line) = lines
        .next_line()
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?
    {
        let Ok(v) = serde_json::from_str::<Value>(&line) else {
            continue;
        };
        if v["type"] != "match" {
            continue;
        }
        if matches.len() >= limit {
            truncated = true;
            let _ = child.kill().await;
            break;
        }

        let data = &v["data"];
        let path = data["path"]["text"]
            .as_str()
            .unwrap_or("")
            .trim_start_matches("./");
        let text = data["lines"]["text"].as_str().unwrap_or("");
        let column = data["submatches"][0]["start"].as_u64().unwrap_or(0) as usize + 1;
        matches.push(json!({
            "path": path,
            "line": data["line_number"].as_u64().unwrap_or(0),
            "column": column,
            "text": search_preview(text, column),
        }));
    }

    if !truncated {
        let status = child
            .wait()
            .await
            .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
        // ripgrep uses 1 for a clean search with no matches.
        if !status.success() && status.code() != Some(1) {
            return Err(err(
                StatusCode::BAD_REQUEST,
                format!("rg exited with {status}"),
            ));
        }
    }

    Ok(Json(json!({
        "path": dir,
        "matches": matches,
        "truncated": truncated,
    })))
}

/// Returns a project's changed files for the git view; the game cannot run `git` inside agent
/// mount namespaces. Non-repositories return 200 with `repo: false`.
pub(super) async fn git_status(State(_m): State<Mgr>, Query(q): Query<GitReq>) -> ApiResult {
    if q.path.is_empty() {
        return Err(err(StatusCode::BAD_REQUEST, "no path"));
    }
    let dir = std::path::PathBuf::from(crate::config::expand(&q.path));

    let out = crate::git::status(&dir)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;

    let Some(st) = out else {
        return Ok(Json(json!({ "repo": false, "path": dir })));
    };

    Ok(Json(json!({
        "repo": true,
        "root": st.root,
        "branch": st.branch,
        "changed": st.changed,
        "added": st.added,
        "deleted": st.deleted,
        "truncated": st.truncated,
        "files": st.changes.iter().map(|c| json!({
            "path": c.path,
            "status": c.status,
            "added": c.added,
            "deleted": c.deleted,
        })).collect::<Vec<_>>(),
    })))
}

#[cfg(test)]
mod tests {
    use std::path::{Path, PathBuf};

    use super::{
        browse_limit, entry_name, file_path, highlighter_argv, list_dir, read_highlight_output,
        read_image_bytes, read_preview, search_preview, source, valid_kind, SearchReq, IMAGE_LIMIT,
        READ_LIMIT, SEARCH_TEXT_LIMIT,
    };
    use axum::http::StatusCode;

    /// Somewhere of our own under the machine's temp dir, cleared on the way in so a run
    /// that died before its cleanup does not poison the next one. No dev-dependency for
    /// this: one directory of empty files is not worth a crate.
    pub(super) fn fixture(tag: &str) -> PathBuf {
        let dir = std::env::temp_dir().join(format!("slopd-browse-{tag}"));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        dir
    }

    pub(super) fn touch(dir: &Path, name: &str) {
        std::fs::write(dir.join(name), b"").unwrap();
    }

    pub(super) fn subdir(dir: &Path, name: &str) {
        std::fs::create_dir_all(dir.join(name)).unwrap();
    }

    /// The dir picker asked for none, so it is handed none, and a directory full of files
    /// is still just its directories - which is also what keeps the cap off it.
    #[tokio::test]
    pub(super) async fn files_are_opt_in() {
        let dir = fixture("optin");
        subdir(&dir, "src");
        touch(&dir, "Cargo.toml");
        touch(&dir, "README.md");

        let quiet = list_dir(&dir, false, false, 500).await.unwrap();
        assert_eq!(quiet.dirs, ["src"]);
        assert!(quiet.files.is_empty());

        let full = list_dir(&dir, true, false, 500).await.unwrap();
        assert_eq!(full.dirs, ["src"]);
        assert_eq!(full.files, ["Cargo.toml", "README.md"]);

        let _ = std::fs::remove_dir_all(&dir);
    }

    #[tokio::test]
    pub(super) async fn preview_reads_utf8_and_rejects_oversized_files() {
        let dir = fixture("preview");
        let path = dir.join("README.md");
        std::fs::write(&path, "# hello\n\nworld\n").unwrap();
        assert_eq!(read_preview(&path).await.unwrap(), "# hello\n\nworld\n");

        let large = dir.join("large.md");
        std::fs::write(&large, vec![b'x'; READ_LIMIT as usize + 1]).unwrap();
        let error = read_preview(&large).await.unwrap_err().to_string();
        assert!(error.contains("preview limit"));

        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    pub(super) fn highlighter_command_appends_or_expands_the_file() {
        let path = Path::new("/tmp/slopworld/highlight/code file.rs");
        assert_eq!(
            highlighter_argv("highlight --out-format=xterm256", path).unwrap(),
            [
                "highlight",
                "--out-format=xterm256",
                "/tmp/slopworld/highlight/code file.rs"
            ]
        );
        assert_eq!(
            highlighter_argv("tool --file=%s", path).unwrap(),
            ["tool", "--file=/tmp/slopworld/highlight/code file.rs"]
        );
        assert!(highlighter_argv("   ", path).is_err());
    }

    #[tokio::test]
    pub(super) async fn highlighted_output_is_bounded_without_losing_short_output() {
        let (bytes, truncated) = read_highlight_output(&b"hello"[..], 5).await.unwrap();
        assert_eq!(bytes, b"hello");
        assert!(!truncated);

        let (bytes, truncated) = read_highlight_output(&b"hello!"[..], 5).await.unwrap();
        assert_eq!(bytes, b"hello");
        assert!(truncated);
    }

    #[test]
    pub(super) fn preset_source_and_kind_errors_are_explicit() {
        assert_eq!(source(true, true), "override");
        assert_eq!(source(true, false), "system");
        assert_eq!(source(false, true), "user");
        assert_eq!(source(false, false), "unknown");
        assert!(valid_kind("sandbox").is_ok());
        assert!(valid_kind("command").is_ok());
        let (status, body) = valid_kind("other").unwrap_err();
        assert_eq!(status, StatusCode::BAD_REQUEST);
        assert!(body.0["error"]
            .as_str()
            .unwrap()
            .contains("unknown preset kind"));
    }

    #[tokio::test]
    pub(super) async fn image_reads_bytes_and_rejects_oversized_files() {
        let dir = fixture("image");
        let path = dir.join("work.png");
        let bytes = vec![137u8, 80, 78, 71];
        std::fs::write(&path, &bytes).unwrap();
        assert_eq!(read_image_bytes(&path).await.unwrap(), bytes);

        let large = dir.join("large.png");
        std::fs::write(&large, vec![0u8; IMAGE_LIMIT as usize + 1]).unwrap();
        let error = read_image_bytes(&large).await.unwrap_err().to_string();
        assert!(error.contains("image") && error.contains("limit"));

        let _ = std::fs::remove_dir_all(&dir);
    }

    /// Both lists, and a dot directory is as hidden as a dot file.
    #[tokio::test]
    pub(super) async fn dotfiles_are_hidden_until_they_are_asked_for() {
        let dir = fixture("hidden");
        subdir(&dir, "src");
        subdir(&dir, ".git");
        touch(&dir, "main.rs");
        touch(&dir, ".gitignore");

        let shy = list_dir(&dir, true, false, 500).await.unwrap();
        assert_eq!(shy.dirs, ["src"]);
        assert_eq!(shy.files, ["main.rs"]);

        let all = list_dir(&dir, true, true, 500).await.unwrap();
        assert_eq!(all.dirs, [".git", "src"]);
        assert_eq!(all.files, [".gitignore", "main.rs"]);

        let _ = std::fs::remove_dir_all(&dir);
    }

    /// `DirEntry::file_type` does not follow a symlink, so without the stat behind it a
    /// linked directory is in neither list and the tree draws it as gone. A link to
    /// nowhere stays in neither, which is the one case where that is the right answer.
    #[cfg(unix)]
    #[tokio::test]
    pub(super) async fn a_symlinked_directory_is_a_directory() {
        let dir = fixture("links");
        subdir(&dir, "real");
        touch(&dir, "file.txt");
        std::os::unix::fs::symlink(dir.join("real"), dir.join("to-dir")).unwrap();
        std::os::unix::fs::symlink(dir.join("file.txt"), dir.join("to-file")).unwrap();
        std::os::unix::fs::symlink(dir.join("nowhere"), dir.join("dangling")).unwrap();

        let out = list_dir(&dir, true, false, 500).await.unwrap();
        assert_eq!(out.dirs, ["real", "to-dir"]);
        assert_eq!(out.files, ["file.txt", "to-file"]);

        let _ = std::fs::remove_dir_all(&dir);
    }

    /// The cap says so rather than lying about a short directory, and it is a cap on what
    /// was read - the answer is that many entries, not that many sorted ones.
    #[tokio::test]
    pub(super) async fn a_long_directory_is_cut_short_and_says_so() {
        let dir = fixture("cap");
        for i in 0..20 {
            touch(&dir, &format!("f{i:02}"));
        }

        let capped = list_dir(&dir, true, false, 5).await.unwrap();
        assert_eq!(capped.dirs.len() + capped.files.len(), 5);
        assert!(capped.truncated);

        let whole = list_dir(&dir, true, false, 500).await.unwrap();
        assert_eq!(whole.files.len(), 20);
        assert!(!whole.truncated);

        let _ = std::fs::remove_dir_all(&dir);
    }

    #[tokio::test]
    pub(super) async fn exactly_the_limit_is_not_truncated() {
        let dir = fixture("exact-cap");
        for i in 0..5 {
            touch(&dir, &format!("f{i:02}"));
        }

        let exact = list_dir(&dir, true, false, 5).await.unwrap();
        assert_eq!(exact.files.len(), 5);
        assert!(!exact.truncated);

        let dirs = fixture("exact-dir-cap");
        for i in 0..5 {
            subdir(&dirs, &format!("d{i:02}"));
        }
        touch(&dirs, "ignored-file");
        let exact_dirs = list_dir(&dirs, false, false, 5).await.unwrap();
        assert_eq!(exact_dirs.dirs.len(), 5);
        assert!(!exact_dirs.truncated);

        let _ = std::fs::remove_dir_all(&dir);
        let _ = std::fs::remove_dir_all(&dirs);
    }

    #[test]
    pub(super) fn browse_limit_is_bounded() {
        assert_eq!(browse_limit(None), 500);
        assert_eq!(browse_limit(Some(0)), 1);
        assert_eq!(browse_limit(Some(12)), 12);
        assert_eq!(browse_limit(Some(usize::MAX)), 500);
    }

    #[test]
    pub(super) fn file_names_are_one_component() {
        assert_eq!(entry_name("note.md").unwrap(), "note.md");
        assert!(entry_name("").is_err());
        assert!(entry_name(".").is_err());
        assert!(entry_name("src/note.md").is_err());
        assert!(entry_name("src\\note.md").is_err());
    }

    #[test]
    pub(super) fn file_paths_keep_whitespace_in_a_real_filename() {
        assert_eq!(
            file_path("/tmp/ notes.txt ").unwrap(),
            Path::new("/tmp/ notes.txt ")
        );
    }

    #[test]
    pub(super) fn search_preview_caps_a_long_line_around_its_match() {
        let text = format!("{}test{}", "x".repeat(2_000), "y".repeat(2_000));
        let preview = search_preview(&text, 2_001);

        assert!(preview.contains("test"));
        assert!(preview.starts_with('…'));
        assert!(preview.ends_with('…'));
        assert!(preview.len() <= SEARCH_TEXT_LIMIT + 6);
    }

    #[test]
    pub(super) fn search_preview_preserves_utf8_boundaries() {
        let text = format!("{}test{}", "é".repeat(800), "é".repeat(800));
        let preview = search_preview(&text, 1_601);

        assert!(preview.contains("test"));
        assert!(std::str::from_utf8(preview.as_bytes()).is_ok());
    }

    #[test]
    pub(super) fn search_filters_gitignored_files_by_default() {
        let missing: Result<SearchReq, _> = serde_json::from_value(serde_json::json!({
            "path": "/tmp/project",
            "q": "needle"
        }));
        assert!(missing.is_err());

        let respect: SearchReq = serde_json::from_value(serde_json::json!({
            "path": "/tmp/project",
            "q": "needle",
            "gitignore": "1"
        }))
        .unwrap();
        assert!(respect.gitignore);

        let include: SearchReq = serde_json::from_value(serde_json::json!({
            "path": "/tmp/project",
            "q": "needle",
            "gitignore": "0"
        }))
        .unwrap();
        assert!(!include.gitignore);
    }
}
