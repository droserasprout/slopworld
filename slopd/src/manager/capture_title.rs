//! Automatic title capture and generation.

use super::*;
use anyhow::anyhow;

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
        let request_prompt = prompt.clone();
        let request_summary_prompt = request.summary_prompt.clone();
        let request_key = request.key_file.clone();
        let request_model = model.clone();
        let result = tokio::task::spawn_blocking(move || {
            crate::title::summarize(
                &request_prompt,
                &request_summary_prompt,
                &request_key,
                &request_model,
            )
        })
        .await;
        let result = match result {
            Ok(r) => r,
            Err(e) => Err(anyhow!("title worker: {e}")),
        };
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
