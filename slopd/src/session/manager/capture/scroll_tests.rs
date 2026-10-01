use super::*;

async fn fixture(bytes: &[u8]) -> Arc<Manager> {
    let manager = crate::session::test_manager(Config::default());
    let mut emu = SessionEmu::new(12, 3);
    emu.feed(bytes);
    let frame = emu.render();
    let mut live = Live::new(
        SessionCfg {
            name: "agent".into(),
            ..Default::default()
        },
        TitleCapture::default(),
    );
    live.cols = 12;
    live.rows = 3;
    live.capture.emu = Some(Arc::new(Mutex::new(emu)));
    manager.live.write().await.insert("agent".into(), live);
    manager.apply_frame("agent", frame).await;
    manager
}

#[tokio::test]
async fn scroll_cache_reuses_frames_but_echoes_each_request() {
    let m = fixture(b"one\r\ntwo\r\nthree\r\nfour\r\nfive").await;
    let first = m.scroll_capture("agent", 1, 41).await.unwrap();
    assert_eq!(first.off, 1);
    assert!(first.history >= 2);
    assert!(first.lines.iter().any(|line| line.contains("two")));
    let next = m.scroll_capture("agent", 1, 42).await.unwrap();
    assert_eq!(next.lines, first.lines);
    assert_eq!(next.request_id, 42);
    assert_eq!(m.scroll_cache.lock().unwrap()["agent"].view.request_id, 41);
    m.forget_scroll("missing");
    m.forget_scroll("agent");
    assert!(m.scroll_cache.lock().unwrap().is_empty());
    assert_eq!(
        m.scroll_capture("agent", 1, 43).await.unwrap().lines,
        first.lines
    );
}

#[tokio::test]
async fn changed_offset_sequence_and_dimensions_invalidate_cached_scroll() {
    let m = fixture(b"one\r\ntwo\r\nthree\r\nfour\r\nfive").await;
    let first = m.scroll_capture("agent", 1, 1).await.unwrap();
    let older = m.scroll_capture("agent", 2, 2).await.unwrap();
    assert_eq!(older.off, 2);
    assert_ne!(older.lines, first.lines);
    // Replace only the cached payload with invalid data.
    // A change to any cache-key component must prevent reuse of this payload.
    for field in ["seq", "cols", "rows"] {
        {
            let mut cache = m.scroll_cache.lock().unwrap();
            let cached = cache.get_mut("agent").unwrap();
            cached.view.title = "stale".into();
            match field {
                "seq" => cached.live_seq += 1,
                "cols" => cached.cols += 1,
                _ => cached.rows += 1,
            }
        }
        let refreshed = m.scroll_capture("agent", 2, 3).await.unwrap();
        assert_ne!(refreshed.title, "stale", "{field}");
        assert_eq!(refreshed.lines, older.lines);
    }
    let clamped = m.scroll_capture("agent", u32::MAX, 4).await.unwrap();
    assert_eq!(clamped.off, clamped.history);
    assert_eq!(clamped.request_id, 4);
}

#[tokio::test]
async fn live_and_empty_history_requests_preserve_the_live_frame() {
    let m = fixture(b"hello").await;
    let live = m.screen("agent").await.unwrap();
    let zero = m.scroll_capture("agent", 0, 9).await.unwrap();
    assert_eq!(zero.lines, live.lines);
    assert_eq!(zero.request_id, live.request_id);
    let no_history = m.scroll_capture("agent", 5, 10).await.unwrap();
    assert_eq!(no_history.lines, live.lines);
    assert_eq!(
        (no_history.off, no_history.history, no_history.request_id),
        (0, 0, 10)
    );
    assert!(m.scroll_cache.lock().unwrap().is_empty());
    assert!(m.scroll_capture("missing", 0, 1).await.is_none());
    assert!(m.scroll_capture("missing", 1, 1).await.is_none());
    m.live.write().await.get_mut("agent").unwrap().capture.emu = None;
    assert!(m.scroll_capture("agent", 1, 1).await.is_none());
    assert!(m.scroll_capture("agent", 0, 1).await.is_some());
}

#[tokio::test]
async fn replacement_emulator_cannot_reuse_a_cache_with_matching_frame_and_size() {
    let manager = fixture(b"one\r\ntwo\r\nthree\r\nfour\r\nfive").await;
    let old = manager.scroll_capture("agent", 1, 1).await.unwrap();
    let mut replacement = SessionEmu::new(12, 3);
    replacement.feed(b"new one\r\nnew two\r\nnew three\r\nnew four\r\nnew five");
    manager
        .live
        .write()
        .await
        .get_mut("agent")
        .unwrap()
        .capture
        .emu = Some(Arc::new(Mutex::new(replacement)));
    let fresh = manager.scroll_capture("agent", 1, 2).await.unwrap();
    assert_ne!(fresh.lines, old.lines);
    assert!(fresh.lines.iter().any(|line| line.contains("new two")));
}

#[tokio::test]
async fn live_update_during_scroll_capture_still_completes_the_request() {
    let manager = fixture(b"one\r\ntwo\r\nthree\r\nfour\r\nfive").await;
    let before = manager.screen("agent").await.unwrap();
    let reached = Arc::new(tokio::sync::Notify::new());
    let release = Arc::new(tokio::sync::Notify::new());
    *manager.scroll_capture_pause.lock().unwrap() = Some((reached.clone(), release.clone()));
    let capture = manager.scroll_capture("agent", 1, 42);
    tokio::pin!(capture);
    tokio::select! {
        () = reached.notified() => {},
        _ = capture.as_mut() => panic!("capture returned before pause"),
    }

    let emu = manager.live.read().await["agent"]
        .capture
        .emu
        .clone()
        .unwrap();
    let frame = {
        let mut emu = emu.lock().unwrap();
        emu.feed(b"\r\nsix");
        emu.render()
    };
    manager.apply_frame("agent", frame).await;
    // A newer request can finish while the old one is awaiting validation.
    let fresh = manager.scroll_capture("agent", 1, 43).await.unwrap();
    release.notify_one();
    let delayed = capture
        .await
        .expect("live output must not strand a scroll request");
    assert_eq!(delayed.request_id, 42);
    assert_eq!(delayed.seq, before.seq);
    assert_eq!(delayed.history, before.history);
    assert!(delayed.lines[0].contains("two"));
    assert_eq!(fresh.history, delayed.history + 1);
    assert!(fresh.lines[0].contains("three"));
    let cache = manager.scroll_cache.lock().unwrap();
    assert_eq!(cache["agent"].view.seq, fresh.seq);
    assert_eq!(cache["agent"].view.lines, fresh.lines);
}

#[tokio::test]
async fn incompatible_terminal_change_during_capture_cannot_repopulate_cache() {
    for change in ["run", "emulator", "size", "removed"] {
        let manager = fixture(b"one\r\ntwo\r\nthree\r\nfour").await;
        let reached = Arc::new(tokio::sync::Notify::new());
        let release = Arc::new(tokio::sync::Notify::new());
        *manager.scroll_capture_pause.lock().unwrap() = Some((reached.clone(), release.clone()));
        let capture = manager.scroll_capture("agent", 1, 42);
        tokio::pin!(capture);
        tokio::select! {
            () = reached.notified() => {},
            _ = capture.as_mut() => panic!("capture returned before pause"),
        }
        {
            let mut live = manager.live.write().await;
            let current = live.get_mut("agent").unwrap();
            match change {
                "run" => current.run_id += 1,
                "emulator" => {
                    current.capture.emu = Some(Arc::new(Mutex::new(SessionEmu::new(12, 3))))
                }
                "size" => current.rows += 1,
                _ => {
                    live.remove("agent");
                }
            }
            manager.forget_scroll("agent");
        }
        release.notify_one();
        assert!(capture.await.is_none(), "{change}");
        assert!(manager.scroll_cache.lock().unwrap().is_empty(), "{change}");
    }
}

#[tokio::test]
async fn live_fallback_during_capture_preserves_current_metadata() {
    for (initial, output) in [
        (b"one".as_slice(), b"\r\ntwo\r\nthree\r\nfour".as_slice()),
        (b"one\r\ntwo\r\nthree\r\nfour", b"\x1b[3J\rnew"),
        (b"one\r\ntwo\r\nthree\r\nfour", b"\x1b[?1049hnew"),
    ] {
        let manager = fixture(initial).await;
        let reached = Arc::new(tokio::sync::Notify::new());
        let release = Arc::new(tokio::sync::Notify::new());
        *manager.scroll_capture_pause.lock().unwrap() = Some((reached.clone(), release.clone()));
        let capture = manager.scroll_capture("agent", 1, 42);
        tokio::pin!(capture);
        tokio::select! {
            () = reached.notified() => {},
            _ = capture.as_mut() => panic!("capture returned before pause"),
        }
        let emu = manager.live.read().await["agent"]
            .capture
            .emu
            .clone()
            .unwrap();
        let frame = {
            let mut emu = emu.lock().unwrap();
            emu.feed(output);
            emu.render()
        };
        manager.apply_frame("agent", frame).await;
        let live = manager.screen("agent").await.unwrap();
        release.notify_one();
        let reply = capture.await.unwrap();
        assert_eq!(reply.request_id, 42);
        assert_eq!(reply.off, 0);
        assert_eq!(reply.seq, live.seq);
        assert_eq!(reply.history, live.history);
        assert_eq!(reply.lines, live.lines);
        assert_eq!(reply.alt_screen, live.alt_screen);
    }
}
