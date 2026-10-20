use crate::music::HostSessions;
use crate::session::SessionRole;

#[tokio::test]
async fn music_adapter_launches_disposable_host_terminal_and_stops_it() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    let socket = crate::test_support::TmuxSocket::new();
    let manager = crate::session::test_manager_with_socket(
        crate::config::Config::default(),
        socket.path.clone(),
    );
    let name = HostSessions::launch(&manager, "sleep 60".into(), Some(90), Some(30))
        .await
        .unwrap();
    assert_eq!(manager.host_sessions().await, std::slice::from_ref(&name));
    {
        let live = manager.live.read().await;
        let row = &live[&name];
        assert_eq!(row.role, SessionRole::HostAction);
        assert_eq!((row.cols, row.rows), (90, 30));
    }
    let cfg = manager.config().await;
    assert!(cfg.sessions.is_empty() && cfg.host_terminals.is_empty());
    HostSessions::stop(&manager, &name).await.unwrap();
    assert!(!manager.tmux.exists(&name).await);
}

#[tokio::test]
async fn music_poll_publishes_only_changed_snapshots() {
    let manager = crate::session::test_manager(crate::config::Config::default());
    let mut events = manager.application.subscribe();
    let mut last = None;
    manager.publish_music_if_changed(&mut last).await;
    assert!(matches!(
        events.try_recv().unwrap().event(),
        crate::application::Event::Audio { .. }
    ));
    manager.publish_music_if_changed(&mut last).await;
    assert!(matches!(
        events.try_recv(),
        Err(tokio::sync::broadcast::error::TryRecvError::Empty)
    ));
    let request = serde_json::from_str::<crate::music::AudioReq>(
        r#"{"volume":0.5,"selection":{"stream":"orphan"}}"#,
    )
    .unwrap();
    manager.select_music(request).await;
    manager.publish_music_if_changed(&mut last).await;
    let event = events.try_recv().unwrap();
    assert!(
        matches!(event.event(), crate::application::Event::Audio { audio } if audio.error.as_deref() == Some("Select a station and stream, or select a file."))
    );
    manager.publish_music_if_changed(&mut last).await;
    assert!(matches!(
        events.try_recv(),
        Err(tokio::sync::broadcast::error::TryRecvError::Empty)
    ));
}

#[tokio::test]
async fn music_poll_task_publishes_initial_state_and_accepts_shutdown() {
    let manager = crate::session::test_manager(crate::config::Config::default());
    let mut events = manager.application.subscribe();
    let poll = manager.spawn_music_poll();
    let initial = tokio::time::timeout(std::time::Duration::from_secs(3), events.recv())
        .await
        .unwrap()
        .unwrap();
    assert!(matches!(
        initial.event(),
        crate::application::Event::Audio { .. }
    ));
    poll.abort();
    assert!(poll.await.unwrap_err().is_cancelled());
}
