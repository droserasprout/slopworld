use std::collections::HashMap;
use std::sync::Arc;
use std::time::Duration;

use crate::grant::{Cap, Level};
use anyhow::bail;
use axum::extract::ws::{Message, WebSocket, WebSocketUpgrade};
use axum::extract::State;
use axum::http::{HeaderMap, StatusCode};
use axum::response::IntoResponse;
use futures::{SinkExt, StreamExt};
use tokio::sync::{broadcast, Mutex};
use tokio::task::JoinHandle;

use crate::session::{Event, WatchGuard};

use super::types::{
    AudioReq, AudioSelection, BreadcrumbReq, ClientMsg, KeysReq, MouseReq, PasteReq, ResizeReq,
    ScrollReq,
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
            Event::Screen { .. } => Some(ev),
            Event::Projects { .. }
            | Event::Shortcuts { .. }
            | Event::Usage { .. }
            | Event::Audio { .. }
            | Event::Jukebox { .. } => None,
        },
    }
}

async fn ws_run(socket: WebSocket, m: Mgr, cap: Cap) {
    // Held for the life of the pump so the manager knows how many clients are
    // attached.
    let _client = m.client_joined();

    let (tx, mut rx) = socket.split();
    let tx = Arc::new(Mutex::new(tx));
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

    while let Some(Ok(msg)) = rx.next().await {
        let Message::Text(text) = msg else { continue };
        let Ok(cm) = serde_json::from_str::<ClientMsg>(&text) else {
            tracing::debug!("unparseable ws message: {text}");
            continue;
        };
        if !handle_client_msg(cm, &m, &cap, &tx, &subs).await {
            break;
        }
    }

    // Explicitly, rather than with the table: the pump holds the other half of that `Arc` and
    // an aborted task is dropped when the runtime gets round to it, which is not when the
    // agent stopped being watched.
    subs.lock().await.clear();
    pump.abort();
}

/// Usage, projects and shortcuts ride along because they speak only on a change: a mod
/// attaching between polls would otherwise draw nothing for a minute. Each goes through the
/// same scope filter the pump uses, so a grant's socket gets its filtered session list and
/// none of the mod's wider view - `scope_event` drops what it may not see.
async fn send_initial_snapshot(tx: &WsTx, m: &Mgr, cap: &Cap) -> bool {
    if let Some(ev) = scope_event(
        cap,
        Event::Sessions {
            sessions: m.views().await,
        },
    ) {
        if send(tx, &ev).await.is_err() {
            return false;
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
        if let Some(ev) = scope_event(cap, ev) {
            let _ = send(tx, &ev).await;
        }
    }
    true
}

/// Coalesce screen frames per client to the monitor cadence: newest wins, and the pending
/// frame flushes on the beat. Other event types remain uncoalesced.
fn spawn_frame_pump(
    mut events: broadcast::Receiver<Event>,
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
}

/// Handles one client message. Unauthorized messages are dropped silently; a failed direct
/// response means the socket is gone and tells the caller to stop reading it.
async fn handle_client_msg(cm: ClientMsg, m: &Mgr, cap: &Cap, tx: &WsTx, subs: &WsSubs) -> bool {
    match cm {
        ClientMsg::Sub { name } => handle_sub(name, m, cap, tx, subs).await,
        ClientMsg::Unsub { name } => handle_unsub(name, subs).await,
        ClientMsg::Keys(req) => handle_keys(req, m, cap).await,
        ClientMsg::Resize(req) => handle_resize(req, m, cap).await,
        ClientMsg::Scroll(req) => handle_scroll(req, m, cap, tx).await,
        ClientMsg::Mouse(req) => handle_mouse(req, m, cap).await,
        ClientMsg::Paste(req) => handle_paste(req, m, cap).await,
        ClientMsg::Breadcrumb(req) => handle_breadcrumb(req, m, cap).await,
        ClientMsg::Audio(req) => handle_audio(req, m, cap).await,
    }
}

async fn handle_sub(name: String, m: &Mgr, cap: &Cap, tx: &WsTx, subs: &WsSubs) -> bool {
    // Require read access for pane subscriptions. A failed direct response means the socket is
    // gone and tells the caller to stop reading it.
    if !m.cap_ok(cap, &name, Level::Ro).await {
        return true;
    }
    subs.lock().await.insert(name.clone(), m.watching(&name));
    // Somebody is looking at this pane now, which is the whole of what a bell was asking for.
    // Broadcast, not answered here: every client draws the mark.
    m.clear_bell(&name).await;
    if let Some(s) = m.screen(&name).await {
        return send(tx, &Event::Screen { screen: s }).await.is_ok();
    }
    true
}

async fn handle_unsub(name: String, subs: &WsSubs) -> bool {
    subs.lock().await.remove(&name);
    true
}

async fn handle_keys(req: KeysReq, m: &Mgr, cap: &Cap) -> bool {
    if !m.cap_ok(cap, &req.name, Level::Rw).await {
        return true;
    }
    m.send_keys(&req.name, req.keys, req.literal, req.random_tips)
        .await;
    true
}

async fn handle_resize(req: ResizeReq, m: &Mgr, cap: &Cap) -> bool {
    if !m.cap_ok(cap, &req.name, Level::Rw).await {
        return true;
    }
    if let Err(e) = m.resize(&req.name, req.cols, req.rows).await {
        tracing::debug!("resize: {e:#}");
    }
    true
}

async fn handle_scroll(req: ScrollReq, m: &Mgr, cap: &Cap, tx: &WsTx) -> bool {
    if !m.cap_ok(cap, &req.name, Level::Ro).await {
        return true;
    }
    // Answer this socket alone: a scrolled frame is private to the wheel request and must not
    // reach live subscribers.
    if let Some(s) = m.scroll_capture(&req.name, req.off, req.request_id).await {
        return send(tx, &Event::Screen { screen: s }).await.is_ok();
    }
    true
}

async fn handle_mouse(req: MouseReq, m: &Mgr, cap: &Cap) -> bool {
    if !m.cap_ok(cap, &req.name, Level::Rw).await {
        return true;
    }
    let Some(action) = crate::emu::MouseAction::parse(&req.action) else {
        tracing::debug!("unknown mouse action: {}", req.action);
        return true;
    };
    let ev = crate::emu::MouseInput {
        action,
        button: req.button,
        col: req.col,
        row: req.row,
    };
    m.send_mouse(&req.name, ev, req.count).await;
    true
}

async fn handle_paste(req: PasteReq, m: &Mgr, cap: &Cap) -> bool {
    if !m.cap_ok(cap, &req.name, Level::Rw).await {
        return true;
    }
    // A paste checks first, so anything left is a paste that did not land - and the only other
    // sign of one is the operator noticing nothing arrived.
    if let Err(e) = m.paste(&req.name, &req.text).await {
        tracing::warn!("paste to {}: {e:#}", req.name);
    }
    true
}

async fn handle_breadcrumb(req: BreadcrumbReq, m: &Mgr, cap: &Cap) -> bool {
    if !m.cap_ok(cap, &req.name, Level::Rw).await {
        return true;
    }
    if let Err(e) = m
        .paste_breadcrumb(&req.name, &req.breadcrumb, req.random_tips)
        .await
    {
        tracing::warn!("breadcrumb to {}: {e:#}", req.name);
    }
    true
}

async fn handle_audio(req: AudioReq, m: &Mgr, cap: &Cap) -> bool {
    if !cap.may_create() {
        return true;
    }
    match req.selection {
        Some(Some(selection)) => match resolve_audio_source(selection) {
            Ok(source) => m.audio.play(&source, req.volume),
            Err(e) => {
                let why = format!("{e:#}");
                tracing::warn!("{why}");
                m.audio.reject(why);
            }
        },
        Some(None) => m.audio.stop(),
        None => m.audio.set_volume(req.volume),
    }
    true
}

fn resolve_audio_source(selection: AudioSelection) -> anyhow::Result<String> {
    match (selection.station, selection.stream, selection.file) {
        (Some(station), Some(stream), None) => crate::jukebox::catalog()
            .resolve(&station, &stream)
            .map_err(|e| anyhow::anyhow!("jukebox selection rejected: {e:#}")),
        (None, None, Some(file)) => Ok(file),
        _ => bail!("jukebox selection must name a station/stream or file"),
    }
}

async fn send(tx: &WsTx, ev: &Event) -> Result<(), axum::Error> {
    let text = serde_json::to_string(ev).unwrap_or_default();
    tx.lock().await.send(Message::Text(text)).await
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::grant::Grant;
    use crate::session::{ScreenView, SessionView, State};
    use std::collections::HashSet;

    /// The only field `scope_event` reads off a session is its name, so the rest is filler that
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
            breadcrumbs: Vec::new(),
            breadcrumb_yolo: false,
            breadcrumbs_pending: false,
            agent: String::new(),
            state: State::Idle,
            alive: true,
            cols: 80,
            rows: 24,
            network: Default::default(),
            network_override: None,
            dns: Default::default(),
            dns_override: None,
            limits: Default::default(),
            limits_override: Default::default(),
            autostart: false,
            ephemeral: false,
            last_change: 0,
            state_since: 0,
            title: String::new(),
            bell: false,
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
        })
    }

    fn names(ev: &Event) -> Vec<String> {
        match ev {
            Event::Sessions { sessions } => sessions.iter().map(|s| s.name.clone()).collect(),
            other => panic!("expected a sessions event, got {other:?}"),
        }
    }

    /// Root is the mod: every event reaches it, and a session list is handed on whole.
    #[test]
    fn root_sees_every_event_unfiltered() {
        let sessions = Event::Sessions {
            sessions: vec![view("a"), view("b")],
        };
        assert_eq!(
            names(&scope_event(&Cap::Root, sessions).expect("root keeps the event")),
            vec!["a".to_string(), "b".to_string()]
        );

        // Categories a scoped grant never sees still reach root untouched.
        for ev in [
            Event::Projects {
                projects: Vec::new(),
            },
            Event::Usage {
                usage: Default::default(),
            },
            Event::Jukebox {
                jukebox: Default::default(),
            },
        ] {
            assert!(scope_event(&Cap::Root, ev).is_some());
        }
    }

    /// A scoped grant's session list is cut down to the names it may see; the rest never
    /// reach the socket, so the holder does not even learn they exist.
    #[test]
    fn a_scoped_grant_sees_only_the_sessions_it_names() {
        let cap = scoped(&["a", "c"], Level::Ro);
        let ev = Event::Sessions {
            sessions: vec![view("a"), view("b"), view("c"), view("d")],
        };
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
        let ev = Event::Sessions {
            sessions: vec![view("a"), view("b")],
        };
        assert!(names(&scope_event(&cap, ev).expect("still an event")).is_empty());
    }

    /// The grant subscribes to a session's screen and must keep receiving it; screens are the
    /// one non-session category a scoped socket is allowed to see.
    #[test]
    fn a_scoped_grant_still_receives_screens() {
        let cap = scoped(&["a"], Level::Ro);
        let screen = ScreenView {
            name: "a".to_string(),
            seq: 1,
            cols: 80,
            rows: 24,
            cx: 0,
            cy: 0,
            off: 0,
            cursor_shape: 0,
            cursor_blink: false,
            app_mouse: false,
            app_drag: false,
            alt_screen: false,
            title: String::new(),
            request_id: 0,
            lines: Vec::new(),
        };
        assert!(scope_event(&cap, Event::Screen { screen }).is_some());
    }

    /// Everything that is neither a session nor a screen - projects, shortcuts, usage, audio,
    /// the jukebox catalog - is host-wide state a scoped grant has no business seeing.
    #[test]
    fn a_scoped_grant_is_denied_host_wide_categories() {
        let cap = scoped(&["a"], Level::Ro);
        let denied = [
            Event::Projects {
                projects: Vec::new(),
            },
            Event::Shortcuts {
                shortcuts: Vec::new(),
            },
            Event::Usage {
                usage: Default::default(),
            },
            Event::Audio {
                audio: Default::default(),
            },
            Event::Jukebox {
                jukebox: Default::default(),
            },
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
            };
            assert!(
                resolve_audio_source(selection).is_err(),
                "expected {station:?}/{stream:?}/{file:?} to be rejected"
            );
        }
    }
}
