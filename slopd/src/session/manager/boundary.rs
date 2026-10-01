//! Serializes authorization/use with session identity changes.
//! Boundary wrappers acquire the session lock; their `_inner` helpers run with it held.
//! Other `_inner` helpers are ordinary implementation details, not a locking guarantee.

use crate::session::Manager;
use std::future::Future;
use std::sync::Arc;

tokio::task_local! {
    static OWNER: (usize, Arc<tokio::sync::OwnedRwLockWriteGuard<()>>);
    static READ_OWNER: usize;
    static REQUEST: usize;
}

impl Manager {
    /// Lifecycle callers already hold the session boundary. Acquire this guard before
    /// tmux identity changes, and keep it through live-state publication or rollback.
    /// Root terminal input uses only this boundary; scoped admission retains the global
    /// authorization boundary. Queued delivery rechecks identity under this guard.
    pub(crate) fn terminal_boundary(&self, name: &str) -> Arc<tokio::sync::RwLock<()>> {
        let mut boundaries = self
            .terminal_boundaries
            .lock()
            .unwrap_or_else(|e| e.into_inner());
        if let Some(boundary) = boundaries.get(name).and_then(std::sync::Weak::upgrade) {
            return boundary;
        }
        // Expired names must not accumulate as disposable workers come and go.
        boundaries.retain(|_, boundary| boundary.strong_count() > 0);
        let boundary = Arc::new(tokio::sync::RwLock::new(()));
        boundaries.insert(name.to_string(), Arc::downgrade(&boundary));
        boundary
    }

    pub(super) fn session_write_operation_active(&self) -> bool {
        OWNER
            .try_with(|current| current.0 == self as *const Self as usize)
            .unwrap_or(false)
    }

    pub(super) fn session_request_active(&self) -> bool {
        REQUEST
            .try_with(|owner| *owner == self as *const Self as usize)
            .unwrap_or(false)
    }

    /// Reload before authorization. Defer further disk reloads until the request completes.
    pub(crate) async fn session_request<F: Future>(self: &Arc<Self>, request: F) -> F::Output {
        self.session_operation(async {
            self.reload_if_changed().await;
            REQUEST.scope(Arc::as_ptr(self) as usize, request).await
        })
        .await
    }

    /// Read operations hold identity and revocation stable while remaining concurrent with
    /// terminal input and worktree Git work. Reload before taking the shared guard.
    pub(crate) async fn session_read_request<F: Future>(self: &Arc<Self>, request: F) -> F::Output {
        self.reload_if_stale().await;
        self.session_read_operation(REQUEST.scope(Arc::as_ptr(self) as usize, request))
            .await
    }

    pub(crate) async fn session_read_operation<'a, F: Future + 'a>(
        &'a self,
        operation: F,
    ) -> F::Output {
        let owner = self as *const Self as usize;
        if OWNER
            .try_with(|current| current.0 == owner)
            .unwrap_or(false)
            || READ_OWNER
                .try_with(|current| *current == owner)
                .unwrap_or(false)
        {
            return operation.await;
        }
        let _guard = self.session_boundary.read().await;
        READ_OWNER.scope(owner, operation).await
    }

    /// Transfer a prepared operation and its exclusive guard to an owned task.
    /// Nested callers share the guard, so cancellation cannot release identity
    /// protection before resource commit or cleanup finishes.
    #[expect(
        clippy::expect_used,
        reason = "propagate a panic from the owned operation rather than treating a partial transaction as successful"
    )]
    pub(super) async fn owned_session_operation<F>(self: &Arc<Self>, operation: F) -> F::Output
    where
        F: Future + Send + 'static,
        F::Output: Send + 'static,
    {
        let owner = Arc::as_ptr(self) as usize;
        let inherited = OWNER
            .try_with(|current| (current.0 == owner).then(|| current.1.clone()))
            .ok()
            .flatten();
        let guard = match inherited {
            Some(guard) => guard,
            None => {
                assert!(
                    !READ_OWNER
                        .try_with(|current| *current == owner)
                        .unwrap_or(false),
                    "exclusive session operation inside a shared boundary"
                );
                Arc::new(self.session_boundary.clone().write_owned().await)
            }
        };
        tokio::spawn(OWNER.scope((owner, guard), operation))
            .await
            .expect("owned session operation panicked")
    }

    /// Nested lifecycle calls share the boundary. Spawned tasks must acquire their own boundary.
    pub(crate) fn session_operation<'a, F: Future + 'a>(
        &'a self,
        operation: F,
    ) -> impl Future<Output = F::Output> + 'a {
        let operation = Box::pin(operation);
        async move {
            let owner = self as *const Self as usize;
            if OWNER
                .try_with(|current| current.0 == owner)
                .unwrap_or(false)
            {
                return operation.await;
            }
            assert!(
                !READ_OWNER
                    .try_with(|current| *current == owner)
                    .unwrap_or(false),
                "exclusive session operation inside a shared boundary"
            );
            let guard = Arc::new(self.session_boundary.clone().write_owned().await);
            OWNER.scope((owner, guard), operation).await
        }
    }
}

#[cfg(test)]
#[path = "boundary_tests.rs"]
mod tests;
