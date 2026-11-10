//! Command orchestration and opener handoffs. Control owns publication; playback owns output I/O.

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::mpsc::{Receiver, RecvTimeoutError, Sender};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

use super::control::{Control, PlayDisposition};
use super::playback::{self, AudioOutput, OutputFactory};
use super::{AudioState, station};
use crate::config::AudioOutputSettings;

pub(super) enum Cmd {
    Play { source: String, generation: u64 },
    Volume(f32),
    Configure(AudioOutputSettings),
    Stop { generation: u64 },
}

pub(super) enum WorkerMsg {
    Command(Cmd),
    Shutdown,
    Barrier(tokio::sync::oneshot::Sender<()>),
    Opened {
        cancel: Arc<AtomicBool>,
        source: String,
        generation: u64,
        result: anyhow::Result<playback::OpenedSource>,
        output: Option<Box<dyn AudioOutput>>,
    },
}

/// Identity and cancellation travel together from opening through committed playback.
struct SourceRun {
    source: String,
    generation: u64,
    cancel: Arc<AtomicBool>,
}

#[derive(Default)]
enum SourcePhase {
    #[default]
    Idle,
    Opening(SourceRun),
    Playing(SourceRun),
    Backoff(OutputRetry),
}

impl SourcePhase {
    fn retire(&mut self) {
        if let Self::Opening(run) | Self::Playing(run) = std::mem::take(self) {
            run.cancel.store(true, Ordering::Release);
        }
    }

    fn opening(&self) -> Option<&SourceRun> {
        match self {
            Self::Opening(run) => Some(run),
            _ => None,
        }
    }

    fn retry(&self) -> Option<&OutputRetry> {
        match self {
            Self::Backoff(retry) => Some(retry),
            _ => None,
        }
    }

    fn take_retry(&mut self) -> Option<OutputRetry> {
        if self.retry().is_some()
            && let Self::Backoff(retry) = std::mem::take(self)
        {
            return Some(retry);
        }
        None
    }

    fn disposition(&self, source: &str, generation: u64) -> PlayDisposition {
        match self {
            Self::Opening(run) if run.source == source && run.generation == generation => {
                PlayDisposition::AlreadyPending
            }
            Self::Playing(run) if run.source == source && run.generation == generation => {
                PlayDisposition::AlreadyPlaying
            }
            _ => PlayDisposition::Open,
        }
    }
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
    failed_devices: Vec<String>,
}

struct AudioWorker {
    state: Arc<Mutex<AudioState>>,
    control: Arc<Control>,
    tx: Sender<WorkerMsg>,
    factory: Arc<dyn OutputFactory>,
    output: Option<Box<dyn AudioOutput>>,
    output_format: Option<station::AudioFormat>,
    source: SourcePhase,
    recovery: OutputRecovery,
    settings: AudioOutputSettings,
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
        output_format: None,
        source: SourcePhase::Idle,
        recovery: OutputRecovery::default(),
        settings: AudioOutputSettings::default(),
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
            self.reset_recovery_generation(self.control.snapshot().1);
            self.source.retire();
            self.recovery.healthy_since = None;
            if let Some(id) = self.output.as_ref().and_then(|output| output.device_id())
                && !self.recovery.failed_devices.contains(&id)
            {
                self.recovery.failed_devices.push(id);
            }
            drop(self.output.take());
            self.output_format = None;
            self.state
                .lock()
                .unwrap_or_else(|p| p.into_inner())
                .output_device = None;
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
        if self.source.retry().is_some_and(|retry| now >= retry.at)
            && let Some(retry) = self.source.take_retry()
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
        self.control
            .publish_recovery(&self.state, &source, generation);
        tracing::warn!(
            attempt = self.recovery.recoveries,
            "audio: {why}; reopening output"
        );
        self.source = SourcePhase::Backoff(OutputRetry {
            source,
            generation,
            at: now + Duration::from_secs(self.recovery.recoveries as u64),
        });
    }

    fn reset_recovery_generation(&mut self, generation: u64) {
        if self.recovery.recovery_generation != Some(generation) {
            self.recovery.recoveries = 0;
            self.recovery.recovery_generation = Some(generation);
            self.recovery.failed_devices.clear();
        }
    }

    fn handle(&mut self, message: WorkerMsg) -> bool {
        match message {
            WorkerMsg::Barrier(done) => {
                // Cancellation of the caller does not undo an already applied stop.
                done.send(()).unwrap_or(());
                true
            }
            WorkerMsg::Shutdown => {
                self.shutdown();
                false
            }
            WorkerMsg::Command(Cmd::Play { source, generation }) => {
                self.play(source, generation);
                true
            }
            WorkerMsg::Command(Cmd::Configure(settings)) => {
                self.configure(settings);
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
        self.source.retire();
        let mut current = self.state.lock().unwrap_or_else(|p| p.into_inner());
        current.playing = false;
        current.source = None;
        current.title = None;
    }

    fn play(&mut self, source: String, generation: u64) {
        if self
            .source
            .retry()
            .is_some_and(|retry| retry.source == source && retry.generation == generation)
        {
            return;
        }
        if !self.control.is_current(&source, generation) {
            return;
        }
        // A new user request supersedes delayed recovery without reviving the old source.
        self.source.take_retry();
        self.reset_recovery_generation(generation);

        let title = station::TitleSink::pending_for(
            Some(self.state.clone()),
            generation,
            self.control.clone(),
        );
        let cancel = title.cancelled.clone();
        let volume = self.control.volume();
        let disposition = self.control.prepare_play(
            &self.state,
            &source,
            generation,
            cancel.clone(),
            self.source.disposition(&source, generation),
        );
        match disposition {
            None | Some(PlayDisposition::AlreadyPending) => {}
            Some(PlayDisposition::AlreadyPlaying) => {
                if let Some(output) = self.output.as_ref() {
                    output.set_volume(volume);
                }
            }
            Some(PlayDisposition::Open) => {
                // Only worker-owned resources are retired here. A concurrent request has
                // already cancelled this candidate through Control's registered handle.
                self.source.retire();
                self.source = SourcePhase::Opening(SourceRun {
                    source: source.clone(),
                    generation,
                    cancel,
                });
                self.recovery.healthy_since = None;
                self.state
                    .lock()
                    .unwrap_or_else(|p| p.into_inner())
                    .output_device = None;
                spawn_open(
                    OpenRequest {
                        source,
                        generation,
                        title,
                        output: self.output.take(),
                        output_format: self.output_format.take(),
                        settings: self.settings.clone(),
                        excluded: self.recovery.failed_devices.clone(),
                    },
                    self.factory.clone(),
                    &self.tx,
                );
            }
        }
    }

    fn configure(&mut self, settings: AudioOutputSettings) {
        if self.settings == settings {
            return;
        }
        self.settings = settings;
        // Retiring the opening run also invalidates its cancellation identity. Its delayed
        // completion cannot restore the old device, even when source generation is unchanged.
        self.source.retire();
        drop(self.output.take());
        self.output_format = None;
        self.state
            .lock()
            .unwrap_or_else(|p| p.into_inner())
            .output_device = None;
        self.recovery = OutputRecovery::default();
        let (source, generation) = self.control.snapshot();
        if let Some(source) = source {
            self.play(source, generation);
        }
    }

    fn set_volume(&self, volume: f32) {
        if let Some(output) = self.output.as_ref() {
            output.set_volume(volume);
        }
        self.state.lock().unwrap_or_else(|p| p.into_inner()).volume = volume;
    }

    fn stop(&mut self, generation: u64) {
        // Retire output before publishing the stopped state used for local handoff.
        if self.control.snapshot().1 == generation {
            self.source.retire();
            self.control.publish_stopped(&self.state, generation);
            self.recovery.recoveries = 0;
        }
    }

    fn opened(
        &mut self,
        source: &str,
        generation: u64,
        cancel: &Arc<AtomicBool>,
        result: anyhow::Result<playback::OpenedSource>,
        opened_output: Option<Box<dyn AudioOutput>>,
    ) {
        let matches = self.source.opening().is_some_and(|waiting| {
            waiting.generation == generation
                && waiting.source == source
                && Arc::ptr_eq(&waiting.cancel, cancel)
        });
        if !matches {
            drop(result);
            drop(opened_output);
            return;
        }
        self.source = SourcePhase::Idle;

        if !self.control.is_current(source, generation) {
            drop(result);
            drop(opened_output);
            return;
        }

        if let Some(opened_output) = opened_output {
            self.output_format = result.as_ref().ok().map(|opened| opened.format);
            self.state
                .lock()
                .unwrap_or_else(|p| p.into_inner())
                .output_device = opened_output.device_id();
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
                let published =
                    self.control
                        .publish_playing(&self.state, source, generation, cancel.clone());
                if !published {
                    committed.title.cancel();
                    return;
                }
                committed.title.commit();
                self.source = SourcePhase::Playing(SourceRun {
                    source: source.to_owned(),
                    generation,
                    cancel,
                });
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
    output: Option<Box<dyn AudioOutput>>,
    output_format: Option<station::AudioFormat>,
    settings: AudioOutputSettings,
    excluded: Vec<String>,
}

/// Drop an incompatible output before opening another: physical PCMs may be exclusive.
/// The opener owns this handoff, and stale completions are discarded by cancellation identity.
fn reuse_or_open_output(
    output: Option<Box<dyn AudioOutput>>,
    previous_format: Option<station::AudioFormat>,
    format: station::AudioFormat,
    open: impl FnOnce() -> anyhow::Result<Box<dyn AudioOutput>>,
) -> anyhow::Result<Box<dyn AudioOutput>> {
    if let Some(output) = output {
        if previous_format == Some(format) && !output.failed() {
            return Ok(output);
        }
        drop(output);
    }
    open()
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
        output,
        output_format,
        settings,
        excluded,
    } = request;
    let thread_source = source.clone();
    let failure_source = source.clone();
    let cancel = title.cancelled.clone();
    let failure_cancel = cancel.clone();
    let completion_tx = tx.clone();
    let spawned = spawn(Box::new(move || {
        let (result, output) = match playback::open(&thread_source, title) {
            Ok(opened) => {
                let output = reuse_or_open_output(output, output_format, opened.format, || {
                    factory.open(&settings, &excluded, opened.format)
                });
                match output {
                    Ok(output) => (Ok(opened), Some(output)),
                    Err(error) => (Err(error), None),
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
