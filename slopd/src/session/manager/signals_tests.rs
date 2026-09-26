use super::*;
use crate::config::Config;
use crate::session::test_manager;

#[tokio::test]
async fn watch_guards_release_their_bookkeeping() {
    let manager = test_manager(Config::default());
    assert!(!manager.watched("agent"));

    let first_watch = manager.watching("agent");
    let second_watch = manager.watching("agent");
    assert!(manager.watched("agent"));
    drop(second_watch);
    assert!(manager.watched("agent"));
    drop(first_watch);
    assert!(!manager.watched("agent"));
}

#[tokio::test]
async fn usage_broadcasts_only_when_the_readout_changes() {
    let manager = test_manager(Config::default());
    let mut events = manager.events.subscribe();
    let unchanged = crate::usage::Snapshot::default();
    manager.set_usage(unchanged).await;
    assert!(events.try_recv().is_err());

    let changed = crate::usage::Snapshot {
        ok: true,
        sources: vec!["test".into()],
        ..Default::default()
    };
    manager.set_usage(changed.clone()).await;
    let event = events.try_recv().expect("usage event");
    assert!(matches!(event.event(), Event::Usage { usage } if usage == &changed));
    assert_eq!(manager.usage().await, changed);
}

#[tokio::test]
async fn timestamp_only_usage_update_refreshes_snapshot_without_broadcasting() {
    let manager = test_manager(Config::default());
    let mut snapshot = crate::usage::Snapshot {
        ok: true,
        fetched_ms: 100,
        sources: vec!["test".into()],
        ..Default::default()
    };
    manager.set_usage(snapshot.clone()).await;
    let mut events = manager.events.subscribe();

    snapshot.fetched_ms = 200;
    manager.set_usage(snapshot.clone()).await;

    assert_eq!(manager.usage().await, snapshot);
    assert!(matches!(
        events.try_recv(),
        Err(tokio::sync::broadcast::error::TryRecvError::Empty)
    ));
}
