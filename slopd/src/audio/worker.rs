//! Command orchestration and opener handoffs. Control owns publication; playback owns output I/O.

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::mpsc::{Receiver, RecvTimeoutError, Sender};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

use super::control::{Control, SourceRequirement};
use super::playback::{self, AudioOutput, OutputFactory};
use super::{AudioState, station};

pub(super) enum Cmd {
    Play { source: String, generation: u64 },
    Volume(f32),
    Stop { generation: u64 },
}

pub(super) enum WorkerMsg {
    Command(Cmd),
    Shutdown,
    Opened {
        cancel: Arc<AtomicBool>,
        source: String,
        generation: u64,
        result: anyhow::Result<playback::OpenedSource>,
        output: Option<Box<dyn AudioOutput>>,
    },
}

struct PendingOpen {
    source: String,
    generation: u64,
    cancel: Arc<AtomicBool>,
}

enum PlayDisposition {
    AlreadyPending,
    AlreadyPlaying,
    Open,
}

const HEALTH_POLL: Duration = Duration::from_millis(100);
const STABLE_OUTPUT: Duration = Duration::from_secs(30);
const MAX_RECOVERIES: usize = 3;

struct OutputRetry {
    source: String,
    generation: u64,
    at: Instant,
}

#[derive(Default)]
struct OutputRecovery {
    recoveries: usize,
    recovery_generation: Option<u64>,
    healthy_since: Option<Instant>,
    retry: Option<OutputRetry>,
}

struct AudioWorker {
    state: Arc<Mutex<AudioState>>,
    control: Arc<Control>,
    tx: Sender<WorkerMsg>,
    factory: Arc<dyn OutputFactory>,
    output: Option<Box<dyn AudioOutput>>,
    pending: Option<PendingOpen>,
    active_cancel: Option<Arc<AtomicBool>>,
    active_generation: Option<u64>,
    recovery: OutputRecovery,
}

#[expect(
    clippy::needless_pass_by_value,
    reason = "the worker owns the sole receiver until its loop exits"
)]
pub(super) fn run(
    rx: Receiver<WorkerMsg>,
    state: Arc<Mutex<AudioState>>,
    control: Arc<Control>,
    tx: Sender<WorkerMsg>,
    factory: Arc<dyn OutputFactory>,
) {
    let mut worker = AudioWorker {
        state,
        control,
        tx,
        factory,
        output: None,
        pending: None,
        active_cancel: None,
        active_generation: None,
        recovery: OutputRecovery::default(),
    };

    loop {
        match rx.recv_timeout(HEALTH_POLL) {
            Ok(message) => {
                if !worker.handle(message) {
                    break;
                }
            }
            Err(RecvTimeoutError::Timeout) => {}
            Err(RecvTimeoutError::Disconnected) => break,
        }
        worker.check_output(Instant::now());
    }
}

impl AudioWorker {
    /// Output lifetime is independent of source generation. Retire the broken device even if
    /// stop/replacement has already changed the request; restart only the latest requested source.
    fn check_output(&mut self, now: Instant) {
        if let Some(output) = self.output.as_ref() {
            output.report_underruns();
        }
        if self.output.as_ref().is_some_and(|output| output.failed()) {
            if let Some(cancel) = self.active_cancel.take() {
                cancel.store(true, Ordering::Release);
            }
            if let Some(waiting) = self.pending.take() {
                waiting.cancel.store(true, Ordering::Release);
            }
            self.active_generation = None;
            self.recovery.healthy_since = None;
            drop(self.output.take());
            let (source, generation) = self.control.snapshot();
            if let Some(source) = source {
                self.schedule_recovery(source, generation, now, "audio output failed");
            }
        } else if self
            .recovery
            .healthy_since
            .is_some_and(|since| now.duration_since(since) >= STABLE_OUTPUT)
        {
            self.recovery.recoveries = 0;
        }
        if self
            .recovery
            .retry
            .as_ref()
            .is_some_and(|retry| now >= retry.at)
            && let Some(retry) = self.recovery.retry.take()
            && self.control.is_current(&retry.source, retry.generation)
        {
            self.play(retry.source, retry.generation);
        }
    }

    fn schedule_recovery(&mut self, source: String, generation: u64, now: Instant, why: &str) {
        if !self.control.is_current(&source, generation) {
            return;
        }
        self.reset_recovery_generation(generation);
        if self.recovery.recoveries >= MAX_RECOVERIES {
            self.control.fail_current(
                &self.state,
                &source,
                generation,
                format!("{why}; audio output recovery exhausted"),
            );
            return;
        }
        self.recovery.recoveries += 1;
        self.control.publish_current(
            &self.state,
            generation,
            SourceRequirement::Exact(&source),
            |_, current| {
                current.playing = false;
                current.error = Some(format!("{why}; recovering audio output"));
            },
        );
        tracing::warn!(
            attempt = self.recovery.recoveries,
            "audio: {why}; reopening output"
        );
        self.recovery.retry = Some(OutputRetry {
            source,
            generation,
            at: now + Duration::from_secs(self.recovery.recoveries as u64),
        });
    }

    fn reset_recovery_generation(&mut self, generation: u64) {
        if self.recovery.recovery_generation != Some(generation) {
            self.recovery.recoveries = 0;
            self.recovery.recovery_generation = Some(generation);
        }
    }

    fn handle(&mut self, message: WorkerMsg) -> bool {
        match message {
            WorkerMsg::Shutdown => {
                self.shutdown();
                false
            }
            WorkerMsg::Command(Cmd::Play { source, generation }) => {
                self.play(source, generation);
                true
            }
            WorkerMsg::Command(Cmd::Volume(volume)) => {
                self.set_volume(volume);
                true
            }
            WorkerMsg::Command(Cmd::Stop { generation }) => {
                self.stop(generation);
                true
            }
            WorkerMsg::Opened {
                cancel,
                source,
                generation,
                result,
                output,
            } => {
                self.opened(&source, generation, &cancel, result, output);
                true
            }
        }
    }

    fn shutdown(&mut self) {
        self.control.shutdown();
        if let Some(waiting) = self.pending.take() {
            waiting.cancel.store(true, Ordering::Release);
        }
        if let Some(cancel) = self.active_cancel.take() {
            cancel.store(true, Ordering::Release);
        }
        let mut current = self.state.lock().unwrap_or_else(|p| p.into_inner());
        current.playing = false;
        current.source = None;
        current.title = None;
    }

    fn play(&mut self, source: String, generation: u64) {
        if self
            .recovery
            .retry
            .as_ref()
            .is_some_and(|retry| retry.source == source && retry.generation == generation)
        {
            return;
        }
        if !self.control.is_current(&source, generation) {
            return;
        }
        // A new user request supersedes delayed recovery without reviving the old source.
        self.recovery.retry = None;
        self.reset_recovery_generation(generation);

        let title = station::TitleSink::pending_for(
            Some(self.state.clone()),
            generation,
            self.control.clone(),
        );
        let cancel = title.cancelled.clone();
        let needs_output = self.output.is_none();
        let volume = self.control.volume();
        let pending = &mut self.pending;
        let active_cancel = &mut self.active_cancel;
        let active_generation = self.active_generation;
        let disposition = self.control.publish_current(
            &self.state,
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
                *pending = Some(PendingOpen {
                    source: source.clone(),
                    generation,
                    cancel: cancel.clone(),
                });
                PlayDisposition::Open
            },
        );
        match disposition {
            None | Some(PlayDisposition::AlreadyPending) => {}
            Some(PlayDisposition::AlreadyPlaying) => {
                if let Some(output) = self.output.as_ref() {
                    output.set_volume(volume);
                }
            }
            Some(PlayDisposition::Open) => spawn_open(
                OpenRequest {
                    source,
                    generation,
                    title,
                    needs_output,
                },
                self.factory.clone(),
                &self.tx,
            ),
        }
    }

    fn set_volume(&self, volume: f32) {
        if let Some(output) = self.output.as_ref() {
            output.set_volume(volume);
        }
        self.state.lock().unwrap_or_else(|p| p.into_inner()).volume = volume;
    }

    fn stop(&mut self, generation: u64) {
        let recovery = &mut self.recovery;
        let pending = &mut self.pending;
        let active_cancel = &mut self.active_cancel;
        self.control.publish_current(
            &self.state,
            generation,
            SourceRequirement::None,
            |_, current| {
                recovery.retry = None;
                recovery.recoveries = 0;
                if let Some(waiting) = pending.take() {
                    waiting.cancel.store(true, Ordering::Release);
                }
                if let Some(cancel) = active_cancel.take() {
                    cancel.store(true, Ordering::Release);
                }
                current.playing = false;
                current.source = None;
                current.title = None;
            },
        );
    }

    fn opened(
        &mut self,
        source: &str,
        generation: u64,
        cancel: &Arc<AtomicBool>,
        result: anyhow::Result<playback::OpenedSource>,
        opened_output: Option<Box<dyn AudioOutput>>,
    ) {
        let matches = self.pending.as_ref().is_some_and(|waiting| {
            waiting.generation == generation
                && waiting.source == source
                && Arc::ptr_eq(&waiting.cancel, cancel)
        });
        if !matches {
            drop(result);
            drop(opened_output);
            return;
        }
        self.pending = None;

        if !self.control.is_current(source, generation) {
            drop(result);
            drop(opened_output);
            return;
        }

        if let Some(opened_output) = opened_output {
            self.output = Some(opened_output);
            self.recovery.healthy_since = Some(Instant::now());
        }
        let opened = match result {
            Ok(opened) => opened,
            Err(error) => {
                if self.recovery.recoveries > 0 && self.output.is_none() {
                    self.schedule_recovery(
                        source.to_owned(),
                        generation,
                        Instant::now(),
                        &format!("{error:#}"),
                    );
                    return;
                }
                self.control.fail_current(
                    &self.state,
                    source,
                    generation,
                    format!("{source}: {error:#}"),
                );
                return;
            }
        };
        let Some(output) = self.output.as_ref() else {
            if self.recovery.recoveries > 0 {
                self.schedule_recovery(
                    source.to_owned(),
                    generation,
                    Instant::now(),
                    "audio output unavailable",
                );
                return;
            }
            self.control.fail_current(
                &self.state,
                source,
                generation,
                "audio output unavailable".to_string(),
            );
            return;
        };
        match playback::commit(opened, output.as_ref(), self.control.volume()) {
            Ok(Some(committed)) => {
                let cancel = committed.title.cancelled.clone();
                let volume = self.control.volume();
                let published = self.control.publish_current(
                    &self.state,
                    generation,
                    SourceRequirement::Exact(source),
                    |request, current| {
                        request.cancel = Some(cancel.clone());
                        current.playing = true;
                        current.source = Some(source.to_owned());
                        current.volume = volume;
                        current.error = None;
                    },
                );
                if published.is_none() {
                    committed.title.cancel();
                    return;
                }
                committed.title.commit();
                self.active_generation = Some(generation);
                self.active_cancel = Some(committed.title.cancelled);
                tracing::info!("audio: playing {}", source);
            }
            Ok(None) => {}
            Err(error) => self.control.fail_current(
                &self.state,
                source,
                generation,
                format!("{source}: {error:#}"),
            ),
        }
    }
}

struct OpenRequest {
    source: String,
    generation: u64,
    title: station::TitleSink,
    needs_output: bool,
}

fn spawn_open(request: OpenRequest, factory: Arc<dyn OutputFactory>, tx: &Sender<WorkerMsg>) {
    spawn_open_with(request, factory, tx, |open| {
        std::thread::Builder::new()
            .name("slopd audio open".into())
            .spawn(open)
            .map(|_| ())
    });
}

fn spawn_open_with(
    request: OpenRequest,
    factory: Arc<dyn OutputFactory>,
    tx: &Sender<WorkerMsg>,
    spawn: impl FnOnce(Box<dyn FnOnce() + Send>) -> std::io::Result<()>,
) {
    let OpenRequest {
        source,
        generation,
        title,
        needs_output,
    } = request;
    let thread_source = source.clone();
    let failure_source = source.clone();
    let cancel = title.cancelled.clone();
    let failure_cancel = cancel.clone();
    let completion_tx = tx.clone();
    let spawned = spawn(Box::new(move || {
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
        drop(completion_tx.send(WorkerMsg::Opened {
            cancel,
            source,
            generation,
            result,
            output,
        }));
    }));
    if let Err(error) = spawned {
        let why = format!("could not start source opener: {error}");
        tracing::warn!("audio: {why}");
        drop(tx.send(WorkerMsg::Opened {
            cancel: failure_cancel,
            source: failure_source,
            generation,
            result: Err(anyhow::Error::msg(why)),
            output: None,
        }));
    }
}

#[cfg(test)]
#[path = "worker_tests.rs"]
mod tests;
