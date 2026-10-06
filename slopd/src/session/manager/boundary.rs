//! Serializes authorization/use with session identity changes.
//! Boundary wrappers acquire the session lock; their `_inner` helpers run with it held.
//! Other `_inner` helpers are ordinary implementation details, not a locking guarantee.

use crate::session::Manager;
use std::collections::HashMap;
use std::future::Future;
use std::sync::{Arc, Mutex, Weak};

tokio::task_local! {
    static OWNER: (usize, Arc<tokio::sync::OwnedRwLockWriteGuard<()>>);
    static READ_OWNER: (usize, Arc<tokio::sync::OwnedRwLockReadGuard<()>>);
    static REQUEST: usize;
}

/// A synchronous task-file owner inherits the active request's session guard.
/// It performs no manager callbacks while holding these leases.
pub(super) fn task_io_guards() -> impl Send + 'static {
    (
        OWNER.try_with(|(_, guard)| guard.clone()).ok(),
        READ_OWNER.try_with(|(_, guard)| guard.clone()).ok(),
    )
}

/// Capture before spawning: a request already reloaded and authorized against
/// accepted state must not trigger another disk reload inside its owned work.
fn carry_request_context<F: Future>(owner: usize, operation: F) -> impl Future<Output = F::Output> {
    let active = REQUEST
        .try_with(|current| *current == owner)
        .unwrap_or(false);
    async move {
        if active {
            REQUEST.scope(owner, operation).await
        } else {
            operation.await
        }
    }
}

/// Reuse live per-name locks and discard expired names as temporary terminals disappear.
fn named_lock<T: Default>(locks: &Mutex<HashMap<String, Weak<T>>>, name: &str) -> Arc<T> {
    let mut locks = locks.lock().unwrap_or_else(|error| error.into_inner());
    if let Some(lock) = locks.get(name).and_then(Weak::upgrade) {
        return lock;
    }
    locks.retain(|_, lock| lock.strong_count() > 0);
    let lock = Arc::new(T::default());
    locks.insert(name.to_string(), Arc::downgrade(&lock));
    lock
}

impl Manager {
    /// Lifecycle callers already hold the session boundary. Acquire this guard before
    /// tmux identity changes, and keep it through live-state publication or rollback.
    /// Root terminal input uses only this boundary; scoped admission retains the global
    /// authorization boundary. Queued delivery rechecks identity under this guard.
    pub(crate) fn terminal_boundary(&self, name: &str) -> Arc<tokio::sync::RwLock<()>> {
        named_lock(&self.terminal_boundaries, name)
    }

    /// Resize publication and redraw restoration serialize only within this name.
    pub(super) fn terminal_resize(&self, name: &str) -> Arc<tokio::sync::Mutex<()>> {
        named_lock(&self.terminal_resizes, name)
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
                .try_with(|current| current.0 == owner)
                .unwrap_or(false)
        {
            return operation.await;
        }
        let guard = Arc::new(self.session_boundary.clone().read_owned().await);
        READ_OWNER.scope((owner, guard), operation).await
    }

    /// Keep a shared operation and its resource guards alive across requester
    /// cancellation. Nested exclusive callers retain their stronger boundary.
    #[expect(
        clippy::expect_used,
        reason = "propagate a panic from the owned operation"
    )]
    pub(super) async fn owned_session_read_operation<F>(self: &Arc<Self>, operation: F) -> F::Output
    where
        F: Future + Send + 'static,
        F::Output: Send + 'static,
    {
        if self.session_write_operation_active() {
            return self.owned_session_operation(operation).await;
        }
        let owner = Arc::as_ptr(self) as usize;
        let inherited = READ_OWNER
            .try_with(|current| (current.0 == owner).then(|| current.1.clone()))
            .ok()
            .flatten();
        let guard = match inherited {
            Some(guard) => guard,
            None => Arc::new(self.session_boundary.clone().read_owned().await),
        };
        tokio::spawn(READ_OWNER.scope((owner, guard), carry_request_context(owner, operation)))
            .await
            .expect("owned shared operation panicked")
    }

    /// Only project metadata commits may inherit a shared session boundary.
    /// Identity/auth mutations require an exclusive boundary. The persistence
    /// gate still serializes candidates and publication in both cases.
    pub(super) async fn owned_config_operation<F>(
        self: &Arc<Self>,
        project_only: bool,
        operation: F,
    ) -> F::Output
    where
        F: Future + Send + 'static,
        F::Output: Send + 'static,
    {
        let owner = Arc::as_ptr(self) as usize;
        if READ_OWNER
            .try_with(|current| current.0 == owner)
            .unwrap_or(false)
            && !self.session_write_operation_active()
        {
            assert!(
                project_only,
                "identity configuration commit inside shared boundary"
            );
            return self.owned_session_read_operation(operation).await;
        }
        self.owned_session_operation(operation).await
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
                        .try_with(|current| current.0 == owner)
                        .unwrap_or(false),
                    "exclusive session operation inside a shared boundary"
                );
                Arc::new(self.session_boundary.clone().write_owned().await)
            }
        };
        tokio::spawn(OWNER.scope((owner, guard), carry_request_context(owner, operation)))
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
                    .try_with(|current| current.0 == owner)
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
