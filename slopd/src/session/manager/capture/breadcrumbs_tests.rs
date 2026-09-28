use super::*;

#[tokio::test]
async fn pending_instruction_discovery_is_consumed_once() {
    let manager = crate::session::test_manager(Config::default());
    let mut live = Live::new(
        SessionCfg {
            name: "agent".into(),
            ..Default::default()
        },
        TitleCapture::default(),
    );
    live.input.breadcrumbs = b"previously armed prompt".to_vec();
    live.input.breadcrumbs_pending = true;
    manager.live.write().await.insert("agent".into(), live);
    assert_eq!(
        manager.consume_breadcrumbs("agent", &[]).await.as_deref(),
        Some(b"previously armed prompt".as_slice())
    );
    let live = manager.live.read().await;
    assert!(!live["agent"].input.breadcrumbs_pending);
    assert_eq!(live["agent"].input.breadcrumbs, b"previously armed prompt");
}

#[tokio::test]
async fn breadcrumbs_are_consumed_once_for_a_delivered_prompt() {
    let cfg = Config::default();
    let manager = crate::session::test_manager(cfg);
    let mut live = Live::new(
        SessionCfg {
            name: "agent".into(),
            ..Default::default()
        },
        TitleCapture::default(),
    );
    live.input.breadcrumbs = b"{{ random_tip }}".to_vec();
    live.input.breadcrumbs_pending = true;
    manager.live.write().await.insert("agent".into(), live);

    assert_eq!(
        manager
            .consume_breadcrumbs("agent", &["tip".into()])
            .await
            .as_deref(),
        Some(b"tip".as_slice())
    );
    assert!(manager.consume_breadcrumbs("agent", &[]).await.is_none());
}
