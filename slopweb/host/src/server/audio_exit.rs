//! One-shot page-exit signals for authenticated WebSocket relays. A beacon can
//! finish only the socket generation that registered it, even across reconnect.
use super::{Host, reply};
use axum::{extract::Request, http::StatusCode, response::Response};
use std::{
    collections::HashMap,
    sync::{Arc, Mutex, MutexGuard},
};
use tokio::sync::oneshot;

pub(super) struct Scope {
    page: String,
    generation: u64,
}

impl Scope {
    pub(super) fn parse(query: &str) -> Option<Self> {
        let (page, generation) = query.strip_prefix("exit=")?.split_once('.')?;
        if page.len() != 32 || !page.bytes().all(|byte| byte.is_ascii_hexdigit()) {
            return None;
        }
        Some(Self {
            page: page.to_owned(),
            generation: generation.parse().ok()?,
        })
    }
}

struct Entry {
    generation: u64,
    stop: Option<oneshot::Sender<()>>,
}

#[derive(Default)]
pub(super) struct Exits {
    pages: Mutex<HashMap<String, Entry>>,
}

pub(super) struct Registration {
    exits: Arc<Exits>,
    scope: Scope,
    stop: oneshot::Receiver<()>,
}

impl Exits {
    fn pages(&self) -> MutexGuard<'_, HashMap<String, Entry>> {
        self.pages.lock().unwrap_or_else(|e| e.into_inner())
    }

    pub(super) fn register(self: &Arc<Self>, scope: Scope) -> Option<Registration> {
        let mut pages = self.pages();
        if pages
            .get(&scope.page)
            .is_some_and(|e| e.generation >= scope.generation)
        {
            return None;
        }
        let (tx, stop) = oneshot::channel();
        pages.insert(
            scope.page.clone(),
            Entry {
                generation: scope.generation,
                stop: Some(tx),
            },
        );
        drop(pages);
        Some(Registration {
            exits: self.clone(),
            scope,
            stop,
        })
    }

    fn signal(&self, scope: &Scope) -> bool {
        self.pages()
            .get_mut(&scope.page)
            .filter(|entry| entry.generation == scope.generation)
            .and_then(|entry| entry.stop.take())
            .is_some_and(|stop| stop.send(()).is_ok())
    }
}

impl Registration {
    // Recheck identity when the relay writer is ready. A newer handshake may
    // have replaced a signalled generation while a large send was in flight.
    pub(super) async fn requested(&mut self) -> bool {
        (&mut self.stop).await.is_ok()
            && self
                .exits
                .pages()
                .get(&self.scope.page)
                .is_some_and(|e| e.generation == self.scope.generation)
    }
}

impl Drop for Registration {
    fn drop(&mut self) {
        let mut pages = self.exits.pages();
        if pages
            .get(&self.scope.page)
            .is_some_and(|e| e.generation == self.scope.generation)
        {
            pages.remove(&self.scope.page);
        }
    }
}

pub(super) fn response(host: &Host, request: &Request) -> Response {
    if request.method() != "POST" || !host.boundary.same_origin(request.headers()) {
        return reply(StatusCode::FORBIDDEN, "");
    }
    let Some(scope) = request.uri().query().and_then(Scope::parse) else {
        return reply(StatusCode::BAD_REQUEST, "");
    };
    if host.audio_exits.signal(&scope) {
        reply(StatusCode::ACCEPTED, "")
    } else {
        reply(StatusCode::GONE, "")
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[tokio::test]
    async fn replacing_a_busy_writer_revokes_its_pending_stop_without_retiring_the_new_socket() {
        let exits = Arc::new(Exits::default());
        let scope = |generation| Scope {
            page: "page".into(),
            generation,
        };
        let mut old = exits.register(scope(1)).unwrap();
        assert!(exits.signal(&scope(1)));
        let mut current = exits.register(scope(2)).unwrap();
        assert!(!old.requested().await);
        drop(old);
        assert!(exits.register(scope(1)).is_none());
        assert!(exits.signal(&scope(2)));
        assert!(current.requested().await);
        assert!(!exits.signal(&scope(2)));
    }
}
