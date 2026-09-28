use super::*;

async fn fixture() -> (Arc<Manager>, TitleRequest) {
    let mut cfg = Config::default();
    cfg.daemon.agent_titles = TitlePolicy::Always;
    cfg.daemon.title_min_chars = 5;
    cfg.daemon.openrouter_key_file = "/nonexistent/slopd-title-refactor-test-key".into();
    let manager = crate::session::test_manager(cfg);
    let live = Live::new(
        SessionCfg {
            name: "agent".into(),
            command: "codex".into(),
            ..Default::default()
        },
        TitleCapture::default(),
    );
    manager.live.write().await.insert("agent".into(), live);
    let request = prepare(&manager, "Explain the project architecture").await;
    (manager, request)
}

// Prepare a real input submission without starting provider I/O; tests control completion order.
async fn prepare(manager: &Manager, prompt: &str) -> TitleRequest {
    let cfg = manager.config().await;
    let mut live = manager.live.write().await;
    let row = live.get_mut("agent").unwrap();
    let settings = TitleSettings::for_session(&cfg, &row.cfg, row.host).unwrap();
    row.title.paste(prompt);
    let Some(TitleAction::Request(request)) =
        row.title.capture_keys(&settings, &["Enter".into()], false)
    else {
        panic!("expected request")
    };
    request
}

fn summary(text: &str) -> Result<Summary> {
    Ok(Summary {
        text: text.into(),
        cache_hit: false,
    })
}

#[tokio::test]
async fn successful_title_is_cached_and_reused_without_a_provider_request() {
    let (m, request) = fixture().await;
    assert!(m.title_request_enabled("agent", &request).await);
    assert!(
        m.apply_title_result("agent", &request, summary("Architecture"))
            .await
    );
    assert!(!m.live.read().await["agent"].title.accepts(&request));
    assert_eq!(
        m.live.read().await["agent"].title.title(),
        Some("Architecture")
    );
    assert_eq!(
        m.title_cache.latest("agent").as_deref(),
        Some("Architecture")
    );
    let next = prepare(&m, &request.input.prompt).await;
    let cached = m.title_cache.resolve(&next.input).await.unwrap();
    assert!(cached.cache_hit);
    assert_eq!(cached.text, "Architecture");
    m.title_cache.clear_latest("agent");
    assert!(m.apply_title_result("agent", &next, Ok(cached)).await);
    assert_eq!(
        m.title_cache.latest("agent").as_deref(),
        Some("Architecture")
    );
    // Repeated completion cannot overwrite a previously committed response.
    assert!(
        !m.apply_title_result("agent", &next, summary("Repeated"))
            .await
    );
}

#[tokio::test]
async fn stale_results_cannot_replace_new_work_or_a_replacement_session() {
    let (m, old) = fixture().await;
    let current = prepare(&m, "Current work on another component").await;
    assert!(!m.title_request_enabled("agent", &old).await);
    assert!(!m.apply_title_result("agent", &old, summary("Stale")).await);
    assert!(m.live.read().await["agent"].title.accepts(&current));
    assert!(m.title_cache.latest("agent").is_none());
    {
        let mut live = m.live.write().await;
        let cfg = live["agent"].cfg.clone();
        live.insert("agent".into(), Live::new(cfg, TitleCapture::default()));
    }
    let replacement = prepare(&m, "Work in a replacement process").await;
    assert!(
        !m.apply_title_result("agent", &current, summary("Old process"))
            .await
    );
    assert!(m.live.read().await["agent"].title.accepts(&replacement));
    assert!(
        !m.apply_title_result("missing", &replacement, summary("Missing"))
            .await
    );
}

#[tokio::test]
async fn disabled_or_short_requests_are_discarded_and_failures_keep_previous_title() {
    let (m, first) = fixture().await;
    assert!(
        m.apply_title_result("agent", &first, summary("Previous"))
            .await
    );
    let failed = prepare(&m, "A prompt whose provider will fail").await;
    assert!(
        !m.apply_title_result("agent", &failed, Err(anyhow::anyhow!("provider failed")))
            .await
    );
    assert!(!m.live.read().await["agent"].title.accepts(&failed));
    assert_eq!(m.live.read().await["agent"].title.title(), Some("Previous"));
    let short = prepare(&m, "Long enough before changing settings").await;
    m.cfg.write().await.daemon.title_min_chars = 1000;
    assert!(!m.title_request_enabled("agent", &short).await);
    assert!(
        !m.apply_title_result("agent", &short, summary("Too short"))
            .await
    );
    m.cfg.write().await.daemon.title_min_chars = 5;
    let disabled = prepare(&m, "Long enough again").await;
    m.cfg.write().await.daemon.agent_titles = TitlePolicy::Never;
    assert!(!m.title_request_enabled("agent", &disabled).await);
    assert!(
        !m.apply_title_result("agent", &disabled, summary("Disabled"))
            .await
    );
    m.clone().run_title_request("agent".into(), disabled).await;
    assert_eq!(m.title_cache.latest("agent").as_deref(), Some("Previous"));
}

#[tokio::test]
async fn disabling_titles_clears_work_and_cache_before_old_results_can_commit() {
    let (m, request) = fixture().await;
    m.capture_title_paste("agent", "unfinished input").await;
    m.title_cache.remember("agent", "Old");
    let mut cfg = m.config().await;
    assert!(!m.reconcile_title_settings(&cfg).await);
    cfg.daemon.agent_titles = TitlePolicy::Never;
    assert!(m.reconcile_title_settings(&cfg).await);
    assert!(!m.reconcile_title_settings(&cfg).await);
    assert!(!m.live.read().await["agent"].title.accepts(&request));
    assert!(m.live.read().await["agent"].title.title().is_none());
    assert!(m.title_cache.latest("agent").is_none());
    assert!(
        !m.apply_title_result("agent", &request, summary("Old result"))
            .await
    );
    // Actual configuration is still enabled; the pending composer must have been cleared.
    m.capture_title_keys("agent", &["Enter".into()], false)
        .await;
    assert!(m.title_cache.latest("agent").is_none());
}

#[tokio::test]
async fn new_conversation_updates_recovery_state_and_rejects_old_results() {
    let (m, request) = fixture().await;
    assert!(
        m.apply_title_result("agent", &request, summary("Old title"))
            .await
    );
    let pending = prepare(&m, "Old request still running").await;
    for (input, expected) in [
        ("/new Named conversation", Some("Named conversation")),
        ("/new", None),
    ] {
        m.capture_title_paste("agent", input).await;
        m.capture_title_keys("agent", &["Enter".into()], false)
            .await;
        assert_eq!(m.live.read().await["agent"].title.title(), expected);
        assert_eq!(m.title_cache.latest("agent").as_deref(), expected);
        assert!(
            !m.apply_title_result("agent", &pending, summary("Late result"))
                .await
        );
    }
    let path = m.cfg_path.with_file_name("summary-cache.toml");
    tokio::task::spawn_blocking(move || {
        m.title_cache.flush().unwrap();
        assert!(crate::title::SummaryCache::load(path)
            .latest("agent")
            .is_none());
    })
    .await
    .unwrap();
}

#[tokio::test]
async fn host_and_fixed_label_sessions_do_not_capture_prompts() {
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
    }
    {
        let mut live = m.live.write().await;
        let agent = live.get_mut("agent").unwrap();
        agent.host = false;
        agent.cfg.label = None;
    }
    m.capture_title_keys("agent", &["Enter".into()], false)
        .await;
    // Nothing was composed, so the original request is still current.
    assert!(m.live.read().await["agent"].title.accepts(&request));
    m.capture_title_paste("missing", "ignored").await;
    m.capture_title_keys("missing", &["Enter".into()], false)
        .await;
}

#[tokio::test]
async fn rename_moves_recovery_title_and_invalidates_pending_requests() {
    let (m, first) = fixture().await;
    assert!(
        m.apply_title_result("agent", &first, summary("Current title"))
            .await
    );
    let pending = prepare(&m, "Work started before rename").await;
    m.readopt("agent", "renamed").await;
    assert!(!m.title_request_enabled("agent", &pending).await);
    assert!(!m.title_request_enabled("renamed", &pending).await);
    assert!(
        !m.apply_title_result("agent", &pending, summary("Old name"))
            .await
    );
    assert!(
        !m.apply_title_result("renamed", &pending, summary("Old request"))
            .await
    );
    assert_eq!(
        m.live.read().await["renamed"].title.title(),
        Some("Current title")
    );
    assert!(m.title_cache.latest("agent").is_none());
    assert_eq!(
        m.title_cache.latest("renamed").as_deref(),
        Some("Current title")
    );
    let path = m.cfg_path.with_file_name("summary-cache.toml");
    tokio::task::spawn_blocking(move || {
        m.title_cache.flush().unwrap();
        let restored = crate::title::SummaryCache::load(path);
        assert!(restored.latest("agent").is_none());
        assert_eq!(restored.latest("renamed").as_deref(), Some("Current title"));
    })
    .await
    .unwrap();
}
