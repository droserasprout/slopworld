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

use crate::config::SessionCfg;
use crate::session::{Event, Manager};

type Mgr = Arc<Manager>;

pub fn router(m: Mgr) -> Router {
    Router::new()
        .route("/api/health", get(health))
        .route("/api/sessions", get(list).post(create))
        .route(
            "/api/sessions/:name",
            get(one).put(update).delete(destroy),
        )
        .route("/api/sessions/:name/start", post(start))
        .route("/api/sessions/:name/stop", post(stop))
        .route("/api/sessions/:name/restart", post(restart))
        .route("/api/sessions/:name/keys", post(keys))
        .route("/api/sessions/:name/resize", post(resize))
        .route("/api/sessions/:name/screen", get(screen))
        .route("/api/config", get(get_config))
        .route("/api/config", put(put_config))
        .route("/api/browse", get(browse))
        .route("/ws", get(ws_upgrade))
        .with_state(m)
}

/// Turns any error into a JSON body the mod can surface in a dialog.
fn err(code: StatusCode, e: impl std::fmt::Display) -> (StatusCode, Json<serde_json::Value>) {
    (code, Json(json!({ "error": e.to_string() })))
}

type ApiResult = Result<Json<serde_json::Value>, (StatusCode, Json<serde_json::Value>)>;

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
    m.add(s).await.map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({ "ok": true })))
}

async fn update(
    State(m): State<Mgr>,
    Path(name): Path<String>,
    Json(s): Json<SessionCfg>,
) -> ApiResult {
    m.update(&name, s)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({ "ok": true })))
}

async fn destroy(State(m): State<Mgr>, Path(name): Path<String>) -> ApiResult {
    m.remove(&name)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({ "ok": true })))
}

async fn start(State(m): State<Mgr>, Path(name): Path<String>) -> ApiResult {
    m.start(&name)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({ "ok": true })))
}

async fn stop(State(m): State<Mgr>, Path(name): Path<String>) -> ApiResult {
    m.stop(&name)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({ "ok": true })))
}

async fn restart(State(m): State<Mgr>, Path(name): Path<String>) -> ApiResult {
    m.restart(&name)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({ "ok": true })))
}

#[derive(Deserialize)]
struct KeysReq {
    /// tmux key names (Enter, C-c, Up) unless `literal`, in which case raw text.
    keys: Vec<String>,
    #[serde(default)]
    literal: bool,
}

async fn keys(
    State(m): State<Mgr>,
    Path(name): Path<String>,
    Json(req): Json<KeysReq>,
) -> ApiResult {
    m.send_keys(&name, req.keys, req.literal)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({ "ok": true })))
}

#[derive(Deserialize)]
struct ResizeReq {
    cols: u16,
    rows: u16,
}

async fn resize(
    State(m): State<Mgr>,
    Path(name): Path<String>,
    Json(req): Json<ResizeReq>,
) -> ApiResult {
    m.resize(&name, req.cols, req.rows)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({ "ok": true })))
}

async fn screen(State(m): State<Mgr>, Path(name): Path<String>) -> ApiResult {
    m.screen(&name)
        .await
        .map(|s| Json(json!(s)))
        .ok_or_else(|| err(StatusCode::NOT_FOUND, format!("no screen for {name}")))
}

async fn get_config(State(m): State<Mgr>) -> ApiResult {
    let text = std::fs::read_to_string(&m.cfg_path)
        .map_err(|e| err(StatusCode::INTERNAL_SERVER_ERROR, e))?;
    Ok(Json(json!({ "path": m.cfg_path, "text": text })))
}

#[derive(Deserialize)]
struct ConfigReq {
    text: String,
}

async fn put_config(State(m): State<Mgr>, Json(req): Json<ConfigReq>) -> ApiResult {
    m.replace_config(&req.text)
        .await
        .map_err(|e| err(StatusCode::BAD_REQUEST, e))?;
    Ok(Json(json!({ "ok": true })))
}

#[derive(Deserialize)]
struct BrowseReq {
    #[serde(default)]
    path: String,
}

/// Directory listing so the in-game "add session" dialog can pick a project dir
/// without the mod ever touching the host filesystem across the Wine boundary.
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

// ---------------------------------------------------------------- websocket

#[derive(Deserialize)]
#[serde(tag = "t", rename_all = "lowercase")]
enum ClientMsg {
    Sub { name: String },
    Unsub { name: String },
    Keys(KeysReq2),
    Resize(ResizeReq2),
}

#[derive(Deserialize)]
struct KeysReq2 {
    name: String,
    keys: Vec<String>,
    #[serde(default)]
    literal: bool,
}

#[derive(Deserialize)]
struct ResizeReq2 {
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
    if !token.is_empty() {
        let ok = headers
            .get("x-slop-token")
            .and_then(|v| v.to_str().ok())
            .map(|v| v == token)
            .unwrap_or(false);
        if !ok {
            return err(StatusCode::UNAUTHORIZED, "bad token").into_response();
        }
    }
    ws.on_upgrade(move |socket| ws_run(socket, m)).into_response()
}

async fn ws_run(socket: WebSocket, m: Mgr) {
    let (tx, mut rx) = socket.split();
    let tx = Arc::new(Mutex::new(tx));
    let subs: Arc<Mutex<HashSet<String>>> = Arc::new(Mutex::new(HashSet::new()));
    let mut events = m.events.subscribe();

    // Fresh clients need the full picture before any deltas arrive.
    let hello = Event::Sessions {
        sessions: m.views().await,
    };
    if send(&tx, &hello).await.is_err() {
        return;
    }

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
                // Push current contents immediately, don't wait for the next change.
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
