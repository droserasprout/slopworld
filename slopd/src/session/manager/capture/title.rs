//! Coordinate title input, provider requests, and validated live-state commits.
//! session::title owns transitions; crate::title owns summary reuse and queued persistence.

use super::*;
use crate::title::Summary;

impl Manager {
    pub(crate) fn spawn_title_request(self: &Arc<Self>, name: String, request: TitleRequest) {
        let manager = self.clone();
        tokio::spawn(async move { manager.run_title_request(name, request).await });
    }

    async fn run_title_request(self: Arc<Self>, name: String, request: TitleRequest) {
        if !self.title_request_enabled(&name, &request).await {
            if let Some(live) = self.live.write().await.get_mut(&name) {
                live.title.finish(&request, None);
            }
            return;
        }
        let result = self.title_cache.resolve(&request.input).await;
        if self.apply_title_result(&name, &request, result).await {
            self.announce_sessions().await;
        }
    }

    async fn title_request_enabled(&self, name: &str, request: &TitleRequest) -> bool {
        let cfg = self.cfg.read().await;
        let live = self.live.read().await;
        live.get(name).is_some_and(|l| {
            l.title.accepts(request)
                && TitleSettings::for_session(&cfg, &l.cfg, l.host)
                    .is_some_and(|settings| settings.allows(&request.input.prompt))
        })
    }

    async fn apply_title_result(
        &self,
        name: &str,
        request: &TitleRequest,
        result: Result<Summary>,
    ) -> bool {
        // Hold current settings through validation/commit. No provider or disk I/O here.
        let cfg = self.cfg.read().await;
        let mut live = self.live.write().await;
        let Some(l) = live.get_mut(name) else {
            return false;
        };
        if !l.title.accepts(request) {
            return false;
        }
        if !TitleSettings::for_session(&cfg, &l.cfg, l.host)
            .is_some_and(|settings| settings.allows(&request.input.prompt))
        {
            l.title.finish(request, None);
            return false;
        }
        match result {
            Ok(summary) => {
                // Update bounded cache memory in live-commit order. The single writer
                // persists later, so /new, disable, and rename cannot be overtaken by this result.
                self.title_cache.store(&request.input, &summary, Some(name));
                l.title.finish(request, Some(summary.text))
            }
            Err(error) => {
                l.title.finish(request, None);
                tracing::warn!(target: "slopd::titles", session = %name, %error,
                    outcome = "request_failed", "session title request failed");
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
        let action = {
            let cfg = self.cfg.read().await;
            let mut live = self.live.write().await;
            let Some(l) = live.get_mut(name) else { return };
            let Some(settings) = TitleSettings::for_session(&cfg, &l.cfg, l.host) else {
                return;
            };
            let action = l.title.capture_keys(&settings, keys, literal);
            if matches!(action, Some(TitleAction::Boundary)) {
                match l.title.title() {
                    Some(title) => self.title_cache.remember(name, title),
                    None => self.title_cache.clear_latest(name),
                }
            }
            action
        };
        match action {
            Some(TitleAction::Boundary) => self.announce_sessions().await,
            Some(TitleAction::Request(request)) => {
                self.spawn_title_request(name.to_string(), request)
            }
            None => {}
        }
    }

    /// Disable pending work and recovery state together, preserving the once-attempt budget.
    pub(crate) async fn reconcile_title_settings(&self, cfg: &Config) -> bool {
        let mut changed = false;
        let mut live = self.live.write().await;
        for (name, l) in live.iter_mut() {
            if TitleSettings::for_session(cfg, &l.cfg, l.host).is_none() {
                changed |= l.title.disable();
                self.title_cache.clear_latest(name);
            }
        }
        changed
    }

    pub(crate) async fn capture_title_paste(&self, name: &str, text: &str) {
        let cfg = self.cfg.read().await;
        let mut live = self.live.write().await;
        let Some(l) = live.get_mut(name) else { return };
        if TitleSettings::for_session(&cfg, &l.cfg, l.host).is_some() {
            l.title.paste(text);
        }
    }
}

#[cfg(test)]
#[path = "title_tests.rs"]
mod tests;
