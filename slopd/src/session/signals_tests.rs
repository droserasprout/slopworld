use crate::config::Config;
use crate::session::test_manager;

#[tokio::test]
async fn deleted_sessions_release_subscription_bookkeeping() {
    use crate::config::SessionCfg;
    use crate::session::{Live, TitleCapture};

    let manager = test_manager(Config::default());
    // A WebSocket owns this same name-to-guard map until unsubscribe or disconnect.
    let mut subscriptions = std::collections::HashMap::new();
    for index in 0..64 {
        let name = format!("memory-repro-{index}");
        manager.live.write().await.insert(
            name.clone(),
            Live::new(
                SessionCfg {
                    name: name.clone(),
                    ..Default::default()
                },
                TitleCapture::default(),
            ),
        );
        subscriptions.insert(name.clone(), manager.watching(&name));
        manager.forget(&name).await;
    }
    assert!(manager.live.read().await.is_empty());
    let retained = manager.signals.watchers.lock().unwrap().len();
    assert!(subscriptions.values().all(|guard| !guard.is_current()));
    // Delayed connection cleanup remains harmless after lifecycle retirement.
    drop(subscriptions);
    assert!(manager.signals.watchers.lock().unwrap().is_empty());
    assert_eq!(
        retained, 0,
        "deleted sessions must retire their watch bookkeeping"
    );
}

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

#[test]
fn redraw_bursts_keep_latest_geometry_and_restart_after_drain() {
    let mut queue = super::RedrawQueue::default();
    assert!(queue.request(None));
    assert_eq!(queue.next(), Some(None));
    assert!(!queue.request(Some((80, 24))));
    assert!(!queue.request(Some((120, 34))));
    assert!(!queue.request(None));
    assert_eq!(queue.next(), Some(Some((120, 34))));
    assert_eq!(queue.next(), None);
    assert!(queue.request(None));
    assert_eq!(queue.next(), Some(None));
    assert_eq!(queue.next(), None);
}

#[tokio::test]
async fn retired_guard_cannot_release_reused_name() {
    let manager = test_manager(Config::default());
    let old = manager.watching("agent");
    manager.retire_watches("agent");
    manager.retire_watches("agent");
    let replacement = manager.watching("agent");
    assert!(!old.is_current());
    assert!(replacement.is_current());
    drop(old);
    assert!(manager.watched("agent"));
    drop(replacement);
    assert!(!manager.watched("agent"));
}

#[tokio::test]
async fn rename_retires_old_name_subscription() {
    use crate::config::SessionCfg;
    use crate::session::{Live, TitleCapture};

    let manager = test_manager(Config::default());
    manager.live.write().await.insert(
        "old".into(),
        Live::new(
            SessionCfg {
                name: "old".into(),
                ..Default::default()
            },
            TitleCapture::default(),
        ),
    );
    let watch = manager.watching("old");
    manager.readopt("old", "new").await;
    assert!(!watch.is_current());
    assert!(!manager.watched("old"));
    assert!(manager.live.read().await.contains_key("new"));
}
