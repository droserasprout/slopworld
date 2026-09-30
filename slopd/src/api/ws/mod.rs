//! WebSocket upgrade, authentication lifetime, subscriptions, and ordered scroll replies.
//! commands owns dispatch; outbound owns snapshots, frame coalescing, and socket writes.

mod commands;
mod outbound;

use commands::handle_client_msg;
use outbound::{send, send_initial_snapshot, spawn_frame_pump};

use std::collections::{HashMap, VecDeque};
use std::sync::Arc;

use crate::grant::{Cap, Level};
use axum::extract::State;
use axum::extract::ws::{Message, WebSocket, WebSocketUpgrade};
use axum::http::{HeaderMap, StatusCode};
use axum::response::IntoResponse;
use futures::StreamExt;
use tokio::sync::{Mutex, OwnedSemaphorePermit, Semaphore, broadcast};
use tokio::task::JoinHandle;

use crate::session::{AuthChange, Event, EventMessage, ScreenView, WatchGuard};

use super::types::{ClientMsg, ScrollReq};
use super::{Mgr, err, presented_token};

type WsTx = Arc<Mutex<futures::stream::SplitSink<WebSocket, Message>>>;
type WsSubs = Arc<Mutex<HashMap<String, WatchGuard>>>;

/// Owns bounded scroll captures and their ordered replies for one connection.
struct ScrollReplies {
    slots: Arc<Semaphore>,
    pending: VecDeque<PendingScroll>,
}

struct PendingScroll {
    capture: JoinHandle<Option<ScreenView>>,
    ready: Option<Arc<EventMessage>>,
    // A completed capture still owns its slot until its reply is sent or skipped.
    _permit: OwnedSemaphorePermit,
}

impl ScrollReplies {
    fn new() -> Self {
        Self {
            slots: Arc::new(Semaphore::new(4)),
            pending: VecDeque::new(),
        }
    }

    fn enqueue(&mut self, req: ScrollReq, m: &Mgr, cap: &Cap) -> bool {
        let Ok(permit) = self.slots.clone().try_acquire_owned() else {
            return false;
        };
        let manager = m.clone();
        let cap = cap.clone();
        let capture = tokio::spawn(async move {
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
        });
        self.pending.push_back(PendingScroll {
            capture,
            ready: None,
            _permit: permit,
        });
        true
    }

    fn has_pending(&self) -> bool {
        !self.pending.is_empty()
    }

    async fn send_next(&mut self, tx: &WsTx, cap: &Cap) -> bool {
        let Some(pending) = self.pending.front_mut() else {
            return true;
        };
        if pending.ready.is_none() {
            match (&mut pending.capture).await {
                Ok(Some(screen)) => {
                    pending.ready = Some(EventMessage::new(Event::Screen { screen }))
                }
                Ok(None) => {
                    self.pending.pop_front();
                    return true;
                }
                Err(error) => {
                    tracing::debug!("scroll task ended: {error}");
                    self.pending.pop_front();
                    return true;
                }
            }
        }
        let event = pending.ready.as_ref().expect("capture returned a screen");
        if send(tx, cap, event).await.is_err() {
            return false;
        }
        self.pending.pop_front();
        true
    }
}

impl Drop for ScrollReplies {
    fn drop(&mut self) {
        for pending in &self.pending {
            pending.capture.abort();
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
            .into_response();
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
    let mut deferred = VecDeque::new();

    loop {
        if !scrolls.has_pending()
            && let Some(cm) = deferred.pop_front()
        {
            if let ClientMsg::Scroll(req) = cm {
                if !scrolls.enqueue(req, &m, &cap) {
                    tracing::debug!("closing WebSocket after scroll queue overload");
                    break;
                }
            } else if !Box::pin(handle_client_msg(cm, &m, &cap, &tx, &subs)).await {
                break;
            }
            continue;
        }
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
                if matches!(msg, Message::Close(_)) { break; }
                let Message::Binary(bytes) = msg else { continue };
                let Ok(cm) = super::client_message::decode(&bytes) else {
                    tracing::debug!("invalid Protobuf command");
                    continue;
                };

                if !deferred.is_empty() {
                    if deferred.len() >= 16 {
                        tracing::debug!("closing WebSocket after deferred command overload");
                        break;
                    }
                    deferred.push_back(cm);
                    continue;
                }
                if let ClientMsg::Scroll(req) = cm {
                    // Overload closes the connection; no request disappears without a reply.
                    if !scrolls.enqueue(req, &m, &cap) {
                        tracing::debug!("closing WebSocket after scroll queue overload");
                        break;
                    }
                    continue;
                }
                if matches!(&cm, ClientMsg::Sub { .. }) && scrolls.has_pending() {
                    deferred.push_back(cm);
                    continue;
                }
                if !Box::pin(handle_client_msg(cm, &m, &cap, &tx, &subs)).await { break; }
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
mod tests;
