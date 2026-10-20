use crate::music::HostSessions;

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
    let app = crate::application::Application::from_runtime(&manager);
    let music = super::MusicOperations::new(&app);
    let name = HostSessions::launch(&music, "sleep 60".into(), Some(90), Some(30))
        .await
        .unwrap();
    assert_eq!(music.host_sessions().await, std::slice::from_ref(&name));
    let view = manager
        .views()
        .await
        .into_iter()
        .find(|view| view.name == name)
        .unwrap();
    assert!(view.ephemeral && view.host);
    assert_eq!((view.runtime.cols, view.runtime.rows), (90, 30));
    let cfg = manager.config().await;
    assert!(cfg.sessions.is_empty() && cfg.host_terminals.is_empty());
    HostSessions::stop(&music, &name).await.unwrap();
    assert!(!manager.tmux.exists(&name).await);
}

#[tokio::test]
async fn failed_music_launch_does_not_retain_a_host_or_saved_record() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    let app = crate::application::test_application_with_socket(
        Default::default(),
        "/dev/null/slopworld-music-test",
    );
    let music = super::MusicOperations::new(&app);
    HostSessions::launch(&music, "sleep 60".into(), None, None)
        .await
        .unwrap_err();
    assert!(music.host_sessions().await.is_empty());
    assert!(app.sessions.views().await.is_empty());
    assert!(
        app.config_authority
            .snapshot()
            .await
            .host_terminals
            .is_empty()
    );
}

#[tokio::test]
async fn invalid_selection_records_error_without_a_direct_reply() {
    let app = crate::application::test_application(Default::default());
    let music = super::MusicOperations::new(&app);
    let request =
        serde_json::from_str(r#"{"volume":0.5,"selection":{"stream":"orphan"}}"#).unwrap();
    assert!(music.select_music(request).await.is_none());
    let audio = app.services.music.state().await;
    assert_eq!(
        audio.error.as_deref(),
        Some("Select a station and stream, or select a file.")
    );
    assert!(music.host_sessions().await.is_empty());
}

#[tokio::test]
async fn spotify_open_propagates_failure_without_retaining_a_terminal() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    let app = crate::application::test_application_with_socket(
        Default::default(),
        "/dev/null/slopworld-spotify-test",
    );
    let music = super::MusicOperations::new(&app);
    music.open_spotify(None, None).await.unwrap_err();
    assert!(music.host_sessions().await.is_empty());
    assert!(
        app.config_authority
            .snapshot()
            .await
            .host_terminals
            .is_empty()
    );
}
