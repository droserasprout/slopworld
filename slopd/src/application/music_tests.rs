use crate::application::test_support::Fixture;

/// These publication tests never need host-terminal lifecycle operations.
struct NoHosts;
impl crate::music::HostSessions for NoHosts {
    async fn host_sessions(&self) -> Vec<String> {
        panic!("unexpected host listing")
    }
    async fn launch(&self, _: String, _: Option<u16>, _: Option<u16>) -> anyhow::Result<String> {
        panic!("unexpected host launch")
    }
    async fn stop(&self, _: &str) -> anyhow::Result<()> {
        panic!("unexpected host stop")
    }
}

#[tokio::test]
async fn music_poll_publishes_only_changed_snapshots() {
    let fixture = Fixture::new();
    let services = &fixture.services;
    let mut events = services.subscribe();
    let mut last = None;
    services.publish_music_if_changed(&mut last).await;
    assert!(matches!(
        events.try_recv().unwrap().event(),
        crate::application::Event::Audio { .. }
    ));
    services.publish_music_if_changed(&mut last).await;
    assert!(matches!(
        events.try_recv(),
        Err(tokio::sync::broadcast::error::TryRecvError::Empty)
    ));
    let request = serde_json::from_str::<crate::music::AudioReq>(
        r#"{"volume":0.5,"selection":{"stream":"orphan"}}"#,
    )
    .unwrap();
    services.music.select(&NoHosts, request).await;
    services.publish_music_if_changed(&mut last).await;
    let event = events.try_recv().unwrap();
    assert!(
        matches!(event.event(), crate::application::Event::Audio { audio } if audio.error.as_deref() == Some("Select a station and stream, or select a file."))
    );
    services.publish_music_if_changed(&mut last).await;
    assert!(matches!(
        events.try_recv(),
        Err(tokio::sync::broadcast::error::TryRecvError::Empty)
    ));
}

#[tokio::test]
async fn music_poll_task_publishes_initial_state_and_accepts_shutdown() {
    let fixture = Fixture::new();
    let services = &fixture.services;
    let mut events = services.subscribe();
    let poll = services.spawn_music_poll();
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
