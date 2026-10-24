//! Playback output, feeder pacing, and the callback-safe sample ring.

use std::path::Path;
use std::sync::Arc;
use std::sync::atomic::{AtomicBool, AtomicU32, AtomicUsize, Ordering};
use std::sync::mpsc::sync_channel;

use anyhow::{Context, Result, anyhow};
use rodio::cpal::traits::{DeviceTrait, HostTrait};
use rodio::{Source, cpal};

use super::REOPEN_PAUSE;
use super::health::OutputHealth;
use super::output_source::OutputSource;
use super::queue::{self, CHUNK, Producer, RING};
use super::ring::{FinishedOnDrop, Ring, enqueue_chunk};
use super::station::{
    AudioFormat, Playlist, TitleSink, local_title, next_playlist_source, open_file, open_source,
};

const PREFILL_SECS: usize = 1;

/// ALSA PCM devices for sound servers, in preference order.
/// cpal uses ALSA on Linux. These PCM plugins connect it to sound servers such as PipeWire.
pub(crate) const SERVER_PCMS: [&str; 3] = ["pipewire", "pulse", "default"];

/// ALSA's `null` PCM accepts and discards every sample, so selecting it looks like playback
/// with no sound. Never use it as a fallback.
pub(crate) const NULL_PCM: &str = "null";

/// Interface between command handling and rodio.
/// Tests use a simulated mixer to check source generations. Production delegates to the rodio player.
pub(crate) trait AudioOutput: Send + Sync {
    fn append(&self, source: Box<dyn Source + Send>);
    fn set_volume(&self, volume: f32);
    fn play(&self);
    fn source_underruns(&self) -> Arc<AtomicUsize> {
        Arc::new(AtomicUsize::new(0))
    }
    fn report_underruns(&self) {}
    /// Backend failures require a fresh device; ordinary recovered xruns do not.
    fn failed(&self) -> bool {
        false
    }
}

pub(crate) trait OutputFactory: Send + Sync {
    fn open(&self) -> Result<Box<dyn AudioOutput>>;
}

pub(crate) struct RodioOutputFactory;

impl OutputFactory for RodioOutputFactory {
    fn open(&self) -> Result<Box<dyn AudioOutput>> {
        let health = Arc::new(OutputHealth::new());
        let sink = open_device_with_health(&health)?;
        Ok(Box::new(RodioOutput {
            _sink: sink,
            volume: Arc::new(AtomicU32::new(1.0f32.to_bits())),
            health,
        }))
    }
}

struct RodioOutput {
    _sink: rodio::MixerDeviceSink,
    volume: Arc<AtomicU32>,
    health: Arc<OutputHealth>,
}

impl AudioOutput for RodioOutput {
    fn source_underruns(&self) -> Arc<AtomicUsize> {
        self.health.source_underruns.clone()
    }

    fn report_underruns(&self) {
        self.health.report();
    }

    fn failed(&self) -> bool {
        self.health.failed.load(Ordering::Acquire)
    }

    fn append(&self, source: Box<dyn Source + Send>) {
        self._sink
            .mixer()
            .add(OutputSource::new(source, self.volume.clone()));
    }

    fn set_volume(&self, volume: f32) {
        self.volume.store(volume.to_bits(), Ordering::Relaxed);
    }

    fn play(&self) {}
}

/// Return the ALSA PCM ID, such as `pipewire`, `pulse`, `default`, or `null`.
/// Match devices by ID instead of display names such as PipeWire Sound Server.
pub(crate) fn pcm_id(device: &cpal::Device) -> String {
    device
        .id()
        .map(|cpal::DeviceId(_, id)| id)
        .unwrap_or_default()
}

/// Prefer named server PCMs because `open_default_sink` can select `null`.
/// Reject `null` because it discards audio.
#[cfg(test)]
pub(crate) fn open_device() -> Result<rodio::MixerDeviceSink> {
    open_device_with_health(&Arc::new(OutputHealth::new()))
}

fn open_device_with_health(health: &Arc<OutputHealth>) -> Result<rodio::MixerDeviceSink> {
    let host = cpal::default_host();

    let named: Vec<(String, cpal::Device)> = host
        .output_devices()
        .map(|ds| ds.map(|d| (pcm_id(&d), d)).collect())
        .unwrap_or_default();

    let preferred = SERVER_PCMS
        .into_iter()
        .filter_map(|want| named.iter().find(|(id, _)| id == want).cloned());
    let (id, sink) = open_first_available(
        preferred,
        || {
            host.default_output_device()
                .map(|device| (pcm_id(&device), device))
        },
        |device| {
            let health = health.clone();
            rodio::DeviceSinkBuilder::from_device(device)
                .and_then(|b| {
                    b.with_error_callback(move |error| {
                        health.record(&error);
                    })
                    .open_stream()
                })
                .map_err(anyhow::Error::from)
        },
    )?;

    tracing::info!("audio: output device {id:?}");
    Ok(sink)
}

/// Enumeration only identifies candidates; a device is usable only after its stream opens.
/// Try the default last, excluding null and already attempted PCM IDs.
fn open_first_available<D, O>(
    preferred: impl IntoIterator<Item = (String, D)>,
    default: impl FnOnce() -> Option<(String, D)>,
    mut open: impl FnMut(D) -> Result<O>,
) -> Result<(String, O)> {
    let mut attempted = Vec::new();
    let mut failures = Vec::new();
    for (id, device) in preferred
        .into_iter()
        .chain(std::iter::once_with(default).flatten())
    {
        if id == NULL_PCM || attempted.contains(&id) {
            continue;
        }
        attempted.push(id.clone());
        match open(device) {
            Ok(output) => return Ok((id, output)),
            Err(error) => failures.push(format!("{id:?}: {error:#}")),
        }
    }
    if failures.is_empty() {
        return Err(anyhow!(
            "no usable output device (excluding ALSA's null sink)"
        ));
    }
    Err(anyhow!("opening output devices: {}", failures.join("; ")))
}

/// The result of source opening and decoder probing. It contains no output-side effects: the
/// command worker can discard it after a replacement or stop without ever appending stale audio.
pub(crate) struct OpenedSource {
    pub(crate) source: String,
    pub(crate) first: Box<dyn Source + Send>,
    pub(crate) playlist: Option<Playlist>,
    pub(crate) format: AudioFormat,
    pub(crate) title: TitleSink,
}

pub(crate) struct CommittedSource {
    pub(crate) title: TitleSink,
}

/// Opens the source and probes its decoder away from the command worker. The title sink remains
/// pending until `commit` has appended the corresponding ring.
#[expect(
    clippy::expect_used,
    reason = "Playlist::from_dir rejects empty directories before the first track is selected"
)]
pub(crate) fn open(source: &str, title: TitleSink) -> Result<OpenedSource> {
    let mut playlist = if Path::new(source).is_dir() {
        Some(Playlist::from_dir(Path::new(source))?)
    } else {
        None
    };
    let first_path = playlist.as_mut().map(|p| {
        p.next()
            .expect("a directory playlist is non-empty by construction")
    });
    if let Some(initial_title) = first_path.as_ref().and_then(|path| local_title(path)) {
        // Queue the first track before the feeder can advance. Activation publishes the most
        // recent pending title, so a fast playlist cannot be overwritten by this initial one.
        title.set(Some(initial_title));
    }
    let first = match first_path.as_ref() {
        Some(path) => open_file(path)?,
        None => open_source(source, &title)?,
    };
    let format = AudioFormat {
        channels: first.channels(),
        rate: first.sample_rate(),
    };

    Ok(OpenedSource {
        source: source.to_string(),
        first,
        playlist,
        format,
        title,
    })
}

/// Commits an opened source to the output only while its generation is still current. The ring
/// carries the same generation, so a concurrent replacement also retires it at the callback.
pub(crate) fn commit(
    opened: OpenedSource,
    output: &dyn AudioOutput,
    volume: f32,
) -> Result<Option<CommittedSource>> {
    commit_with_spawn(opened, output, volume, |feed| {
        std::thread::Builder::new()
            .name("slopd audio feed".into())
            .spawn(feed)
            .map(|_| ())
    })
}

// Startup is fallible; output publication is deferred until a feeder exists. The gate prevents
// decoding or title updates before append, and drops the prepared feeder on stale work or unwind.
fn commit_with_spawn(
    opened: OpenedSource,
    output: &dyn AudioOutput,
    volume: f32,
    spawn: impl FnOnce(Box<dyn FnOnce() + Send>) -> std::io::Result<()>,
) -> Result<Option<CommittedSource>> {
    let OpenedSource {
        source,
        first,
        playlist,
        format,
        title,
    } = opened;
    let generation = title.generation;
    if generation != title.current_generation.load(Ordering::SeqCst) {
        return Ok(None);
    }

    let (tx, rx) = queue::channel(RING);
    let ready = Arc::new(AtomicBool::new(false));
    let queued = Arc::new(AtomicUsize::new(0));
    let prefill_samples =
        format.rate.get() as usize * format.channels.get() as usize * PREFILL_SECS;
    let finished = Arc::new(AtomicBool::new(false));
    let ring = Ring {
        rx,
        held: [0.0; CHUNK],
        held_len: 0,
        underruns: output.source_underruns(),
        at: 0,
        channels: format.channels,
        rate: format.rate,
        generation,
        current_generation: title.current_generation.clone(),
        cancelled: title.cancelled.clone(),
        ready: ready.clone(),
        finished: finished.clone(),
        queued: queued.clone(),
        prefill_samples,
    };
    let feed_title = title.clone();
    let (start_tx, start_rx) = sync_channel(1);
    spawn(Box::new(move || {
        let _finished_on_exit = FinishedOnDrop(finished);
        if start_rx.recv().is_ok() {
            feed(FeedArgs {
                first,
                source,
                playlist,
                format,
                tx,
                generation,
                title: feed_title,
                ready,
                queued,
            });
        }
    }))
    .context("starting the feeder")?;

    // Replacement while starting the feeder discards both the ring and the gated feeder.
    if generation != title.current_generation.load(Ordering::SeqCst)
        || title.cancelled.load(Ordering::Acquire)
    {
        return Ok(None);
    }
    output.set_volume(volume);
    output.append(Box::new(ring));
    output.play();
    // Replacement during output I/O is handled by the ring's generation check; never start
    // decoding a candidate that has already been retired.
    if generation != title.current_generation.load(Ordering::SeqCst)
        || title.cancelled.load(Ordering::Acquire)
    {
        return Ok(None);
    }
    start_tx
        .send(())
        .context("feeder exited before output commit")?;
    Ok(Some(CommittedSource { title }))
}

// The feeder owns its inputs so the audio thread can run independently of the caller.
struct FeedArgs {
    first: Box<dyn Source + Send>,
    source: String,
    playlist: Option<Playlist>,
    format: AudioFormat,
    tx: Producer,
    generation: u64,
    title: TitleSink,
    ready: Arc<AtomicBool>,
    queued: Arc<AtomicUsize>,
}

/// Decode audio into the ring buffer until the source ends.
/// Repeat files, advance through randomized directory playlists, and reconnect interrupted stations.
/// Retry until a new run replaces this run or the ring consumer closes.
fn feed(args: FeedArgs) {
    feed_with_reopen(args, |source, title| {
        super::wait_for_retry(
            REOPEN_PAUSE,
            title.generation,
            &title.current_generation,
            Some(&title.cancelled),
        )
        .then(|| open_source(source, title))
    });
}

// Keep retry timing and source opening injectable while testing the real feeder loop.
#[expect(
    clippy::expect_used,
    reason = "the playlist branch is entered only when a playlist is present"
)]
#[expect(
    clippy::indexing_slicing,
    reason = "len advances to CHUNK then resets; the final tail is bounded by CHUNK"
)]
fn feed_with_reopen(
    args: FeedArgs,
    mut reopen: impl FnMut(&str, &TitleSink) -> Option<Result<Box<dyn Source + Send>>>,
) {
    let FeedArgs {
        first,
        source,
        mut playlist,
        format,
        mut tx,
        generation,
        title,
        ready,
        queued,
    } = args;
    let prefill_samples =
        format.rate.get() as usize * format.channels.get() as usize * PREFILL_SECS;
    let mut current = Some(first);

    loop {
        if title.cancelled.load(Ordering::Acquire)
            || generation != title.current_generation.load(Ordering::SeqCst)
        {
            return;
        }
        let decoded = match current.take() {
            Some(d) => d,
            None if playlist.is_some() => {
                match next_playlist_source(
                    playlist.as_mut().expect("playlist checked above"),
                    format,
                    &title,
                ) {
                    Some(decoded) => decoded,
                    None => {
                        tracing::warn!("audio: no playable files remain in {source}");
                        return;
                    }
                }
            }
            None => {
                // Ordinary HTTP body breaks are repaired below the decoder by `Reconnect`.
                // Reaching here means the decoder itself stopped, so do not rebuild it in a
                // tight loop and repeatedly play a relay's connection-opening buffer.
                tracing::warn!("Audio decoder for {source} ended. Rebuilding after a pause.");
                let Some(reopened) = reopen(&source, &title) else {
                    return;
                };
                match reopened {
                    Ok(d) if d.channels() == format.channels && d.sample_rate() == format.rate => d,
                    Ok(_) => {
                        // Keep the ring alive while a station changes encoders or a local file
                        // is replaced. Playing it through the old shape would be wrong, but
                        // dropping the selected source is worse. The next retry may match.
                        tracing::warn!(
                            "Audio source {source} returned in another format. Retrying."
                        );
                        continue;
                    }
                    Err(e) => {
                        tracing::debug!("audio: reopening {source}: {e:#}");
                        continue;
                    }
                }
            }
        };

        let mut chunk = [0.0; CHUNK];
        let mut len = 0;
        for sample in decoded {
            if title.cancelled.load(Ordering::Acquire)
                || generation != title.current_generation.load(Ordering::SeqCst)
            {
                return;
            }
            chunk[len] = sample;
            len += 1;
            if len < CHUNK {
                continue;
            }
            // Wait when the ring buffer is full to limit downloads to the playback rate.
            if !enqueue_chunk(&mut tx, &chunk, &ready, &queued, prefill_samples, &title) {
                return;
            }
            len = 0;
        }

        // A dropped connection or a short local file can end between chunk boundaries. Keep
        // the tail instead of throwing it away before the reconnect or playlist handoff.
        if len > 0
            && !enqueue_chunk(
                &mut tx,
                &chunk[..len],
                &ready,
                &queued,
                prefill_samples,
                &title,
            )
        {
            return;
        }
    }
}

#[cfg(test)]
#[path = "playback_tests.rs"]
mod tests;
