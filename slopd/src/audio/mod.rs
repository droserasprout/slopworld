//! Play audio on the host to keep station networking outside Unity.
//! FMOD lacks TLS and AAC support and requires Content-Length, which Icecast omits.
//! A feeder decodes URLs and files into a ring buffer suitable for audio callbacks.

use std::sync::atomic::{AtomicU32, AtomicU64, Ordering};
use std::sync::mpsc::{Receiver, Sender};
use std::sync::{Arc, Mutex};
use std::time::Duration;

use serde::Serialize;

mod playback;
mod station;

use playback::{AudioOutput, OutputFactory, RodioOutputFactory};

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

enum Cmd {
    Play { source: String, generation: u64 },
    Volume(f32),
    Stop { generation: u64 },
}

enum WorkerMsg {
    Command(Cmd),
    Opened {
        source: String,
        generation: u64,
        result: anyhow::Result<playback::OpenedSource>,
        output: Option<Box<dyn AudioOutput>>,
    },
}

struct RequestState {
    source: Option<String>,
    generation: u64,
    cancel: Option<Arc<std::sync::atomic::AtomicBool>>,
}

struct Control {
    request: Mutex<RequestState>,
    volume: AtomicU32,
}

impl Control {
    fn new() -> Self {
        Self {
            request: Mutex::new(RequestState {
                source: None,
                generation: GENERATION.load(Ordering::SeqCst),
                cancel: None,
            }),
            volume: AtomicU32::new(1.0f32.to_bits()),
        }
    }

    fn volume(&self) -> f32 {
        f32::from_bits(self.volume.load(Ordering::Acquire))
    }

    fn request(&self) -> (Option<String>, u64) {
        let request = self.request.lock().unwrap_or_else(|p| p.into_inner());
        (request.source.clone(), request.generation)
    }

    fn play(&self, source: &str) -> u64 {
        let mut request = self.request.lock().unwrap_or_else(|p| p.into_inner());
        if request.source.as_deref() != Some(source) {
            if let Some(cancel) = request.cancel.as_ref() {
                cancel.store(true, Ordering::Release);
            }
            request.generation = GENERATION.fetch_add(1, Ordering::SeqCst).wrapping_add(1);
            request.source = Some(source.to_string());
            request.cancel = None;
        }
        request.generation
    }

    fn stop(&self) -> u64 {
        let mut request = self.request.lock().unwrap_or_else(|p| p.into_inner());
        if let Some(cancel) = request.cancel.as_ref() {
            cancel.store(true, Ordering::Release);
        }
        request.generation = GENERATION.fetch_add(1, Ordering::SeqCst).wrapping_add(1);
        request.source = None;
        request.cancel = None;
        request.generation
    }

    fn clear_if(&self, source: &str, generation: u64) {
        let mut request = self.request.lock().unwrap_or_else(|p| p.into_inner());
        if request.generation == generation && request.source.as_deref() == Some(source) {
            request.source = None;
            request.cancel = None;
        }
    }

    fn set_cancel(&self, cancel: Option<Arc<std::sync::atomic::AtomicBool>>) {
        self.request
            .lock()
            .unwrap_or_else(|p| p.into_inner())
            .cancel = cancel;
    }
}

/// A shared player handle. Cloning has little cost, and callers can use it from any thread.
/// Each method sends a message.
#[derive(Clone)]
pub struct Audio {
    tx: Sender<WorkerMsg>,
    state: Arc<Mutex<AudioState>>,
    control: Arc<Control>,
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
        let worker_tx = tx.clone();
        // Detached: shutdown only needs the worker to observe its closed channel.
        let spawned = std::thread::Builder::new()
            .name("slopd audio".into())
            .spawn(move || {
                let result = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
                    run(rx, worker_state, worker_control, worker_tx, factory)
                }));
                if result.is_err() {
                    fail(
                        &failure_state,
                        "audio worker terminated unexpectedly".to_string(),
                    );
                }
            });
        if let Err(e) = spawned {
            fail(&state, format!("audio worker failed to start: {e}"));
        }

        Audio { tx, state, control }
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
        self.control
            .volume
            .store(volume.to_bits(), Ordering::Release);
        let generation = self.control.play(source);
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
        self.control
            .volume
            .store(volume.to_bits(), Ordering::Release);
        self.send(WorkerMsg::Command(Cmd::Volume(volume)), "set volume");
    }

    pub fn stop(&self) {
        let generation = self.control.stop();
        self.send(WorkerMsg::Command(Cmd::Stop { generation }), "stop");
    }

    fn send(&self, message: WorkerMsg, operation: &str) {
        if let Err(e) = self.tx.send(message) {
            fail(
                &self.state,
                format!("audio worker unavailable during {operation}: {e}"),
            );
        }
    }

    /// Publishes a request-side error without opening a source, stopping stale playback first.
    pub fn reject(&self, why: impl Into<String>) {
        self.control.stop();
        fail(&self.state, why.into());
    }

    pub fn state(&self) -> AudioState {
        self.state.lock().unwrap_or_else(|p| p.into_inner()).clone()
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

struct PendingOpen {
    source: String,
    generation: u64,
    cancel: Arc<std::sync::atomic::AtomicBool>,
}

fn run(
    rx: Receiver<WorkerMsg>,
    state: Arc<Mutex<AudioState>>,
    control: Arc<Control>,
    tx: Sender<WorkerMsg>,
    factory: Arc<dyn OutputFactory>,
) {
    let mut output: Option<Box<dyn AudioOutput>> = None;
    let mut pending: Option<PendingOpen> = None;
    let mut active_cancel: Option<Arc<std::sync::atomic::AtomicBool>> = None;
    let mut active_generation = None;

    while let Ok(message) = rx.recv() {
        match message {
            WorkerMsg::Command(Cmd::Play { source, generation }) => {
                let (wanted, wanted_generation) = control.request();
                if generation != wanted_generation || wanted.as_deref() != Some(source.as_str()) {
                    continue;
                }

                if let Some(waiting) = pending.as_ref() {
                    if waiting.generation == generation && waiting.source == source {
                        continue;
                    }
                }

                // Published state may still describe a ring retired by queued stop/play.
                let already_playing = active_source(&state, &source, active_generation, generation);
                if already_playing {
                    if let Some(output) = output.as_ref() {
                        output.set_volume(control.volume());
                    }
                    state.lock().unwrap_or_else(|p| p.into_inner()).volume = control.volume();
                    continue;
                }

                if let Some(waiting) = pending.take() {
                    waiting.cancel.store(true, Ordering::Release);
                }
                if let Some(cancel) = active_cancel.take() {
                    cancel.store(true, Ordering::Release);
                }
                let mut current = state.lock().unwrap_or_else(|p| p.into_inner());
                current.playing = false;
                current.source = None;
                current.title = None;
                current.error = None;
                drop(current);

                let title = station::TitleSink::pending(Some(state.clone()), generation);
                let cancel = title.cancelled.clone();
                control.set_cancel(Some(cancel.clone()));
                let needs_output = output.is_none();
                pending = Some(PendingOpen {
                    source: source.clone(),
                    generation,
                    cancel,
                });
                spawn_open(
                    source,
                    generation,
                    title,
                    needs_output,
                    factory.clone(),
                    tx.clone(),
                );
            }

            WorkerMsg::Command(Cmd::Volume(volume)) => {
                if let Some(output) = output.as_ref() {
                    output.set_volume(volume);
                }
                state.lock().unwrap_or_else(|p| p.into_inner()).volume = volume;
            }

            WorkerMsg::Command(Cmd::Stop { generation }) => {
                if generation != GENERATION.load(Ordering::SeqCst) {
                    continue;
                }
                if let Some(waiting) = pending.take() {
                    waiting.cancel.store(true, Ordering::Release);
                }
                if let Some(cancel) = active_cancel.take() {
                    cancel.store(true, Ordering::Release);
                }
                let mut current = state.lock().unwrap_or_else(|p| p.into_inner());
                current.playing = false;
                current.source = None;
                current.title = None;
            }

            WorkerMsg::Opened {
                source,
                generation,
                result,
                output: opened_output,
            } => {
                let matches = pending
                    .as_ref()
                    .map(|waiting| waiting.generation == generation && waiting.source == source)
                    .unwrap_or(false);
                if !matches {
                    drop(result);
                    drop(opened_output);
                    continue;
                }
                pending = None;

                let (wanted, wanted_generation) = control.request();
                if generation != wanted_generation
                    || wanted.as_deref() != Some(source.as_str())
                    || generation != GENERATION.load(Ordering::SeqCst)
                {
                    drop(result);
                    drop(opened_output);
                    continue;
                }

                if let Some(opened_output) = opened_output {
                    output = Some(opened_output);
                }
                let opened = match result {
                    Ok(opened) => opened,
                    Err(error) => {
                        fail_current(
                            &state,
                            &control,
                            &source,
                            generation,
                            format!("{source}: {error:#}"),
                        );
                        continue;
                    }
                };
                let Some(output) = output.as_ref() else {
                    fail_current(
                        &state,
                        &control,
                        &source,
                        generation,
                        "audio output unavailable".to_string(),
                    );
                    continue;
                };
                match playback::commit(opened, output.as_ref(), control.volume()) {
                    Ok(Some(committed)) => {
                        if generation != GENERATION.load(Ordering::SeqCst) {
                            committed.title.cancel();
                            continue;
                        }
                        committed.title.commit();
                        if let Some(title) = committed.initial_title {
                            committed.title.set(Some(title));
                        }
                        active_generation = Some(generation);
                        active_cancel = Some(committed.title.cancelled.clone());
                        control.set_cancel(active_cancel.clone());
                        let mut current = state.lock().unwrap_or_else(|p| p.into_inner());
                        current.playing = true;
                        current.source = Some(source.clone());
                        current.volume = control.volume();
                        current.error = None;
                        tracing::info!("audio: playing {}", source);
                    }
                    Ok(None) => {}
                    Err(error) => {
                        fail_current(
                            &state,
                            &control,
                            &source,
                            generation,
                            format!("{source}: {error:#}"),
                        );
                    }
                }
            }
        }
    }
}

fn spawn_open(
    source: String,
    generation: u64,
    title: station::TitleSink,
    needs_output: bool,
    factory: Arc<dyn OutputFactory>,
    tx: Sender<WorkerMsg>,
) {
    let thread_source = source.clone();
    let spawned = std::thread::Builder::new()
        .name("slopd audio open".into())
        .spawn(move || {
            let (result, output) = match playback::open(&thread_source, title) {
                Ok(opened) => {
                    if needs_output {
                        match factory.open() {
                            Ok(output) => (Ok(opened), Some(output)),
                            Err(error) => (Err(error), None),
                        }
                    } else {
                        (Ok(opened), None)
                    }
                }
                Err(error) => (Err(error), None),
            };
            let _ = tx.send(WorkerMsg::Opened {
                source,
                generation,
                result,
                output,
            });
        });
    if let Err(error) = spawned {
        tracing::warn!("audio: could not start source opener: {error}");
    }
}

fn fail_current(
    state: &Arc<Mutex<AudioState>>,
    control: &Control,
    source: &str,
    generation: u64,
    why: String,
) {
    let (wanted, wanted_generation) = control.request();
    if generation != wanted_generation
        || wanted.as_deref() != Some(source)
        || generation != GENERATION.load(Ordering::SeqCst)
    {
        return;
    }
    control.clear_if(source, generation);
    fail(state, why);
}

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

fn fail(state: &Arc<Mutex<AudioState>>, why: String) {
    tracing::warn!("audio: {why}");
    let mut s = state.lock().unwrap_or_else(|p| p.into_inner());
    s.playing = false;
    s.source = None;
    s.title = None;
    s.error = Some(why);
}

fn wait_for_retry(pause: Duration, generation: u64, active: &AtomicU64) -> bool {
    let mut left = pause;
    while !left.is_zero() {
        let sleep = left.min(QUEUE_POLL);
        std::thread::sleep(sleep);
        if generation != active.load(Ordering::SeqCst) {
            return false;
        }
        left = left.saturating_sub(sleep);
    }
    true
}

#[cfg(test)]
pub(crate) use playback::{enqueue_chunk, open_device, pcm_id, Ring, NULL_PCM, RING, SERVER_PCMS};
#[cfg(test)]
pub(crate) use station::{
    local_title, stream_title, supported_file, Feed, Icy, Playlist, Reconnect, StreamBody,
    StreamConnector, TitleSink,
};

#[cfg(test)]
#[path = "tests.rs"]
mod tests;
