//! Playback output, feeder pacing, and the callback-safe sample ring.

use std::path::Path;
use std::sync::Arc;
use std::sync::atomic::{AtomicBool, AtomicUsize, Ordering};
use std::sync::mpsc::{SyncSender, sync_channel};

use anyhow::{Context, Result, anyhow};
use rodio::cpal::traits::{DeviceTrait, HostTrait};
use rodio::{Sample, Source, cpal};

use super::ring::{FinishedOnDrop, RING, Ring, enqueue_chunk};
use super::station::{
    AudioFormat, Playlist, TitleSink, local_title, next_playlist_source, open_file, open_source,
};
use super::{GENERATION, REOPEN_PAUSE};

const CHUNK: usize = 4096;
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
    if generation != GENERATION.load(Ordering::SeqCst) {
        return Ok(None);
    }

    let (tx, rx) = sync_channel::<Vec<Sample>>(RING);
    let ready = Arc::new(AtomicBool::new(false));
    let queued = Arc::new(AtomicUsize::new(0));
    let prefill_samples =
        format.rate.get() as usize * format.channels.get() as usize * PREFILL_SECS;
    let finished = Arc::new(AtomicBool::new(false));
    let ring = Ring {
        rx,
        held: Vec::new(),
        at: 0,
        channels: format.channels,
        rate: format.rate,
        generation,
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
    if generation != GENERATION.load(Ordering::SeqCst) || title.cancelled.load(Ordering::Acquire) {
        return Ok(None);
    }
    output.set_volume(volume);
    output.append(Box::new(ring));
    output.play();
    // Replacement during output I/O is handled by the ring's generation check; never start
    // decoding a candidate that has already been retired.
    if generation != GENERATION.load(Ordering::SeqCst) || title.cancelled.load(Ordering::Acquire) {
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
    tx: SyncSender<Vec<Sample>>,
    generation: u64,
    title: TitleSink,
    ready: Arc<AtomicBool>,
    queued: Arc<AtomicUsize>,
}

/// Decode audio into the ring buffer until the source ends.
/// Repeat files, advance through randomized directory playlists, and reconnect interrupted stations.
/// Retry until a new run replaces this run or the ring consumer closes.
#[expect(
    clippy::expect_used,
    reason = "the playlist branch is entered only when a playlist is present"
)]
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
    let prefill_samples =
        format.rate.get() as usize * format.channels.get() as usize * PREFILL_SECS;
    let mut current = Some(first);

    loop {
        if title.cancelled.load(Ordering::Acquire)
            || generation != GENERATION.load(Ordering::SeqCst)
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
                if !super::wait_for_retry(
                    REOPEN_PAUSE,
                    generation,
                    &GENERATION,
                    Some(&title.cancelled),
                ) {
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
            if title.cancelled.load(Ordering::Acquire)
                || generation != GENERATION.load(Ordering::SeqCst)
            {
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

#[cfg(test)]
#[path = "playback_tests.rs"]
mod tests;
