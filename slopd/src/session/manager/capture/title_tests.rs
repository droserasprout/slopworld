use super::*;

async fn fixture() -> (Arc<Manager>, TitleRequest) {
    let mut cfg = Config::default();
    cfg.daemon.agent_titles = TitlePolicy::Always;
    cfg.daemon.title_min_chars = 5;
    let manager = crate::session::test_manager(cfg);
    let mut live = Live::new(
        SessionCfg {
            name: "agent".into(),
            command: "codex".into(),
            ..Default::default()
        },
        TitleCapture::default(),
    );
    let request = begin_title_request(
        &mut live,
        "Explain the project architecture".into(),
        String::new(),
        "test-model".into(),
        "Summarize".into(),
    );
    manager.live.write().await.insert("agent".into(), live);
    (manager, request)
}

#[tokio::test]
async fn successful_title_is_cached_and_reused_without_a_provider_request() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    let (m, request) = fixture().await;
    assert!(m.title_request_enabled("agent", &request).await);
    assert!(
        m.apply_title_result("agent", &request, Ok("Architecture".into()), false)
            .await
    );
    assert!(!m.live.read().await["agent"].title.pending);
    assert_eq!(
        m.live.read().await["agent"].title.override_title.as_deref(),
        Some("Architecture")
    );
    assert_eq!(
        m.title_cache.latest("agent").as_deref(),
        Some("Architecture")
    );
    let (result, hit) = m.resolve_title_request("agent", &request).await;
    assert!(hit);
    assert_eq!(result.unwrap(), "Architecture");
    m.title_cache.clear_latest("agent").unwrap();
    assert!(
        m.apply_title_result("agent", &request, Ok("Architecture".into()), true)
            .await
    );
    assert_eq!(
        m.title_cache.latest("agent").as_deref(),
        Some("Architecture")
    );
}

#[tokio::test]
async fn stale_results_cannot_replace_a_new_conversation_or_request() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    let (m, request) = fixture().await;
    for conversation_changed in [false, true] {
        {
            let mut live = m.live.write().await;
            let title = &mut live.get_mut("agent").unwrap().title;
            title.conversation = request.conversation + u64::from(conversation_changed);
            title.generation = request.generation + u64::from(!conversation_changed);
            title.override_title = Some("Current".into());
        }
        assert!(
            !m.apply_title_result("agent", &request, Ok("Stale".into()), false)
                .await
        );
        let live = m.live.read().await;
        assert!(live["agent"].title.pending);
        assert_eq!(
            live["agent"].title.override_title.as_deref(),
            Some("Current")
        );
        assert!(m.title_cache.latest("agent").is_none());
    }
    assert!(
        !m.apply_title_result("missing", &request, Ok("Stale".into()), false)
            .await
    );
}

#[tokio::test]
async fn disabled_or_short_requests_are_discarded_and_failures_keep_previous_title() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    let (m, mut request) = fixture().await;
    m.live
        .write()
        .await
        .get_mut("agent")
        .unwrap()
        .title
        .override_title = Some("Previous".into());
    assert!(
        !m.apply_title_result(
            "agent",
            &request,
            Err(anyhow::anyhow!("provider failed")),
            false
        )
        .await
    );
    assert!(!m.live.read().await["agent"].title.pending);
    assert_eq!(
        m.live.read().await["agent"].title.override_title.as_deref(),
        Some("Previous")
    );
    request.prompt = "tiny".into();
    assert!(!m.title_request_enabled("agent", &request).await);
    assert!(
        !m.apply_title_result("agent", &request, Ok("Too short".into()), false)
            .await
    );
    request.prompt = "Long enough again".into();
    m.cfg.write().await.daemon.agent_titles = TitlePolicy::Never;
    assert!(!m.title_request_enabled("agent", &request).await);
    assert!(!m.title_request_enabled("missing", &request).await);
    assert!(
        !m.apply_title_result("agent", &request, Ok("Disabled".into()), false)
            .await
    );
    m.clone().run_title_request("agent".into(), request).await;
    assert!(m.title_cache.latest("agent").is_none());
}

#[tokio::test]
async fn disabling_titles_clears_pending_work_composition_and_cached_title() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    let (m, request) = fixture().await;
    m.capture_title_paste("agent", "unfinished input").await;
    m.title_cache.remember("agent", "Old").unwrap();
    let mut cfg = m.config().await;
    assert!(!m.reconcile_title_settings(&cfg).await);
    cfg.daemon.agent_titles = TitlePolicy::Never;
    assert!(m.reconcile_title_settings(&cfg).await);
    assert!(!m.reconcile_title_settings(&cfg).await);
    let live = m.live.read().await;
    let title = &live["agent"].title;
    assert_eq!(title.generation, request.generation + 1);
    assert!(!title.pending);
    assert!(title.override_title.is_none());
    assert!(m.title_cache.latest("agent").is_none());
    drop(live);
    let mut live = m.live.write().await;
    let (submission, _) = build_title_submission(
        &mut live.get_mut("agent").unwrap().title.composer,
        &["Enter".into()],
        false,
    );
    assert!(submission.is_none());
}
#[tokio::test]
async fn new_conversation_keys_reset_pending_work_and_update_latest_title() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    let (m, request) = fixture().await;
    for (input, expected) in [
        ("/new Named conversation", Some("Named conversation")),
        ("/new", None),
    ] {
        m.capture_title_paste("agent", input).await;
        m.capture_title_keys("agent", &["Enter".into()], false)
            .await;
        let live = m.live.read().await;
        assert!(!live["agent"].title.pending);
        assert!(!live["agent"].title.once_requested);
        assert_eq!(live["agent"].title.override_title.as_deref(), expected);
        assert_eq!(m.title_cache.latest("agent").as_deref(), expected);
    }
    assert_eq!(
        m.live.read().await["agent"].title.conversation,
        request.conversation + 2
    );
    assert!(
        !m.apply_title_result("agent", &request, Ok("Old response".into()), false)
            .await
    );
}

#[tokio::test]
async fn host_and_fixed_label_sessions_do_not_capture_prompts() {
    let Some(_) = crate::test_support::isolated() else {
        return;
    };
    let (m, request) = fixture().await;
    for host in [false, true] {
        {
            let mut live = m.live.write().await;
            let agent = live.get_mut("agent").unwrap();
            agent.host = host;
            agent.cfg.label = if host { None } else { Some("Fixed".into()) };
        }
        assert!(!m.title_request_enabled("agent", &request).await);
        m.capture_title_paste("agent", "Private shell command")
            .await;
        m.capture_title_keys("agent", &["literal input".into()], true)
            .await;
        let mut live = m.live.write().await;
        let (submission, _) = build_title_submission(
            &mut live.get_mut("agent").unwrap().title.composer,
            &["Enter".into()],
            false,
        );
        assert!(submission.is_none());
    }
    m.capture_title_paste("missing", "ignored").await;
    m.capture_title_keys("missing", &["Enter".into()], false)
        .await;
}
