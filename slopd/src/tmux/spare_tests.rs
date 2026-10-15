use super::*;

#[tokio::test]
async fn spare_is_hidden_and_promotion_preserves_pane_at_requested_size() {
    let socket = crate::test_support::TmuxSocket::new();
    let tmux = Tmux::new(&socket.path);
    assert!(!tmux.take_spare("cold", 80, 24).await);
    tmux.warm_spare().await.unwrap();
    assert!(tmux.list_checked().await.unwrap().is_empty());
    let before = tmux
        .run(&[
            "display-message",
            "-p",
            "-t",
            &format!("={NAME}"),
            "#{pane_id}",
        ])
        .await
        .unwrap();
    tmux.warm_spare().await.unwrap();
    assert!(tmux.clone().take_spare("diff", 137, 42).await);
    assert_eq!(tmux.size("diff").await, Some((137, 42)));
    let after = tmux
        .run(&["display-message", "-p", "-t", "=diff", "#{pane_id}"])
        .await
        .unwrap();
    assert_eq!(before, after);
    assert!(!tmux.take_spare("again", 80, 24).await);
    tmux.warm_spare().await.unwrap();
    assert_eq!(tmux.list_checked().await.unwrap(), vec!["diff"]);
}

#[tokio::test]
async fn busy_slot_falls_back_and_concurrent_claims_promote_only_once() {
    let socket = crate::test_support::TmuxSocket::new();
    let tmux = Tmux::new(&socket.path);
    tmux.warm_spare().await.unwrap();
    let guard = tmux.spare.lock().await;
    assert!(!tmux.take_spare("busy", 80, 24).await);
    tmux.warm_spare().await.unwrap();
    drop(guard);
    let (a, b) = tokio::join!(tmux.take_spare("a", 80, 24), tmux.take_spare("b", 80, 24));
    assert_ne!(a, b);
    assert_eq!(tmux.list_checked().await.unwrap().len(), 1);
}

#[tokio::test]
async fn restart_replaces_old_spare_and_dead_spare_does_not_touch_other_sessions() {
    let socket = crate::test_support::TmuxSocket::new();
    let tmux = Tmux::new(&socket.path);
    tmux.warm_spare().await.unwrap();
    let restarted = Tmux::new(&socket.path);
    restarted.warm_spare().await.unwrap();
    assert!(restarted.list_checked().await.unwrap().is_empty());
    restarted.kill(&format!("={NAME}")).await.unwrap();
    restarted
        .spawn(
            "unrelated",
            "/",
            80,
            24,
            &["sleep".into(), "60".into()],
            true,
        )
        .await
        .unwrap();
    assert!(!restarted.take_spare("diff", 137, 42).await);
    assert_eq!(restarted.size("unrelated").await, Some((80, 24)));
    restarted.warm_spare().await.unwrap();
    assert!(restarted.take_spare("recovered", 80, 24).await);
}

#[tokio::test]
async fn failed_promotion_discards_only_the_spare_and_refill_recovers() {
    let socket = crate::test_support::TmuxSocket::new();
    let tmux = Tmux::new(&socket.path);
    tmux.spawn(
        "occupied",
        "/",
        80,
        24,
        &["sleep".into(), "60".into()],
        true,
    )
    .await
    .unwrap();
    tmux.warm_spare().await.unwrap();
    assert!(!tmux.take_spare("occupied", 90, 30).await);
    assert_eq!(tmux.size("occupied").await, Some((80, 24)));
    assert_eq!(tmux.list_checked().await.unwrap(), vec!["occupied"]);
    tmux.warm_spare().await.unwrap();
    assert!(tmux.take_spare("recovered", 90, 30).await);
}
