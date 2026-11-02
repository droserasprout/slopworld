use super::*;

async fn fixture() -> (Arc<SessionRuntime>, mpsc::UnboundedReceiver<QueuedInput>) {
    let session = SessionCfg {
        name: "agent".into(),
        project: "repo".into(),
        cmd: Some("codex --yolo".into()),
        ..Default::default()
    };
    let manager = crate::session::test_manager(Config {
        projects: vec![ProjectCfg { name: "repo".into(), dir: "/tmp/project".into(), ..Default::default() }],
        sessions: vec![session.clone()],
        library: vec![LibraryItemCfg {
            name: "context".into(), kind: LibraryItemKind::Prompt,
            text: "{{ agent }} in {{ project }} at {{ directory }} using {{ command }}\n{{ random_tip }} / {{ random_tip }}".into(),
            ..Default::default()
        }],
        ..Default::default()
    });
    let (tx, rx) = mpsc::unbounded_channel();
    let mut live = Live::new(session, TitleCapture::default());
    live.state = State::Working;
    live.input.sender = Some(tx);
    manager.live.write().await.insert("agent".into(), live);
    (manager, rx)
}

#[tokio::test]
async fn explicit_prompt_renders_context_and_tips_without_submitting() {
    let (manager, mut rx) = fixture().await;
    manager
        .paste_prompt("agent", "context", vec!["first".into(), "second".into()])
        .await
        .unwrap();
    assert!(
        matches!(rx.try_recv().unwrap().command, Input::Paste { bytes }
        if bytes == b"agent in repo at /tmp/project using codex --yolo\nfirst / second")
    );
    assert!(
        rx.try_recv().is_err(),
        "manual prompt paste must not add Enter or a delay"
    );
}

#[tokio::test]
async fn invalid_or_stopped_prompt_target_queues_nothing() {
    let (manager, mut rx) = fixture().await;
    assert!(
        manager
            .paste_prompt("missing", "context", vec![])
            .await
            .is_err()
    );
    assert!(
        manager
            .paste_prompt("agent", "missing", vec![])
            .await
            .is_err()
    );
    manager.config_authority.write().await.library[0].kind = LibraryItemKind::Shell;
    assert!(
        manager
            .paste_prompt("agent", "context", vec![])
            .await
            .is_err()
    );
    manager.config_authority.write().await.library[0].kind = LibraryItemKind::Prompt;
    manager.live.write().await.get_mut("agent").unwrap().state = State::Down;
    assert!(
        manager
            .paste_prompt("agent", "context", vec![])
            .await
            .is_err()
    );
    assert!(rx.try_recv().is_err());
}
