use super::commands::handle_audio;
use super::outbound::scope_event;
use super::*;
use crate::api::types::{AudioReq, AudioSelection};
use crate::grant::Grant;
use crate::session::{
    ScreenView, SessionLaunchView, SessionReaderView, SessionRuntimeView, SessionView,
    SessionWorkerView, State,
};
use std::collections::HashSet;
use std::time::Duration;

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
        assert!(reply
            .error
            .unwrap()
            .contains("Select ncspot or another audio source, not both."));
        assert!(reply.session.is_none());
    }
}

/// Event filtering reads the name and host flag. The rest is filler that
/// keeps a `SessionView` compiling without pulling in a whole live manager.
fn view(name: &str) -> SessionView {
    SessionView {
        worktree: String::new(),
        worktree_name: String::new(),
        name: name.to_string(),
        label: String::new(),
        intent: String::new(),
        reader: SessionReaderView {
            path: String::new(),
            key: String::new(),
            scope: String::new(),
            pinned: false,
            line: 0,
        },
        project: String::new(),
        dir: String::new(),
        launch: SessionLaunchView {
            command: String::new(),
            command_preset: String::new(),
            cmd: None,
            sandbox: Vec::new(),
            persistent_tmp: false,
            agent: String::new(),
            network: Default::default(),
            dns: Default::default(),
            limits: Default::default(),
            mounts: Vec::new(),
            autostart: false,
            auto_resume: false,
        },
        worker: SessionWorkerView {
            enabled: false,
            parent: String::new(),
            task_id: String::new(),
            durable: false,
        },
        ephemeral: false,
        host: false,
        runtime: SessionRuntimeView {
            auto_resume_pending: false,
            state: State::Idle,
            alive: true,
            cols: 80,
            rows: 24,
            process_running: false,
            last_change: 0,
            state_since: 0,
            title: String::new(),
            bell: false,
            run_id: 0,
            seq: 0,
        },
    }
}

#[test]
fn session_groups_match_wire_schema() {
    let mut session = view("agent");
    session.launch.command = "custom".into();
    session.launch.command_preset = "shell".into();
    session.launch.cmd = Some("echo hello".into());
    session.launch.agent = "sh".into();
    session.launch.sandbox = vec!["git".into()];
    session.launch.persistent_tmp = true;
    session.launch.autostart = true;
    session.launch.auto_resume = true;
    session.worker = SessionWorkerView {
        enabled: true,
        parent: "parent".into(),
        task_id: "task-7".into(),
        durable: true,
    };
    session.reader = SessionReaderView {
        path: "/tmp/file.rs".into(),
        key: "file-key".into(),
        scope: "project/main".into(),
        pinned: true,
        line: 17,
    };
    session.runtime = SessionRuntimeView {
        auto_resume_pending: true,
        state: State::Working,
        alive: true,
        cols: 120,
        rows: 40,
        process_running: true,
        last_change: 123,
        state_since: 100,
        title: "editor".into(),
        bell: true,
        run_id: 7,
        seq: 19,
    };
    let expected = serde_json::json!({
        "reader_path": "/tmp/file.rs",
        "reader_key": "file-key",
        "reader_scope": "project/main",
        "reader_pinned": true,
        "reader_line": 17,
        "auto_resume_pending": true,
        "state": "working",
        "alive": true,
        "cols": 120,
        "rows": 40,
        "process_running": true,
        "last_change": 123,
        "state_since": 100,
        "title": "editor",
        "bell": true,
        "run_id": 7,
        "seq": 19,
    });
    let json = serde_json::to_value(&session).unwrap();
    assert!(json.get("command").is_none());

    for (field, value) in expected.as_object().unwrap() {
        let actual = if let Some(field) = field.strip_prefix("reader_") {
            &json["reader"][field]
        } else {
            &json["runtime"][field]
        };
        assert_eq!(actual, value, "JSON field {field}");
        assert!(json.get(field).is_none(), "unexpected flat field {field}");
    }
    assert_eq!(json["worker"]["enabled"], true);
    assert_eq!(json["launch"]["command"], "custom");
    assert_eq!(json["worker"]["parent"], "parent");
    assert_eq!(json["worker"]["task_id"], "task-7");
    assert_eq!(json["worker"]["durable"], true);

    let event = Event::Sessions {
        sessions: vec![session],
    }
    .to_protobuf()
    .unwrap();
    let Some(crate::shared::wire::event::Payload::Sessions(reply)) = event.payload else {
        panic!("expected sessions payload");
    };
    let wire = &reply.sessions[0];
    let launch = wire.launch.as_ref().unwrap();
    let worker = wire.worker.as_ref().unwrap();
    assert_eq!(launch.command, "custom");
    assert_eq!(launch.command_preset, "shell");
    assert_eq!(launch.cmd.as_deref(), Some("echo hello"));
    assert_eq!(launch.agent, "sh");
    assert_eq!(launch.sandbox, ["git"]);
    assert!(launch.persistent_tmp && launch.autostart && launch.auto_resume);
    assert!(worker.enabled && worker.durable);
    assert_eq!(worker.parent, "parent");
    assert_eq!(worker.task_id, "task-7");
    let wire_json = serde_json::to_value(wire).unwrap();
    for (field, value) in expected.as_object().unwrap() {
        let actual = if let Some(field) = field.strip_prefix("reader_") {
            &wire_json["reader"][field]
        } else {
            &wire_json["runtime"][field]
        };
        assert_eq!(actual, value, "Protobuf field {field}");
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

/// A scoped grant's session list is cut down to the names it may see. The rest never
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

/// The grant subscribes to a session's screen and must keep receiving it. Screens are the
/// one non-session category a scoped socket is allowed to see.
#[test]
fn screens_require_a_live_grant_for_the_named_session() {
    let cap = scoped(&["a"], Level::Ro);
    let screen = ScreenView {
        input_timings: Vec::new(),
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

type ClientSocket =
    tokio_tungstenite::WebSocketStream<tokio_tungstenite::MaybeTlsStream<tokio::net::TcpStream>>;

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
        input_timings: Vec::new(),
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
    let mut scrolls = ScrollReplies::new();
    scrolls.pending = VecDeque::from([
        first,
        tokio::spawn(async { None }),
        cancelled,
        tokio::spawn(async { Some(screen("a", 2)) }),
    ]);
    release.send(()).unwrap();
    assert!(scrolls.flush(&socket.tx, &Cap::Root).await);
    assert!(!scrolls.has_pending());
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
    scrolls
        .pending
        .push_back(tokio::spawn(async { Some(screen("a", 3)) }));
    assert!(!scrolls.flush(&socket.tx, &cap).await);
}

#[tokio::test]
async fn dropping_scroll_replies_cancels_pending_captures() {
    let mut scrolls = ScrollReplies::new();
    let task = tokio::spawn(std::future::pending::<Option<ScreenView>>());
    let handle = task.abort_handle();
    scrolls.pending.push_back(task);
    drop(scrolls);
    tokio::time::timeout(Duration::from_secs(2), async {
        while !handle.is_finished() {
            tokio::task::yield_now().await;
        }
    })
    .await
    .unwrap();
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

#[tokio::test]
async fn latency_stamp_is_per_send_without_mutating_shared_frame() {
    use crate::shared::wire::event::Payload;
    let mut socket = socket_pair().await;
    let mut frame = screen("a", 3);
    frame.input_timings.push(crate::shared::wire::InputTiming {
        id: "0123456789abcdef0123456789abcdef".into(),
        received_us: 1,
        tmux_us: 1,
        first_capture_us: 1,
        capture_us: 1,
        first_seq: 2,
        ..Default::default()
    });
    let event = EventMessage::new(Event::Screen { screen: frame });
    let cached = event.encoded().unwrap();
    send(&socket.tx, &Cap::Root, &event).await.unwrap();
    let Payload::Screen(sent) = receive(&mut socket.client).await else {
        panic!("screen")
    };
    assert!(sent.input_timings[0].send_us >= sent.input_timings[0].capture_us);
    assert_eq!(sent.input_timings[0].first_seq, 2);
    assert_eq!(event.encoded().unwrap().as_ref(), cached.as_ref());
    let Event::Screen { screen } = event.event() else {
        panic!("screen")
    };
    assert_eq!(screen.input_timings[0].send_us, 0);
}

#[tokio::test]
async fn frame_pump_stops_when_authority_is_revoked_while_waiting_to_send() {
    let socket = socket_pair().await;
    let cap = scoped(&["a"], Level::Ro);
    let (events, rx) = broadcast::channel(2);
    // Hold the writer until the pump has accepted the event, then revoke authority.
    let writer = socket.tx.lock().await;
    let pump = spawn_frame_pump(rx, socket.tx.clone(), WsSubs::default(), cap.clone());
    let event = message(Event::Sessions {
        sessions: vec![view("a")],
    });
    let accepted = Arc::downgrade(&event);
    assert!(events.send(event).is_ok());
    tokio::time::timeout(Duration::from_secs(2), async {
        // Scoping replaces a session event; its original payload is dropped before send.
        while accepted.upgrade().is_some() {
            tokio::task::yield_now().await;
        }
    })
    .await
    .unwrap();
    let Cap::Scoped(grant) = &cap else {
        unreachable!()
    };
    grant
        .revoked
        .store(true, std::sync::atomic::Ordering::Release);
    drop(writer);
    tokio::time::timeout(Duration::from_secs(2), pump)
        .await
        .unwrap()
        .unwrap();
    // Keep the broadcaster alive: termination must come from failed delivery.
    assert_eq!(events.receiver_count(), 0);
}
