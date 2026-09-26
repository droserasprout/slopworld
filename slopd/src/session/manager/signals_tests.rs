use super::*;
use crate::config::Config;
use crate::session::test_manager;

#[tokio::test]
async fn client_and_watch_guards_release_their_bookkeeping() {
    let manager = test_manager(Config::default());
    assert!(!manager.watched("agent"));

    let first_client = manager.client_joined();
    let second_client = manager.client_joined();
    assert_eq!(manager.signals.clients.load(Ordering::Relaxed), 2);
    assert!(manager.signals.clients_since.load(Ordering::Relaxed) > 0);
    drop(second_client);
    assert_eq!(manager.signals.clients.load(Ordering::Relaxed), 1);
    drop(first_client);
    assert_eq!(manager.signals.clients.load(Ordering::Relaxed), 0);

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
