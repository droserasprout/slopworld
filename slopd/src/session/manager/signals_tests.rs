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
