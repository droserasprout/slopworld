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

    /// Reload before authorization. Defer further disk reloads until the request completes.
    pub(crate) async fn session_request<F: Future>(self: &Arc<Self>, request: F) -> F::Output {
        self.session_operation(async {
            self.reload_if_changed().await;
            REQUEST.scope(Arc::as_ptr(self) as usize, request).await
        })
        .await
    }

    /// Nested lifecycle calls share the boundary. Spawned tasks must acquire their own boundary.
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
#[path = "boundary_tests.rs"]
mod tests;
