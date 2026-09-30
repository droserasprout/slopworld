//! Play audio on the host to keep station networking outside Unity.
//! FMOD lacks TLS and AAC support and requires Content-Length, which Icecast omits.
//! A feeder decodes URLs and files into a ring buffer suitable for audio callbacks.

use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::mpsc::Sender;
use std::sync::{Arc, Mutex};
use std::time::Duration;

use serde::Serialize;

mod control;
mod playback;
mod ring;
mod station;
#[cfg(test)]
mod test_support;
mod title;
mod worker;

use control::Control;
use playback::{OutputFactory, RodioOutputFactory};
use worker::{Cmd, WorkerMsg, run};

pub(crate) const CONNECT: Duration = Duration::from_secs(15);
/// Limit the time to open headers and the decoder. Clear this deadline after each successful response.
/// STREAM_IDLE controls the live response body separately.
pub(crate) const OPEN: Duration = Duration::from_secs(10);
// A live station should deliver encoded audio within this interval.
// After a network change, the old TCP connection can remain open without delivering data or reporting an error.
// The body timeout triggers reconnection without limiting the total stream duration.
pub(crate) const STREAM_IDLE: Duration = Duration::from_secs(30);
pub(crate) const REOPEN_PAUSE: Duration = Duration::from_secs(2);
pub(crate) const QUEUE_POLL: Duration = Duration::from_millis(5);

#[derive(Clone, Debug, Default, PartialEq, Serialize)]
pub struct AudioState {
    pub playing: bool,
    pub source: Option<String>,
    pub volume: f32,
    pub error: Option<String>,
    pub title: Option<String>,
    pub session: Option<String>,
}

/// A shared player handle. Cloning has little cost, and callers can use it from any thread.
/// Each method sends a message.
pub struct Audio {
    inner: Arc<AudioInner>,
}

struct AudioInner {
    tx: Sender<WorkerMsg>,
    state: Arc<Mutex<AudioState>>,
    control: Arc<Control>,
}

impl Clone for Audio {
    fn clone(&self) -> Self {
        Self {
            inner: self.inner.clone(),
        }
    }
}

impl Drop for AudioInner {
    fn drop(&mut self) {
        // Arc drops the inner value exactly once, including simultaneous final clone drops.
        // Completion senders outlive callers, so channel closure cannot signal shutdown.
        drop(self.tx.send(WorkerMsg::Shutdown));
    }
}

impl Audio {
    pub fn new() -> Audio {
        Self::with_factory(Arc::new(RodioOutputFactory))
    }

    fn with_factory(factory: Arc<dyn OutputFactory>) -> Audio {
        let (tx, rx) = std::sync::mpsc::channel();
        let state = Arc::new(Mutex::new(AudioState {
            volume: 1.0,
            ..Default::default()
        }));
        let control = Arc::new(Control::new());

        let worker_state = state.clone();
        let failure_state = state.clone();
        let worker_control = control.clone();
        let failure_control = control.clone();
        let worker_tx = tx.clone();
        // The final public handle sends Shutdown; opener completions may keep the channel alive.
        let spawned = std::thread::Builder::new()
            .name("slopd audio".into())
            .spawn(move || {
                let result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
                    run(rx, worker_state, worker_control, worker_tx, factory)
                }));
                if result.is_err() {
                    let (source, generation) = failure_control.snapshot();
                    failure_control.fail_request(
                        &failure_state,
                        source.as_deref(),
                        generation,
                        "audio worker terminated unexpectedly".to_string(),
                    );
                }
            });
        if let Err(e) = spawned {
            fail(&state, format!("audio worker failed to start: {e}"));
        }

        Audio {
            inner: Arc::new(AudioInner { tx, state, control }),
        }
    }

    /// `source` is a URL, file path, or directory. `volume` ranges from 0 to 1.
    pub fn play(&self, source: &str, volume: f32) {
        if crate::runtime::is_slopcar() {
            self.reject(
                "Audio playback is unavailable in slopcar. Playback stays in the native game.",
            );
            return;
        }
        let volume = volume.clamp(0.0, 1.0);
        self.inner
            .control
            .volume
            .store(volume.to_bits(), Ordering::Release);
        let generation = self.inner.control.play(source);
        self.send(
            WorkerMsg::Command(Cmd::Play {
                source: source.to_string(),
                generation,
            }),
            "play",
        );
    }

    pub fn set_volume(&self, volume: f32) {
        let volume = volume.clamp(0.0, 1.0);
        self.inner
            .control
            .volume
            .store(volume.to_bits(), Ordering::Release);
        self.send(WorkerMsg::Command(Cmd::Volume(volume)), "set volume");
    }

    pub fn stop(&self) {
        let generation = self.inner.control.stop();
        self.send(WorkerMsg::Command(Cmd::Stop { generation }), "stop");
    }

    fn send(&self, message: WorkerMsg, operation: &str) {
        if let Err(e) = self.inner.tx.send(message) {
            let (source, generation) = self.inner.control.snapshot();
            self.inner.control.fail_request(
                &self.inner.state,
                source.as_deref(),
                generation,
                format!("audio worker unavailable during {operation}: {e}"),
            );
        }
    }

    /// Publishes a request-side error without opening a source, stopping stale playback first.
    pub fn reject(&self, why: impl Into<String>) {
        self.inner.control.reject(&self.inner.state, why.into());
    }

    pub fn state(&self) -> AudioState {
        self.inner
            .state
            .lock()
            .unwrap_or_else(|p| p.into_inner())
            .clone()
    }
}

impl Default for Audio {
    fn default() -> Self {
        Audio::new()
    }
}

/// Polls player state and broadcasts changes without coupling the audio thread to the event bus.
pub fn spawn(m: std::sync::Arc<crate::session::Manager>) -> tokio::task::JoinHandle<()> {
    const BEAT: Duration = Duration::from_millis(500);

    tokio::spawn(async move {
        let mut last: Option<AudioState> = None;
        let mut tick = tokio::time::interval(BEAT);
        tick.set_missed_tick_behavior(tokio::time::MissedTickBehavior::Delay);
        loop {
            tick.tick().await;
            let now = m.music_state().await;
            if last.as_ref() == Some(&now) {
                continue;
            }
            last = Some(now.clone());
            m.emit(crate::session::Event::Audio { audio: now });
        }
    })
}

/// The active source generation. Old feeders stop before writing to the new ring buffer.
static GENERATION: AtomicU64 = AtomicU64::new(0);

fn fail(state: &Arc<Mutex<AudioState>>, why: String) {
    tracing::warn!("audio: {why}");
    let mut s = state.lock().unwrap_or_else(|p| p.into_inner());
    fail_state(&mut s, why);
}

fn fail_state(state: &mut AudioState, why: String) {
    state.playing = false;
    state.source = None;
    state.title = None;
    state.error = Some(why);
}

#[cfg(test)]
fn active_source(
    state: &Arc<Mutex<AudioState>>,
    source: &str,
    active_generation: Option<u64>,
    requested_generation: u64,
) -> bool {
    let current = state.lock().unwrap_or_else(|p| p.into_inner());
    active_generation == Some(requested_generation)
        && current.playing
        && current.source.as_deref() == Some(source)
}

fn wait_for_retry(
    pause: Duration,
    generation: u64,
    active: &AtomicU64,
    cancelled: Option<&AtomicBool>,
) -> bool {
    let mut left = pause;
    while !left.is_zero() {
        let sleep = left.min(QUEUE_POLL);
        std::thread::sleep(sleep);
        if generation != active.load(Ordering::SeqCst)
            || cancelled.is_some_and(|cancel| cancel.load(Ordering::Acquire))
        {
            return false;
        }
        left = left.saturating_sub(sleep);
    }
    true
}

#[cfg(test)]
pub(crate) use playback::{NULL_PCM, SERVER_PCMS, open_device, pcm_id};
#[cfg(test)]
pub(crate) use ring::{RING, Ring, enqueue_chunk};
#[cfg(test)]
pub(crate) use station::{
    Feed, Icy, Playlist, Reconnect, StreamBody, StreamConnector, local_title, stream_title,
    supported_file,
};

#[cfg(test)]
pub(crate) use title::TitleSink;

#[cfg(test)]
#[path = "tests.rs"]
mod tests;
