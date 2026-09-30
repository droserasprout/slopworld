use super::*;

async fn queued_manager() -> (Arc<Manager>, mpsc::UnboundedReceiver<Input>) {
    let manager = crate::session::test_manager_with_socket(
        Config::default(),
        format!("slopd-input-{}", uuid::Uuid::new_v4()),
    );
    let (tx, rx) = mpsc::unbounded_channel();
    let mut live = Live::new(
        SessionCfg {
            name: "target".into(),
            ..Default::default()
        },
        TitleCapture::default(),
    );
    live.input.sender = Some(tx);
    manager.live.write().await.insert("target".into(), live);
    (manager, rx)
}

#[tokio::test]
async fn paste_queues_for_a_live_session_without_listing_tmux_sessions() {
    let (manager, mut rx) = queued_manager().await;
    manager.live.write().await.get_mut("target").unwrap().state = State::Working;

    manager.paste("target", "hello λ").await.unwrap();

    assert!(
        matches!(rx.try_recv().unwrap(), Input::Paste { bytes } if bytes == "hello λ".as_bytes())
    );
}

#[tokio::test]
async fn paste_queues_after_reader_attachment_before_first_frame() {
    let (manager, mut rx) = queued_manager().await;
    manager
        .live
        .write()
        .await
        .get_mut("target")
        .unwrap()
        .capture
        .reader_token = Some(Arc::new(()));

    manager.paste("target", "early").await.unwrap();

    assert!(matches!(rx.try_recv().unwrap(), Input::Paste { bytes } if bytes == b"early"));
}

#[tokio::test]
async fn ordinary_enter_and_literal_keys_pass_through_without_injected_input() {
    let (manager, mut rx) = queued_manager().await;
    for literal in [false, true] {
        let keys = vec!["Left".into(), "Enter".into(), "Right".into()];
        manager.send_keys("target", keys.clone(), literal).await;
        assert!(
            matches!(rx.try_recv().unwrap(), Input::Keys { keys: actual, literal: mode }
            if actual == keys && mode == literal)
        );
        assert!(rx.try_recv().is_err());
    }
}

#[tokio::test]
async fn mouse_input_requires_reporting_and_repeats_at_least_once() {
    let (manager, mut rx) = queued_manager().await;
    let emu = Arc::new(std::sync::Mutex::new(crate::emu::SessionEmu::new(80, 24)));
    manager
        .live
        .write()
        .await
        .get_mut("target")
        .unwrap()
        .capture
        .emu = Some(emu.clone());
    let event = || MouseInput {
        action: crate::emu::MouseAction::Press,
        button: 0,
        col: 3,
        row: 2,
    };
    manager.send_mouse("missing", event(), 1).await;
    manager.send_mouse("target", event(), 1).await;
    assert!(rx.try_recv().is_err());
    emu.lock().unwrap().feed(b"\x1b[?1000h\x1b[?1006h");
    for count in [0, 1, 3] {
        manager.send_mouse("target", event(), count).await;
        assert!(matches!(rx.try_recv().unwrap(), Input::Bytes(bytes)
                if bytes == b"\x1b[<0;4;3M".repeat(usize::from(count.max(1)))));
    }
    assert!(rx.try_recv().is_err());
}

#[tokio::test]
async fn stopped_session_resize_clamps_dimensions_and_updates_the_mirror() {
    use crate::shared::protocol::{
        TERMINAL_MAX_COLS, TERMINAL_MAX_ROWS, TERMINAL_MIN_COLS, TERMINAL_MIN_ROWS,
    };
    let (manager, _) = queued_manager().await;
    let emu = Arc::new(std::sync::Mutex::new(crate::emu::SessionEmu::new(80, 24)));
    manager
        .live
        .write()
        .await
        .get_mut("target")
        .unwrap()
        .capture
        .emu = Some(emu.clone());
    assert!(
        manager
            .resize("missing", 80, 24)
            .await
            .unwrap_err()
            .to_string()
            .contains("no such session")
    );
    for (cols, rows, expected) in [
        (0, 0, (TERMINAL_MIN_COLS, TERMINAL_MIN_ROWS)),
        (u16::MAX, u16::MAX, (TERMINAL_MAX_COLS, TERMINAL_MAX_ROWS)),
        (80, 24, (80, 24)),
    ] {
        manager.resize("target", cols, rows).await.unwrap();
        let live = manager.live.read().await;
        assert_eq!((live["target"].cols, live["target"].rows), expected);
        let frame = emu.lock().unwrap().render();
        assert_eq!(frame.lines.len(), usize::from(expected.1));
    }
    manager.resize("target", 80, 24).await.unwrap();
    assert!(
        manager
            .paste("target", "text")
            .await
            .unwrap_err()
            .to_string()
            .contains("not running")
    );
}

#[tokio::test]
async fn stale_input_queues_exit_without_draining_into_replacements() {
    for replaced_identity in [true, false] {
        let manager = crate::session::test_manager(Config::default());
        let (sink, mut received) = mpsc::unbounded_channel();
        *manager.input_sink.lock().unwrap() = Some(sink);
        let (tx, rx) = mpsc::unbounded_channel();
        let mut live = Live::new(
            SessionCfg {
                name: "target".into(),
                ..Default::default()
            },
            TitleCapture::default(),
        );
        let old_identity = live.cfg.state_id.clone();
        if replaced_identity {
            live.cfg.state_id = uuid::Uuid::new_v4().to_string();
        } else {
            live.run_id = 1;
        }
        live.input.sender = Some(tx.clone());
        manager.live.write().await.insert("target".into(), live);
        tx.send(Input::Keys {
            keys: vec!["Enter".into()],
            literal: false,
        })
        .unwrap();
        tokio::time::timeout(
            Duration::from_secs(1),
            Manager::run_input(
                Arc::downgrade(&manager),
                "target".into(),
                old_identity,
                0,
                rx,
            ),
        )
        .await
        .expect("stale queue kept consuming input");
        assert!(tx.is_closed());
        assert!(received.try_recv().is_err(), "stale input reached dispatch");
        let (identity, run) = {
            let live = manager.live.read().await;
            (live["target"].cfg.state_id.clone(), live["target"].run_id)
        };
        assert!(
            manager
                .dispatch_queued_input("target", &identity, run, Input::Bytes(b"current".to_vec()))
                .await
        );
        assert!(matches!(received.try_recv().unwrap(), Input::Bytes(bytes) if bytes == b"current"));
    }
}

// Explicit diagnostic: production batching and tmux dispatch, isolated from the
// user's server and desktop. No timing assertion: report capacity, not a flaky test.
#[tokio::test]
#[ignore = "isolated tmux throughput diagnostic"]
async fn benchmark_mixed_input_dispatch() {
    let socket_owner = crate::test_support::TmuxSocket::new();
    let output = tokio::process::Command::new("tmux")
        .args([
            "-S",
            &socket_owner.path,
            "-f",
            "/dev/null",
            "new-session",
            "-d",
            "-s",
            "sink",
            "stty raw -echo; cat >/dev/null",
        ])
        .output()
        .await
        .unwrap();
    assert!(
        output.status.success(),
        "{}",
        String::from_utf8_lossy(&output.stderr)
    );
    let tmux = Tmux::new(&socket_owner.path);
    for label in ["mixed", "keys"] {
        let events = (0..600)
            .map(|index| {
                if label == "mixed" && index % 3 != 2 {
                    Input::Bytes(b"\x1b[<64;10;10M".to_vec())
                } else {
                    Input::Keys {
                        keys: vec!["Up".into()],
                        literal: false,
                    }
                }
            })
            .collect();
        // Give the batcher the entire workload at once: this is its best-case
        // command reduction, not a recreation of arrival/frame scheduling.
        let batch = merge_input(events);
        let commands = batch.len();
        let start = std::time::Instant::now();
        for item in batch {
            // Use the production tmux operations, but propagate failures instead
            // of reporting rejected commands as successful throughput.
            match item {
                Input::Bytes(bytes) => tmux.send_bytes("sink", &bytes).await.unwrap(),
                Input::Keys { keys, literal } => {
                    tmux.send_keys("sink", &keys, literal).await.unwrap()
                }
                _ => panic!("diagnostic only creates bytes and keys"),
            }
        }
        println!(
            "input dispatch {label}: events=600 commands={commands} elapsed_ms={:.3} events_per_second={:.1}",
            start.elapsed().as_secs_f64() * 1000.0,
            600.0 / start.elapsed().as_secs_f64()
        );
    }
}
