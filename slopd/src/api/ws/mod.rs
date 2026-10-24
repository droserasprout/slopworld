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
use tokio::sync::{Mutex, broadcast};
use tokio::task::JoinHandle;

use crate::application::AuthChange;
use crate::application::{Event, EventMessage};
use crate::session::{ScreenView, WatchGuard};

use super::types::{ClientMsg, ScrollReq};
use super::{App, err, presented_token};

/// The writer carries the credential epoch to every command and publication path.
#[derive(Clone)]
struct WsTx {
    sink: Arc<Mutex<futures::stream::SplitSink<WebSocket, Message>>>,
    authority: Arc<crate::application::ConfigAuthority>,
    root_generation: Option<u64>,
}

impl WsTx {
    fn new(
        sink: futures::stream::SplitSink<WebSocket, Message>,
        m: &App,
        cap: &Cap,
        root_generation: u64,
    ) -> Self {
        Self {
            sink: Arc::new(Mutex::new(sink)),
            authority: m.config_authority.clone(),
            root_generation: cap.may_create().then_some(root_generation),
        }
    }

    fn is_valid(&self, cap: &Cap) -> bool {
        cap.is_valid()
            && self
                .root_generation
                .is_none_or(|epoch| epoch == self.authority.root_generation())
    }
}

impl std::ops::Deref for WsTx {
    type Target = Mutex<futures::stream::SplitSink<WebSocket, Message>>;

    fn deref(&self) -> &Self::Target {
        &self.sink
    }
}
type WsSubs = Arc<Mutex<HashMap<String, WatchGuard>>>;

/// Owns bounded scroll captures and their ordered replies for one connection.
struct ScrollReplies {
    pending: VecDeque<PendingScroll>,
}

const MAX_PENDING_SCROLLS: usize = 4;

struct PendingScroll {
    capture: JoinHandle<Option<ScreenView>>,
    ready: Option<Arc<EventMessage>>,
}

impl ScrollReplies {
    fn new() -> Self {
        Self {
            pending: VecDeque::new(),
        }
    }

    fn enqueue(&mut self, req: ScrollReq, m: &App, cap: &Cap, tx: &WsTx) -> bool {
        if self.pending.len() >= MAX_PENDING_SCROLLS {
            return false;
        }
        let manager = m.clone();
        let cap = cap.clone();
        let tx = tx.clone();
        let capture = tokio::spawn(async move {
            let operation = async {
                if !tx.is_valid(&cap) || !manager.sessions.cap_ok(&cap, &req.name, Level::Ro).await
                {
                    return None;
                }
                manager
                    .sessions
                    .scroll_capture(&req.name, req.off, req.request_id)
                    .await
            };
            if cap.may_create() {
                let _terminal = manager.sessions.terminal_input_guard(&req.name).await?;
                operation.await
            } else {
                manager.sessions.session_read_operation(operation).await
            }
        });
        self.pending.push_back(PendingScroll {
            capture,
            ready: None,
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
        #[expect(
            clippy::expect_used,
            reason = "capture success installs the ready event; all other outcomes return before publication"
        )]
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
    State(m): State<App>,
    headers: HeaderMap,
    ws: WebSocketUpgrade,
) -> impl IntoResponse {
    // Credentials arrive only during upgrade. Reject changes during resolution,
    // then carry the capability and generation into the connection lifetime.
    let generation_before = m.config_authority.auth_generation();
    let root_generation = m.config_authority.root_generation();
    let Some(cap) = m
        .config_authority
        .resolve_cap(presented_token(&headers).as_deref())
        .await
    else {
        return err(
            StatusCode::UNAUTHORIZED,
            "The authentication token is missing or invalid.",
        )
        .into_response();
    };
    let generation = m.config_authority.auth_generation();
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
        // A client image is at most 20 MiB, plus its Protobuf envelope. The mod
        // sends one binary frame, so the frame limit must match the message limit.
        .max_frame_size(32 * 1024 * 1024)
        .max_message_size(32 * 1024 * 1024)
        .on_upgrade(move |socket| ws_run(socket, m, cap, generation, root_generation))
        .into_response()
}

async fn ws_run(socket: WebSocket, m: App, cap: Cap, generation: u64, root_generation: u64) {
    let (tx, rx) = socket.split();
    let tx = WsTx::new(tx, &m, &cap, root_generation);
    let mut auth_changes = m.config_authority.auth_changes();
    if m.config_authority.auth_generation() != generation {
        return;
    }
    // Each subscription owns a guard that keeps its reader rendering at the watched rate.
    let subs: WsSubs = Arc::new(Mutex::new(HashMap::new()));

    // Subscribe before snapshot preparation so concurrent session starts cannot be lost.
    let events = m.services.subscribe();
    if !initialize_snapshot(&tx, &m, &cap, &mut auth_changes).await {
        return;
    }

    let pump = spawn_frame_pump(events, tx.clone(), subs.clone(), cap.clone());
    let mut intake = CommandIntake {
        tx,
        rx,
        subs: subs.clone(),
        auth_changes,
        scrolls: ScrollReplies::new(),
    };
    intake.run(&m, &cap).await;

    // Clear subscriptions immediately because the frame task also owns this Arc.
    // Aborting the task does not guarantee immediate release of its watch guards.
    subs.lock().await.clear();
    pump.abort();
}

/// Initialization has no committed effects, so revocation can cancel stalled reads/writes.
async fn initialize_snapshot(
    tx: &WsTx,
    m: &App,
    cap: &Cap,
    changes: &mut broadcast::Receiver<AuthChange>,
) -> bool {
    tokio::select! {
        biased;
        _ = auth_invalidated(changes, cap) => false,
        sent = send_initial_snapshot(tx, m, cap) => sent,
    }
}

/// Socket intake remains independent of history capture completion. Replies keep
/// their own FIFO; subscriptions and input retain their wire arrival order.
struct CommandIntake {
    tx: WsTx,
    rx: futures::stream::SplitStream<WebSocket>,
    subs: WsSubs,
    auth_changes: broadcast::Receiver<AuthChange>,
    scrolls: ScrollReplies,
}

impl CommandIntake {
    async fn run(&mut self, m: &App, cap: &Cap) {
        let mut watch_changes = m.sessions.watch_changes();
        // Include removals that happened before this receiver was installed.
        self.subs.lock().await.retain(|_, guard| guard.is_current());
        loop {
            tokio::select! {
                biased;
                invalidated = auth_invalidated(&mut self.auth_changes, cap) => {
                    if invalidated { break; }
                }
                changed = watch_changes.changed() => {
                    if changed.is_err() { break; }
                    self.subs.lock().await.retain(|_, guard| guard.is_current());
                }
                sent = self.scrolls.send_next(&self.tx, cap), if self.scrolls.has_pending() => {
                    if !sent { break; }
                }
                msg = self.rx.next() => {
                    let Some(Ok(msg)) = msg else { break };
                    if matches!(msg, Message::Close(_)) { break; }
                    let Message::Binary(bytes) = msg else { continue };
                    let Ok(cm) = super::client_message::decode(&bytes) else {
                        tracing::debug!("invalid Protobuf command");
                        continue;
                    };

                    if let ClientMsg::Scroll(req) = cm {
                        // Overload closes the connection; no request disappears without a reply.
                        if !self.scrolls.enqueue(req, m, cap, &self.tx) {
                            tracing::debug!("closing WebSocket after scroll queue overload");
                            break;
                        }
                        continue;
                    }
                    // History replies carry their session and request identity. Keep
                    // their FIFO order, but never put panel switches or input behind them.
                    if !Box::pin(handle_client_msg(cm, m, cap, &self.tx, &self.subs)).await { break; }
                }
            }
        }
    }
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
