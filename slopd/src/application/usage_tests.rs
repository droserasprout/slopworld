use super::*;
use crate::application::test_support::Fixture;

#[tokio::test]
async fn usage_broadcasts_only_when_the_readout_changes() {
    let fixture = Fixture::new();
    let services = &fixture.services;
    let mut events = services.subscribe();
    let unchanged = crate::usage::Snapshot::default();
    services.set_usage(unchanged).await;
    assert!(events.try_recv().is_err());

    let changed = crate::usage::Snapshot {
        ok: true,
        sources: vec!["test".into()],
        ..Default::default()
    };
    services.set_usage(changed.clone()).await;
    let event = events.try_recv().expect("usage event");
    assert!(matches!(event.event(), Event::Usage { usage } if usage == &changed));
    assert_eq!(services.usage().await, changed);
    assert!(matches!(
        events.try_recv(),
        Err(tokio::sync::broadcast::error::TryRecvError::Empty)
    ));
}

#[tokio::test]
async fn timestamp_only_usage_update_refreshes_snapshot_without_broadcasting() {
    let fixture = Fixture::new();
    let services = &fixture.services;
    let mut snapshot = crate::usage::Snapshot {
        ok: true,
        fetched_ms: 100,
        sources: vec!["test".into()],
        ..Default::default()
    };
    services.set_usage(snapshot.clone()).await;
    let mut events = services.subscribe();

    snapshot.fetched_ms = 200;
    services.set_usage(snapshot.clone()).await;

    assert_eq!(services.usage().await, snapshot);
    assert!(matches!(
        events.try_recv(),
        Err(tokio::sync::broadcast::error::TryRecvError::Empty)
    ));
}

#[tokio::test]
async fn usage_loop_runs_without_sessions_and_releases_settings_on_shutdown() {
    let fixture = Fixture::new();
    let services = &fixture.services;
    services
        .set_usage(crate::usage::Snapshot {
            ok: true,
            sources: vec!["previous".into()],
            ..Default::default()
        })
        .await;
    let mut events = services.subscribe();
    let config = Arc::new(tokio::sync::RwLock::new(crate::config::Config::default()));
    let weak = Arc::downgrade(&config);
    let poll = services.spawn_usage_poll(DaemonSettings::new(config));
    let event = tokio::time::timeout(std::time::Duration::from_secs(3), events.recv())
        .await
        .unwrap()
        .unwrap();
    let Event::Usage { usage } = event.event() else {
        panic!("expected usage event")
    };
    assert!(usage.sources.is_empty());
    assert!(usage.rows.iter().all(|row| !row.poll));
    assert_eq!(services.usage().await, *usage);
    assert!(weak.upgrade().is_some());
    poll.abort();
    assert!(poll.await.unwrap_err().is_cancelled());
    assert!(weak.upgrade().is_none());
}
