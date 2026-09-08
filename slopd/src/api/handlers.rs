use axum::extract::{Path, Query, State};
use axum::http::StatusCode;
use axum::{Extension, Json};
use serde_json::json;

use crate::config::{LibraryItemCfg, ProjectCfg, SessionCfg};
use crate::grant::{Cap, Level};
use crate::session::RunWhere;

use super::types::*;
use super::{err, ApiResult, Mgr};

#[path = "handlers_clipboard.rs"]
mod handlers_clipboard;
#[path = "handlers_config.rs"]
mod handlers_config;
#[path = "handlers_files.rs"]
mod handlers_files;
#[path = "handlers_system.rs"]
mod handlers_system;
#[path = "handlers_tasks.rs"]
mod handlers_tasks;

pub(crate) use handlers_clipboard::*;
pub(crate) use handlers_config::*;
pub(crate) use handlers_files::*;
pub(crate) use handlers_system::*;
pub(crate) use handlers_tasks::*;
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
        "version": env!("SLOPWORLD_VERSION"),
        "hostname": crate::runtime::hostname(),
        "tmux_socket": crate::config::tmux_socket(),
    })))
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

pub(super) async fn empty_trash(State(m): State<Mgr>) -> ApiResult {
    ok_json(m.empty_trash().await)
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

root_action!(destroy_project => remove_project, destroy_library_item => remove_library_item);

pub(super) async fn list_library(State(m): State<Mgr>) -> ApiResult {
    Ok(Json(json!({ "library": m.library().await })))
}

pub(super) async fn create_library_item(
    State(m): State<Mgr>,
    Json(sc): Json<LibraryItemCfg>,
) -> ApiResult {
    ok_json(m.add_library_item(sc).await)
}

pub(super) async fn update_library_item(
    State(m): State<Mgr>,
    Path(name): Path<String>,
    Json(sc): Json<LibraryItemCfg>,
) -> ApiResult {
    ok_json(m.update_library_item(&name, sc).await)
}

/// Answers with the temporary agent's name as soon as it is up; the text lands well past the
/// point this client would have given up waiting. The body says where to run -
/// `{"project":"..."}` or `{"temp":true}` - and `Option<Json<_>>` is so a bodyless curl still
/// runs the errands that already know.
pub(super) async fn run_library_item(
    State(m): State<Mgr>,
    Path(name): Path<String>,
    want: Option<Json<RunWhere>>,
) -> ApiResult {
    let session = m
        .run_library_item(&name, want.map(|Json(w)| w).unwrap_or_default())
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
    if command.is_empty() && q.kind != crate::config::LibraryItemKind::Shell {
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
    let sc = LibraryItemCfg {
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
        && q.kind == crate::config::LibraryItemKind::Shell
        && !project.is_empty()
        && q.path.trim().is_empty()
        && !q.temp;
    let like = q.like.trim().to_string();
    let session = m
        .run_errand(sc, want, q.host, persistent_host, &like)
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
        let missing: SearchReq = serde_json::from_value(serde_json::json!({
            "path": "/tmp/project",
            "q": "needle"
        }))
        .unwrap();
        assert!(!missing.gitignore);

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
