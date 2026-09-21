//! Automatic title capture and generation.

use super::*;

impl Manager {
    pub(crate) fn spawn_title_request(self: &Arc<Self>, name: String, request: TitleRequest) {
        let manager = self.clone();
        tokio::spawn(async move { manager.run_title_request(name, request).await });
    }

    async fn run_title_request(self: Arc<Self>, name: String, request: TitleRequest) {
        if !self.title_request_enabled(&name, &request).await {
            tracing::debug!(
                target: "slopd::titles",
                session = %name,
                generation = request.generation,
                outcome = "skipped_disabled",
                "discarding title request after its policy was disabled"
            );
            return;
        }
        let (result, cache_hit) = self.resolve_title_request(&name, &request).await;
        if self
            .apply_title_result(&name, &request, result, cache_hit)
            .await
        {
            tracing::debug!(
                target: "slopd::titles",
                session = %name,
                outcome = "applied",
                "session title applied"
            );
            self.announce_sessions().await;
        }
    }

    async fn title_request_enabled(&self, name: &str, request: &TitleRequest) -> bool {
        let cfg = self.config().await;
        let live = self.live.read().await;
        live.get(name)
            .and_then(|l| title_settings(&cfg, &l.cfg, l.host))
            .is_some_and(|(policy, _)| {
                policy != TitlePolicy::Never
                    && prompt_is_long_enough(&request.prompt, cfg.daemon.title_min_chars)
            })
    }

    async fn resolve_title_request(
        &self,
        name: &str,
        request: &TitleRequest,
    ) -> (Result<String>, bool) {
        let prompt = &request.prompt;
        let model = &request.model;
        if let Some(title) = self.title_cache.get(prompt, &request.summary_prompt, model) {
            tracing::debug!(
                target: "slopd::titles",
                session = %name,
                generation = request.generation,
                model = %model,
                outcome = "cache_hit",
                "using cached session title"
            );
            return (Ok(title), true);
        }

        tracing::debug!(
            target: "slopd::titles",
            session = %name,
            generation = request.generation,
            model = %model,
            outcome = "request_started",
            "generating session title"
        );
        let result = crate::title::summarize_async(
            prompt,
            &request.summary_prompt,
            &request.key_file,
            model,
            "title worker",
        )
        .await;
        (result, false)
    }

    async fn apply_title_result(
        &self,
        name: &str,
        request: &TitleRequest,
        result: Result<String>,
        cache_hit: bool,
    ) -> bool {
        let cfg = self.config().await;
        let mut live = self.live.write().await;
        let Some(l) = live.get_mut(name) else {
            return false;
        };
        if l.title.conversation != request.conversation || l.title.generation != request.generation
        {
            tracing::debug!(
                target: "slopd::titles",
                session = %name,
                generation = request.generation,
                outcome = "stale_response",
                "discarding stale session title response"
            );
            return false;
        }
        if !title_settings(&cfg, &l.cfg, l.host).is_some_and(|(policy, _)| {
            policy != TitlePolicy::Never
                && prompt_is_long_enough(&request.prompt, cfg.daemon.title_min_chars)
        }) {
            l.title.pending = false;
            tracing::debug!(
                target: "slopd::titles",
                session = %name,
                generation = request.generation,
                outcome = "stale_disabled",
                "discarding title response after its policy was disabled"
            );
            return false;
        }
        if let Ok(title) = &result {
            let cache_result = if cache_hit {
                self.title_cache.remember(name, title)
            } else {
                self.title_cache.insert(
                    name,
                    &request.prompt,
                    &request.summary_prompt,
                    &request.model,
                    title,
                )
            };
            if let Err(error) = cache_result {
                tracing::warn!(
                    target: "slopd::titles",
                    session = %name,
                    error = %error,
                    outcome = "cache_write_failed",
                    "could not persist session title cache"
                );
            }
        }
        l.title.pending = false;
        match result {
            Ok(title) => {
                l.title.override_title = Some(title);
                true
            }
            Err(e) => {
                tracing::warn!(
                    target: "slopd::titles",
                    session = %name,
                    generation = request.generation,
                    error = %e,
                    outcome = "request_failed",
                    "session title request failed"
                );
                false
            }
        }
    }

    pub(crate) async fn capture_title_keys(
        self: &Arc<Self>,
        name: &str,
        keys: &[String],
        literal: bool,
    ) {
        let cfg = self.config().await;

        let action = {
            let mut live = self.live.write().await;
            let Some(l) = live.get_mut(name) else { return };
            let Some((policy, model)) = title_settings(&cfg, &l.cfg, l.host) else {
                return;
            };
            if policy == TitlePolicy::Never {
                return;
            }
            title_capture_action(l, name, &cfg, policy, model, keys, literal)
        };

        match action {
            Some(TitleCaptureAction::New(native)) => {
                self.persist_title_boundary(name, native.as_deref());
                self.announce_sessions().await;
            }
            Some(TitleCaptureAction::Request(request)) => {
                self.spawn_title_request(name.to_string(), request);
            }
            None => {}
        }
    }

    /// A settings change must invalidate title work already captured for a session. Otherwise a
    /// request queued just before turning summaries off can still reach OpenRouter or overwrite
    /// a terminal's native title after the save has completed.
    pub(crate) async fn reconcile_title_settings(&self, cfg: &Config) -> bool {
        let mut clear = Vec::new();
        let mut changed = false;
        {
            let mut live = self.live.write().await;
            for (name, l) in live.iter_mut() {
                let enabled = title_settings(cfg, &l.cfg, l.host)
                    .is_some_and(|(policy, _)| policy != TitlePolicy::Never);
                if enabled {
                    continue;
                }

                if l.title.pending || l.title.override_title.is_some() {
                    l.title.generation = l.title.generation.wrapping_add(1);
                    changed = true;
                }
                l.title.pending = false;
                l.title.composer = Composer::ready();
                l.title.override_title = None;
                clear.push(name.clone());
            }
        }

        for name in clear {
            if let Err(error) = self.title_cache.clear_latest(&name) {
                tracing::warn!(
                    target: "slopd::titles",
                    session = %name,
                    error = %error,
                    outcome = "cache_write_failed",
                    "could not clear disabled session title"
                );
            }
        }
        changed
    }

    fn persist_title_boundary(&self, name: &str, native: Option<&str>) {
        let cache_result = match native {
            Some(title) => self.title_cache.remember(name, title),
            None => self.title_cache.clear_latest(name),
        };
        if let Err(error) = cache_result {
            tracing::warn!(
                target: "slopd::titles",
                session = %name,
                error = %error,
                outcome = "cache_write_failed",
                "could not update session title cache"
            );
        }
    }

    pub(crate) async fn capture_title_paste(&self, name: &str, text: &str) {
        let cfg = self.config().await;
        let mut live = self.live.write().await;
        let Some(l) = live.get_mut(name) else { return };
        let Some((policy, _)) = title_settings(&cfg, &l.cfg, l.host) else {
            return;
        };
        if policy != TitlePolicy::Never {
            l.title.composer.paste(text);
        }
    }
}

#[cfg(test)]
mod tests {
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
}
