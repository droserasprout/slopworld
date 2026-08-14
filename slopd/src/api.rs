use std::collections::HashMap;
use std::sync::Arc;
use std::time::Duration;

use anyhow::{bail, Context};
use axum::extract::ws::{Message, WebSocket, WebSocketUpgrade};
use axum::extract::{Path, Query, Request, State};
use axum::http::{HeaderMap, StatusCode};
use axum::middleware::{self, Next};
use axum::response::{IntoResponse, Response};
use axum::routing::{delete, get, post, put};
use axum::{Extension, Json, Router};

use crate::grant::{Cap, Level};
use futures::{SinkExt, StreamExt};
use serde::Deserialize;
use serde_json::{json, Value};
use tokio::sync::Mutex;

use crate::config::{ProjectCfg, SessionCfg, ShortcutCfg};
use crate::session::{Event, Manager, RunWhere, WatchGuard};

type Mgr = Arc<Manager>;

pub fn router(m: Mgr) -> Router {
    // Reachable by a scoped grant as well as the root. Each handler checks its own session and
    // level (`guard`), the list is filtered, and `/ws` gates every message - so a grant reaches
    // exactly the sessions it names, at the level it was given, and the host through none of it.
    // Creating a session (`POST /api/sessions`) is root-only even here, by `guard_create`.
    let scoped = Router::new()
        .route("/api/health", get(health))
        .route("/api/sessions", get(list).post(create))
        .route("/api/sessions/:name", get(one).put(update).delete(destroy))
        .route("/api/sessions/:name/start", post(start))
        .route("/api/sessions/:name/stop", post(stop))
        .route("/api/sessions/:name/restart", post(restart))
        .route("/api/sessions/:name/state/reset", post(reset_state))
        .route(
            "/api/tasks",
            get(list_tasks).post(create_task).delete(prune_tasks),
        )
        .route(
            "/api/tasks/:id",
            get(one_task).post(update_task).delete(remove_task),
        )
        .route("/ws", get(ws_upgrade));

    // The mod's own: the file, the projects, the machine, and minting grants itself. Default
    // deny - a grant reaches none of it, and a route added here is root-only until someone
    // moves it into `scoped` on purpose. The layer reads the capability `auth` already hung on
    // the request.
    let root = Router::new()
        .route("/api/projects", get(list_projects).post(create_project))
        .route(
            "/api/projects/:name",
            get(one_project).put(update_project).delete(destroy_project),
        )
        .route("/api/shortcuts", get(list_shortcuts).post(create_shortcut))
        .route(
            "/api/shortcuts/:name",
            put(update_shortcut).delete(destroy_shortcut),
        )
        .route("/api/shortcuts/:name/run", post(run_shortcut))
        .route("/api/run", post(run))
        .route("/api/file-action", post(file_action))
        .route("/api/grants", get(list_grants).post(mint_grant))
        .route("/api/grants/:grantor", delete(revoke_grants))
        .route("/api/state", get(stored_states))
        .route("/api/state/:kind/:key", delete(delete_stored_state))
        .route("/api/state/trash/:key/restore", post(restore_stored_state))
        .route("/api/presets", get(presets))
        .route(
            "/api/presets/:kind/:name",
            put(update_preset).delete(delete_preset),
        )
        .route("/api/presets/:kind/:name/copy", post(copy_preset))
        .route("/api/config", get(get_config))
        .route("/api/config", put(put_config))
        .route("/api/config/patch", put(put_config_patch))
        .route("/api/clipboard", get(clip_read).post(clip_write))
        .route("/api/open", post(open_url))
        .route("/api/usage", get(usage))
        .route("/api/audio", get(audio))
        .route("/api/jukebox", get(jukebox))
        .route("/api/browse", get(browse))
        .route(
            "/api/files",
            post(create_file).put(rename_file).delete(remove_file),
        )
        .route("/api/search", get(search))
        .route("/api/git", get(git_status))
        .route("/api/game", get(game))
        .route("/api/game/restart", post(restart_game))
        .layer(middleware::from_fn(require_root));

    scoped.merge(root).with_state(m)
}

/// The default-deny half of the API: everything but the session read/drive routes is the mod's,
/// so a scoped grant gets 403 here before a handler runs. The capability was resolved and hung
/// on the request by `auth`, which is the outer layer, so it is always present by now.
async fn require_root(Extension(cap): Extension<Cap>, req: Request, next: Next) -> Response {
    if cap.may_create() {
        next.run(req).await
    } else {
        err(
            StatusCode::FORBIDDEN,
            "this route needs the daemon's own token",
        )
        .into_response()
    }
}

fn err(code: StatusCode, e: impl std::fmt::Display) -> (StatusCode, Json<serde_json::Value>) {
    (code, Json(json!({ "error": e.to_string() })))
}

/// The token a request presents, if any. What `Manager::resolve_cap` weighs against the root
/// and the live grants. Shared with the `/ws` upgrade, which reads it itself because the header
/// rides only on the upgrade request.
pub fn presented_token(headers: &HeaderMap) -> Option<String> {
    headers
        .get("x-slop-token")
        .and_then(|v| v.to_str().ok())
        .map(str::to_string)
}

type ApiResult = Result<Json<serde_json::Value>, (StatusCode, Json<serde_json::Value>)>;

fn ok_json(r: anyhow::Result<()>) -> ApiResult {
    r.map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({ "ok": true })))
}

/// 403 unless `cap` may touch `name` at `need` - the check every per-session route makes. Root
/// passes everything; a grant passes only a session it names, and never the host. 403 rather
/// than 404, since a scoped caller has no business learning whether the name it cannot touch
/// exists (a missing name and a forbidden one read the same to it).
async fn guard(
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
fn guard_create(cap: &Cap) -> Result<(), (StatusCode, Json<serde_json::Value>)> {
    if cap.may_create() {
        Ok(())
    } else {
        Err(err(
            StatusCode::FORBIDDEN,
            "only the daemon's own token may create sessions",
        ))
    }
}

async fn health(State(m): State<Mgr>) -> ApiResult {
    Ok(Json(json!({
        "ok": true,
        "version": env!("CARGO_PKG_VERSION"),
        "tmux_socket": m.config().await.daemon.tmux_socket,
    })))
}

fn task_principal(cap: &Cap, headers: &HeaderMap) -> Result<String, (StatusCode, Json<Value>)> {
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
async fn task_endpoint_known(m: &Mgr, who: &str) -> bool {
    who == crate::tasks::HOST || m.session_known(who).await
}

#[derive(Deserialize)]
struct CreateTaskReq {
    to: String,
    body: String,
}

async fn create_task(
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

async fn list_tasks(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    headers: HeaderMap,
) -> ApiResult {
    let who = task_principal(&cap, &headers)?;
    Ok(Json(json!({ "tasks": m.tasks_for(&who) })))
}

async fn one_task(
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

#[derive(Deserialize)]
struct UpdateTaskReq {
    status: crate::tasks::Status,
    #[serde(default)]
    note: Option<String>,
}

async fn update_task(
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

#[derive(Deserialize)]
struct PruneTasksQuery {
    /// Every finished task in the store rather than the caller's own. Root-only: it reaches
    /// mailboxes the caller is not party to.
    #[serde(default)]
    all: bool,
}

async fn prune_tasks(
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

async fn remove_task(
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

async fn list(State(m): State<Mgr>, Extension(cap): Extension<Cap>) -> ApiResult {
    // Filtered to what the caller may see: a grant lists the sessions it names and no others, so
    // a name it cannot touch is a name it never learns. Host-ness is not carried on a view and
    // is not needed here - a grant never names a host session, so `can_see` refuses it by
    // membership alone.
    let sessions: Vec<_> = m
        .views()
        .await
        .into_iter()
        .filter(|s| cap.can_see(&s.name, false))
        .collect();
    Ok(Json(json!({ "sessions": sessions })))
}

async fn one(
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

async fn create(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    Json(s): Json<SessionCfg>,
) -> ApiResult {
    guard_create(&cap)?;
    ok_json(m.add(s).await)
}

async fn update(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    Path(name): Path<String>,
    Json(s): Json<SessionCfg>,
) -> ApiResult {
    guard(&m, &cap, &name, Level::Rw).await?;
    ok_json(m.update(&name, s).await)
}

async fn destroy(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    Path(name): Path<String>,
) -> ApiResult {
    guard(&m, &cap, &name, Level::Rw).await?;
    ok_json(m.remove(&name).await)
}

async fn start(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    Path(name): Path<String>,
) -> ApiResult {
    guard(&m, &cap, &name, Level::Rw).await?;
    ok_json(m.start(&name).await)
}

async fn stop(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    Path(name): Path<String>,
) -> ApiResult {
    guard(&m, &cap, &name, Level::Rw).await?;
    ok_json(m.stop(&name).await)
}

async fn reset_state(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    Path(name): Path<String>,
) -> ApiResult {
    guard(&m, &cap, &name, Level::Rw).await?;
    ok_json(m.reset_state(&name).await)
}

async fn stored_states(State(m): State<Mgr>) -> ApiResult {
    m.stored_states()
        .await
        .map(|entries| Json(json!({ "entries": entries })))
        .map_err(|e| err(StatusCode::INTERNAL_SERVER_ERROR, e))
}

async fn delete_stored_state(
    State(m): State<Mgr>,
    Path((kind, key)): Path<(String, String)>,
) -> ApiResult {
    ok_json(m.delete_stored_state(&kind, &key).await)
}

async fn restore_stored_state(State(m): State<Mgr>, Path(key): Path<String>) -> ApiResult {
    m.restore_stored_state(&key)
        .await
        .map(|session| Json(json!({ "ok": true, "session": session })))
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))
}

async fn restart(
    State(m): State<Mgr>,
    Extension(cap): Extension<Cap>,
    Path(name): Path<String>,
) -> ApiResult {
    guard(&m, &cap, &name, Level::Rw).await?;
    ok_json(m.restart(&name).await)
}

async fn list_projects(State(m): State<Mgr>) -> ApiResult {
    Ok(Json(json!({ "projects": m.projects().await })))
}

async fn one_project(State(m): State<Mgr>, Path(name): Path<String>) -> ApiResult {
    m.projects()
        .await
        .into_iter()
        .find(|p| p.name == name)
        .map(|p| Json(json!(p)))
        .ok_or_else(|| err(StatusCode::NOT_FOUND, format!("no such project: {name}")))
}

async fn create_project(State(m): State<Mgr>, Json(p): Json<ProjectCfg>) -> ApiResult {
    ok_json(m.add_project(p).await)
}

async fn update_project(
    State(m): State<Mgr>,
    Path(name): Path<String>,
    Json(p): Json<ProjectCfg>,
) -> ApiResult {
    ok_json(m.update_project(&name, p).await)
}

async fn destroy_project(State(m): State<Mgr>, Path(name): Path<String>) -> ApiResult {
    ok_json(m.remove_project(&name).await)
}

async fn list_shortcuts(State(m): State<Mgr>) -> ApiResult {
    Ok(Json(json!({ "shortcuts": m.shortcuts().await })))
}

async fn create_shortcut(State(m): State<Mgr>, Json(sc): Json<ShortcutCfg>) -> ApiResult {
    ok_json(m.add_shortcut(sc).await)
}

async fn update_shortcut(
    State(m): State<Mgr>,
    Path(name): Path<String>,
    Json(sc): Json<ShortcutCfg>,
) -> ApiResult {
    ok_json(m.update_shortcut(&name, sc).await)
}

async fn destroy_shortcut(State(m): State<Mgr>, Path(name): Path<String>) -> ApiResult {
    ok_json(m.remove_shortcut(&name).await)
}

/// Answers with the temporary agent's name as soon as it is up; the text lands well past the
/// point this client would have given up waiting. The body says where to run -
/// `{"project":"..."}` or `{"temp":true}` - and `Option<Json<_>>` is so a bodyless curl still
/// runs the errands that already know.
async fn run_shortcut(
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

/// An errand nobody wrote down: the same temporary agent `/api/shortcuts/NAME/run` makes,
/// spelled out in the body instead of looked up. `text` is optional here where it is
/// required of an entry - `less` on a file is a command with nothing to type after it.
#[derive(Deserialize)]
struct RunReq {
    #[serde(default)]
    project: String,
    #[serde(default)]
    kind: crate::config::ShortcutKind,
    /// A preset name or a command line, read exactly as a shortcut's is.
    #[serde(default)]
    command: String,
    /// Raw selected Files-sidebar path. When present, the daemon expands it and replaces the
    /// quoted raw path in `command`, so interactive and captured file actions agree.
    #[serde(default)]
    path: String,
    /// Keep a file-action terminal open after its command exits so short output remains visible.
    #[serde(default)]
    hold: bool,
    #[serde(default)]
    text: String,
    /// Names the session and, through `slug`, the tmux session behind it. The errand's own
    /// word for itself, since there is no entry to take one from. Empty with `host` set is
    /// the one case the daemon answers instead - see `sandbox::host_session_name`.
    #[serde(default)]
    label: String,
    #[serde(default)]
    temp: bool,
    /// The game supplies loading-screen tips for `{{ random_tip }}`, already distinct: one is
    /// spent per mention, so a text with five bullets gets five different lines.
    #[serde(default)]
    random_tips: Vec<String>,
    /// Outside the sandbox: the sidebar's "Terminal (host)". Only an errand can ask - there
    /// is no such key on a session or a shortcut.
    #[serde(default)]
    host: bool,
}

async fn run(State(m): State<Mgr>, Json(q): Json<RunReq>) -> ApiResult {
    let project = q.project.trim();
    let command = if q.path.trim().is_empty() {
        q.command.trim().to_string()
    } else {
        m.file_action_command(project, &q.path, q.command.trim())
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
    let session = m
        .run_errand(sc, want, q.host)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({ "ok": true, "session": session })))
}

#[derive(Deserialize)]
struct FileActionReq {
    project: String,
    path: String,
    command: String,
}

/// Run a non-interactive Files action in the project's normal sandbox and return a small
/// result for a game message. Interactive actions use `/api/run`, since their terminal needs
/// a tmux session and a persistent screen.
async fn file_action(State(m): State<Mgr>, Json(q): Json<FileActionReq>) -> ApiResult {
    let output = m
        .file_action(&q.project, &q.path, &q.command)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({ "ok": true, "output": output })))
}

/// Mint a grant: let `grantor` watch or drive the named `sessions`. Root-only by the router,
/// so only the mod asks. The daemon refuses a host session in the scope - the one line the
/// whole scheme is for - and refuses a grantor or target that is not there. The token comes
/// back once and is never stored on the file; losing it means minting another.
#[derive(Deserialize)]
struct GrantReq {
    grantor: String,
    #[serde(default)]
    sessions: Vec<String>,
    /// `ro` to watch, `rw` to drive.
    level: String,
}

async fn mint_grant(State(m): State<Mgr>, Json(q): Json<GrantReq>) -> ApiResult {
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
async fn list_grants(State(m): State<Mgr>) -> ApiResult {
    Ok(Json(json!({ "grants": m.grant_count().await })))
}

/// Drop every grant a session minted, by hand rather than by its exit. The mod's revoke.
async fn revoke_grants(State(m): State<Mgr>, Path(grantor): Path<String>) -> ApiResult {
    m.revoke_grants(&grantor).await;
    Ok(Json(json!({ "ok": true })))
}

/// So the GUI draws a checkbox per preset and a row per command rather than a list
/// somebody keeps in step by hand. Both tables are files, so this is also how the mod
/// learns about one that was added while it was running. `source` is explicit because a
/// merged table alone cannot tell a built-in from a user override.
async fn presets(State(_m): State<Mgr>) -> ApiResult {
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

fn source(has_builtin: bool, has_user: bool) -> &'static str {
    match (has_builtin, has_user) {
        (true, true) => "override",
        (true, false) => "system",
        (false, true) => "user",
        (false, false) => "unknown",
    }
}

fn sandbox_json(
    p: &crate::presets::SandboxPreset,
    builtins: &crate::presets::Table,
    users: &crate::presets::Table,
) -> serde_json::Value {
    json!({
        "name": p.name,
        "source": source(builtins.sandbox(&p.name).is_some(), users.sandbox(&p.name).is_some()),
        "category": p.category,
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
    })
}

fn command_json(
    c: &crate::presets::CommandPreset,
    builtins: &crate::presets::Table,
    users: &crate::presets::Table,
) -> serde_json::Value {
    json!({
        "name": c.name,
        "source": source(builtins.command(&c.name).is_some(), users.command(&c.name).is_some()),
        "category": c.category,
        "description": c.description,
        "cmd": c.cmd,
        "sandbox": c.sandbox,
    })
}

#[derive(Deserialize)]
struct CopyPresetReq {
    #[serde(default)]
    name: String,
}

fn valid_kind(kind: &str) -> Result<(), (StatusCode, Json<serde_json::Value>)> {
    if kind == "sandbox" || kind == "command" {
        Ok(())
    } else {
        Err(err(
            StatusCode::BAD_REQUEST,
            format!("unknown preset kind: {kind}"),
        ))
    }
}

async fn copy_preset(
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

async fn update_preset(
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

async fn delete_preset(
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
async fn get_config(State(m): State<Mgr>) -> ApiResult {
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

#[derive(Deserialize)]
struct ConfigReq {
    text: String,
}

async fn put_config(State(m): State<Mgr>, Json(req): Json<ConfigReq>) -> ApiResult {
    ok_json(m.replace_config(&req.text).await)
}

/// Apply only the fields named by the client, leaving unmentioned fields untouched.
async fn put_config_patch(State(m): State<Mgr>, Json(req): Json<Value>) -> ApiResult {
    ok_json(m.patch_config(req).await)
}

#[derive(Deserialize)]
struct RestartGameReq {
    /// Milliseconds to wait before launching, covering the caller's own exit.
    #[serde(default = "default_restart_delay")]
    delay_ms: u64,
}

fn default_restart_delay() -> u64 {
    4000
}

/// The mod calls this after saving, then quits: the game cannot exec itself across
/// a Unity shutdown, and slopd outlives it.
async fn restart_game(State(m): State<Mgr>, body: Option<Json<RestartGameReq>>) -> ApiResult {
    let delay = body
        .map(|Json(r)| r.delay_ms)
        .unwrap_or_else(default_restart_delay);
    ok_json(m.restart_game(delay).await)
}

/// Written for an agent inside a session, which cannot see the host's process
/// table at all - see `game.rs`.
async fn game(State(m): State<Mgr>) -> ApiResult {
    Ok(Json(json!(m.game().await)))
}

async fn usage(State(m): State<Mgr>) -> ApiResult {
    Ok(Json(json!(m.usage().await)))
}

/// What the jukebox is doing, for anything that would rather ask than listen - which in
/// practice means a person with `curl` and a suspicion. The mod hears the same thing as an
/// event; nothing needs this route to work.
async fn audio(State(m): State<Mgr>) -> ApiResult {
    Ok(Json(json!(m.audio.state())))
}

/// The station catalog for clients that draw the jukebox. URLs stay daemon-side; this carries
/// only ids, stream keys, rates and the metadata a UI may render.
async fn jukebox() -> ApiResult {
    Ok(Json(json!(crate::jukebox::catalog())))
}

#[derive(Deserialize)]
struct ClipReq {
    #[serde(default)]
    text: String,
}

/// A tool that is missing or wedged is a 502 rather than a 400, because nothing
/// about the request was wrong.
async fn clip_read() -> ApiResult {
    let text = crate::clipboard::read()
        .await
        .map_err(|e| err(StatusCode::BAD_GATEWAY, e))?;
    Ok(Json(json!({ "text": text })))
}

async fn clip_write(Json(q): Json<ClipReq>) -> ApiResult {
    crate::clipboard::write(&q.text)
        .await
        .map_err(|e| err(StatusCode::BAD_GATEWAY, e))?;
    Ok(Json(json!({ "ok": true })))
}

#[derive(Deserialize)]
struct OpenReq {
    #[serde(default)]
    url: String,
}

async fn open_url(State(m): State<Mgr>, Json(q): Json<OpenReq>) -> ApiResult {
    crate::open::check(q.url.trim()).map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    let opener = m.config().await.commands.opener;
    crate::open::url(&opener, &q.url)
        .await
        .map_err(|e| err(StatusCode::BAD_GATEWAY, e))?;
    Ok(Json(json!({ "ok": true })))
}

/// `?files=1` and `?files=true` are the same answer. serde's own bool takes only the
/// second, and half of what this endpoint is for is being asked by hand from a shell.
/// A word that is neither is refused rather than read as "on": a typo silently turning
/// a switch on is worse than a 400 saying so.
fn flag<'de, D: serde::Deserializer<'de>>(d: D) -> Result<bool, D::Error> {
    use serde::de::Error;
    let s = String::deserialize(d)?;
    match s.trim() {
        "" | "0" | "false" | "no" | "off" => Ok(false),
        "1" | "true" | "yes" | "on" => Ok(true),
        other => Err(D::Error::custom(format!(
            "expected a yes or a no, got {other:?}"
        ))),
    }
}

#[derive(Deserialize)]
struct BrowseReq {
    #[serde(default)]
    path: String,
    /// Opt-in, so the project-dir picker - which wants directories and nothing else -
    /// pays neither the read nor the wire for a directory full of files.
    #[serde(default, deserialize_with = "flag")]
    files: bool,
    #[serde(default, deserialize_with = "flag")]
    hidden: bool,
    #[serde(default)]
    limit: Option<usize>,
}

/// Enough to fill a column several screens deep, and short of the answer to
/// `read_dir` on `.git` or `node_modules` being a reply nobody reads.
const BROWSE_LIMIT: usize = 500;

fn browse_limit(requested: Option<usize>) -> usize {
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
async fn browse(State(_m): State<Mgr>, Query(q): Query<BrowseReq>) -> ApiResult {
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

#[derive(Deserialize)]
struct FileReq {
    path: String,
    #[serde(default)]
    name: String,
    #[serde(default)]
    kind: String,
}

fn file_path(path: &str) -> anyhow::Result<std::path::PathBuf> {
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

fn entry_name(name: &str) -> anyhow::Result<&str> {
    let name = name.trim();
    if name.is_empty() || name == "." || name == ".." {
        bail!("name is empty");
    }
    if name.contains('/') || name.contains('\\') || name.contains('\0') {
        bail!("name must be one path component");
    }
    Ok(name)
}

async fn create_file(Json(q): Json<FileReq>) -> ApiResult {
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

async fn rename_file(Json(q): Json<FileReq>) -> ApiResult {
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

async fn remove_file(Json(q): Json<FileReq>) -> ApiResult {
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

#[derive(Deserialize)]
struct SearchReq {
    #[serde(default)]
    path: String,
    #[serde(default)]
    q: String,
    #[serde(default, deserialize_with = "flag")]
    regex: bool,
    #[serde(default, deserialize_with = "flag")]
    case: bool,
    #[serde(default, deserialize_with = "flag")]
    word: bool,
    #[serde(default, deserialize_with = "flag")]
    hidden: bool,
    #[serde(deserialize_with = "flag")]
    gitignore: bool,
    #[serde(default)]
    limit: Option<usize>,
}

const SEARCH_LIMIT: usize = 200;
// `rg --max-columns` controls its human output, but JSON match records still carry the whole
// line. A generated or minified asset can therefore turn one sidebar row into tens of thousands
// of glyphs. Keep the answer a preview, centred near the first match when there is room.
const SEARCH_TEXT_LIMIT: usize = 1_000;

fn search_preview(text: &str, column: usize) -> String {
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

/// Search one project without putting a pattern or path through a shell. `rg --json` keeps
/// filenames and matching text unambiguous; reading it a line at a time lets the endpoint
/// stop the process once the UI-sized answer is full rather than collecting an unbounded
/// repository search in memory.
async fn search(State(_m): State<Mgr>, Query(q): Query<SearchReq>) -> ApiResult {
    use tokio::io::{AsyncBufReadExt, BufReader};
    use tokio::process::Command;

    if q.path.is_empty() {
        return Err(err(StatusCode::BAD_REQUEST, "no path"));
    }
    if q.q.is_empty() {
        return Err(err(StatusCode::BAD_REQUEST, "no query"));
    }

    let dir = std::path::PathBuf::from(crate::config::expand(&q.path));
    let limit = q.limit.unwrap_or(SEARCH_LIMIT).clamp(1, SEARCH_LIMIT);
    let mut cmd = Command::new("rg");
    cmd.current_dir(&dir)
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

    let mut child = cmd
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

#[derive(Deserialize)]
struct GitReq {
    #[serde(default)]
    path: String,
}

/// Returns a project's changed files for the git view; the game cannot run `git` inside agent
/// mount namespaces. Non-repositories return 200 with `repo: false`.
async fn git_status(State(_m): State<Mgr>, Query(q): Query<GitReq>) -> ApiResult {
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

#[derive(Deserialize)]
#[serde(tag = "t", rename_all = "lowercase")]
enum ClientMsg {
    Sub { name: String },
    Unsub { name: String },
    Keys(KeysReq),
    Resize(ResizeReq),
    Scroll(ScrollReq),
    Mouse(MouseReq),
    Paste(PasteReq),
    Breadcrumb(BreadcrumbReq),
    Audio(AudioReq),
}

/// The jukebox. `selection` is absent for a volume-only update, null for silence, a station
/// id/stream key for a catalog entry, or a file/directory path for the mod's OST.
#[derive(Deserialize)]
#[serde(deny_unknown_fields)]
struct AudioReq {
    #[serde(default, deserialize_with = "some_option")]
    selection: Option<Option<AudioSelection>>,
    volume: f32,
}

#[derive(Deserialize)]
struct AudioSelection {
    #[serde(default)]
    station: Option<String>,
    #[serde(default)]
    stream: Option<String>,
    #[serde(default)]
    file: Option<String>,
}

/// Tells "the key was absent" from "the key was null", which is the difference between a
/// volume change and a stop.
fn some_option<'de, D, T>(d: D) -> Result<Option<Option<T>>, D::Error>
where
    D: serde::Deserializer<'de>,
    T: Deserialize<'de>,
{
    Option::deserialize(d).map(Some)
}

#[derive(Deserialize)]
struct PasteReq {
    name: String,
    text: String,
}

#[derive(Deserialize)]
struct BreadcrumbReq {
    name: String,
    breadcrumb: String,
    #[serde(default)]
    random_tips: Vec<String>,
}

#[derive(Deserialize)]
struct MouseReq {
    name: String,
    /// press | release | drag | wheelup | wheeldown
    action: String,
    /// 0/1/2 = left/middle/right; ignored for the wheel.
    #[serde(default)]
    button: u8,
    col: u16,
    row: u16,
    /// How many times to repeat the report. The mod batches wheel events so a single
    /// gesture notch does not spawn one tmux process per scrolled line.
    #[serde(default)]
    count: u8,
}

#[derive(Deserialize)]
struct ScrollReq {
    name: String,
    /// Lines scrolled up into scrollback; 0 returns to the live bottom.
    off: u32,
    /// Echoed back in the response so the mod can reject a stale reply to an older request.
    #[serde(default)]
    request_id: u64,
}

#[derive(Deserialize)]
struct KeysReq {
    name: String,
    /// tmux key names (Enter, C-c, Up) unless `literal`, in which case raw text.
    keys: Vec<String>,
    #[serde(default)]
    literal: bool,
    /// The game supplies loading-screen tips for `{{ random_tip }}`, already distinct: one is
    /// spent per mention, so a text with five bullets gets five different lines.
    #[serde(default)]
    random_tips: Vec<String>,
}

#[derive(Deserialize)]
struct ResizeReq {
    name: String,
    cols: u16,
    rows: u16,
}

async fn ws_upgrade(
    State(m): State<Mgr>,
    headers: HeaderMap,
    ws: WebSocketUpgrade,
) -> impl IntoResponse {
    // The header rides on the upgrade only, so the capability is resolved here and carried into
    // the pump rather than read off a request per message. A grant drives what it may from the
    // same socket the mod uses; a bad token never upgrades.
    let cap = match m.resolve_cap(presented_token(&headers).as_deref()).await {
        Some(c) => c,
        None => return err(StatusCode::UNAUTHORIZED, "bad token").into_response(),
    };
    ws.on_upgrade(move |socket| ws_run(socket, m, cap))
        .into_response()
}

/// Filters events for a socket capability. Root sees all; scoped grants see named sessions and
/// screens, but not projects, shortcuts, usage, audio, or jukebox catalog data.
fn scope_event(cap: &Cap, ev: Event) -> Option<Event> {
    match cap {
        Cap::Root => Some(ev),
        Cap::Scoped(_) => match ev {
            Event::Sessions { sessions } => Some(Event::Sessions {
                sessions: sessions
                    .into_iter()
                    .filter(|s| cap.can_see(&s.name, false))
                    .collect(),
            }),
            Event::Screen { .. } | Event::Quit => Some(ev),
            Event::Projects { .. }
            | Event::Shortcuts { .. }
            | Event::Usage { .. }
            | Event::Audio { .. }
            | Event::Jukebox { .. } => None,
        },
    }
}

async fn ws_run(socket: WebSocket, m: Mgr, cap: Cap) {
    // Held for the life of the pump, so /api/game can say whether anything is
    // attached.
    let _client = m.client_joined();

    let (tx, mut rx) = socket.split();
    let tx = Arc::new(Mutex::new(tx));
    // A `WatchGuard` apiece rather than bare names: the daemon renders a pane at a reader's
    // rate only while somebody is holding one, and this socket has several ways out.
    let subs: Arc<Mutex<HashMap<String, WatchGuard>>> = Arc::new(Mutex::new(HashMap::new()));
    let mut events = m.events.subscribe();

    // Usage, projects and shortcuts ride along because they speak only on a change: a mod
    // attaching between polls would otherwise draw nothing for a minute. Each goes through the
    // same scope filter the pump uses, so a grant's socket gets its filtered session list and
    // none of the mod's wider view - `scope_event` drops what it may not see.
    if let Some(ev) = scope_event(
        &cap,
        Event::Sessions {
            sessions: m.views().await,
        },
    ) {
        if send(&tx, &ev).await.is_err() {
            return;
        }
    }
    for ev in [
        Event::Usage {
            usage: m.usage().await,
        },
        Event::Projects {
            projects: m.projects().await,
        },
        Event::Shortcuts {
            shortcuts: m.shortcuts().await,
        },
        // On connect too, and for the same reason: a game that has just come up has to learn
        // whether the music it asked for last time is playing.
        Event::Audio {
            audio: m.audio.state(),
        },
        Event::Jukebox {
            jukebox: crate::jukebox::catalog(),
        },
    ] {
        if let Some(ev) = scope_event(&cap, ev) {
            let _ = send(&tx, &ev).await;
        }
    }

    // Coalesce screen frames per client to the monitor cadence: newest wins, and the pending
    // frame flushes on the beat. Other event types remain uncoalesced.
    const FRAME_COALESCE: Duration = Duration::from_millis(16);

    let pump = {
        use tokio::time::{sleep_until, Instant as TokioInstant};
        let tx = tx.clone();
        let subs = subs.clone();
        let cap = cap.clone();
        tokio::spawn(async move {
            let mut last_screen = TokioInstant::now() - FRAME_COALESCE;
            // One slot per pane, not one global slot: a socket can subscribe to several
            // panes, and a frame for one must not overwrite a held frame for another.
            let mut pending: HashMap<String, Event> = HashMap::new();
            loop {
                // Only arm the beat when a frame is being held; otherwise the timer would
                // fire on a past deadline and spin while nothing is pending.
                let flush = async {
                    if !pending.is_empty() {
                        sleep_until(last_screen + FRAME_COALESCE).await;
                    } else {
                        std::future::pending::<()>().await;
                    }
                };
                tokio::select! {
                    ev = events.recv() => match ev {
                        Ok(ev) => {
                            // Filtered to this socket's capability before anything else looks at
                            // it: a grant's pump never even coalesces a frame it may not see.
                            let Some(ev) = scope_event(&cap, ev) else { continue };
                            if let Event::Screen { ref screen } = ev {
                                if !subs.lock().await.contains_key(&screen.name) {
                                    continue;
                                }
                                let due = TokioInstant::now()
                                    .saturating_duration_since(last_screen) >= FRAME_COALESCE;
                                if due {
                                    // Sending now, drop any held frame for this pane: it is
                                    // older and must not flush after the newer one.
                                    pending.remove(&screen.name);
                                    if send(&tx, &ev).await.is_err() {
                                        break;
                                    }
                                    last_screen = TokioInstant::now();
                                } else {
                                    // Newest wins for this pane; a still pane never reads stale.
                                    pending.insert(screen.name.clone(), ev);
                                }
                            } else {
                                // A non-screen event (sessions, usage, quit) must not wait
                                // behind a stale frame, so anything held goes out first.
                                if !pending.is_empty() {
                                    for (_, pe) in std::mem::take(&mut pending) {
                                        if send(&tx, &pe).await.is_err() {
                                            break;
                                        }
                                    }
                                    last_screen = TokioInstant::now();
                                }
                                if send(&tx, &ev).await.is_err() {
                                    break;
                                }
                            }
                        }
                        // A slow client misses frames; the next capture resyncs it.
                        Err(tokio::sync::broadcast::error::RecvError::Lagged(_)) => continue,
                        Err(_) => break,
                    },
                    _ = flush => {
                        if !pending.is_empty() {
                            for (_, pe) in std::mem::take(&mut pending) {
                                if send(&tx, &pe).await.is_err() {
                                    break;
                                }
                            }
                            last_screen = TokioInstant::now();
                        }
                    }
                }
            }
        })
    };

    while let Some(Ok(msg)) = rx.next().await {
        let Message::Text(text) = msg else { continue };
        let Ok(cm) = serde_json::from_str::<ClientMsg>(&text) else {
            tracing::debug!("unparseable ws message: {text}");
            continue;
        };
        // Require ro for pane reads, rw for input, and neither for host terminals; unauthorized
        // messages are dropped silently.
        match cm {
            ClientMsg::Sub { name } => {
                if !m.cap_ok(&cap, &name, Level::Ro).await {
                    continue;
                }
                subs.lock().await.insert(name.clone(), m.watching(&name));
                // Somebody is looking at this pane now, which is the whole of what a bell
                // was asking for. Broadcast, not answered here: every client draws the mark.
                m.clear_bell(&name).await;
                if let Some(s) = m.screen(&name).await {
                    let _ = send(&tx, &Event::Screen { screen: s }).await;
                }
            }
            ClientMsg::Unsub { name } => {
                subs.lock().await.remove(&name);
            }
            ClientMsg::Keys(k) => {
                if !m.cap_ok(&cap, &k.name, Level::Rw).await {
                    continue;
                }
                m.send_keys(&k.name, k.keys, k.literal, k.random_tips).await;
            }
            ClientMsg::Resize(r) => {
                if !m.cap_ok(&cap, &r.name, Level::Rw).await {
                    continue;
                }
                if let Err(e) = m.resize(&r.name, r.cols, r.rows).await {
                    tracing::debug!("resize: {e:#}");
                }
            }
            ClientMsg::Scroll(sr) => {
                if !m.cap_ok(&cap, &sr.name, Level::Ro).await {
                    continue;
                }
                // Answer this socket alone: a scrolled frame is private to the wheel request and
                // must not reach live subscribers.
                if let Some(s) = m.scroll_capture(&sr.name, sr.off, sr.request_id).await {
                    let _ = send(&tx, &Event::Screen { screen: s }).await;
                }
            }
            ClientMsg::Mouse(mr) => {
                if !m.cap_ok(&cap, &mr.name, Level::Rw).await {
                    continue;
                }
                let Some(action) = crate::emu::MouseAction::parse(&mr.action) else {
                    tracing::debug!("unknown mouse action: {}", mr.action);
                    continue;
                };
                let ev = crate::emu::MouseInput {
                    action,
                    button: mr.button,
                    col: mr.col,
                    row: mr.row,
                };
                m.send_mouse(&mr.name, ev, mr.count).await;
            }
            ClientMsg::Paste(pr) => {
                if !m.cap_ok(&cap, &pr.name, Level::Rw).await {
                    continue;
                }
                // A paste checks first, so anything left is a paste that did not land - and
                // the only other sign of one is the operator noticing nothing arrived.
                if let Err(e) = m.paste(&pr.name, &pr.text).await {
                    tracing::warn!("paste to {}: {e:#}", pr.name);
                }
            }
            ClientMsg::Breadcrumb(br) => {
                if !m.cap_ok(&cap, &br.name, Level::Rw).await {
                    continue;
                }
                if let Err(e) = m
                    .paste_breadcrumb(&br.name, &br.breadcrumb, br.random_tips)
                    .await
                {
                    tracing::warn!("breadcrumb to {}: {e:#}", br.name);
                }
            }
            ClientMsg::Audio(ar) => {
                if !cap.may_create() {
                    continue;
                }
                match ar.selection {
                    Some(Some(selection)) => {
                        let source = match (selection.station, selection.stream, selection.file) {
                            (Some(station), Some(stream), None) => {
                                match crate::jukebox::catalog().resolve(&station, &stream) {
                                    Ok(source) => Some(source),
                                    Err(e) => {
                                        let why = format!("jukebox selection rejected: {e:#}");
                                        tracing::warn!("{why}");
                                        m.audio.reject(why);
                                        None
                                    }
                                }
                            }
                            (None, None, Some(file)) => Some(file),
                            _ => {
                                let why = "jukebox selection must name a station/stream or file";
                                tracing::warn!("{why}");
                                m.audio.reject(why);
                                None
                            }
                        };
                        if let Some(source) = source {
                            m.audio.play(&source, ar.volume);
                        }
                    }
                    Some(None) => m.audio.stop(),
                    None => m.audio.set_volume(ar.volume),
                }
            }
        }
    }

    // Explicitly, rather than with the table: the pump holds the other half of that `Arc` and
    // an aborted task is dropped when the runtime gets round to it, which is not when the
    // agent stopped being watched.
    subs.lock().await.clear();
    pump.abort();
}

async fn send(
    tx: &Arc<Mutex<futures::stream::SplitSink<WebSocket, Message>>>,
    ev: &Event,
) -> Result<(), axum::Error> {
    let text = serde_json::to_string(ev).unwrap_or_default();
    tx.lock().await.send(Message::Text(text)).await
}

#[cfg(test)]
mod tests {
    use std::path::{Path, PathBuf};

    use super::{
        browse_limit, entry_name, file_path, list_dir, search_preview, SearchReq, SEARCH_TEXT_LIMIT,
    };

    /// Somewhere of our own under the machine's temp dir, cleared on the way in so a run
    /// that died before its cleanup does not poison the next one. No dev-dependency for
    /// this: one directory of empty files is not worth a crate.
    fn fixture(tag: &str) -> PathBuf {
        let dir = std::env::temp_dir().join(format!("slopd-browse-{tag}"));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        dir
    }

    fn touch(dir: &Path, name: &str) {
        std::fs::write(dir.join(name), b"").unwrap();
    }

    fn subdir(dir: &Path, name: &str) {
        std::fs::create_dir_all(dir.join(name)).unwrap();
    }

    /// The dir picker asked for none, so it is handed none, and a directory full of files
    /// is still just its directories - which is also what keeps the cap off it.
    #[tokio::test]
    async fn files_are_opt_in() {
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

    /// Both lists, and a dot directory is as hidden as a dot file.
    #[tokio::test]
    async fn dotfiles_are_hidden_until_they_are_asked_for() {
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
    async fn a_symlinked_directory_is_a_directory() {
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
    async fn a_long_directory_is_cut_short_and_says_so() {
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
    async fn exactly_the_limit_is_not_truncated() {
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
    fn browse_limit_is_bounded() {
        assert_eq!(browse_limit(None), 500);
        assert_eq!(browse_limit(Some(0)), 1);
        assert_eq!(browse_limit(Some(12)), 12);
        assert_eq!(browse_limit(Some(usize::MAX)), 500);
    }

    #[test]
    fn file_names_are_one_component() {
        assert_eq!(entry_name("note.md").unwrap(), "note.md");
        assert!(entry_name("").is_err());
        assert!(entry_name(".").is_err());
        assert!(entry_name("src/note.md").is_err());
        assert!(entry_name("src\\note.md").is_err());
    }

    #[test]
    fn file_paths_keep_whitespace_in_a_real_filename() {
        assert_eq!(
            file_path("/tmp/ notes.txt ").unwrap(),
            Path::new("/tmp/ notes.txt ")
        );
    }

    #[test]
    fn search_preview_caps_a_long_line_around_its_match() {
        let text = format!("{}test{}", "x".repeat(2_000), "y".repeat(2_000));
        let preview = search_preview(&text, 2_001);

        assert!(preview.contains("test"));
        assert!(preview.starts_with('…'));
        assert!(preview.ends_with('…'));
        assert!(preview.len() <= SEARCH_TEXT_LIMIT + 6);
    }

    #[test]
    fn search_preview_preserves_utf8_boundaries() {
        let text = format!("{}test{}", "é".repeat(800), "é".repeat(800));
        let preview = search_preview(&text, 1_601);

        assert!(preview.contains("test"));
        assert!(std::str::from_utf8(preview.as_bytes()).is_ok());
    }

    #[test]
    fn search_filters_gitignored_files_by_default() {
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

    #[test]
    fn audio_selection_has_distinct_stop_volume_and_catalog_shapes() {
        let station: super::AudioReq = serde_json::from_str(
            r#"{"selection":{"station":"fixture","stream":"local"},"volume":0.5}"#,
        )
        .unwrap();
        let selected = station.selection.unwrap().unwrap();
        assert_eq!(selected.station.as_deref(), Some("fixture"));
        assert_eq!(selected.stream.as_deref(), Some("local"));

        let stop: super::AudioReq =
            serde_json::from_str(r#"{"selection":null,"volume":0.5}"#).unwrap();
        assert!(stop.selection.is_some_and(|selection| selection.is_none()));

        let volume: super::AudioReq = serde_json::from_str(r#"{"volume":0.5}"#).unwrap();
        assert!(volume.selection.is_none());

        assert!(serde_json::from_str::<super::AudioReq>(
            r#"{"source":"/tmp/old.ogg","volume":0.5}"#
        )
        .is_err());
    }
}
