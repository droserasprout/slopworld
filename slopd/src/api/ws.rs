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
};
use super::{err, presented_token, Mgr};

type WsTx = Arc<Mutex<futures::stream::SplitSink<WebSocket, Message>>>;
type WsSubs = Arc<Mutex<HashMap<String, WatchGuard>>>;

pub(super) async fn ws_upgrade(
    State(m): State<Mgr>,
    headers: HeaderMap,
    ws: WebSocketUpgrade,
) -> impl IntoResponse {
    // The header rides on the upgrade only, so the capability is resolved here and carried into
    // the pump rather than read off a request per message. A grant drives what it may from the
    // same socket the mod uses; a bad token never upgrades.
    let generation_before = m.auth_generation();
    let cap = match m.resolve_cap(presented_token(&headers).as_deref()).await {
        Some(c) => c,
        None => return err(StatusCode::UNAUTHORIZED, "bad token").into_response(),
    };
    let generation = m.auth_generation();
    if generation != generation_before {
        return err(StatusCode::UNAUTHORIZED, "capability changed").into_response();
    }
    if !headers
        .get("sec-websocket-protocol")
        .and_then(|v| v.to_str().ok())
        .is_some_and(|v| v.split(',').any(|p| p.trim() == "slopworld.protobuf.v2"))
    {
        return err(
            StatusCode::BAD_REQUEST,
            "expected slopworld.protobuf.v2 WebSocket subprotocol",
        )
        .into_response();
    }
    ws.protocols(["slopworld.protobuf.v2"])
        .on_upgrade(move |socket| ws_run(socket, m, cap, generation))
        .into_response()
}

/// Filters events for a socket capability. Root sees all; scoped grants see named sessions and
/// screens, but not projects, library, usage, audio, or jukebox catalog data.
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
    // Held for the life of the pump so the manager knows how many clients are
    // attached.
    let _client = m.client_joined();

    let (tx, mut rx) = socket.split();
    let tx = Arc::new(Mutex::new(tx));
    let mut auth_changes = m.auth_changes();
    if m.auth_generation() != generation {
        return;
    }
    // A `WatchGuard` apiece rather than bare names: the daemon renders a pane at a reader's
    // rate only while somebody is holding one, and this socket has several ways out.
    let subs: WsSubs = Arc::new(Mutex::new(HashMap::new()));

    // Subscribe before sending the initial snapshot. A session can be created while the
    // snapshot is being assembled; subscribing after it would lose that live update until
    // the client reconnects.
    let events = m.events.subscribe();
    if !send_initial_snapshot(&tx, &m, &cap).await {
        return;
    }

    let pump = spawn_frame_pump(events, tx.clone(), subs.clone(), cap.clone());
    // Scrollback is an emulator snapshot and can be expensive on a large pane. Keep a small
    // bounded lane for it so one wheel request does not stop intake of keys, resize, or newer
    // wheel requests. Results stay in request order below.
    let scroll_slots = Arc::new(Semaphore::new(4));
    let mut pending_scrolls: VecDeque<JoinHandle<Option<ScreenView>>> = VecDeque::new();

    loop {
        tokio::select! {
            biased;
            invalidated = auth_invalidated(&mut auth_changes, &cap) => {
                if invalidated { break; }
            }
            result = async {
                match pending_scrolls.front_mut() {
                    Some(task) => Some(task.await),
                    None => std::future::pending().await,
                }
            } => {
                let Some(result) = result else { unreachable!() };
                pending_scrolls.pop_front();
                match result {
                    Ok(Some(screen)) => {
                        if send(&tx, &cap, &EventMessage::new(Event::Screen { screen })).await.is_err() { break; }
                    }
                    Ok(None) => {}
                    Err(error) => tracing::debug!("scroll task ended: {error}"),
                }
            }
            msg = rx.next() => {
                let Some(Ok(msg)) = msg else { break };
                let Message::Binary(bytes) = msg else { continue };
                let Ok(cm) = super::client_message::decode(&bytes) else {
                    tracing::debug!("invalid Protobuf command");
                    continue;
                };

                if let ClientMsg::Scroll(req) = cm {
                    let permit = match scroll_slots.clone().try_acquire_owned() {
                        Ok(permit) => permit,
                        Err(_) => continue,
                    };
                    let manager = m.clone();
                    let cap = cap.clone();
                    pending_scrolls.push_back(tokio::spawn(async move {
                        let _permit = permit;
                        manager.session_operation(async {
                            if !manager.cap_ok(&cap, &req.name, Level::Ro).await { return None; }
                            manager.scroll_capture(&req.name, req.off, req.request_id).await
                        }).await
                    }));
                    continue;
                }

                // `Sub` can answer immediately with a screen. Drain older scroll responses first
                // so direct responses retain the same order as the incoming commands.
                if matches!(&cm, ClientMsg::Sub { .. })
                    && !flush_scrolls(&tx, &cap, &mut pending_scrolls).await { break; }
                if !handle_client_msg(cm, &m, &cap, &tx, &subs).await { break; }
            }
        }
    }

    for pending in pending_scrolls {
        pending.abort();
    }

    // Explicitly, rather than with the table: the pump holds the other half of that `Arc` and
    // an aborted task is dropped when the runtime gets round to it, which is not when the
    // agent stopped being watched.
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

/// Usage, projects and library ride along because they speak only on a change: a mod
/// attaching between polls would otherwise draw nothing for a minute. Each goes through the
/// same scope filter the pump uses, so a grant's socket gets its filtered session list and
/// none of the mod's wider view - `scope_event` drops what it may not see.
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
        // On connect too, and for the same reason: a game that has just come up has to learn
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

/// Coalesce screen frames per client to the monitor cadence: newest wins, and the pending
/// frame flushes on the beat. Other event types remain uncoalesced.
fn spawn_frame_pump(
    mut events: broadcast::Receiver<Arc<EventMessage>>,
    tx: WsTx,
    subs: WsSubs,
    cap: Cap,
) -> JoinHandle<()> {
    const FRAME_COALESCE: Duration = Duration::from_millis(16);

    tokio::spawn(async move {
        use tokio::time::{sleep_until, Instant as TokioInstant};

        let mut last_screen = TokioInstant::now() - FRAME_COALESCE;
        // One slot per pane, not one global slot: a socket can subscribe to several
        // panes, and a frame for one must not overwrite a held frame for another.
        let mut pending: HashMap<String, Arc<EventMessage>> = HashMap::new();
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
                                // Newest wins for this pane; a still pane never reads stale.
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
                    // A slow client misses frames; the next capture resyncs it.
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

/// Handles one client message. Unauthorized messages are dropped silently; a failed direct
/// response means the socket is gone and tells the caller to stop reading it.
async fn handle_client_msg(cm: ClientMsg, m: &Mgr, cap: &Cap, tx: &WsTx, subs: &WsSubs) -> bool {
    if let ClientMsg::Sub { name } = cm {
        return handle_sub(name, m, cap, tx, subs).await;
    }
    m.session_operation(async {
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
    })
    .await
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
        .session_operation(async {
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
    // A paste checks first, so anything left is a paste that did not land - and the only other
    // sign of one is the operator noticing nothing arrived.
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
    let _transition = m.music_transition.lock().await;
    let result = match req.selection {
        Some(Some(selection)) if selection.ncspot => {
            if selection.station.is_some() || selection.stream.is_some() || selection.file.is_some()
            {
                Err(anyhow::anyhow!(
                    "ncspot cannot be combined with another audio source"
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
                        m.audio.play(&source, req.volume);
                        Ok(())
                    }
                    Err(e) => Err(e),
                },
                None => {
                    m.audio.stop();
                    Ok(())
                }
            },
        },
        None => m.music_volume(req.volume).await,
    };
    if let Err(e) = result {
        let error = format!("{e:#}");
        m.audio.reject(error.clone());
        if spotify {
            return Some(crate::audio::AudioState {
                source: Some("ncspot".into()),
                error: Some(error),
                ..Default::default()
            });
        }
    }
    // Reply even when the player was already open and its broadcast state did not change.
    // Launch and shutdown now use one ordered socket; no late HTTP launch can undo a stop.
    if spotify {
        Some(m.music_state().await)
    } else {
        None
    }
}

fn resolve_audio_source(selection: AudioSelection) -> anyhow::Result<String> {
    anyhow::ensure!(!selection.ncspot, "ncspot is not a decoded audio source");
    match (selection.station, selection.stream, selection.file) {
        (Some(station), Some(stream), None) => crate::jukebox::catalog()
            .resolve(&station, &stream)
            .map_err(|e| anyhow::anyhow!("jukebox selection rejected: {e:#}")),
        (None, None, Some(file)) => Ok(file),
        _ => bail!("jukebox selection must name a station/stream or file"),
    }
}

async fn send(tx: &WsTx, cap: &Cap, ev: &EventMessage) -> Result<(), axum::Error> {
    let _perf = crate::perf::timer("websocket-send");
    let started = crate::perf::enabled().then(std::time::Instant::now);
    let text = ev
        .encoded()
        .map_err(|e| axum::Error::new(std::io::Error::other(e)))?
        .to_vec();
    crate::perf::count("websocket-bytes", text.len() as u64);
    let mut tx = tx.lock().await;
    if !cap.is_valid() {
        return Err(axum::Error::new(std::io::Error::other(
            "capability revoked",
        )));
    }
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

async fn flush_scrolls(
    tx: &WsTx,
    cap: &Cap,
    pending: &mut VecDeque<JoinHandle<Option<ScreenView>>>,
) -> bool {
    while let Some(task) = pending.pop_front() {
        match task.await {
            Ok(Some(screen)) => {
                if send(tx, cap, &EventMessage::new(Event::Screen { screen }))
                    .await
                    .is_err()
                {
                    return false;
                }
            }
            Ok(None) => {}
            Err(error) => tracing::debug!("scroll task ended: {error}"),
        }
    }
    true
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::grant::Grant;
    use crate::session::{ScreenView, SessionView, State};
    use std::collections::HashSet;

    #[tokio::test]
    async fn rejected_spotify_selection_replies_without_waiting_for_a_state_change() {
        // Reject advances the process-wide playback generation. Isolate it from the
        // decoder tests, which deliberately keep a generation alive across assertions.
        const CHILD: &str = "SLOPWORLD_TEST_SPOTIFY_REPLY";
        if std::env::var_os(CHILD).is_none() {
            let output = crate::process::run_bounded(
                tokio::process::Command::new(std::env::current_exe().unwrap())
                    .env(CHILD, "1")
                    .args(["--exact", "api::ws::tests::rejected_spotify_selection_replies_without_waiting_for_a_state_change"]),
                Duration::from_secs(10),
                crate::process::CaptureLimits { stdout: 8192, stderr: 8192 },
            ).await.unwrap();
            assert!(output.status.success(), "{output:?}");
            return;
        }
        let manager = crate::session::test_manager_with_socket(
            crate::config::Config::default(),
            format!("slop-audio-reply-{}", uuid::Uuid::new_v4()),
        );
        // Invalid source combinations fail before any external player is launched.
        // Each attempt still needs a reply so the terminal-open UI can release its latch.
        for _ in 0..2 {
            let reply = handle_audio(
                AudioReq {
                    selection: Some(Some(AudioSelection {
                        ncspot: true,
                        file: Some("/tmp/song.mp3".into()),
                        station: None,
                        stream: None,
                    })),
                    volume: 0.42,
                },
                &manager,
                &Cap::Root,
            )
            .await
            .unwrap();
            assert_eq!(reply.source.as_deref(), Some("ncspot"));
            assert!(reply.error.unwrap().contains("cannot be combined"));
            assert!(reply.session.is_none());
        }
    }

    /// Event filtering reads the name and host flag; the rest is filler that
    /// keeps a `SessionView` compiling without pulling in a whole live manager.
    fn view(name: &str) -> SessionView {
        SessionView {
            name: name.to_string(),
            label: String::new(),
            project: String::new(),
            dir: String::new(),
            command: String::new(),
            command_preset: String::new(),
            cmd: None,
            sandbox: Vec::new(),
            persistent_tmp: false,
            auto_resume_pending: false,
            agent: String::new(),
            state: State::Idle,
            alive: true,
            cols: 80,
            rows: 24,
            network: Default::default(),
            dns: Default::default(),
            limits: Default::default(),
            mounts: Vec::new(),
            autostart: false,
            auto_resume: false,
            worker: false,
            parent: String::new(),
            task_id: String::new(),
            durable: false,
            ephemeral: false,
            host: false,
            process_running: false,
            last_change: 0,
            state_since: 0,
            title: String::new(),
            bell: false,
            run_id: 0,
            seq: 0,
        }
    }

    fn scoped(sessions: &[&str], level: Level) -> Cap {
        Cap::Scoped(Grant {
            grantor: "g".to_string(),
            sessions: sessions
                .iter()
                .map(|s| s.to_string())
                .collect::<HashSet<_>>(),
            level,
            revoked: Default::default(),
        })
    }

    fn message(event: Event) -> Arc<EventMessage> {
        EventMessage::new(event)
    }

    fn names(ev: &EventMessage) -> Vec<String> {
        match ev.event() {
            Event::Sessions { sessions } => sessions.iter().map(|s| s.name.clone()).collect(),
            other => panic!("expected a sessions event, got {other:?}"),
        }
    }

    /// Root is the mod: every event reaches it, and a session list is handed on whole.
    #[test]
    fn root_sees_every_event_unfiltered() {
        let sessions = message(Event::Sessions {
            sessions: vec![view("a"), view("b")],
        });
        assert_eq!(
            names(&scope_event(&Cap::Root, sessions).expect("root keeps the event")),
            vec!["a".to_string(), "b".to_string()]
        );

        // Categories a scoped grant never sees still reach root untouched.
        for ev in [
            message(Event::Projects {
                projects: Vec::new(),
            }),
            message(Event::Usage {
                usage: Default::default(),
            }),
            message(Event::Jukebox {
                jukebox: Default::default(),
            }),
        ] {
            assert!(scope_event(&Cap::Root, ev).is_some());
        }
    }

    /// A scoped grant's session list is cut down to the names it may see; the rest never
    /// reach the socket, so the holder does not even learn they exist.
    #[test]
    fn a_scoped_grant_sees_only_the_sessions_it_names() {
        let cap = scoped(&["a", "c", "host"], Level::Ro);
        let mut host = view("host");
        host.host = true;
        let ev = message(Event::Sessions {
            sessions: vec![view("a"), view("b"), view("c"), view("d"), host],
        });
        assert_eq!(
            names(&scope_event(&cap, ev).expect("a filtered list is still an event")),
            vec!["a".to_string(), "c".to_string()]
        );
    }

    /// A grant that names nothing present gets an empty list rather than nothing at all - the
    /// socket still hears that a sessions update happened.
    #[test]
    fn a_scoped_grant_with_no_matches_gets_an_empty_list() {
        let cap = scoped(&["x"], Level::Rw);
        let ev = message(Event::Sessions {
            sessions: vec![view("a"), view("b")],
        });
        assert!(names(&scope_event(&cap, ev).expect("still an event")).is_empty());
    }

    /// The grant subscribes to a session's screen and must keep receiving it; screens are the
    /// one non-session category a scoped socket is allowed to see.
    #[test]
    fn screens_require_a_live_grant_for_the_named_session() {
        let cap = scoped(&["a"], Level::Ro);
        let screen = ScreenView {
            name: "a".to_string(),
            seq: 1,
            cols: 80,
            rows: 24,
            cx: 0,
            cy: 0,
            off: 0,
            history: 0,
            cursor_shape: 0,
            cursor_blink: false,
            app_mouse: false,
            app_drag: false,
            alt_screen: false,
            title: String::new(),
            request_id: 0,
            lines: Vec::new(),
        };
        let event = message(Event::Screen { screen });
        assert!(scope_event(&cap, event.clone()).is_some());
        assert!(scope_event(&scoped(&["other"], Level::Ro), event.clone()).is_none());
        let Cap::Scoped(grant) = cap.clone() else {
            unreachable!()
        };
        let mut grants = crate::grant::Grants::default();
        grants.mint(grant);
        grants.invalidate_session("a");
        assert!(scope_event(&cap, event).is_none());
    }

    /// Everything that is neither a session nor a screen - projects, library, usage, audio,
    /// the jukebox catalog - is host-wide state a scoped grant has no business seeing.
    #[test]
    fn a_scoped_grant_is_denied_host_wide_categories() {
        let cap = scoped(&["a"], Level::Ro);
        let denied = [
            message(Event::Projects {
                projects: Vec::new(),
            }),
            message(Event::Library {
                library: Vec::new(),
            }),
            message(Event::Usage {
                usage: Default::default(),
            }),
            message(Event::Audio {
                audio: Default::default(),
            }),
            message(Event::Jukebox {
                jukebox: Default::default(),
            }),
        ];
        for ev in denied {
            assert!(scope_event(&cap, ev).is_none());
        }
    }

    /// A jukebox pick names a station and a stream together, or names a plain file, and nothing
    /// in between; a plain file passes straight through.
    #[test]
    fn resolve_audio_accepts_a_plain_file() {
        let selection = AudioSelection {
            station: None,
            stream: None,
            file: Some("/tmp/song.mp3".to_string()),
            ncspot: false,
        };
        assert_eq!(resolve_audio_source(selection).unwrap(), "/tmp/song.mp3");
    }

    /// Every half-formed or contradictory combination is refused before it can reach a source.
    #[test]
    fn resolve_audio_rejects_malformed_selections() {
        let cases = [
            (None, None, None),
            (Some("s"), None, None),
            (None, Some("t"), None),
            (Some("s"), Some("t"), Some("f")),
            (None, Some("t"), Some("f")),
        ];
        for (station, stream, file) in cases {
            let selection = AudioSelection {
                station: station.map(str::to_string),
                stream: stream.map(str::to_string),
                file: file.map(str::to_string),
                ncspot: false,
            };
            assert!(
                resolve_audio_source(selection).is_err(),
                "expected {station:?}/{stream:?}/{file:?} to be rejected"
            );
        }
    }
    type ClientSocket = tokio_tungstenite::WebSocketStream<
        tokio_tungstenite::MaybeTlsStream<tokio::net::TcpStream>,
    >;

    struct SocketPair {
        tx: WsTx,
        client: ClientSocket,
        server: JoinHandle<()>,
    }

    impl Drop for SocketPair {
        fn drop(&mut self) {
            self.server.abort();
        }
    }

    async fn socket_pair() -> SocketPair {
        let (ready, socket) = tokio::sync::oneshot::channel();
        let ready = Arc::new(Mutex::new(Some(ready)));
        let app = axum::Router::new().route(
            "/",
            axum::routing::get(move |upgrade: WebSocketUpgrade| {
                let ready = ready.clone();
                async move {
                    upgrade.on_upgrade(move |socket| async move {
                        ready
                            .lock()
                            .await
                            .take()
                            .unwrap()
                            .send(socket)
                            .ok()
                            .unwrap();
                    })
                }
            }),
        );
        let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
        let address = listener.local_addr().unwrap();
        let server = tokio::spawn(async move {
            axum::serve(listener, app).await.unwrap();
        });
        let (client, _) = tokio_tungstenite::connect_async(format!("ws://{address}/"))
            .await
            .unwrap();
        let (tx, _) = socket.await.unwrap().split();
        SocketPair {
            tx: Arc::new(Mutex::new(tx)),
            client,
            server,
        }
    }

    async fn receive(client: &mut ClientSocket) -> crate::shared::wire::event::Payload {
        use prost::Message as _;
        let frame = tokio::time::timeout(Duration::from_secs(2), client.next())
            .await
            .expect("socket response timed out")
            .unwrap()
            .unwrap();
        let tokio_tungstenite::tungstenite::Message::Binary(bytes) = frame else {
            panic!("expected binary event")
        };
        crate::shared::wire::Event::decode(bytes.as_slice())
            .unwrap()
            .payload
            .unwrap()
    }

    fn screen(name: &str, seq: u64) -> ScreenView {
        ScreenView {
            name: name.into(),
            seq,
            cols: 80,
            rows: 24,
            cx: 0,
            cy: 0,
            off: 0,
            history: 0,
            cursor_shape: 0,
            cursor_blink: false,
            app_mouse: false,
            app_drag: false,
            alt_screen: false,
            title: String::new(),
            request_id: 0,
            lines: vec![format!("frame {seq}").into()],
        }
    }

    #[tokio::test]
    async fn initial_snapshots_send_binary_host_state_only_to_root() {
        use crate::shared::wire::event::Payload as P;
        let manager = crate::session::test_manager(crate::config::Config {
            sessions: vec![
                crate::config::SessionCfg {
                    name: "a".into(),
                    ..Default::default()
                },
                crate::config::SessionCfg {
                    name: "b".into(),
                    ..Default::default()
                },
            ],
            ..Default::default()
        });
        manager.sync_from_config().await;
        let mut socket = socket_pair().await;
        assert!(send_initial_snapshot(&socket.tx, &manager, &Cap::Root).await);
        assert!(matches!(
            receive(&mut socket.client).await,
            P::Capabilities(_)
        ));
        let P::Sessions(list) = receive(&mut socket.client).await else {
            panic!("sessions")
        };
        assert_eq!(
            list.sessions
                .iter()
                .map(|s| s.name.as_str())
                .collect::<Vec<_>>(),
            vec!["a", "b"]
        );
        assert!(matches!(receive(&mut socket.client).await, P::Usage(_)));
        assert!(matches!(receive(&mut socket.client).await, P::Projects(_)));
        assert!(matches!(receive(&mut socket.client).await, P::Library(_)));
        assert!(matches!(receive(&mut socket.client).await, P::Audio(_)));
        assert!(matches!(receive(&mut socket.client).await, P::Jukebox(_)));
        assert!(send_initial_snapshot(&socket.tx, &manager, &scoped(&["a"], Level::Ro)).await);
        // A subsequent marker proves no host-wide events were queued after the scoped list.
        send(
            &socket.tx,
            &Cap::Root,
            &message(Event::Screen {
                screen: screen("marker", 9),
            }),
        )
        .await
        .unwrap();
        let P::Sessions(list) = receive(&mut socket.client).await else {
            panic!("scoped sessions")
        };
        assert_eq!(
            list.sessions
                .iter()
                .map(|s| s.name.as_str())
                .collect::<Vec<_>>(),
            vec!["a"]
        );
        assert!(matches!(receive(&mut socket.client).await, P::Screen(s) if s.name == "marker"));
        std::fs::remove_dir_all(manager.cfg_path.parent().unwrap()).unwrap();
    }

    #[tokio::test]
    async fn subscriptions_filter_frames_and_flush_latest_frames_before_control_events() {
        use crate::shared::wire::event::Payload as P;
        let manager = crate::session::test_manager(crate::config::Config::default());
        let mut socket = socket_pair().await;
        let subs: WsSubs = Default::default();
        let cap = scoped(&["a", "b"], Level::Ro);
        for name in ["a", "b", "secret", "a"] {
            assert!(
                handle_client_msg(
                    ClientMsg::Sub { name: name.into() },
                    &manager,
                    &cap,
                    &socket.tx,
                    &subs
                )
                .await
            );
        }
        assert_eq!(subs.lock().await.len(), 2);
        let (events, rx) = broadcast::channel(32);
        let pump = spawn_frame_pump(rx, socket.tx.clone(), subs.clone(), cap.clone());
        for (name, seq) in [
            ("secret", 99),
            ("a", 1),
            ("a", 2),
            ("b", 3),
            ("a", 4),
            ("b", 5),
        ] {
            events
                .send(message(Event::Screen {
                    screen: screen(name, seq),
                }))
                .ok()
                .unwrap();
        }
        events
            .send(message(Event::Usage {
                usage: Default::default(),
            }))
            .ok()
            .unwrap();
        events
            .send(message(Event::Sessions {
                sessions: vec![view("a"), view("secret")],
            }))
            .ok()
            .unwrap();
        let mut latest = HashMap::new();
        loop {
            match receive(&mut socket.client).await {
                P::Screen(s) => {
                    assert!(s.name == "a" || s.name == "b");
                    if let Some(previous) = latest.insert(s.name, s.seq) {
                        assert!(s.seq > previous);
                    }
                }
                P::Sessions(s) => {
                    assert_eq!(s.sessions.len(), 1);
                    assert_eq!(s.sessions[0].name, "a");
                    break;
                }
                other => panic!("scoped socket received {other:?}"),
            }
        }
        assert_eq!(latest.get("a"), Some(&4));
        assert_eq!(latest.get("b"), Some(&5));
        assert!(
            handle_client_msg(
                ClientMsg::Unsub { name: "a".into() },
                &manager,
                &cap,
                &socket.tx,
                &subs
            )
            .await
        );
        assert!(!subs.lock().await.contains_key("a"));
        events
            .send(message(Event::Screen {
                screen: screen("a", 6),
            }))
            .ok()
            .unwrap();
        events
            .send(message(Event::Sessions { sessions: vec![] }))
            .ok()
            .unwrap();
        assert!(matches!(receive(&mut socket.client).await, P::Sessions(_)));
        drop(events);
        tokio::time::timeout(Duration::from_secs(2), pump)
            .await
            .unwrap()
            .unwrap();
        subs.lock().await.clear();
        std::fs::remove_dir_all(manager.cfg_path.parent().unwrap()).unwrap();
    }

    #[tokio::test]
    async fn scroll_replies_keep_request_order_and_skip_missing_or_cancelled_captures() {
        use crate::shared::wire::event::Payload as P;
        let mut socket = socket_pair().await;
        let (release, ready) = tokio::sync::oneshot::channel();
        let first = tokio::spawn(async move {
            ready.await.unwrap();
            Some(screen("a", 1))
        });
        let cancelled = tokio::spawn(std::future::pending::<Option<ScreenView>>());
        cancelled.abort();
        let mut pending = VecDeque::from([
            first,
            tokio::spawn(async { None }),
            cancelled,
            tokio::spawn(async { Some(screen("a", 2)) }),
        ]);
        release.send(()).unwrap();
        assert!(flush_scrolls(&socket.tx, &Cap::Root, &mut pending).await);
        assert!(pending.is_empty());
        for expected in [1, 2] {
            assert!(matches!(receive(&mut socket.client).await, P::Screen(s) if s.seq == expected));
        }
        let cap = scoped(&["a"], Level::Ro);
        let Cap::Scoped(grant) = &cap else {
            unreachable!()
        };
        grant
            .revoked
            .store(true, std::sync::atomic::Ordering::Release);
        pending.push_back(tokio::spawn(async { Some(screen("a", 3)) }));
        assert!(!flush_scrolls(&socket.tx, &cap, &mut pending).await);
    }

    #[tokio::test]
    async fn auth_changes_ignore_unrelated_credentials_but_close_on_revocation_or_lost_events() {
        let (events, mut rx) = broadcast::channel(2);
        events.send(AuthChange::GrantsRevoked).unwrap();
        events.send(AuthChange::RootTokenChanged).unwrap();
        assert!(auth_invalidated(&mut rx, &Cap::Root).await);
        let cap = scoped(&["a"], Level::Ro);
        events.send(AuthChange::RootTokenChanged).unwrap();
        events.send(AuthChange::GrantsRevoked).unwrap();
        assert!(
            tokio::time::timeout(Duration::from_millis(20), auth_invalidated(&mut rx, &cap))
                .await
                .is_err()
        );
        let Cap::Scoped(grant) = &cap else {
            unreachable!()
        };
        grant
            .revoked
            .store(true, std::sync::atomic::Ordering::Release);
        events.send(AuthChange::GrantsRevoked).unwrap();
        assert!(auth_invalidated(&mut rx, &cap).await);
        for _ in 0..3 {
            events.send(AuthChange::RootTokenChanged).unwrap();
        }
        assert!(auth_invalidated(&mut rx, &Cap::Root).await); // lagged
        drop(events);
        while rx.try_recv().is_ok() {}
        assert!(auth_invalidated(&mut rx, &Cap::Root).await); // closed
    }
}
