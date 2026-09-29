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
    Shutdown,
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

enum SourceRequirement<'a> {
    Active,
    Exact(&'a str),
    None,
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

    /// Publish a short state change while the request identity is locked. Lock order is request
    /// then audio state. Callers must finish playback and other I/O before entering this boundary.
    fn publish_current<R>(
        &self,
        state: &Arc<Mutex<AudioState>>,
        generation: u64,
        source: SourceRequirement<'_>,
        publish: impl FnOnce(&mut RequestState, &mut AudioState) -> R,
    ) -> Option<R> {
        let mut request = self.request.lock().unwrap_or_else(|p| p.into_inner());
        let source_matches = match source {
            SourceRequirement::Active => request.source.is_some(),
            SourceRequirement::Exact(source) => request.source.as_deref() == Some(source),
            SourceRequirement::None => request.source.is_none(),
        };
        if request.generation != generation
            || generation != GENERATION.load(Ordering::SeqCst)
            || !source_matches
        {
            return None;
        }

        let mut current = state.lock().unwrap_or_else(|p| p.into_inner());
        Some(publish(&mut request, &mut current))
    }

    fn is_current(&self, source: &str, generation: u64) -> bool {
        let request = self.request.lock().unwrap_or_else(|p| p.into_inner());
        request.generation == generation
            && request.source.as_deref() == Some(source)
            && generation == GENERATION.load(Ordering::SeqCst)
    }

    fn snapshot(&self) -> (Option<String>, u64) {
        let request = self.request.lock().unwrap_or_else(|p| p.into_inner());
        (request.source.clone(), request.generation)
    }

    fn fail_request(
        &self,
        state: &Arc<Mutex<AudioState>>,
        source: Option<&str>,
        generation: u64,
        why: String,
    ) {
        let log_why = why.clone();
        {
            let mut request = self.request.lock().unwrap_or_else(|p| p.into_inner());
            if request.generation != generation
                || request.source.as_deref() != source
                || generation != GENERATION.load(Ordering::SeqCst)
            {
                return;
            }
            if let Some(cancel) = request.cancel.take() {
                cancel.store(true, Ordering::Release);
            }
            request.source = None;
            let mut current = state.lock().unwrap_or_else(|p| p.into_inner());
            fail_state(&mut current, why);
        }
        tracing::warn!("audio: {log_why}");
    }

    fn fail_current(
        &self,
        state: &Arc<Mutex<AudioState>>,
        source: &str,
        generation: u64,
        why: String,
    ) {
        let log_why = why.clone();
        let published = self
            .publish_current(
                state,
                generation,
                SourceRequirement::Exact(source),
                |request, current| {
                    if let Some(cancel) = request.cancel.take() {
                        cancel.store(true, Ordering::Release);
                    }
                    request.source = None;
                    fail_state(current, why);
                },
            )
            .is_some();
        if published {
            tracing::warn!("audio: {log_why}");
        }
    }

    fn reject(&self, state: &Arc<Mutex<AudioState>>, why: String) {
        let log_why = why.clone();
        {
            let mut request = self.request.lock().unwrap_or_else(|p| p.into_inner());
            if let Some(cancel) = request.cancel.take() {
                cancel.store(true, Ordering::Release);
            }
            request.generation = GENERATION.fetch_add(1, Ordering::SeqCst).wrapping_add(1);
            request.source = None;
            let mut current = state.lock().unwrap_or_else(|p| p.into_inner());
            fail_state(&mut current, why);
        }
        tracing::warn!("audio: {log_why}");
    }
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

impl Drop for Audio {
    fn drop(&mut self) {
        // The worker and opener threads hold completion senders, so receiver closure cannot
        // signal that callers are gone. Wake it explicitly when the final public handle drops.
        if Arc::strong_count(&self.inner) == 1 {
            let _ = self.inner.tx.send(WorkerMsg::Shutdown);
        }
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

struct PendingOpen {
    source: String,
    generation: u64,
    cancel: Arc<std::sync::atomic::AtomicBool>,
}

enum PlayDisposition {
    AlreadyPending,
    AlreadyPlaying,
    Open,
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
            WorkerMsg::Shutdown => {
                control.stop();
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
                break;
            }
            WorkerMsg::Command(Cmd::Play { source, generation }) => {
                let title = station::TitleSink::pending_for(
                    Some(state.clone()),
                    generation,
                    control.clone(),
                );
                let cancel = title.cancelled.clone();
                let needs_output = output.is_none();
                let volume = control.volume();
                let disposition = control.publish_current(
                    &state,
                    generation,
                    SourceRequirement::Exact(&source),
                    |request, current| {
                        if pending.as_ref().is_some_and(|waiting| {
                            waiting.generation == generation && waiting.source == source
                        }) {
                            return PlayDisposition::AlreadyPending;
                        }

                        // Published state may still describe a ring retired by queued stop/play.
                        if active_generation == Some(generation)
                            && current.playing
                            && current.source.as_deref() == Some(source.as_str())
                        {
                            current.volume = volume;
                            return PlayDisposition::AlreadyPlaying;
                        }

                        if let Some(waiting) = pending.take() {
                            waiting.cancel.store(true, Ordering::Release);
                        }
                        if let Some(old_cancel) = active_cancel.take() {
                            old_cancel.store(true, Ordering::Release);
                        }
                        current.playing = false;
                        current.source = None;
                        current.title = None;
                        current.error = None;
                        request.cancel = Some(cancel.clone());
                        pending = Some(PendingOpen {
                            source: source.clone(),
                            generation,
                            cancel: cancel.clone(),
                        });
                        PlayDisposition::Open
                    },
                );
                match disposition {
                    None | Some(PlayDisposition::AlreadyPending) => continue,
                    Some(PlayDisposition::AlreadyPlaying) => {
                        if let Some(output) = output.as_ref() {
                            output.set_volume(volume);
                        }
                    }
                    Some(PlayDisposition::Open) => spawn_open(
                        source,
                        generation,
                        title,
                        needs_output,
                        factory.clone(),
                        tx.clone(),
                    ),
                }
            }

            WorkerMsg::Command(Cmd::Volume(volume)) => {
                if let Some(output) = output.as_ref() {
                    output.set_volume(volume);
                }
                state.lock().unwrap_or_else(|p| p.into_inner()).volume = volume;
            }

            WorkerMsg::Command(Cmd::Stop { generation }) => {
                if control
                    .publish_current(&state, generation, SourceRequirement::None, |_, current| {
                        if let Some(waiting) = pending.take() {
                            waiting.cancel.store(true, Ordering::Release);
                        }
                        if let Some(cancel) = active_cancel.take() {
                            cancel.store(true, Ordering::Release);
                        }
                        current.playing = false;
                        current.source = None;
                        current.title = None;
                    })
                    .is_none()
                {
                    continue;
                }
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

                if !control.is_current(&source, generation) {
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
                        let cancel = committed.title.cancelled.clone();
                        let volume = control.volume();
                        let published = control.publish_current(
                            &state,
                            generation,
                            SourceRequirement::Exact(&source),
                            |request, current| {
                                request.cancel = Some(cancel.clone());
                                current.playing = true;
                                current.source = Some(source.clone());
                                current.volume = volume;
                                current.error = None;
                            },
                        );
                        if published.is_none() {
                            committed.title.cancel();
                            continue;
                        }
                        committed.title.commit();
                        if let Some(title) = committed.initial_title {
                            committed.title.set(Some(title));
                        }
                        active_generation = Some(generation);
                        active_cancel = Some(committed.title.cancelled.clone());
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
    let failure_source = source.clone();
    let completion_tx = tx.clone();
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
            let _ = completion_tx.send(WorkerMsg::Opened {
                source,
                generation,
                result,
                output,
            });
        });
    if let Err(error) = spawned {
        let why = format!("could not start source opener: {error}");
        tracing::warn!("audio: {why}");
        let _ = tx.send(WorkerMsg::Opened {
            source: failure_source,
            generation,
            result: Err(anyhow::Error::msg(why)),
            output: None,
        });
    }
}

fn fail_current(
    state: &Arc<Mutex<AudioState>>,
    control: &Control,
    source: &str,
    generation: u64,
    why: String,
) {
    control.fail_current(state, source, generation, why);
}

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
