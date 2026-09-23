//! Playback output, feeder pacing, and the callback-safe sample ring.

use std::path::Path;
use std::sync::atomic::{AtomicBool, AtomicUsize, Ordering};
use std::sync::mpsc::{sync_channel, Receiver, SyncSender, TryRecvError, TrySendError};
use std::sync::Arc;
use std::time::Duration;

use anyhow::{anyhow, Context, Result};
use rodio::cpal::traits::{DeviceTrait, HostTrait};
use rodio::{cpal, ChannelCount, Sample, SampleRate, Source};

use super::station::{
    local_title, next_playlist_source, open_file, open_source, AudioFormat, Playlist, TitleSink,
};
use super::{GENERATION, QUEUE_POLL, REOPEN_PAUSE};

const CHUNK: usize = 4096;
pub(crate) const RING: usize = 256;
const PREFILL_SECS: usize = 1;
const SPAN: usize = 8192;

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
}

pub(crate) trait OutputFactory: Send + Sync {
    fn open(&self) -> Result<Box<dyn AudioOutput>>;
}

pub(crate) struct RodioOutputFactory;

impl OutputFactory for RodioOutputFactory {
    fn open(&self) -> Result<Box<dyn AudioOutput>> {
        let (sink, player) = open_device()?;
        Ok(Box::new(RodioOutput {
            _sink: sink,
            player,
        }))
    }
}

struct RodioOutput {
    _sink: rodio::MixerDeviceSink,
    player: rodio::Player,
}

impl AudioOutput for RodioOutput {
    fn append(&self, source: Box<dyn Source + Send>) {
        self.player.append(source);
    }

    fn set_volume(&self, volume: f32) {
        self.player.set_volume(volume);
    }

    fn play(&self) {
        self.player.play();
    }
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
pub(crate) fn open_device() -> Result<(rodio::MixerDeviceSink, rodio::Player)> {
    let host = cpal::default_host();

    let named: Vec<(String, cpal::Device)> = host
        .output_devices()
        .map(|ds| ds.map(|d| (pcm_id(&d), d)).collect())
        .unwrap_or_default();

    let mut chosen = None;
    for want in SERVER_PCMS {
        if let Some((id, device)) = named.iter().find(|(id, _)| id == want) {
            chosen = Some((id.clone(), device.clone()));
            break;
        }
    }

    // If no named server PCM is available, try the default ALSA device.
    // Still reject the null device.
    let (id, device) = match chosen {
        Some(found) => found,
        None => {
            let device = host
                .default_output_device()
                .ok_or_else(|| anyhow!("no output device at all"))?;
            let id = pcm_id(&device);
            if id == NULL_PCM {
                return Err(anyhow!("the only output device is ALSA's null sink"));
            }
            (id, device)
        }
    };

    let sink = rodio::DeviceSinkBuilder::from_device(device)
        .and_then(|b| b.open_stream())
        .map_err(|e| anyhow!("{e}"))
        .with_context(|| format!("opening output device {id:?}"))?;

    tracing::info!("audio: output device {id:?}");
    let player = rodio::Player::connect_new(sink.mixer());
    Ok((sink, player))
}

/// The result of source opening and decoder probing. It contains no output-side effects: the
/// command worker can discard it after a replacement or stop without ever appending stale audio.
pub(crate) struct OpenedSource {
    pub(crate) source: String,
    pub(crate) first: Box<dyn Source + Send>,
    pub(crate) playlist: Option<Playlist>,
    pub(crate) format: AudioFormat,
    pub(crate) title: TitleSink,
    pub(crate) initial_title: Option<String>,
}

pub(crate) struct CommittedSource {
    pub(crate) title: TitleSink,
    pub(crate) initial_title: Option<String>,
}

/// Opens the source and probes its decoder away from the command worker. The title sink remains
/// pending until `commit` has appended the corresponding ring.
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
    let initial_title = first_path.as_ref().and_then(|path| local_title(path));
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
        initial_title,
    })
}

/// Commits an opened source to the output only while its generation is still current. The ring
/// carries the same generation, so a concurrent replacement also retires it at the callback.
pub(crate) fn commit(
    opened: OpenedSource,
    output: &dyn AudioOutput,
    volume: f32,
) -> Result<Option<CommittedSource>> {
    let OpenedSource {
        source,
        first,
        playlist,
        format,
        title,
        initial_title,
    } = opened;
    let generation = title.generation;
    if generation != GENERATION.load(Ordering::SeqCst) {
        return Ok(None);
    }

    let (tx, rx) = sync_channel::<Vec<Sample>>(RING);
    let ready = Arc::new(AtomicBool::new(false));
    let queued = Arc::new(AtomicUsize::new(0));
    let prefill_samples =
        format.rate.get() as usize * format.channels.get() as usize * PREFILL_SECS;
    output.set_volume(volume);
    output.append(Box::new(Ring {
        rx,
        held: Vec::new(),
        at: 0,
        channels: format.channels,
        rate: format.rate,
        generation,
        ready: ready.clone(),
        queued: queued.clone(),
        prefill_samples,
    }));

    let feed_title = title.clone();
    std::thread::Builder::new()
        .name("slopd audio feed".into())
        .spawn(move || {
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
            })
        })
        .context("starting the feeder")?;

    output.play();
    Ok(Some(CommittedSource {
        title,
        initial_title,
    }))
}

// The feeder owns its inputs so the audio thread can run independently of the caller.
struct FeedArgs {
    first: Box<dyn Source + Send>,
    source: String,
    playlist: Option<Playlist>,
    format: AudioFormat,
    tx: SyncSender<Vec<Sample>>,
    generation: u64,
    title: TitleSink,
    ready: Arc<AtomicBool>,
    queued: Arc<AtomicUsize>,
}

/// Decode audio into the ring buffer until the source ends.
/// Repeat files, advance through randomized directory playlists, and reconnect interrupted stations.
/// Retry until a new run replaces this run or the ring consumer closes.
fn feed(args: FeedArgs) {
    let FeedArgs {
        first,
        source,
        mut playlist,
        format,
        tx,
        generation,
        title,
        ready,
        queued,
    } = args;
    let _ready_on_exit = ReadyOnDrop(ready.clone());
    let prefill_samples =
        format.rate.get() as usize * format.channels.get() as usize * PREFILL_SECS;
    let mut current = Some(first);

    loop {
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
                if !super::wait_for_retry(REOPEN_PAUSE, generation, &GENERATION) {
                    return;
                }
                match open_source(&source, &title) {
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

        let mut chunk = Vec::with_capacity(CHUNK);
        for sample in decoded {
            if generation != GENERATION.load(Ordering::SeqCst) {
                return;
            }
            chunk.push(sample);
            if chunk.len() < CHUNK {
                continue;
            }
            // Wait when the ring buffer is full to limit downloads to the playback rate.
            let full = std::mem::replace(&mut chunk, Vec::with_capacity(CHUNK));
            if !enqueue_chunk(&tx, full, &ready, &queued, prefill_samples, generation) {
                return;
            }
        }

        // A dropped connection or a short local file can end between chunk boundaries. Keep
        // the tail instead of throwing it away before the reconnect or playlist handoff.
        if !chunk.is_empty()
            && !enqueue_chunk(&tx, chunk, &ready, &queued, prefill_samples, generation)
        {
            return;
        }
    }
}

pub(crate) fn enqueue_chunk(
    tx: &SyncSender<Vec<Sample>>,
    chunk: Vec<Sample>,
    ready: &AtomicBool,
    queued: &AtomicUsize,
    prefill_samples: usize,
    generation: u64,
) -> bool {
    let len = chunk.len();
    let mut chunk = chunk;
    loop {
        // Publish the count before the chunk: an active consumer may receive immediately after a
        // successful send. Failed sends roll it back before retrying.
        queued.fetch_add(len, Ordering::AcqRel);
        match tx.try_send(chunk) {
            Ok(()) => break,
            Err(TrySendError::Disconnected(_)) => {
                queued.fetch_sub(len, Ordering::AcqRel);
                return false;
            }
            Err(TrySendError::Full(returned)) => {
                queued.fetch_sub(len, Ordering::AcqRel);
                if generation != GENERATION.load(Ordering::SeqCst) {
                    return false;
                }
                chunk = returned;
                std::thread::sleep(QUEUE_POLL);
            }
        }
    }

    if !ready.load(Ordering::Relaxed) && queued.load(Ordering::Acquire) >= prefill_samples {
        ready.store(true, Ordering::Release);
    }
    true
}

struct ReadyOnDrop(Arc<AtomicBool>);

impl Drop for ReadyOnDrop {
    fn drop(&mut self) {
        self.0.store(true, Ordering::Release);
    }
}

/// Supply decoded samples to the audio callback.
/// Return silence when the ring buffer is empty so network delays do not block the callback.
pub(crate) struct Ring {
    pub(crate) rx: Receiver<Vec<Sample>>,
    pub(crate) held: Vec<Sample>,
    pub(crate) at: usize,
    pub(crate) channels: ChannelCount,
    pub(crate) rate: SampleRate,
    /// The source generation for this run.
    /// A generation change ends this source so the player advances to the next source.
    pub(crate) generation: u64,
    /// Playback waits here while the feeder builds a small amount of live headroom.
    pub(crate) ready: Arc<AtomicBool>,
    /// Samples still in the channel, excluding `held`. Used only at chunk boundaries so the audio
    /// callback does not perform an atomic operation per sample.
    pub(crate) queued: Arc<AtomicUsize>,
    pub(crate) prefill_samples: usize,
}

impl Iterator for Ring {
    type Item = Sample;

    fn next(&mut self) -> Option<Sample> {
        // Superseded, or stopped. This is the retirement `Player::clear` could not do:
        // clear pauses the player and blocks on a source that never ends.
        if self.generation != GENERATION.load(Ordering::SeqCst) {
            return None;
        }
        if !self.ready.load(Ordering::Acquire) {
            return Some(0.0);
        }
        if self.at >= self.held.len() {
            match self.rx.try_recv() {
                Ok(next) => {
                    let len = next.len();
                    // Direct test senders do not publish a count. Production always does. Keep
                    // the accounting saturating without adding work to every sample callback.
                    let _ = self
                        .queued
                        .fetch_update(Ordering::AcqRel, Ordering::Acquire, |n| {
                            Some(n.saturating_sub(len))
                        });
                    self.held = next;
                    self.at = 0;
                }
                // The buffer is empty, but the feeder remains active.
                // Do not return None because that would end playback.
                Err(TryRecvError::Empty) => {
                    // Restore the initial buffer level before resuming playback.
                    // Otherwise, a long reconnection can cause repeated buffer underruns.
                    self.ready.store(false, Ordering::Release);
                    if self.queued.load(Ordering::Acquire) >= self.prefill_samples {
                        self.ready.store(true, Ordering::Release);
                    }
                    return Some(0.0);
                }
                Err(TryRecvError::Disconnected) => return None,
            }
        }
        let sample = self.held[self.at];
        self.at += 1;
        Some(sample)
    }
}

impl Source for Ring {
    /// Return a finite span aligned to complete audio frames.
    /// Rodio rebuilds channel and sample-rate converters only at span boundaries.
    /// None would preserve the first station's format and play later streams at incorrect speeds.
    /// Frame alignment preserves channel positions. SPAN remains below rodio's 32768-sample limit.
    fn current_span_len(&self) -> Option<usize> {
        let channels = self.channels.get() as usize;
        Some(SPAN.div_ceil(channels) * channels)
    }

    fn channels(&self) -> ChannelCount {
        self.channels
    }

    fn sample_rate(&self) -> SampleRate {
        self.rate
    }

    fn total_duration(&self) -> Option<Duration> {
        None
    }
}
