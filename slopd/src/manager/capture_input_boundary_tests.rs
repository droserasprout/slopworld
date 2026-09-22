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
    live.input = Some(tx);
    manager.live.write().await.insert("target".into(), live);
    (manager, rx)
}

#[tokio::test]
async fn enter_injects_breadcrumbs_once_between_preceding_keys_and_submission() {
    let (manager, mut rx) = queued_manager().await;
    {
        let mut live = manager.live.write().await;
        let live = live.get_mut("target").unwrap();
        live.breadcrumbs = b"remember {{ random_tip }}".to_vec();
        live.breadcrumbs_pending = true;
    }
    manager
        .send_keys(
            "target",
            vec!["Left".into(), "Enter".into(), "Right".into()],
            false,
            vec!["the tests".into()],
        )
        .await;
    assert!(
        matches!(rx.try_recv().unwrap(), Input::Keys { keys, literal: false } if keys == ["Left"])
    );
    assert!(
        matches!(rx.try_recv().unwrap(), Input::Paste { bytes } if bytes == b"remember the tests")
    );
    assert!(
        matches!(rx.try_recv().unwrap(), Input::Gap(delay) if delay == Duration::from_millis(ENTER_GAP_MS))
    );
    assert!(
        matches!(rx.try_recv().unwrap(), Input::Keys { keys, literal: false } if keys == ["Enter", "Right"])
    );
    assert!(rx.try_recv().is_err());
    manager
        .send_keys("target", vec!["Enter".into()], false, vec![])
        .await;
    assert!(matches!(rx.try_recv().unwrap(), Input::Keys { keys, .. } if keys == ["Enter"]));
    assert!(rx.try_recv().is_err());
    assert!(!manager.live.read().await["target"].breadcrumbs_pending);
}

#[tokio::test]
async fn literal_enter_and_other_keys_leave_breadcrumbs_pending() {
    let (manager, mut rx) = queued_manager().await;
    manager
        .live
        .write()
        .await
        .get_mut("target")
        .unwrap()
        .breadcrumbs_pending = true;
    for (key, literal) in [("Enter", true), ("Left", false)] {
        manager
            .send_keys("target", vec![key.into()], literal, vec![])
            .await;
        assert!(
            matches!(rx.try_recv().unwrap(), Input::Keys { keys, literal: actual } if keys == [key] && actual == literal)
        );
        assert!(manager.live.read().await["target"].breadcrumbs_pending);
    }
    // An empty pending breadcrumb is consumed without adding a paste or a delay.
    manager
        .send_keys("target", vec!["Enter".into()], false, vec![])
        .await;
    assert!(matches!(rx.try_recv().unwrap(), Input::Keys { .. }));
    assert!(rx.try_recv().is_err());
    assert!(!manager.live.read().await["target"].breadcrumbs_pending);
    assert!(manager.consume_breadcrumbs("missing", &[]).await.is_none());
}

#[tokio::test]
async fn mouse_input_requires_reporting_and_repeats_at_least_once() {
    let (manager, mut rx) = queued_manager().await;
    let emu = Arc::new(std::sync::Mutex::new(crate::emu::SessionEmu::new(80, 24)));
    manager.live.write().await.get_mut("target").unwrap().emu = Some(emu.clone());
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
    manager.live.write().await.get_mut("target").unwrap().emu = Some(emu.clone());
    assert!(manager
        .resize("missing", 80, 24)
        .await
        .unwrap_err()
        .to_string()
        .contains("no such session"));
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
    assert!(manager
        .paste("target", "text")
        .await
        .unwrap_err()
        .to_string()
        .contains("not running"));
}

#[tokio::test]
async fn stale_input_queues_exit_without_draining_into_replacements() {
    for replaced_identity in [true, false] {
        let manager = crate::session::test_manager(Config::default());
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
        live.input = Some(tx.clone());
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
    }
}
