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
async fn invalid_selection_replies_without_changing_shared_state() {
    let app = crate::application::test_application(Default::default());
    let music = super::MusicOperations::new(&app);
    use crate::shared::wire::{AudioRequest, AudioSelection, audio_request::Change};
    let request = AudioRequest {
        change: Some(Change::Selection(AudioSelection {
            stream: Some("orphan".into()),
            ..Default::default()
        })),
        ..Default::default()
    }
    .into();
    let reply = music.select_music(request).await.unwrap();
    assert!(reply.error.unwrap().contains("Select a station and stream"));
    assert!(app.services.music.state().await.error.is_none());
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
