use super::*;
use futures::{StreamExt, future::pending};
use std::sync::atomic::AtomicUsize;

struct Endless(Arc<AtomicUsize>);

impl Read for Endless {
    fn read(&mut self, buffer: &mut [u8]) -> io::Result<usize> {
        self.0.fetch_add(1, Ordering::SeqCst);
        buffer.fill(0xff);
        Ok(buffer.len())
    }
}

#[tokio::test]
async fn backpressure_reads_only_one_bounded_chunk_per_pull() {
    let listeners = Arc::new(Listeners::default());
    let id = Uuid::new_v4();
    let registration = listeners.register(id).unwrap();
    let reads = Arc::new(AtomicUsize::new(0));
    let mut chunks = Box::pin(registration.chunks(Box::new(Endless(reads.clone())), pending()));
    assert_eq!(reads.load(Ordering::SeqCst), 0);
    for expected in 1..=3 {
        assert_eq!(chunks.next().await.unwrap().unwrap().len(), CHUNK);
        assert_eq!(reads.load(Ordering::SeqCst), expected);
    }
    drop(chunks);
    wait_for_release(&listeners, id).await;
}

#[tokio::test]
async fn rotation_releases_an_unpolled_response_and_its_listener_slot() {
    let listeners = Arc::new(Listeners::default());
    let id = Uuid::new_v4();
    let (rotate, rotated) = oneshot::channel();
    let reads = Arc::new(AtomicUsize::new(0));
    let stream = listeners
        .register(id)
        .unwrap()
        .chunks(Box::new(Endless(reads.clone())), async {
            rotated.await.unwrap();
        });
    rotate.send(()).unwrap();
    wait_for_release(&listeners, id).await;
    assert_eq!(reads.load(Ordering::SeqCst), 0);
    assert!(Box::pin(stream).next().await.is_none());
}

async fn wait_for_release(listeners: &Listeners, id: Uuid) {
    tokio::time::timeout(std::time::Duration::from_secs(2), async {
        while listeners.title(id).is_some() {
            tokio::task::yield_now().await;
        }
    })
    .await
    .unwrap();
}

#[test]
fn reusing_a_closed_listener_id_cannot_publish_a_delayed_old_title() {
    let listeners = Arc::new(Listeners::default());
    let id = Uuid::new_v4();
    let first = listeners.register(id).unwrap();
    let old = first.listener.clone();
    drop(first);
    let _current = listeners.register(id).unwrap();
    assert!(old.cancelled.load(Ordering::Acquire));
    *old.title.lock().unwrap() = Some("late".into());
    assert_eq!(listeners.title(id), Some(None));
    listeners.shutdown();
    assert!(matches!(
        listeners.register(Uuid::new_v4()),
        Err(OpenError::Unavailable)
    ));
}

#[tokio::test]
async fn dropping_a_listener_before_station_headers_retires_its_opening_request() {
    use tokio::io::AsyncReadExt;
    let station = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
    let url = format!("http://{}", station.local_addr().unwrap());
    let listeners = Arc::new(Listeners::default());
    let id = Uuid::new_v4();
    let owner = listeners.clone();
    let open = tokio::spawn(async move { owner.open(id, url, pending()).await.map(drop) });
    tokio::time::timeout(std::time::Duration::from_secs(3), async {
        let (mut socket, _) = station.accept().await.unwrap();
        let mut request = Vec::new();
        while !request.ends_with(b"\r\n\r\n") {
            request.push(socket.read_u8().await.unwrap());
        }
        assert_eq!(listeners.title(id), Some(None));
        open.abort();
        assert!(open.await.is_err_and(|error| error.is_cancelled()));
        assert_eq!(listeners.title(id), None);
        assert_eq!(socket.read(&mut [0; 1]).await.unwrap(), 0);
    })
    .await
    .expect("opening a silent station should cancel before its header deadline");
}

#[test]
fn full_registry_retains_active_listeners_and_releases_capacity_on_disconnect() {
    let listeners = Arc::new(Listeners::default());
    let mut active = (0..MAX_LISTENERS)
        .map(|_| listeners.register(Uuid::new_v4()).unwrap())
        .collect::<Vec<_>>();
    let id = active.last().unwrap().id;
    assert!(matches!(listeners.register(id), Err(OpenError::Duplicate)));
    assert!(matches!(
        listeners.register(Uuid::new_v4()),
        Err(OpenError::Unavailable)
    ));
    drop(active.pop().unwrap());
    assert_eq!(listeners.title(id), None);
    drop(listeners.register(id).unwrap());
}
