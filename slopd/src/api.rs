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
        .route("/api/presets", get(presets))
        .route("/api/config", get(get_config))
        .route("/api/config", put(put_config))
        .route("/api/config/values", put(put_config_values))
        .route("/api/clipboard", get(clip_read).post(clip_write))
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

/// Answers with the name of the temporary agent as soon as it is up, so the caller
/// can open a terminal on it; the text lands well past the point this client would
/// have given up waiting. The body says where to run - `{"project":"..."}` or
/// `{"temp":true}` - and `Option<Json<_>>` is so a bodyless curl still runs the
/// errands that already know.
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

/// So the GUI draws a checkbox per preset rather than a list somebody keeps in
/// step by hand.
async fn presets(State(_m): State<Mgr>) -> ApiResult {
    let list: Vec<serde_json::Value> = crate::sandbox::PRESETS
        .iter()
        .map(|p| {
            json!({
                "name": p.name,
                "description": p.description,
                "ro": p.ro,
                "rw": p.rw,
                "dev": p.dev,
                "env": p.env,
                "setenv": p.setenv.iter().map(|(k, v)| format!("{k}={v}")).collect::<Vec<_>>(),
            })
        })
        .collect();
    Ok(Json(json!({ "presets": list })))
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
struct BrowseReq {
    #[serde(default)]
    path: String,
}

/// So the dialog can pick a project dir without the mod touching the host
/// filesystem itself.
async fn browse(State(_m): State<Mgr>, Query(q): Query<BrowseReq>) -> ApiResult {
    let base = if q.path.is_empty() {
        dirs::home_dir().unwrap_or_else(|| "/".into())
    } else {
        std::path::PathBuf::from(crate::config::expand(&q.path))
    };

    let mut dirs_out: Vec<String> = Vec::new();
    let mut rd = tokio::fs::read_dir(&base)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    while let Ok(Some(e)) = rd.next_entry().await {
        if e.file_type().await.map(|t| t.is_dir()).unwrap_or(false) {
            let name = e.file_name().to_string_lossy().into_owned();
            if !name.starts_with('.') {
                dirs_out.push(name);
            }
        }
    }
    dirs_out.sort();

    Ok(Json(json!({
        "path": base,
        "parent": base.parent(),
        "dirs": dirs_out,
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

    // Usage, projects and shortcuts ride along because they speak only on a change: a
    // mod attaching between polls would otherwise draw nothing for a minute, and one
    // attaching after the last edit would have nothing to fill the project dropdown
    // from.
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
