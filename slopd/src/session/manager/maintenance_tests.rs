use super::*;

#[tokio::test]
async fn consumed_spare_refills_after_launch_without_waiting_for_config_poll() {
    let socket = crate::test_support::TmuxSocket::new();
    let manager =
        crate::session::manager::test_manager_with_socket(Config::default(), &socket.path);
    // Keep every catalog poll in the future: the refill must use its own request.
    for clock in [
        &manager.config_state.config_checked,
        &manager.config_state.presets_checked,
        &manager.config_state.jukebox_checked,
    ] {
        clock.store(u64::MAX, Ordering::Release);
    }
    manager.tmux.warm_spare().await.unwrap();
    assert!(manager.tmux.take_spare("first", 80, 24).await);
    let launch = manager.session_boundary.write().await;
    manager.request_spare_refill();
    manager.request_spare_refill();
    // notify_one retains a request sent before the poller starts waiting.
    let wake = manager.maintenance_wake();
    assert!(futures::poll!(std::pin::pin!(wake.notified())).is_ready());
    let mut refill = std::pin::pin!(manager.reload_if_due());
    assert!(futures::poll!(&mut refill).is_pending());
    assert!(!manager.tmux.take_spare("premature", 80, 24).await);
    drop(launch);
    refill.await;
    assert!(manager.tmux.take_spare("second", 90, 30).await);
    // Repeated consumption must request another refill without a two-second wait.
    manager.request_spare_refill();
    manager.reload_if_due().await;
    assert!(manager.tmux.take_spare("third", 100, 40).await);
    assert_eq!(manager.tmux.size("first").await, Some((80, 24)));
    assert_eq!(manager.tmux.size("second").await, Some((90, 30)));
}
