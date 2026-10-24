//! Recovery through the real process, HTTP snapshots, and WebSocket terminal transport.
use super::*;
use futures::{SinkExt, StreamExt};
use prost::Message;
use tokio_tungstenite::tungstenite::{Message as WsMessage, client::IntoClientRequest};

#[expect(
    dead_code,
    reason = "the shared wire schema includes other API operations"
)]
mod wire {
    include!(concat!(env!("OUT_DIR"), "/slopworld.rs"));
}

type Socket =
    tokio_tungstenite::WebSocketStream<tokio_tungstenite::MaybeTlsStream<tokio::net::TcpStream>>;
const WORKER: &str = "recovery-worker";
const STATE_ID: &str = "2222222222222222";

impl Daemon {
    fn checked_tmux(&self, args: &[&str]) -> Vec<u8> {
        let output = self.tmux(args);
        assert!(
            output.status.success(),
            "{}",
            String::from_utf8_lossy(&output.stderr)
        );
        output.stdout
    }

    fn seed_worker(&self) {
        fs::create_dir_all(self.root.join("config/projects")).unwrap();
        fs::create_dir_all(self.root.join("data/agents")).unwrap();
        fs::write(
            self.root.join("config/projects/1111111111111111.toml"),
            format!(
                "storage_order = 0\nid = '1111111111111111'\nname = 'repo'\ndir = '{}'\n",
                self.root.display()
            ),
        )
        .unwrap();
        fs::write(
            self.root.join("data/agents/3333333333333333.toml"),
            "storage_order = 0\nname = 'parent'\nstate_id = '3333333333333333'\nproject = 'repo'\ncmd = 'sh'\n",
        )
        .unwrap();
        fs::write(self.root.join(format!("data/agents/{STATE_ID}.toml")), format!(
            "storage_order = 1\nname = '{WORKER}'\nstate_id = '{STATE_ID}'\nproject = 'repo'\ncmd = 'sh'\nworker = true\nparent = 'parent'\ntask_id = 'recovery-task'\n"
        )).unwrap();
        // Seed a surviving worker directly so this test needs neither a sandbox nor an agent CLI.
        // It still passes through full startup adoption before each daemon replacement.
        self.checked_tmux(&["-f", "/dev/null", "new-session", "-d", "-s", WORKER,
            "-x", "90", "-y", "28", "-c", self.root.to_str().unwrap(),
            "stty -echo; printf 'RECOVERY_READY\\n'; while IFS= read -r line; do printf 'ACK:%s\\n' \"$line\"; done"]);
        for (key, value) in [
            ("@slopworld_worker", "1"),
            ("@slopworld_worker_parent", "parent"),
            ("@slopworld_worker_task", "recovery-task"),
            ("@slopworld_worker_durable", "1"),
            ("@slopworld_worker_state", STATE_ID),
        ] {
            self.checked_tmux(&["set-option", "-t", WORKER, key, value]);
        }
    }

    fn worker_view(&self) -> wire::SessionView {
        let bytes = ureq::get(&format!("http://{}/api/sessions", self.address))
            .config()
            .timeout_global(Some(Duration::from_secs(3)))
            .build()
            .header("x-slop-token", "lifecycle-secret")
            .call()
            .unwrap()
            .body_mut()
            .read_to_vec()
            .unwrap();
        let sessions = wire::SessionsReply::decode(bytes.as_slice())
            .unwrap()
            .sessions;
        assert_eq!(sessions.iter().filter(|s| s.name == WORKER).count(), 1);
        sessions.into_iter().find(|s| s.name == WORKER).unwrap()
    }

    async fn worker_socket(&self, marker: &str) -> Socket {
        let view = self.worker_view();
        assert!(!view.host && !view.ephemeral);
        assert_eq!(view.project, "repo");
        let worker = view.worker.unwrap();
        assert!(worker.enabled && worker.durable);
        assert_eq!(worker.parent, "parent");
        assert_eq!(worker.task_id, "recovery-task");
        assert!(view.runtime.unwrap().alive, "{}", self.logs());
        let mut request = format!("ws://{}/ws", self.address)
            .into_client_request()
            .unwrap();
        request
            .headers_mut()
            .insert("x-slop-token", "lifecycle-secret".parse().unwrap());
        request.headers_mut().insert(
            "sec-websocket-protocol",
            "slopworld.protobuf.v2".parse().unwrap(),
        );
        let (mut socket, _) = tokio_tungstenite::connect_async(request).await.unwrap();
        send(
            &mut socket,
            wire::client_message::Payload::Sub(wire::NameReq {
                name: Some(WORKER.into()),
            }),
        )
        .await;
        let screen = screen_containing(&mut socket, marker).await;
        assert_eq!((screen.cols, screen.rows), (Some(90), Some(28)));
        assert!(screen.lines.join("\n").contains("RECOVERY_READY"));
        if marker != "RECOVERY_READY" {
            assert!(screen.lines.join("\n").contains("ACK:before-restart"));
        }
        socket
    }

    fn signal(&mut self, signal: &str) {
        assert!(
            Command::new("kill")
                .args(["-s", signal, &self.child.as_ref().unwrap().id().to_string()])
                .status()
                .unwrap()
                .success()
        );
        assert_eq!(self.wait().success(), signal != "KILL", "{}", self.logs());
    }

    async fn offline_output(&self, text: &str) {
        self.checked_tmux(&["send-keys", "-t", WORKER, "-l", text]);
        self.checked_tmux(&["send-keys", "-t", WORKER, "Enter"]);
        tokio::time::timeout(Duration::from_secs(5), async {
            loop {
                let bytes = self.checked_tmux(&["capture-pane", "-p", "-t", WORKER]);
                if String::from_utf8(bytes)
                    .unwrap()
                    .contains(&format!("ACK:{text}"))
                {
                    return;
                }
                tokio::time::sleep(Duration::from_millis(20)).await;
            }
        })
        .await
        .expect("worker did not produce output while daemon was stopped");
    }
}

async fn send(socket: &mut Socket, payload: wire::client_message::Payload) {
    socket
        .send(WsMessage::Binary(
            wire::ClientMessage {
                payload: Some(payload),
            }
            .encode_to_vec(),
        ))
        .await
        .unwrap();
}

async fn screen_containing(socket: &mut Socket, marker: &str) -> wire::ScreenView {
    tokio::time::timeout(Duration::from_secs(10), async {
        loop {
            let message = socket.next().await.expect("socket closed").unwrap();
            if let WsMessage::Binary(bytes) = message
                && let Some(wire::event::Payload::Screen(screen)) =
                    wire::Event::decode(bytes.as_slice()).unwrap().payload
                && screen.name == WORKER
                && screen.lines.join("\n").contains(marker)
            {
                return screen;
            }
        }
    })
    .await
    .unwrap_or_else(|_| panic!("screen did not contain {marker:?}"))
}

async fn assert_input(daemon: &Daemon, socket: &mut Socket, text: &str) {
    let run_id = daemon.worker_view().runtime.unwrap().run_id;
    send(
        socket,
        wire::client_message::Payload::Keys(wire::KeysReq {
            name: Some(WORKER.into()),
            keys: vec![text.into()],
            literal: Some(true),
            run_id: Some(run_id),
            ..Default::default()
        }),
    )
    .await;
    send(
        socket,
        wire::client_message::Payload::Keys(wire::KeysReq {
            name: Some(WORKER.into()),
            keys: vec!["Enter".into()],
            run_id: Some(run_id),
            ..Default::default()
        }),
    )
    .await;
    screen_containing(socket, &format!("ACK:{text}")).await;
}

#[tokio::test]
async fn managed_worker_recovers_identity_capture_and_input_after_term_and_kill() {
    let reservation = TcpListener::bind("127.0.0.1:0").unwrap();
    let mut daemon = Daemon::new(reservation.local_addr().unwrap());
    daemon.seed_worker();
    let pane = daemon.checked_tmux(&["display-message", "-p", "-t", WORKER, "#{pane_pid}"]);
    drop(reservation);
    daemon.start();
    daemon.ready();
    let mut socket = daemon.worker_socket("RECOVERY_READY").await;
    assert_input(&daemon, &mut socket, "before-restart").await;
    for signal in ["TERM", "KILL"] {
        daemon.signal(signal);
        drop(socket);
        // Output produced while the daemon is absent must be recovered from tmux on adoption.
        let offline = format!("offline-{signal}");
        daemon.offline_output(&offline).await;
        daemon.start();
        daemon.ready();
        assert_eq!(
            daemon.checked_tmux(&["display-message", "-p", "-t", WORKER, "#{pane_pid}"]),
            pane
        );
        assert_eq!(
            String::from_utf8(daemon.checked_tmux(&[
                "show-options",
                "-v",
                "-t",
                WORKER,
                "@slopworld_worker_state"
            ]))
            .unwrap()
            .trim(),
            STATE_ID
        );
        socket = daemon.worker_socket(&format!("ACK:{offline}")).await;
        assert_input(&daemon, &mut socket, &format!("after-{signal}")).await;
    }
    socket.close(None).await.unwrap();
    daemon.signal("TERM");
}
