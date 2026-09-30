//! Capability-filtered snapshots, live frame coalescing, and serialized socket writes.
//! EventMessage owns shared encoding; per-send latency stamps stay local to this socket.

use std::collections::HashMap;
use std::sync::Arc;
use std::time::Duration;

use axum::extract::ws::Message;
use futures::SinkExt;
use tokio::sync::broadcast;
use tokio::task::JoinHandle;
use tokio::time::{Instant, sleep_until};

use super::super::Mgr;
use super::{WsSubs, WsTx};
use crate::grant::Cap;
use crate::session::{Event, EventMessage};

/// Filter events by socket capability. Root receives all events.
/// Scoped grants receive permitted sessions and screens.
/// Exclude projects, library, usage, audio, and jukebox catalog data from scoped connections.
pub(super) fn scope_event(cap: &Cap, ev: Arc<EventMessage>) -> Option<Arc<EventMessage>> {
    if !cap.is_valid() {
        return None;
    }
    match cap {
        Cap::Root => Some(ev),
        Cap::Scoped(_) => match ev.event() {
            Event::Sessions { sessions } => Some(EventMessage::new(Event::Sessions {
                sessions: sessions
                    .iter()
                    .filter(|s| cap.can_see(&s.name, s.host))
                    .cloned()
                    .collect(),
            })),
            Event::Screen { screen } => cap.can_see(&screen.name, false).then_some(ev),
            Event::Capabilities { .. }
            | Event::Projects { .. }
            | Event::Library { .. }
            | Event::Usage { .. }
            | Event::Audio { .. }
            | Event::Jukebox { .. } => None,
        },
    }
}

/// Seed state that may not broadcast again until it changes, using the live event scope filter.
pub(super) async fn send_initial_snapshot(tx: &WsTx, m: &Mgr, cap: &Cap) -> bool {
    if let Some(ev) = scope_event(
        cap,
        EventMessage::new(Event::Capabilities {
            capabilities: crate::runtime::capabilities(),
        }),
    ) && send(tx, cap, &ev).await.is_err()
    {
        return false;
    }
    if let Some(ev) = scope_event(
        cap,
        EventMessage::new(Event::Sessions {
            sessions: m.views().await,
        }),
    ) && send(tx, cap, &ev).await.is_err()
    {
        return false;
    }
    if !cap.may_create() {
        return true;
    }
    // Query each root-only source only after the preceding send succeeds.
    let ev = EventMessage::new(Event::Usage {
        usage: m.usage().await,
    });
    if send(tx, cap, &ev).await.is_err() {
        return false;
    }
    let ev = EventMessage::new(Event::Projects {
        projects: m.projects().await,
    });
    if send(tx, cap, &ev).await.is_err() {
        return false;
    }
    let ev = EventMessage::new(Event::Library {
        library: m.library().await,
    });
    if send(tx, cap, &ev).await.is_err() {
        return false;
    }
    let ev = EventMessage::new(Event::Audio {
        audio: m.music_state().await,
    });
    if send(tx, cap, &ev).await.is_err() {
        return false;
    }
    let ev = EventMessage::new(Event::Jukebox {
        jukebox: crate::jukebox::catalog(),
    });
    if send(tx, cap, &ev).await.is_err() {
        return false;
    }
    true
}

// Capture has a separate 16 ms rate limit; the shorter hold limits socket latency.
const FRAME_COALESCE: Duration = Duration::from_millis(8);

/// Latest authorized frame per pane and the last completed socket draw.
struct PendingFrames {
    frames: HashMap<String, Arc<EventMessage>>,
    last_sent: Instant,
}

impl PendingFrames {
    fn new() -> Self {
        Self {
            frames: HashMap::new(),
            last_sent: Instant::now() - FRAME_COALESCE,
        }
    }

    async fn ready(&self) {
        if self.frames.is_empty() {
            // Clean connections have no recurring timer.
            std::future::pending::<()>().await;
        } else {
            sleep_until(self.last_sent + FRAME_COALESCE).await;
        }
    }

    async fn screen(
        &mut self,
        tx: &WsTx,
        cap: &Cap,
        event: Arc<EventMessage>,
        name: String,
    ) -> Result<(), axum::Error> {
        if Instant::now().saturating_duration_since(self.last_sent) < FRAME_COALESCE {
            self.frames.insert(name, event);
        } else {
            // A held older frame must never follow this immediate send.
            self.frames.remove(&name);
            send(tx, cap, &event).await?;
            self.last_sent = Instant::now();
        }
        Ok(())
    }

    async fn flush(&mut self, tx: &WsTx, cap: &Cap) -> Result<(), axum::Error> {
        if !self.frames.is_empty() {
            for event in std::mem::take(&mut self.frames).into_values() {
                send(tx, cap, &event).await?;
            }
            self.last_sent = Instant::now();
        }
        Ok(())
    }
}

/// Coalesce live screens per pane; flush them before forwarding each control event.
pub(super) fn spawn_frame_pump(
    events: broadcast::Receiver<Arc<EventMessage>>,
    tx: WsTx,
    subs: WsSubs,
    cap: Cap,
) -> JoinHandle<()> {
    tokio::spawn(async move {
        if let Err(error) = pump_frames(events, &tx, &subs, &cap).await {
            tracing::debug!("frame pump ended: {error}");
        }
    })
}

async fn pump_frames(
    mut events: broadcast::Receiver<Arc<EventMessage>>,
    tx: &WsTx,
    subs: &WsSubs,
    cap: &Cap,
) -> Result<(), axum::Error> {
    let mut pending = PendingFrames::new();
    loop {
        tokio::select! {
            event = events.recv() => match event {
                Ok(event) => {
                    // Filter before retaining payloads in this connection's pending frames.
                    let Some(event) = scope_event(cap, event) else { continue };
                    if let Event::Screen { screen } = event.event() {
                        let name = screen.name.clone();
                        if !subs.lock().await.contains_key(&name) {
                            continue;
                        }
                        pending.screen(tx, cap, event, name).await?;
                    } else {
                        pending.flush(tx, cap).await?;
                        send(tx, cap, &event).await?;
                    }
                }
                // The next capture restores the screen after a missed broadcast.
                Err(broadcast::error::RecvError::Lagged(_)) => continue,
                Err(broadcast::error::RecvError::Closed) => return Ok(()),
            },
            () = pending.ready() => pending.flush(tx, cap).await?,
        }
    }
}

pub(super) async fn send(tx: &WsTx, cap: &Cap, ev: &EventMessage) -> Result<(), axum::Error> {
    let _perf = crate::perf::timer("websocket-send");
    let started = crate::perf::enabled().then(std::time::Instant::now);
    let mut bytes = ev
        .encoded()
        .map_err(|e| axum::Error::new(std::io::Error::other(e)))?
        .to_vec();
    let mut tx = tx.lock().await;
    if !cap.is_valid() {
        return Err(axum::Error::new(std::io::Error::other(
            "capability revoked",
        )));
    }
    if let Event::Screen { screen } = ev.event()
        && !screen.input_timings.is_empty()
    {
        use prost::Message as _;
        let mut message = ev
            .event()
            .to_protobuf()
            .map_err(|e| axum::Error::new(std::io::Error::other(e.to_string())))?;
        if let Some(crate::shared::wire::event::Payload::Screen(s)) = &mut message.payload {
            let at = crate::latency::now();
            for t in &mut s.input_timings {
                t.send_us = at;
            }
        }
        bytes = message.encode_to_vec();
    }
    crate::perf::count("websocket-bytes", bytes.len() as u64);
    let result = tx.send(Message::Binary(bytes)).await;
    if let Some(started) = started {
        tracing::debug!(
            target: "slopd::perf",
            lane = "websocket-send",
            elapsed_us = crate::clock::duration_us(started.elapsed()),
            "websocket event sent"
        );
    }
    result
}
