//! Serializes authorization/use with session identity changes.

use crate::session::Manager;
use std::future::Future;
use std::sync::Arc;

tokio::task_local! {
    static OWNER: usize;
    static REQUEST: usize;
}

impl Manager {
    pub(super) fn session_request_active(&self) -> bool {
        REQUEST
            .try_with(|owner| *owner == self as *const Self as usize)
            .unwrap_or(false)
    }

    /// Reload before authorization, then defer disk reloads until the request completes.
    pub(crate) async fn session_request<F: Future>(self: &Arc<Self>, request: F) -> F::Output {
        self.session_operation(async {
            self.reload_if_changed().await;
            REQUEST.scope(Arc::as_ptr(self) as usize, request).await
        })
        .await
    }

    /// Nested lifecycle calls share the boundary; spawned tasks must acquire their own.
    pub(crate) fn session_operation<'a, F: Future + 'a>(
        &'a self,
        operation: F,
    ) -> impl Future<Output = F::Output> + 'a {
        let operation = Box::pin(operation);
        async move {
            let owner = self as *const Self as usize;
            if OWNER.try_with(|current| *current == owner).unwrap_or(false) {
                return operation.await;
            }
            let _guard = self.session_boundary.lock().await;
            OWNER.scope(owner, operation).await
        }
    }
}

#[cfg(test)]
mod tests {
    use crate::config::{Config, Daemon, SessionCfg};
    use crate::grant::Level;

    #[tokio::test]
    async fn replacement_waits_for_authorized_use_and_then_rejects_the_old_capability() {
        for level in [Level::Ro, Level::Rw] {
            let manager = crate::session::test_manager(Config {
                daemon: Daemon {
                    token: "root".into(),
                    ..Default::default()
                },
                sessions: ["grantor", "target"]
                    .into_iter()
                    .map(|name| SessionCfg {
                        name: name.into(),
                        ..Default::default()
                    })
                    .collect(),
                ..Default::default()
            });
            let token = manager
                .mint_grant("grantor".into(), vec!["target".into()], level)
                .await
                .unwrap();
            let cap = manager.resolve_cap(Some(&token)).await.unwrap();
            let original = manager.config().await;
            let mut replacement = original.clone();
            replacement.sessions[1].state_id = uuid::Uuid::new_v4().to_string();
            let text = toml::to_string(&replacement).unwrap();
            let (release, wait) = tokio::sync::oneshot::channel();
            let (checked, check_done) = tokio::sync::oneshot::channel();
            let request = manager.session_request(async {
                assert!(manager.cap_ok(&cap, "target", level).await);
                checked.send(()).unwrap();
                wait.await.unwrap();
                assert_eq!(
                    manager.config().await.sessions[1].state_id,
                    original.sessions[1].state_id
                );
            });
            tokio::pin!(request);
            tokio::select! {
                _ = request.as_mut() => panic!("request finished before release"),
                _ = check_done => {},
            }
            let replace = manager.replace_config(&text);
            tokio::pin!(replace);
            assert!(futures::poll!(replace.as_mut()).is_pending());
            release.send(()).unwrap();
            request.await;
            replace.await.unwrap();
            assert!(!manager.cap_ok(&cap, "target", level).await);
        }
    }
}
