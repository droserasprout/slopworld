use std::collections::HashSet;
use std::sync::Arc;

use axum::extract::ws::{Message, WebSocket, WebSocketUpgrade};
use axum::extract::{Path, Query, State};
use axum::http::{HeaderMap, StatusCode};
use axum::response::IntoResponse;
use axum::routing::{get, post, put};
use axum::{Json, Router};
use futures::{SinkExt, StreamExt};
use serde::Deserialize;
use serde_json::json;
use tokio::sync::Mutex;

use crate::config::{ProjectCfg, SessionCfg, ShortcutCfg};
use crate::session::{Event, Manager, RunWhere};

type Mgr = Arc<Manager>;

pub fn router(m: Mgr) -> Router {
    Router::new()
        .route("/api/health", get(health))
        .route("/api/sessions", get(list).post(create))
        .route("/api/sessions/:name", get(one).put(update).delete(destroy))
        .route("/api/sessions/:name/start", post(start))
        .route("/api/sessions/:name/stop", post(stop))
        .route("/api/sessions/:name/restart", post(restart))
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
        .route("/api/presets", get(presets))
        .route("/api/config", get(get_config))
        .route("/api/config", put(put_config))
        .route("/api/config/values", put(put_config_values))
        .route("/api/clipboard", get(clip_read).post(clip_write))
        .route("/api/open", post(open_url))
        .route("/api/usage", get(usage))
        .route("/api/browse", get(browse))
        .route("/api/game", get(game))
        .route("/api/game/restart", post(restart_game))
        .route("/ws", get(ws_upgrade))
        .with_state(m)
}

fn err(code: StatusCode, e: impl std::fmt::Display) -> (StatusCode, Json<serde_json::Value>) {
    (code, Json(json!({ "error": e.to_string() })))
}

/// An empty configured token means no auth. Shared with the `/ws` upgrade, which
/// re-checks because the header rides only on the upgrade request.
pub fn token_ok(headers: &HeaderMap, token: &str) -> bool {
    token.is_empty()
        || headers
            .get("x-slop-token")
            .and_then(|v| v.to_str().ok())
            .map(|v| v == token)
            .unwrap_or(false)
}

type ApiResult = Result<Json<serde_json::Value>, (StatusCode, Json<serde_json::Value>)>;

fn ok_json(r: anyhow::Result<()>) -> ApiResult {
    r.map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({ "ok": true })))
}

async fn health(State(m): State<Mgr>) -> ApiResult {
    Ok(Json(json!({
        "ok": true,
        "version": env!("CARGO_PKG_VERSION"),
        "tmux_socket": m.config().await.daemon.tmux_socket,
    })))
}

async fn list(State(m): State<Mgr>) -> ApiResult {
    Ok(Json(json!({ "sessions": m.views().await })))
}

async fn one(State(m): State<Mgr>, Path(name): Path<String>) -> ApiResult {
    m.views()
        .await
        .into_iter()
        .find(|s| s.name == name)
        .map(|s| Json(json!(s)))
        .ok_or_else(|| err(StatusCode::NOT_FOUND, format!("no such session: {name}")))
}

async fn create(State(m): State<Mgr>, Json(s): Json<SessionCfg>) -> ApiResult {
    ok_json(m.add(s).await)
}

async fn update(
    State(m): State<Mgr>,
    Path(name): Path<String>,
    Json(s): Json<SessionCfg>,
) -> ApiResult {
    ok_json(m.update(&name, s).await)
}

async fn destroy(State(m): State<Mgr>, Path(name): Path<String>) -> ApiResult {
    ok_json(m.remove(&name).await)
}

async fn start(State(m): State<Mgr>, Path(name): Path<String>) -> ApiResult {
    ok_json(m.start(&name).await)
}

async fn stop(State(m): State<Mgr>, Path(name): Path<String>) -> ApiResult {
    ok_json(m.stop(&name).await)
}

async fn restart(State(m): State<Mgr>, Path(name): Path<String>) -> ApiResult {
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
    #[serde(default)]
    text: String,
    /// Names the session and, through `slug`, the tmux session behind it. The errand's own
    /// word for itself, since there is no entry to take one from.
    #[serde(default)]
    label: String,
    #[serde(default)]
    temp: bool,
}

async fn run(State(m): State<Mgr>, Json(q): Json<RunReq>) -> ApiResult {
    let command = q.command.trim();
    if command.is_empty() {
        return Err(err(
            StatusCode::BAD_REQUEST,
            "an errand must say what to run",
        ));
    }
    let project = q.project.trim();
    if project.is_empty() && !q.temp {
        return Err(err(
            StatusCode::BAD_REQUEST,
            "an errand must name a project or ask for a temporary one",
        ));
    }

    let label = match q.label.trim() {
        "" => "run",
        l => l,
    };
    let sc = ShortcutCfg {
        name: label.to_string(),
        kind: q.kind,
        link: crate::config::ShortcutLink::Project,
        project: project.to_string(),
        text: q.text,
        command: Some(command.to_string()),
    };

    // The project is checked by `run_errand` itself, which is also where a temporary one is
    // coined - so `temp` rides over as the override it already is rather than a second road.
    let want = RunWhere {
        project: None,
        temp: q.temp,
    };
    let session = m
        .run_errand(sc, want)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({ "ok": true, "session": session })))
}

/// So the GUI draws a checkbox per preset and a row per command rather than a list
/// somebody keeps in step by hand. Both tables are files, so this is also how the mod
/// learns about one that was added while it was running.
async fn presets(State(_m): State<Mgr>) -> ApiResult {
    let t = crate::presets::table();
    let sandbox: Vec<serde_json::Value> = t
        .sandbox
        .iter()
        .map(|p| {
            json!({
                "name": p.name,
                "category": p.category,
                "description": p.description,
                "ro": p.ro,
                "rw": p.rw,
                "dev": p.dev,
                "env": p.env,
                "setenv": p.setenv.iter().map(|(k, v)| format!("{k}={v}")).collect::<Vec<_>>(),
            })
        })
        .collect();

    let commands: Vec<serde_json::Value> = t
        .commands
        .iter()
        .map(|c| {
            json!({
                "name": c.name,
                "category": c.category,
                "description": c.description,
                "cmd": c.cmd,
                "sandbox": c.sandbox,
            })
        })
        .collect();

    Ok(Json(
        json!({ "presets": sandbox, "commands": commands, "dir": crate::presets::Table::dir() }),
    ))
}

/// Text for the raw editor, parsed for the settings GUI, so a mod can offer either
/// without parsing TOML.
async fn get_config(State(m): State<Mgr>) -> ApiResult {
    let text = std::fs::read_to_string(&m.cfg_path)
        .map_err(|e| err(StatusCode::INTERNAL_SERVER_ERROR, e))?;
    Ok(Json(
        json!({ "path": m.cfg_path, "text": text, "values": m.config().await }),
    ))
}

#[derive(Deserialize)]
struct ConfigReq {
    text: String,
}

async fn put_config(State(m): State<Mgr>, Json(req): Json<ConfigReq>) -> ApiResult {
    ok_json(m.replace_config(&req.text).await)
}

/// A section left out is left alone, which keeps a settings GUI from writing back
/// sessions and state rules it never showed the player.
#[derive(Deserialize)]
struct SectionsReq {
    #[serde(default)]
    daemon: Option<crate::config::Daemon>,
    #[serde(default)]
    defaults: Option<crate::config::Defaults>,
    #[serde(default)]
    sandbox: Option<crate::config::Sandbox>,
}

async fn put_config_values(State(m): State<Mgr>, Json(req): Json<SectionsReq>) -> ApiResult {
    ok_json(
        m.update_sections(req.daemon, req.defaults, req.sandbox)
            .await,
    )
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

async fn open_url(Json(q): Json<OpenReq>) -> ApiResult {
    crate::open::check(q.url.trim()).map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    crate::open::url(&q.url)
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
        other => Err(D::Error::custom(format!("expected a yes or a no, got {other:?}"))),
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
    while let Ok(Some(e)) = rd.next_entry().await {
        let name = e.file_name().to_string_lossy().into_owned();
        if !hidden && name.starts_with('.') {
            continue;
        }

        let Ok(t) = e.file_type().await else { continue };
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

        if is_dir {
            out.dirs.push(name);
        } else if want_files {
            out.files.push(name);
        } else {
            // Not asked for, so not counted either: a directory holding three folders and
            // ten thousand files is three rows, not a truncated answer.
            continue;
        }

        if out.dirs.len() + out.files.len() >= limit {
            out.truncated = true;
            break;
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

    let limit = q.limit.unwrap_or(BROWSE_LIMIT).max(1);
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
#[serde(tag = "t", rename_all = "lowercase")]
enum ClientMsg {
    Sub { name: String },
    Unsub { name: String },
    Keys(KeysReq),
    Resize(ResizeReq),
    Scroll(ScrollReq),
    Mouse(MouseReq),
    Paste(PasteReq),
}

#[derive(Deserialize)]
struct PasteReq {
    name: String,
    text: String,
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
}

#[derive(Deserialize)]
struct ScrollReq {
    name: String,
    /// Lines scrolled up into scrollback; 0 returns to the live bottom.
    off: u16,
}

#[derive(Deserialize)]
struct KeysReq {
    name: String,
    /// tmux key names (Enter, C-c, Up) unless `literal`, in which case raw text.
    keys: Vec<String>,
    #[serde(default)]
    literal: bool,
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
    let token = m.config().await.daemon.token;
    if !token_ok(&headers, &token) {
        return err(StatusCode::UNAUTHORIZED, "bad token").into_response();
    }
    ws.on_upgrade(move |socket| ws_run(socket, m))
        .into_response()
}

async fn ws_run(socket: WebSocket, m: Mgr) {
    // Held for the life of the pump, so /api/game can say whether anything is
    // attached.
    let _client = m.client_joined();

    let (tx, mut rx) = socket.split();
    let tx = Arc::new(Mutex::new(tx));
    let subs: Arc<Mutex<HashSet<String>>> = Arc::new(Mutex::new(HashSet::new()));
    let mut events = m.events.subscribe();

    // Usage, projects and shortcuts ride along because they speak only on a change: a mod
    // attaching between polls would otherwise draw nothing for a minute.
    let hello = Event::Sessions {
        sessions: m.views().await,
    };
    if send(&tx, &hello).await.is_err() {
        return;
    }
    let _ = send(
        &tx,
        &Event::Usage {
            usage: m.usage().await,
        },
    )
    .await;
    let _ = send(
        &tx,
        &Event::Projects {
            projects: m.projects().await,
        },
    )
    .await;
    let _ = send(
        &tx,
        &Event::Shortcuts {
            shortcuts: m.shortcuts().await,
        },
    )
    .await;

    let pump = {
        let tx = tx.clone();
        let subs = subs.clone();
        tokio::spawn(async move {
            loop {
                match events.recv().await {
                    Ok(ev) => {
                        if let Event::Screen { ref screen } = ev {
                            if !subs.lock().await.contains(&screen.name) {
                                continue;
                            }
                        }
                        if send(&tx, &ev).await.is_err() {
                            break;
                        }
                    }
                    // A slow client misses frames; the next capture resyncs it.
                    Err(tokio::sync::broadcast::error::RecvError::Lagged(_)) => continue,
                    Err(_) => break,
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
        match cm {
            ClientMsg::Sub { name } => {
                subs.lock().await.insert(name.clone());
                if let Some(s) = m.screen(&name).await {
                    let _ = send(&tx, &Event::Screen { screen: s }).await;
                }
            }
            ClientMsg::Unsub { name } => {
                subs.lock().await.remove(&name);
            }
            ClientMsg::Keys(k) => {
                if let Err(e) = m.send_keys(&k.name, k.keys, k.literal).await {
                    tracing::debug!("send_keys: {e:#}");
                }
            }
            ClientMsg::Resize(r) => {
                if let Err(e) = m.resize(&r.name, r.cols, r.rows).await {
                    tracing::debug!("resize: {e:#}");
                }
            }
            ClientMsg::Scroll(sr) => {
                // Answer this socket alone: a scrolled frame is private to the wheel request and
                // must not reach live subscribers.
                if let Some(s) = m.scroll_capture(&sr.name, sr.off).await {
                    let _ = send(&tx, &Event::Screen { screen: s }).await;
                }
            }
            ClientMsg::Mouse(mr) => {
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
                if let Err(e) = m.send_mouse(&mr.name, ev).await {
                    tracing::debug!("send_mouse: {e:#}");
                }
            }
            ClientMsg::Paste(pr) => {
                if let Err(e) = m.paste(&pr.name, &pr.text).await {
                    tracing::debug!("paste: {e:#}");
                }
            }
        }
    }

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

    use super::list_dir;

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
}
