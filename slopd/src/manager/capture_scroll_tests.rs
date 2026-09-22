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
    live.emu = Some(Arc::new(Mutex::new(emu)));
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
    // Poison only the cached payload: every key component must independently reject it.
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
    m.live.write().await.get_mut("agent").unwrap().emu = None;
    assert!(m.scroll_capture("agent", 1, 1).await.is_none());
    assert!(m.scroll_capture("agent", 0, 1).await.is_some());
}
