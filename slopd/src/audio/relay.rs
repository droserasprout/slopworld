//! Per-listener encoded radio and ICY metadata. A response owns its station socket
//! and registration from opening through disconnect, independently of the shared player.

use super::{
    STREAM_IDLE,
    station_http::{StationHttp, StreamBody},
};
use axum::body::Bytes;
use futures::{Stream, stream};
use std::collections::HashMap;
use std::future::Future;
use std::io::{self, Read};
use std::sync::{
    Arc, Mutex,
    atomic::{AtomicBool, Ordering},
};
use tokio::sync::{mpsc, oneshot, watch};
use uuid::Uuid;

const CHUNK: usize = 16 * 1024;
const MAX_LISTENERS: usize = 64;

#[derive(Default)]
pub(crate) struct Listeners {
    active: Mutex<HashMap<Uuid, Arc<Listener>>>,
    shutdown: watch::Sender<bool>,
}

#[derive(Default)]
struct Listener {
    title: Mutex<Option<String>>,
    cancelled: Arc<AtomicBool>,
}

#[derive(Debug)]
pub(crate) enum OpenError {
    Duplicate,
    Revoked,
    Unavailable,
    Station(io::Error),
}

impl Listeners {
    pub(crate) fn title(&self, id: Uuid) -> Option<Option<String>> {
        let active = self.active.lock().unwrap_or_else(|p| p.into_inner());
        let listener = active.get(&id)?;
        Some(
            listener
                .title
                .lock()
                .unwrap_or_else(|p| p.into_inner())
                .clone(),
        )
    }

    pub(crate) fn shutdown(&self) {
        let active = self.active.lock().unwrap_or_else(|p| p.into_inner());
        self.shutdown.send_replace(true);
        for listener in active.values() {
            listener.cancelled.store(true, Ordering::Release);
        }
    }

    fn register(self: &Arc<Self>, id: Uuid) -> Result<Registration, OpenError> {
        let mut active = self.active.lock().unwrap_or_else(|p| p.into_inner());
        // Each live listener can occupy one blocking station read and one HTTP connection.
        let at_capacity = active.len() >= MAX_LISTENERS;
        if *self.shutdown.borrow() {
            return Err(OpenError::Unavailable);
        }
        let std::collections::hash_map::Entry::Vacant(slot) = active.entry(id) else {
            return Err(OpenError::Duplicate);
        };
        if at_capacity {
            return Err(OpenError::Unavailable);
        }
        let listener = slot.insert(Arc::default()).clone();
        Ok(Registration {
            owner: self.clone(),
            id,
            listener,
        })
    }

    pub(crate) async fn open(
        self: &Arc<Self>,
        id: Uuid,
        url: String,
        revoked: impl Future<Output = ()> + Send + 'static,
    ) -> Result<
        (
            String,
            impl Stream<Item = io::Result<Bytes>> + Send + 'static,
        ),
        OpenError,
    > {
        let registration = self.register(id)?;
        let listener = registration.listener.clone();
        let opening = async move {
            tokio::task::spawn_blocking(move || {
                let http = StationHttp::new(listener.cancelled.clone(), STREAM_IDLE);
                http.open(&url, move |title| {
                    *listener.title.lock().unwrap_or_else(|p| p.into_inner()) = title;
                })
            })
            .await
        };
        let mut revoked = Box::pin(revoked);
        let (content_type, reader) = tokio::select! {
            biased;
            () = &mut revoked => return Err(OpenError::Revoked),
            result = opening => result,
        }
        .map_err(|error| OpenError::Station(io::Error::other(error)))?
        .map_err(OpenError::Station)?;

        Ok((content_type, registration.chunks(reader, revoked)))
    }
}

struct Registration {
    owner: Arc<Listeners>,
    id: Uuid,
    listener: Arc<Listener>,
}

impl Registration {
    fn chunks(
        self,
        reader: StreamBody,
        revoked: impl Future<Output = ()> + Send + 'static,
    ) -> impl Stream<Item = io::Result<Bytes>> + Send + 'static {
        // The owner can release the socket on rotation even while Hyper is backpressured.
        // Each downstream pull authorizes exactly one read.
        let mut shutdown = self.owner.shutdown.subscribe();
        let (demand, requests) = mpsc::channel(1);
        tokio::spawn(async move {
            tokio::select! {
                biased;
                () = revoked => {},
                _ = shutdown.wait_for(|stopping| *stopping) => {},
                () = serve_chunks(reader, requests) => {},
            }
            drop(self);
        });
        stream::unfold(demand, |demand| async move {
            let (reply, chunk) = oneshot::channel();
            demand.send(reply).await.ok()?;
            Some((chunk.await.ok()?, demand))
        })
    }
}

async fn serve_chunks(
    mut reader: StreamBody,
    mut requests: mpsc::Receiver<oneshot::Sender<io::Result<Bytes>>>,
) {
    while let Some(mut reply) = requests.recv().await {
        let read = tokio::task::spawn_blocking(move || {
            let mut buffer = vec![0; CHUNK];
            let count = reader.read(&mut buffer)?;
            buffer.truncate(count);
            Ok::<_, io::Error>((Bytes::from(buffer), reader))
        });
        let result = tokio::select! {
            () = reply.closed() => return,
            result = read => result.map_err(io::Error::other).and_then(|chunk| chunk),
        };
        match result {
            Ok((chunk, next)) => {
                if chunk.is_empty() || reply.send(Ok(chunk)).is_err() {
                    return;
                }
                reader = next;
            }
            Err(error) => {
                drop(reply.send(Err(error)));
                return;
            }
        }
    }
}

impl Drop for Registration {
    fn drop(&mut self) {
        self.listener.cancelled.store(true, Ordering::Release);
        self.owner
            .active
            .lock()
            .unwrap_or_else(|p| p.into_inner())
            .remove(&self.id);
    }
}

#[cfg(test)]
#[path = "relay_tests.rs"]
mod tests;
