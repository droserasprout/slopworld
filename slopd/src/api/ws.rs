//! WebSocket upgrade, authentication lifetime, subscriptions, and ordered scroll replies.
//! commands owns dispatch; outbound owns snapshots, frame coalescing, and socket writes.

mod commands;
mod outbound;

use commands::handle_client_msg;
use outbound::{send, send_initial_snapshot, spawn_frame_pump};

use std::collections::{HashMap, VecDeque};
use std::sync::Arc;

use crate::grant::{Cap, Level};
use axum::extract::ws::{Message, WebSocket, WebSocketUpgrade};
use axum::extract::State;
use axum::http::{HeaderMap, StatusCode};
use axum::response::IntoResponse;
use futures::StreamExt;
use tokio::sync::{broadcast, Mutex, Semaphore};
use tokio::task::JoinHandle;

use crate::session::{AuthChange, Event, EventMessage, ScreenView, WatchGuard};

use super::types::{ClientMsg, ScrollReq};
use super::{err, presented_token, Mgr};

type WsTx = Arc<Mutex<futures::stream::SplitSink<WebSocket, Message>>>;
type WsSubs = Arc<Mutex<HashMap<String, WatchGuard>>>;

/// Owns bounded scroll captures and their ordered replies for one connection.
struct ScrollReplies {
    slots: Arc<Semaphore>,
    pending: VecDeque<JoinHandle<Option<ScreenView>>>,
}

impl ScrollReplies {
    fn new() -> Self {
        Self {
            slots: Arc::new(Semaphore::new(4)),
            pending: VecDeque::new(),
        }
    }

    fn enqueue(&mut self, req: ScrollReq, m: &Mgr, cap: &Cap) {
        let Ok(permit) = self.slots.clone().try_acquire_owned() else {
            return;
        };
        let manager = m.clone();
        let cap = cap.clone();
        self.pending.push_back(tokio::spawn(async move {
            let _permit = permit;
            manager
                .session_read_operation(async {
                    if !manager.cap_ok(&cap, &req.name, Level::Ro).await {
                        return None;
                    }
                    manager
                        .scroll_capture(&req.name, req.off, req.request_id)
                        .await
                })
                .await
        }));
    }

    fn has_pending(&self) -> bool {
        !self.pending.is_empty()
    }

    async fn send_next(&mut self, tx: &WsTx, cap: &Cap) -> bool {
        let Some(task) = self.pending.front_mut() else {
            return true;
        };
        let result = task.await;
        self.pending.pop_front();
        match result {
            Ok(Some(screen)) => send(tx, cap, &EventMessage::new(Event::Screen { screen }))
                .await
                .is_ok(),
            Ok(None) => true,
            Err(error) => {
                tracing::debug!("scroll task ended: {error}");
                true
            }
        }
    }

    async fn flush(&mut self, tx: &WsTx, cap: &Cap) -> bool {
        while self.has_pending() {
            if !self.send_next(tx, cap).await {
                return false;
            }
        }
        true
    }
}

impl Drop for ScrollReplies {
    fn drop(&mut self) {
        for task in &self.pending {
            task.abort();
        }
    }
}

pub(super) async fn ws_upgrade(
    State(m): State<Mgr>,
    headers: HeaderMap,
    ws: WebSocketUpgrade,
) -> impl IntoResponse {
    // Credentials arrive only during upgrade. Reject changes during resolution,
    // then carry the capability and generation into the connection lifetime.
    let generation_before = m.auth_generation();
    let cap = match m.resolve_cap(presented_token(&headers).as_deref()).await {
        Some(c) => c,
        None => {
            return err(
                StatusCode::UNAUTHORIZED,
                "The authentication token is missing or invalid.",
            )
            .into_response()
        }
    };
    let generation = m.auth_generation();
    if generation != generation_before {
        return err(
            StatusCode::UNAUTHORIZED,
            "The caller's permissions changed during this request.",
        )
        .into_response();
    }
    if !headers
        .get("sec-websocket-protocol")
        .and_then(|v| v.to_str().ok())
        .is_some_and(|v| v.split(',').any(|p| p.trim() == "slopworld.protobuf.v2"))
    {
        return err(
            StatusCode::BAD_REQUEST,
            "Use the slopworld.protobuf.v2 WebSocket subprotocol.",
        )
        .into_response();
    }
    ws.protocols(["slopworld.protobuf.v2"])
        .on_upgrade(move |socket| ws_run(socket, m, cap, generation))
        .into_response()
}

async fn ws_run(socket: WebSocket, m: Mgr, cap: Cap, generation: u64) {
    let (tx, mut rx) = socket.split();
    let tx = Arc::new(Mutex::new(tx));
    let mut auth_changes = m.auth_changes();
    if m.auth_generation() != generation {
        return;
    }
    // Each subscription owns a guard that keeps its reader rendering at the watched rate.
    let subs: WsSubs = Arc::new(Mutex::new(HashMap::new()));

    // Subscribe before snapshot preparation so concurrent session starts cannot be lost.
    let events = m.events.subscribe();
    if !send_initial_snapshot(&tx, &m, &cap).await {
        return;
    }

    let pump = spawn_frame_pump(events, tx.clone(), subs.clone(), cap.clone());
    // Capture scrollback concurrently with command intake, but reply in request order.
    let mut scrolls = ScrollReplies::new();

    loop {
        tokio::select! {
            biased;
            invalidated = auth_invalidated(&mut auth_changes, &cap) => {
                if invalidated { break; }
            }
            sent = scrolls.send_next(&tx, &cap), if scrolls.has_pending() => {
                if !sent { break; }
            }
            msg = rx.next() => {
                let Some(Ok(msg)) = msg else { break };
                let Message::Binary(bytes) = msg else { continue };
                let Ok(cm) = super::client_message::decode(&bytes) else {
                    tracing::debug!("invalid Protobuf command");
                    continue;
                };

                if let ClientMsg::Scroll(req) = cm {
                    scrolls.enqueue(req, &m, &cap);
                    continue;
                }

                // `Sub` can answer immediately with a screen. Drain older scroll responses first
                // so direct responses retain the same order as the incoming commands.
                if matches!(&cm, ClientMsg::Sub { .. })
                    && !scrolls.flush(&tx, &cap).await { break; }
                if !handle_client_msg(cm, &m, &cap, &tx, &subs).await { break; }
            }
        }
    }

    // Clear subscriptions immediately because the frame task also owns this Arc.
    // Aborting the task does not guarantee immediate release of its watch guards.
    subs.lock().await.clear();
    pump.abort();
}

async fn auth_invalidated(changes: &mut broadcast::Receiver<AuthChange>, cap: &Cap) -> bool {
    loop {
        match changes.recv().await {
            Ok(AuthChange::GrantsRevoked) => {
                if !cap.is_valid() {
                    return true;
                }
            }
            Ok(AuthChange::RootTokenChanged) => {
                if cap.may_create() {
                    return true;
                }
            }
            Err(_) => return true,
        }
    }
}

#[cfg(test)]
#[path = "ws_tests.rs"]
mod tests;
