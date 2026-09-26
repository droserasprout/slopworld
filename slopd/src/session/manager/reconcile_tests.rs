use super::*;

#[tokio::test]
async fn cancelling_batch_cleanup_aborts_all_removed_readers() {
    let manager = crate::session::test_manager(Config::default());
    let mut completions = Vec::new();
    for name in ["first", "second"] {
        let (done, completion) = tokio::sync::oneshot::channel::<()>();
        let mut live = Live::new(
            SessionCfg {
                name: name.into(),
                ..Default::default()
            },
            TitleCapture::default(),
        );
        live.reader = Some(tokio::spawn(async move {
            let _done = done;
            std::future::pending::<()>().await;
        }));
        manager.live.write().await.insert(name.into(), live);
        completions.push(completion);
    }

    // Keep cleanup from completing even if tmux activity clearing returns immediately.
    let _temp = manager.temp.write().await;
    let cfg = Config::default();
    let mut pruning = Box::pin(manager.prune_removed(&cfg));
    assert!(futures::poll!(pruning.as_mut()).is_pending());
    assert!(manager.live.read().await.is_empty());
    drop(pruning);

    for completion in completions {
        assert!(tokio::time::timeout(Duration::from_secs(1), completion)
            .await
            .expect("removed reader survived cancellation of batch cleanup")
            .is_err());
    }
}
