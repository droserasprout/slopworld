use std::collections::{HashMap, VecDeque};
use std::sync::Arc;
use std::time::Duration;

use crate::grant::{Cap, Level};
use anyhow::bail;
use axum::extract::ws::{Message, WebSocket, WebSocketUpgrade};
use axum::extract::State;
use axum::http::{HeaderMap, StatusCode};
use axum::response::IntoResponse;
use futures::{SinkExt, StreamExt};
use tokio::sync::{broadcast, Mutex, Semaphore};
use tokio::task::JoinHandle;

use crate::session::{AuthChange, Event, EventMessage, ScreenView, WatchGuard};

use super::types::{
    AudioReq, AudioSelection, BreadcrumbReq, ClientMsg, KeysReq, MouseReq, PasteReq, ResizeReq,
    ScrollReq,
};
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
    // The client sends the header only with the upgrade request.
    // Resolve the capability here and pass it to the message loop.
    // Grants use the same socket endpoint as the mod, with restricted access. Reject invalid tokens before upgrading.
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

/// Filter events by socket capability. Root receives all events.
/// Scoped grants receive permitted sessions and screens.
/// Exclude projects, library, usage, audio, and jukebox catalog data from scoped connections.
fn scope_event(cap: &Cap, ev: Arc<EventMessage>) -> Option<Arc<EventMessage>> {
    if !cap.is_valid() {
        return None;
    }
    match cap {
        Cap::Root => Some(ev),
        Cap::Scoped(_) => match ev.event() {
            Event::Sessions { sessions } => Some(EventMessage::new(Event::Sessions {
                sessions: sessions
                    .iter()
                    .filter(|s| cap.can_see(&s.name, s.host))
                    .cloned()
                    .collect(),
            })),
            Event::Screen { screen } => cap.can_see(&screen.name, false).then_some(ev),
            Event::Capabilities { .. }
            | Event::Projects { .. }
            | Event::Library { .. }
            | Event::Usage { .. }
            | Event::Audio { .. }
            | Event::Jukebox { .. } => None,
        },
    }
}

async fn ws_run(socket: WebSocket, m: Mgr, cap: Cap, generation: u64) {
    let (tx, mut rx) = socket.split();
    let tx = Arc::new(Mutex::new(tx));
    let mut auth_changes = m.auth_changes();
    if m.auth_generation() != generation {
        return;
    }
    // Store a WatchGuard for each subscription.
    // A guard enables rendering at the reader rate. Release it when the subscription ends.
    let subs: WsSubs = Arc::new(Mutex::new(HashMap::new()));

    // Subscribe before sending the initial snapshot.
    // A session can start during snapshot preparation. Subscribing later could lose that update until the client reconnects.
    let events = m.events.subscribe();
    if !send_initial_snapshot(&tx, &m, &cap).await {
        return;
    }

    let pump = spawn_frame_pump(events, tx.clone(), subs.clone(), cap.clone());
    // Scrollback snapshots can take time for large panes.
    // Limit concurrent captures without blocking receipt of keys, resize commands, or later wheel requests.
    // Return results in request order.
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

/// Include usage, projects, and library in the initial snapshot because their updates occur only after changes.
/// Otherwise, a newly connected mod could show no data until the next poll.
/// Apply the same scope filter as the event loop.
/// Scoped connections receive only permitted events and session names.
async fn send_initial_snapshot(tx: &WsTx, m: &Mgr, cap: &Cap) -> bool {
    if let Some(ev) = scope_event(
        cap,
        EventMessage::new(Event::Capabilities {
            capabilities: crate::runtime::capabilities(),
        }),
    ) {
        if send(tx, cap, &ev).await.is_err() {
            return false;
        }
    }
    if let Some(ev) = scope_event(
        cap,
        EventMessage::new(Event::Sessions {
            sessions: m.views().await,
        }),
    ) {
        if send(tx, cap, &ev).await.is_err() {
            return false;
        }
    }
    for ev in [
        EventMessage::new(Event::Usage {
            usage: m.usage().await,
        }),
        EventMessage::new(Event::Projects {
            projects: m.projects().await,
        }),
        EventMessage::new(Event::Library {
            library: m.library().await,
        }),
        // On connect too, and for the same reason. A game that has just come up has to learn
        // whether the music it asked for last time is playing.
        EventMessage::new(Event::Audio {
            audio: m.music_state().await,
        }),
        EventMessage::new(Event::Jukebox {
            jukebox: crate::jukebox::catalog(),
        }),
    ] {
        if let Some(ev) = scope_event(cap, ev) {
            let _ = send(tx, cap, &ev).await;
        }
    }
    true
}

/// Combine screen updates for each client at the monitor interval.
/// Keep only the latest pending frame for each pane. Send it at the next interval.
/// Preserve individual events of other types.
fn spawn_frame_pump(
    mut events: broadcast::Receiver<Arc<EventMessage>>,
    tx: WsTx,
    subs: WsSubs,
    cap: Cap,
) -> JoinHandle<()> {
    // A half-frame hold reduces capture-to-send delay while the reader's
    // separate 16 ms limit still controls the rate of captured frames.
    const FRAME_COALESCE: Duration = Duration::from_millis(8);

    tokio::spawn(async move {
        use tokio::time::{sleep_until, Instant as TokioInstant};

        let mut last_screen = TokioInstant::now() - FRAME_COALESCE;
        // One slot per pane, not one global slot. A socket can subscribe to several panes, and a
        // frame for one must not overwrite a held frame for another.
        let mut pending: HashMap<String, Arc<EventMessage>> = HashMap::new();
        loop {
            // Start the timer only when a frame is pending.
            // Otherwise, an expired deadline would cause repeated immediate wakeups.
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
                        if let Event::Screen { ref screen } = *ev.event() {
                            if !subs.lock().await.contains_key(&screen.name) {
                                continue;
                            }
                            let due = TokioInstant::now()
                                .saturating_duration_since(last_screen) >= FRAME_COALESCE;
                            if due {
                                // Sending now, drop any held frame for this pane: it is
                                // older and must not flush after the newer one.
                                pending.remove(&screen.name);
                                if send(&tx, &cap, &ev).await.is_err() {
                                    break;
                                }
                                last_screen = TokioInstant::now();
                            } else {
                                // Keep the latest frame for this pane.
                                pending.insert(screen.name.clone(), ev);
                            }
                        } else {
                            // A non-screen event (sessions, usage, quit) must not wait
                            // behind a stale frame, so anything held goes out first.
                            if !pending.is_empty() {
                                for (_, pe) in std::mem::take(&mut pending) {
                                    if send(&tx, &cap, &pe).await.is_err() {
                                        break;
                                    }
                                }
                                last_screen = TokioInstant::now();
                            }
                            if send(&tx, &cap, &ev).await.is_err() {
                                break;
                            }
                        }
                    }
                    // A slow client can miss frames. The next capture restores synchronization.
                    Err(tokio::sync::broadcast::error::RecvError::Lagged(_)) => continue,
                    Err(_) => break,
                },
                _ = flush => {
                    if !pending.is_empty() {
                        for (_, pe) in std::mem::take(&mut pending) {
                            if send(&tx, &cap, &pe).await.is_err() {
                                break;
                            }
                        }
                        last_screen = TokioInstant::now();
                    }
                }
            }
        }
    })
}

/// Handle one client message. Ignore unauthorized messages without a response.
/// A failed direct response indicates a closed socket and tells the caller to stop reading.
async fn handle_client_msg(cm: ClientMsg, m: &Mgr, cap: &Cap, tx: &WsTx, subs: &WsSubs) -> bool {
    if let ClientMsg::Sub { name } = cm {
        return handle_sub(name, m, cap, tx, subs).await;
    }
    let trace = match &cm {
        ClientMsg::Keys(r) => crate::latency::InputTrace::begin(&r.trace_id),
        ClientMsg::Mouse(r) => crate::latency::InputTrace::begin(&r.trace_id),
        ClientMsg::Paste(r) => crate::latency::InputTrace::begin(&r.trace_id),
        _ => None,
    };
    let shared = matches!(
        &cm,
        ClientMsg::Keys(_)
            | ClientMsg::Resize(_)
            | ClientMsg::Mouse(_)
            | ClientMsg::Paste(_)
            | ClientMsg::Breadcrumb(_)
    );
    let operation = async {
        match cm {
            ClientMsg::Redraw { cols, rows } => handle_redraw(m, cap, cols.zip(rows)),
            ClientMsg::Sub { .. } => unreachable!(),
            ClientMsg::Unsub { name } => handle_unsub(name, subs).await,
            ClientMsg::Keys(req) => handle_keys(req, m, cap).await,
            ClientMsg::Resize(req) => handle_resize(req, m, cap).await,
            // Scroll requests are intercepted by ws_run so capture work can run in its bounded
            // lane without stopping command intake.
            ClientMsg::Scroll(_) => {}
            ClientMsg::Mouse(req) => handle_mouse(req, m, cap).await,
            ClientMsg::Paste(req) => handle_paste(req, m, cap).await,
            ClientMsg::Breadcrumb(req) => handle_breadcrumb(req, m, cap).await,
            ClientMsg::Audio(req) => {
                if let Some(audio) = handle_audio(req, m, cap).await {
                    // The game may already be closing its read side after sending Stop.
                    // A failed audio reply must not prevent draining that queued command.
                    m.emit(Event::Audio { audio });
                }
            }
        }
        true
    };
    if shared {
        if let Some(trace) = trace {
            m.session_read_operation(crate::latency::CURRENT.scope(Some(trace), operation))
                .await
        } else {
            m.session_read_operation(operation).await
        }
    } else {
        m.session_operation(operation).await
    }
}

fn handle_redraw(m: &Mgr, cap: &Cap, shape: Option<(u16, u16)>) {
    // A panel refresh reaches every live tmux session, so it is deliberately root-only. A
    // scoped grant must never be able to cause work or visible flicker in sessions it cannot
    // see.
    if cap.may_create() {
        m.request_redraw(shape);
    }
}

async fn handle_sub(name: String, m: &Mgr, cap: &Cap, tx: &WsTx, subs: &WsSubs) -> bool {
    let screen = m
        .session_read_operation(async {
            if !m.cap_ok(cap, &name, Level::Ro).await {
                return None;
            }
            subs.lock().await.insert(name.clone(), m.watching(&name));
            m.clear_bell(&name).await;
            m.screen(&name).await
        })
        .await;
    // Socket backpressure must not hold the session boundary.
    if let Some(s) = screen {
        return send(tx, cap, &EventMessage::new(Event::Screen { screen: s }))
            .await
            .is_ok();
    }
    true
}

async fn handle_unsub(name: String, subs: &WsSubs) {
    subs.lock().await.remove(&name);
}

async fn handle_keys(req: KeysReq, m: &Mgr, cap: &Cap) {
    if !m.cap_ok(cap, &req.name, Level::Rw).await {
        return;
    }
    m.send_keys(&req.name, req.keys, req.literal, req.random_tips)
        .await;
}

async fn handle_resize(req: ResizeReq, m: &Mgr, cap: &Cap) {
    if !m.cap_ok(cap, &req.name, Level::Rw).await {
        return;
    }
    if let Err(e) = m.resize(&req.name, req.cols, req.rows).await {
        tracing::debug!("resize: {e:#}");
    }
}

async fn handle_mouse(req: MouseReq, m: &Mgr, cap: &Cap) {
    if !m.cap_ok(cap, &req.name, Level::Rw).await {
        return;
    }
    let Some(action) = crate::emu::MouseAction::parse(&req.action) else {
        tracing::debug!("unknown mouse action: {}", req.action);
        return;
    };
    let ev = crate::emu::MouseInput {
        action,
        button: req.button,
        col: req.col,
        row: req.row,
    };
    m.send_mouse(&req.name, ev, req.count).await;
}

async fn handle_paste(req: PasteReq, m: &Mgr, cap: &Cap) {
    if !m.cap_ok(cap, &req.name, Level::Rw).await {
        return;
    }
    // Authorization is already complete. Report delivery errors so failed pastes are visible in the log.
    if let Err(e) = m.paste(&req.name, &req.text).await {
        tracing::warn!("paste to {}: {e:#}", req.name);
    }
}

async fn handle_breadcrumb(req: BreadcrumbReq, m: &Mgr, cap: &Cap) {
    if !m.cap_ok(cap, &req.name, Level::Rw).await {
        return;
    }
    if let Err(e) = m
        .paste_breadcrumb(&req.name, &req.breadcrumb, req.random_tips)
        .await
    {
        tracing::warn!("breadcrumb to {}: {e:#}", req.name);
    }
}

async fn handle_audio(req: AudioReq, m: &Mgr, cap: &Cap) -> Option<crate::audio::AudioState> {
    if !cap.may_create() {
        return None;
    }
    let spotify = req
        .selection
        .as_ref()
        .and_then(Option::as_ref)
        .is_some_and(|s| s.ncspot);
    let _transition = m.music.transition.lock().await;
    let result = match req.selection {
        Some(Some(selection)) if selection.ncspot => {
            if selection.station.is_some() || selection.stream.is_some() || selection.file.is_some()
            {
                Err(anyhow::anyhow!(
                    "Select ncspot or another audio source, not both."
                ))
            } else {
                m.open_ncspot(None, None, Some(req.volume))
                    .await
                    .map(|_| ())
            }
        }
        Some(selection) => match m.stop_ncspot().await {
            Err(e) => Err(e),
            Ok(()) => match selection {
                Some(s) => match resolve_audio_source(s) {
                    Ok(source) => {
                        m.music.audio.play(&source, req.volume);
                        Ok(())
                    }
                    Err(e) => Err(e),
                },
                None => {
                    m.music.audio.stop();
                    Ok(())
                }
            },
        },
        None => m.music_volume(req.volume).await,
    };
    if let Err(e) = result {
        let error = format!("{e:#}");
        m.music.audio.reject(error.clone());
        if spotify {
            return Some(crate::audio::AudioState {
                source: Some("ncspot".into()),
                error: Some(error),
                ..Default::default()
            });
        }
    }
    // Reply even when the player was already open and its broadcast state did not change.
    // Launch and shutdown now use one ordered socket. No late HTTP launch can undo a stop.
    if spotify {
        Some(m.music_state().await)
    } else {
        None
    }
}

fn resolve_audio_source(selection: AudioSelection) -> anyhow::Result<String> {
    anyhow::ensure!(
        !selection.ncspot,
        "The daemon does not decode audio from ncspot."
    );
    match (selection.station, selection.stream, selection.file) {
        (Some(station), Some(stream), None) => crate::jukebox::catalog()
            .resolve(&station, &stream)
            .map_err(|e| anyhow::anyhow!("jukebox selection rejected: {e:#}")),
        (None, None, Some(file)) => Ok(file),
        _ => bail!("Select a station and stream, or select a file."),
    }
}

async fn send(tx: &WsTx, cap: &Cap, ev: &EventMessage) -> Result<(), axum::Error> {
    let _perf = crate::perf::timer("websocket-send");
    let started = crate::perf::enabled().then(std::time::Instant::now);
    let mut text = ev
        .encoded()
        .map_err(|e| axum::Error::new(std::io::Error::other(e)))?
        .to_vec();
    let mut tx = tx.lock().await;
    if !cap.is_valid() {
        return Err(axum::Error::new(std::io::Error::other(
            "capability revoked",
        )));
    }
    if let Event::Screen { screen } = ev.event() {
        if !screen.input_timings.is_empty() {
            use prost::Message as _;
            let mut message = ev
                .event()
                .to_protobuf()
                .map_err(|e| axum::Error::new(std::io::Error::other(e.to_string())))?;
            if let Some(crate::shared::wire::event::Payload::Screen(s)) = &mut message.payload {
                let at = crate::latency::now();
                for t in &mut s.input_timings {
                    t.send_us = at;
                }
            }
            text = message.encode_to_vec();
        }
    }
    crate::perf::count("websocket-bytes", text.len() as u64);
    let result = tx.send(Message::Binary(text)).await;
    if let Some(started) = started {
        tracing::debug!(
            target: "slopd::perf",
            lane = "websocket-send",
            elapsed_us = started.elapsed().as_micros() as u64,
            "websocket event sent"
        );
    }
    result
}

#[cfg(test)]
#[path = "ws_tests.rs"]
mod tests;
