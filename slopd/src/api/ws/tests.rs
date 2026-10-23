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
    let manager = crate::application::test_application_with_socket(
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
        assert!(
            reply
                .error
                .unwrap()
                .contains("Select ncspot or another audio source, not both.")
        );
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
            args: None,
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
        panic!("test capability must be scoped")
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
        message(Event::Clipboard {
            name: "a".into(),
            run_id: 7,
            text: "copy".into(),
        }),
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

async fn raw_socket_pair() -> (WebSocket, ClientSocket, JoinHandle<()>) {
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
    (socket.await.unwrap(), client, server)
}

async fn socket_pair() -> SocketPair {
    socket_pair_for(&crate::application::test_application(
        crate::config::Config::default(),
    ))
    .await
}

async fn socket_pair_for(manager: &App) -> SocketPair {
    let (socket, client, server) = raw_socket_pair().await;
    let (tx, _) = socket.split();
    SocketPair {
        tx: WsTx::new(
            tx,
            manager,
            &Cap::Root,
            manager.config_authority.root_generation(),
        ),
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
    let manager = crate::application::test_application(crate::config::Config {
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
    manager.sessions.sync_from_config().await;
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
    std::fs::remove_dir_all(manager.sessions.cfg_path().parent().unwrap()).unwrap();
}

#[tokio::test]
async fn subscriptions_filter_frames_and_flush_latest_frames_before_control_events() {
    use crate::shared::wire::event::Payload as P;
    let manager = crate::application::test_application(crate::config::Config::default());
    let mut socket = socket_pair().await;
    let subs: WsSubs = Default::default();
    let cap = scoped(&["a", "b"], Level::Ro);
    for name in ["a", "b", "secret", "a"] {
        assert!(
            Box::pin(handle_client_msg(
                ClientMsg::Sub { name: name.into() },
                &manager,
                &cap,
                &socket.tx,
                &subs
            ))
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
        Box::pin(handle_client_msg(
            ClientMsg::Unsub { name: "a".into() },
            &manager,
            &cap,
            &socket.tx,
            &subs
        ))
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
    std::fs::remove_dir_all(manager.sessions.cfg_path().parent().unwrap()).unwrap();
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
    let (later_done, later_wait) = tokio::sync::oneshot::channel();
    let mut scrolls = ScrollReplies::new();
    for capture in [
        first,
        tokio::spawn(async { None }),
        cancelled,
        tokio::spawn(async move {
            later_done.send(()).unwrap();
            Some(screen("a", 2))
        }),
    ] {
        let permit = scrolls.slots.clone().try_acquire_owned().unwrap();
        scrolls.pending.push_back(PendingScroll {
            capture,
            ready: None,
            _permit: permit,
        });
    }
    let tx = socket.tx.clone();
    let sending = tokio::spawn(async move {
        assert!(scrolls.send_next(&tx, &Cap::Root).await);
        scrolls
    });
    later_wait.await.unwrap();
    tokio::time::timeout(Duration::from_millis(20), socket.client.next())
        .await
        .expect_err("later capture overtook the held head");
    release.send(()).unwrap();
    let mut scrolls = sending.await.unwrap();
    while scrolls.has_pending() {
        assert!(scrolls.send_next(&socket.tx, &Cap::Root).await);
    }
    assert!(!scrolls.has_pending());
    for expected in [1, 2] {
        assert!(matches!(receive(&mut socket.client).await, P::Screen(s) if s.seq == expected));
    }
    let cap = scoped(&["a"], Level::Ro);
    let Cap::Scoped(grant) = &cap else {
        panic!("test capability must be scoped")
    };
    grant
        .revoked
        .store(true, std::sync::atomic::Ordering::Release);
    let permit = scrolls.slots.clone().try_acquire_owned().unwrap();
    scrolls.pending.push_back(PendingScroll {
        capture: tokio::spawn(async { Some(screen("a", 3)) }),
        ready: None,
        _permit: permit,
    });
    assert!(!scrolls.send_next(&socket.tx, &cap).await);
}

#[tokio::test]
async fn cancelled_scroll_send_keeps_ready_reply_and_slot() {
    use crate::shared::wire::event::Payload as P;
    let mut socket = socket_pair().await;
    let mut scrolls = ScrollReplies::new();
    let permit = scrolls.slots.clone().try_acquire_owned().unwrap();
    scrolls.pending.push_back(PendingScroll {
        capture: tokio::spawn(async { Some(screen("a", 7)) }),
        ready: None,
        _permit: permit,
    });
    let writer = socket.tx.lock().await;
    tokio::time::timeout(
        Duration::from_millis(20),
        scrolls.send_next(&socket.tx, &Cap::Root),
    )
    .await
    .expect_err("writer should hold the reply");
    assert!(scrolls.pending.front().unwrap().ready.is_some());
    assert_eq!(scrolls.slots.available_permits(), 3);
    drop(writer);
    assert!(scrolls.send_next(&socket.tx, &Cap::Root).await);
    assert!(matches!(receive(&mut socket.client).await, P::Screen(s) if s.seq == 7));
    assert_eq!(scrolls.slots.available_permits(), 4);
}

#[tokio::test]
async fn scroll_admission_counts_completed_replies_until_delivery() {
    let socket = socket_pair().await;
    let manager = crate::application::test_application(crate::config::Config::default());
    let mut scrolls = ScrollReplies::new();
    for id in 0..4 {
        assert!(scrolls.enqueue(
            ScrollReq {
                name: "missing".into(),
                off: 0,
                request_id: id,
            },
            &manager,
            &Cap::Root,
            &socket.tx
        ));
    }
    tokio::task::yield_now().await;
    assert_eq!(scrolls.slots.available_permits(), 0);
    assert!(!scrolls.enqueue(
        ScrollReq {
            name: "missing".into(),
            off: 0,
            request_id: 5,
        },
        &manager,
        &Cap::Root,
        &socket.tx
    ));
    drop(scrolls);
}

#[tokio::test]
async fn dropping_scroll_replies_cancels_pending_captures() {
    let mut scrolls = ScrollReplies::new();
    let task = tokio::spawn(std::future::pending::<Option<ScreenView>>());
    let handle = task.abort_handle();
    let permit = scrolls.slots.clone().try_acquire_owned().unwrap();
    scrolls.pending.push_back(PendingScroll {
        capture: task,
        ready: None,
        _permit: permit,
    });
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
    tokio::time::timeout(Duration::from_millis(20), auth_invalidated(&mut rx, &cap))
        .await
        .unwrap_err();
    let Cap::Scoped(grant) = &cap else {
        panic!("test capability must be scoped")
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
        panic!("test capability must be scoped")
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

#[tokio::test]
async fn root_panel_subscription_and_history_continue_during_unrelated_lifecycle_work() {
    let manager = crate::application::test_application(crate::config::Config {
        sessions: vec![crate::config::SessionCfg {
            name: "pager".into(),
            autostart: false,
            ..Default::default()
        }],
        ..Default::default()
    });
    manager.sessions.sync_from_config().await;
    let mut emu = crate::emu::SessionEmu::new(80, 24);
    emu.feed(b"pager ready");
    manager.sessions.apply_frame("pager", emu.render()).await;
    let mut socket = socket_pair().await;
    let subs: WsSubs = Default::default();
    let (release, wait) = tokio::sync::oneshot::channel::<()>();
    let transaction = manager
        .sessions
        .session_operation(async { wait.await.unwrap() });
    tokio::pin!(transaction);
    assert!(futures::poll!(transaction.as_mut()).is_pending());
    let subscribe = handle_client_msg(
        ClientMsg::Sub {
            name: "pager".into(),
        },
        &manager,
        &Cap::Root,
        &socket.tx,
        &subs,
    );
    assert!(
        tokio::time::timeout(Duration::from_secs(1), subscribe)
            .await
            .expect("subscription waited on unrelated lifecycle")
    );
    assert!(
        matches!(receive(&mut socket.client).await, crate::shared::wire::event::Payload::Screen(s) if s.name == "pager")
    );
    let mut scrolls = ScrollReplies::new();
    assert!(scrolls.enqueue(
        ScrollReq {
            name: "pager".into(),
            off: 0,
            request_id: 17
        },
        &manager,
        &Cap::Root,
        &socket.tx
    ));
    assert!(
        tokio::time::timeout(
            Duration::from_secs(1),
            scrolls.send_next(&socket.tx, &Cap::Root)
        )
        .await
        .unwrap()
    );
    receive(&mut socket.client).await;
    let unsub = handle_client_msg(
        ClientMsg::Unsub {
            name: "pager".into(),
        },
        &manager,
        &Cap::Root,
        &socket.tx,
        &subs,
    );
    assert!(
        tokio::time::timeout(Duration::from_secs(1), unsub)
            .await
            .unwrap()
    );
    assert!(subs.lock().await.is_empty());
    release.send(()).unwrap();
    transaction.await;
}

#[tokio::test]
async fn panel_switch_and_unsubscribe_overtake_an_unfinished_history_capture() {
    use crate::shared::wire::{self, client_message::Payload as C, event::Payload as E};
    use futures::SinkExt;
    use prost::Message as _;
    let manager = crate::application::test_application(crate::config::Config {
        sessions: ["pager", "other"]
            .into_iter()
            .map(|name| crate::config::SessionCfg {
                name: name.into(),
                autostart: false,
                ..Default::default()
            })
            .collect(),
        ..Default::default()
    });
    manager.sessions.sync_from_config().await;
    for name in ["pager", "other"] {
        let mut emu = crate::emu::SessionEmu::new(80, 24);
        emu.feed(b"ready");
        manager.sessions.apply_frame(name, emu.render()).await;
    }
    let (socket, mut client, server) = raw_socket_pair().await;
    let (tx, rx) = socket.split();
    let subs: WsSubs = Default::default();
    let mut scrolls = ScrollReplies::new();
    let (release, wait) = tokio::sync::oneshot::channel::<()>();
    let permit = scrolls.slots.clone().try_acquire_owned().unwrap();
    scrolls.pending.push_back(PendingScroll {
        capture: tokio::spawn(async move {
            wait.await.unwrap();
            Some(screen("pager", 17))
        }),
        ready: None,
        _permit: permit,
    });
    let mut intake = CommandIntake {
        tx: WsTx::new(
            tx,
            &manager,
            &Cap::Root,
            manager.config_authority.root_generation(),
        ),
        rx,
        subs: subs.clone(),
        auth_changes: manager.config_authority.auth_changes(),
        scrolls,
    };
    let driving = tokio::spawn(async move { intake.run(&manager, &Cap::Root).await });
    for command in [
        C::Sub(wire::NameReq {
            name: Some("pager".into()),
        }),
        C::Unsub(wire::NameReq {
            name: Some("pager".into()),
        }),
        C::Sub(wire::NameReq {
            name: Some("other".into()),
        }),
    ] {
        client
            .send(tokio_tungstenite::tungstenite::Message::Binary(
                wire::ClientMessage {
                    payload: Some(command),
                }
                .encode_to_vec(),
            ))
            .await
            .unwrap();
    }
    assert!(matches!(receive(&mut client).await, E::Screen(s) if s.name == "pager" && s.seq != 17));
    assert!(matches!(receive(&mut client).await, E::Screen(s) if s.name == "other"));
    assert!(!subs.lock().await.contains_key("pager"));
    assert!(subs.lock().await.contains_key("other"));
    release.send(()).unwrap();
    assert!(matches!(receive(&mut client).await, E::Screen(s) if s.name == "pager" && s.seq == 17));
    client.close(None).await.unwrap();
    tokio::time::timeout(Duration::from_secs(1), driving)
        .await
        .unwrap()
        .unwrap();
    server.abort();
}

#[tokio::test]
async fn broadcast_lag_closes_socket_for_authoritative_catalog_recovery() {
    let mut socket = socket_pair().await;
    let (events, rx) = broadcast::channel(256);
    let writer = socket.tx.lock().await;
    let pump = spawn_frame_pump(rx, socket.tx.clone(), WsSubs::default(), Cap::Root);
    events
        .send(message(Event::Sessions { sessions: vec![] }))
        .ok()
        .unwrap();
    // The event leaves the ring before its send waits for the held writer.
    tokio::time::timeout(Duration::from_secs(2), async {
        while !events.is_empty() {
            tokio::task::yield_now().await;
        }
    })
    .await
    .ok()
    .unwrap();
    events
        .send(message(Event::Projects { projects: vec![] }))
        .ok()
        .unwrap();
    for seq in 0..257 {
        events
            .send(message(Event::Screen {
                screen: screen("unsubscribed", seq),
            }))
            .ok()
            .unwrap();
    }
    drop(writer);
    receive(&mut socket.client).await;
    let next = tokio::time::timeout(Duration::from_secs(2), socket.client.next())
        .await
        .unwrap();
    assert!(matches!(
        next,
        Some(Ok(tokio_tungstenite::tungstenite::Message::Close(_)))
    ));
    tokio::time::timeout(Duration::from_secs(2), pump)
        .await
        .unwrap()
        .unwrap();
}

#[tokio::test]
async fn terminal_commands_require_write_scope_and_current_run() {
    use crate::session::Input;
    let manager = crate::application::test_application(crate::config::Config::default());
    let mut input = manager.sessions.terminal_input_fixture("target", 7).await;
    let socket = socket_pair().await;
    let subs = WsSubs::default();
    let keys = |run_id| {
        ClientMsg::Keys(
            serde_json::from_value(serde_json::json!({
                "name": "target", "keys": ["hello"], "literal": true, "run_id": run_id
            }))
            .unwrap(),
        )
    };
    let paste = |run_id| {
        let request = serde_json::json!({
            "name": "target", "text": "paste λ", "run_id": run_id
        });
        ClientMsg::Paste(serde_json::from_value(request).unwrap())
    };
    let revoked = scoped(&["target"], Level::Rw);
    if let Cap::Scoped(grant) = &revoked {
        grant
            .revoked
            .store(true, std::sync::atomic::Ordering::Release);
    }
    for cap in [
        scoped(&["target"], Level::Ro),
        scoped(&["other"], Level::Rw),
        revoked,
    ] {
        for command in [keys(Some(7)), paste(Some(7))] {
            assert_eq!(
                handle_client_msg(command, &manager, &cap, &socket.tx, &subs).await,
                cap.is_valid()
            );
            assert!(input.try_recv().is_err());
        }
    }
    for cap in [Cap::Root, scoped(&["target"], Level::Rw)] {
        for command in [keys(Some(6)), paste(Some(6))] {
            assert!(handle_client_msg(command, &manager, &cap, &socket.tx, &subs).await);
            assert!(input.try_recv().is_err(), "stale command was queued");
        }
        for run in [Some(7), None] {
            assert!(handle_client_msg(keys(run), &manager, &cap, &socket.tx, &subs).await);
            assert!(
                matches!(input.try_recv().unwrap(), Input::Keys { keys, literal: true } if keys == ["hello"])
            );
            assert!(handle_client_msg(paste(run), &manager, &cap, &socket.tx, &subs).await);
            assert!(
                matches!(input.try_recv().unwrap(), Input::Paste { bytes } if bytes == "paste λ".as_bytes())
            );
        }
    }
    // File URIs are ordinary text for every authorized client, never host imports.
    for cap in [Cap::Root, scoped(&["target"], Level::Rw)] {
        let uri = "file:///tmp/desktop-image.png";
        let paste = ClientMsg::Paste(
            serde_json::from_value(serde_json::json!({
                "name": "target", "text": uri, "run_id": 7
            }))
            .unwrap(),
        );
        assert!(handle_client_msg(paste, &manager, &cap, &socket.tx, &subs).await);
        assert!(
            matches!(input.try_recv().unwrap(), Input::Paste { bytes } if bytes == uri.as_bytes())
        );
    }
    // Image uploads never fall through to literal text, including unauthorized
    // callers, stale runs, and a root caller targeting a non-Codex terminal.
    for cap in [Cap::Root, scoped(&["target"], Level::Rw)] {
        for run in [None, Some(6), Some(7)] {
            let upload = ClientMsg::Paste(
                serde_json::from_value(serde_json::json!({
                    "name": "target", "text": "must not paste", "run_id": run,
                    "image_data": [137, 80, 78, 71, 0, 255]
                }))
                .unwrap(),
            );
            assert!(handle_client_msg(upload, &manager, &cap, &socket.tx, &subs).await);
            assert!(
                input.try_recv().is_err(),
                "rejected image became text input"
            );
        }
    }
}

#[tokio::test]
async fn mouse_and_resize_commands_enforce_scope_before_changing_terminal() {
    use crate::session::Input;
    let manager = crate::application::test_application(crate::config::Config::default());
    let mut input = manager.sessions.terminal_input_fixture("target", 7).await;
    let socket = socket_pair().await;
    let subs = WsSubs::default();
    let mouse = |action| {
        ClientMsg::Mouse(
            serde_json::from_value(serde_json::json!({
                "name": "target", "action": action, "col": 3, "row": 2, "count": 2
            }))
            .unwrap(),
        )
    };
    let resize = || {
        ClientMsg::Resize(
            serde_json::from_value(serde_json::json!({
                "name": "target", "cols": 90, "rows": 30
            }))
            .unwrap(),
        )
    };
    let ro = scoped(&["target"], Level::Ro);
    assert!(handle_client_msg(mouse("press"), &manager, &ro, &socket.tx, &subs).await);
    assert!(handle_client_msg(resize(), &manager, &ro, &socket.tx, &subs).await);
    assert!(input.try_recv().is_err());
    let view = manager.sessions.views().await.pop().unwrap();
    assert_ne!((view.runtime.cols, view.runtime.rows), (90, 30));
    let rw = scoped(&["target"], Level::Rw);
    assert!(handle_client_msg(mouse("invalid"), &manager, &rw, &socket.tx, &subs).await);
    assert!(input.try_recv().is_err());
    assert!(handle_client_msg(mouse("press"), &manager, &rw, &socket.tx, &subs).await);
    assert!(
        matches!(input.try_recv().unwrap(), Input::Bytes(bytes) if bytes == b"\x1b[<0;4;3M".repeat(2))
    );
    assert!(handle_client_msg(resize(), &manager, &rw, &socket.tx, &subs).await);
    let view = manager.sessions.views().await.pop().unwrap();
    assert_eq!((view.runtime.cols, view.runtime.rows), (90, 30));
}

#[tokio::test]
async fn deleted_sessions_release_socket_subscriptions_without_unsubscribe() {
    use crate::shared::wire::{self, client_message::Payload as C, event::Payload as E};
    use futures::SinkExt;
    use prost::Message as _;

    let names: Vec<String> = (0..64).map(|index| format!("temporary-{index}")).collect();
    let manager = crate::application::test_application(crate::config::Config {
        sessions: names
            .iter()
            .chain(std::iter::once(&"retained".to_string()))
            .map(|name| crate::config::SessionCfg {
                name: name.clone(),
                autostart: false,
                ..Default::default()
            })
            .collect(),
        ..Default::default()
    });
    manager.sessions.sync_from_config().await;
    let mut emu = crate::emu::SessionEmu::new(80, 24);
    emu.feed(b"ready");
    manager.sessions.apply_frame("retained", emu.render()).await;
    let (socket, mut client, server) = raw_socket_pair().await;
    let (tx, rx) = socket.split();
    let subs = WsSubs::default();
    for name in &names {
        subs.lock()
            .await
            .insert(name.clone(), manager.sessions.watching(name));
    }
    let mut intake = CommandIntake {
        tx: WsTx::new(
            tx,
            &manager,
            &Cap::Root,
            manager.config_authority.root_generation(),
        ),
        rx,
        subs: subs.clone(),
        auth_changes: manager.config_authority.auth_changes(),
        scrolls: ScrollReplies::new(),
    };
    let running_manager = manager.clone();
    let driving = tokio::spawn(async move { intake.run(&running_manager, &Cap::Root).await });
    for name in &names {
        manager.sessions.forget(name).await;
    }
    // Intake prioritizes watch retirement before this marker command. Its reply
    // establishes that cleanup ran while the connection remained usable.
    client
        .send(tokio_tungstenite::tungstenite::Message::Binary(
            wire::ClientMessage {
                payload: Some(C::Sub(wire::NameReq {
                    name: Some("retained".into()),
                })),
            }
            .encode_to_vec(),
        ))
        .await
        .unwrap();
    assert!(matches!(receive(&mut client).await, E::Screen(s) if s.name == "retained"));
    let subscriptions = subs.lock().await;
    assert_eq!(subscriptions.len(), 1);
    assert!(subscriptions.contains_key("retained"));
    drop(subscriptions);
    client.close(None).await.unwrap();
    tokio::time::timeout(Duration::from_secs(2), driving)
        .await
        .unwrap()
        .unwrap();
    server.abort();
}

#[tokio::test]
async fn clipboard_events_only_reach_current_root_subscriptions_in_order() {
    use crate::shared::wire::event::Payload as E;
    let manager = crate::session::test_manager(crate::config::Config::default());
    let mut socket = socket_pair().await;
    let subs = WsSubs::default();
    subs.lock()
        .await
        .insert("watched".into(), manager.watching("watched"));
    subs.lock()
        .await
        .insert("retired".into(), manager.watching("retired"));
    subs.lock().await.remove("retired");
    let (events, rx) = broadcast::channel(32);
    let pump = spawn_frame_pump(rx, socket.tx.clone(), subs.clone(), Cap::Root);
    for (name, text) in [
        ("unwatched", "ignore"),
        ("retired", "ignore"),
        ("watched", "first"),
        ("watched", "second"),
    ] {
        events
            .send(message(Event::Clipboard {
                name: name.into(),
                run_id: 7,
                text: text.into(),
            }))
            .ok()
            .unwrap();
    }
    events
        .send(message(Event::Sessions { sessions: vec![] }))
        .ok()
        .unwrap();
    for expected in ["first", "second"] {
        assert!(
            matches!(receive(&mut socket.client).await, E::Clipboard(copy) if copy.name == "watched" && copy.run_id == 7 && copy.text == expected)
        );
    }
    assert!(matches!(receive(&mut socket.client).await, E::Sessions(_)));
    pump.abort();
}

async fn rotate_root(manager: &App) {
    let mut config = manager.config_authority.snapshot().await;
    config.daemon.token = uuid::Uuid::new_v4().to_string();
    manager
        .config_authority
        .publish_config(crate::config::transactions::ConfigChange {
            new: config,
            root_token_changed: true,
        })
        .await;
}

#[tokio::test]
async fn root_commands_queued_behind_rotation_have_no_effect() {
    use crate::api::types::BreadcrumbReq;
    use crate::config::{Config, LibraryItemCfg, LibraryItemKind, ProjectCfg};
    let manager = crate::application::test_application(Config {
        sessions: vec![crate::config::SessionCfg {
            name: "target".into(),
            project: "repo".into(),
            ..Default::default()
        }],
        projects: vec![ProjectCfg {
            name: "repo".into(),
            dir: "/tmp/project".into(),
            ..Default::default()
        }],
        library: vec![LibraryItemCfg {
            name: "context".into(),
            kind: LibraryItemKind::Breadcrumb,
            text: "context".into(),
            ..Default::default()
        }],
        ..Default::default()
    });
    let mut input = manager.sessions.terminal_input_fixture("target", 7).await;
    manager
        .sessions
        .paste_breadcrumb("target", "context", vec![])
        .await
        .unwrap();
    assert!(
        matches!(input.try_recv().unwrap(), crate::session::Input::Paste { bytes } if bytes == b"context")
    );
    let socket = socket_pair_for(&manager).await;
    let subs = WsSubs::default();
    let volume = manager.services.music.state().await.volume;
    let mut transaction = Box::pin(
        manager
            .sessions
            .session_operation(std::future::pending::<()>()),
    );
    assert!(futures::poll!(transaction.as_mut()).is_pending());
    let admitted = std::sync::atomic::AtomicBool::new(false);
    let audio = super::commands::dispatch_client_msg(
        ClientMsg::Audio(AudioReq {
            selection: None,
            volume: if volume < 0.5 { 0.75 } else { 0.25 },
        }),
        &manager,
        &Cap::Root,
        &socket.tx,
        &subs,
        &admitted,
    );
    let breadcrumb = super::commands::dispatch_client_msg(
        ClientMsg::Breadcrumb(BreadcrumbReq {
            name: "target".into(),
            breadcrumb: "context".into(),
            random_tips: vec![],
        }),
        &manager,
        &Cap::Root,
        &socket.tx,
        &subs,
        &admitted,
    );
    tokio::pin!(audio, breadcrumb);
    assert!(futures::poll!(audio.as_mut()).is_pending());
    assert!(futures::poll!(breadcrumb.as_mut()).is_pending());
    rotate_root(&manager).await;
    drop(transaction);
    assert!(!audio.await);
    assert!(!breadcrumb.await);
    assert!(input.try_recv().is_err());
    assert_eq!(manager.services.music.state().await.volume, volume);
}

#[tokio::test]
async fn root_terminal_input_rechecks_rotation_after_terminal_admission() {
    let manager = crate::application::test_application(crate::config::Config::default());
    let mut input = manager.sessions.terminal_input_fixture("target", 7).await;
    let socket = socket_pair_for(&manager).await;
    let subs = WsSubs::default();
    let guard = manager
        .sessions
        .terminal_boundary("target")
        .write_owned()
        .await;
    let admitted = std::sync::atomic::AtomicBool::new(false);
    let command = super::commands::dispatch_client_msg(
        ClientMsg::Keys(
            serde_json::from_value(serde_json::json!({
                "name": "target", "keys": ["hello"], "literal": true
            }))
            .unwrap(),
        ),
        &manager,
        &Cap::Root,
        &socket.tx,
        &subs,
        &admitted,
    );
    tokio::pin!(command);
    assert!(futures::poll!(command.as_mut()).is_pending());
    rotate_root(&manager).await;
    drop(guard);
    assert!(!command.await);
    assert!(input.try_recv().is_err());
}

#[tokio::test]
async fn root_snapshot_revocation_interrupts_initialization_while_writer_is_blocked() {
    let manager = crate::application::test_application(crate::config::Config::default());
    let mut socket = socket_pair_for(&manager).await;
    let writer = socket.tx.lock().await;
    let mut changes = manager.config_authority.auth_changes();
    let snapshot = initialize_snapshot(&socket.tx, &manager, &Cap::Root, &mut changes);
    tokio::pin!(snapshot);
    assert!(futures::poll!(snapshot.as_mut()).is_pending());
    rotate_root(&manager).await;
    assert!(
        !tokio::time::timeout(Duration::from_secs(2), snapshot)
            .await
            .unwrap()
    );
    drop(writer);
    tokio::time::timeout(Duration::from_millis(20), socket.client.next())
        .await
        .expect_err("revoked root published a snapshot event");
}

#[tokio::test]
async fn root_snapshot_and_live_sends_recheck_rotation_after_waiting_for_writer() {
    for snapshot in [true, false] {
        let manager = crate::application::test_application(crate::config::Config::default());
        let mut socket = socket_pair_for(&manager).await;
        let writer = socket.tx.lock().await;
        let event = message(Event::Projects { projects: vec![] });
        let publishing = async {
            if snapshot {
                send_initial_snapshot(&socket.tx, &manager, &Cap::Root).await
            } else {
                super::outbound::send_authorized(&socket.tx, &Cap::Root, &event)
                    .await
                    .is_ok()
            }
        };
        tokio::pin!(publishing);
        assert!(futures::poll!(publishing.as_mut()).is_pending());
        rotate_root(&manager).await;
        drop(writer);
        assert!(!publishing.await);
        tokio::time::timeout(Duration::from_millis(20), socket.client.next())
            .await
            .expect_err("revoked root published an event");
    }
}

#[tokio::test]
async fn root_revocation_cancels_dispatch_still_waiting_for_admission() {
    let manager = crate::application::test_application(crate::config::Config::default());
    let socket = socket_pair_for(&manager).await;
    let subs = WsSubs::default();
    let mut transaction = Box::pin(
        manager
            .sessions
            .session_operation(std::future::pending::<()>()),
    );
    assert!(futures::poll!(transaction.as_mut()).is_pending());
    let dispatch = handle_client_msg(
        ClientMsg::Audio(AudioReq {
            selection: None,
            volume: 0.25,
        }),
        &manager,
        &Cap::Root,
        &socket.tx,
        &subs,
    );
    tokio::pin!(dispatch);
    assert!(futures::poll!(dispatch.as_mut()).is_pending());
    rotate_root(&manager).await;
    assert!(
        !tokio::time::timeout(Duration::from_secs(2), dispatch)
            .await
            .unwrap()
    );
    // Cancellation finishes even though the exclusive boundary is still held.
    drop(transaction);
}

#[tokio::test]
async fn root_revocation_finishes_admitted_subscription_without_blocking_on_publication() {
    let manager = crate::application::test_application(crate::config::Config::default());
    manager.sessions.terminal_input_fixture("target", 7).await;
    let mut emu = crate::emu::SessionEmu::new(80, 24);
    emu.feed(b"ready");
    manager.sessions.apply_frame("target", emu.render()).await;
    let socket = socket_pair_for(&manager).await;
    let subs = WsSubs::default();
    let writer = socket.tx.lock().await;
    let dispatch = handle_client_msg(
        ClientMsg::Sub {
            name: "target".into(),
        },
        &manager,
        &Cap::Root,
        &socket.tx,
        &subs,
    );
    tokio::pin!(dispatch);
    assert!(futures::poll!(dispatch.as_mut()).is_pending());
    assert!(subs.lock().await.contains_key("target"));
    rotate_root(&manager).await;
    // Publication is cancellable even though the admitted subscription completed.
    assert!(!dispatch.await);
    drop(writer);
    assert!(subs.lock().await.contains_key("target"));
}

#[tokio::test]
async fn socket_writer_retains_the_epoch_from_authentication() {
    let manager = crate::application::test_application(crate::config::Config::default());
    let epoch = manager.config_authority.root_generation();
    let original = manager.config_authority.snapshot().await;
    rotate_root(&manager).await;
    manager
        .config_authority
        .publish_config(crate::config::transactions::ConfigChange {
            new: original,
            root_token_changed: true,
        })
        .await;
    let (socket, _client, server) = raw_socket_pair().await;
    let (sink, _) = socket.split();
    let tx = WsTx::new(sink, &manager, &Cap::Root, epoch);
    assert!(
        !tx.is_valid(&Cap::Root),
        "restoring a token must not revive its epoch"
    );
    server.abort();
}

#[tokio::test]
async fn root_revocation_retains_admitted_command_and_its_guard_until_completion() {
    let manager = crate::application::test_application(crate::config::Config::default());
    manager.sessions.terminal_input_fixture("target", 7).await;
    let socket = socket_pair_for(&manager).await;
    let subs = WsSubs::default();
    let subscriptions = subs.lock().await;
    let terminal = manager.sessions.terminal_boundary("target");
    let dispatch = handle_client_msg(
        ClientMsg::Sub {
            name: "target".into(),
        },
        &manager,
        &Cap::Root,
        &socket.tx,
        &subs,
    );
    tokio::pin!(dispatch);
    // Admission acquires the terminal guard before waiting to commit the subscription.
    assert!(futures::poll!(dispatch.as_mut()).is_pending());
    terminal
        .try_write()
        .expect_err("admitted command must retain its guard");
    rotate_root(&manager).await;
    assert!(futures::poll!(dispatch.as_mut()).is_pending());
    terminal
        .try_write()
        .expect_err("revocation dropped an admitted guard");
    drop(subscriptions);
    assert!(dispatch.await);
    assert!(
        subs.lock().await.contains_key("target"),
        "admitted work was cancelled"
    );
    drop(
        terminal
            .try_write()
            .expect("completed command retained its guard"),
    );
}
