//! Playback output, feeder pacing, and the callback-safe sample ring.

use std::path::Path;
use std::sync::atomic::{AtomicBool, AtomicUsize, Ordering};
use std::sync::mpsc::{sync_channel, Receiver, SyncSender, TryRecvError, TrySendError};
use std::sync::{Arc, Mutex};
use std::time::Duration;

use anyhow::{anyhow, Context, Result};
use rodio::cpal::traits::{DeviceTrait, HostTrait};
use rodio::{cpal, ChannelCount, Sample, SampleRate, Source};

use super::station::{
    local_title, next_playlist_source, open_file, open_source, AudioFormat, Playlist, TitleSink,
};
use super::{AudioState, GENERATION, QUEUE_POLL, REOPEN_PAUSE};

const CHUNK: usize = 4096;
pub(crate) const RING: usize = 256;
const PREFILL_SECS: usize = 1;
const SPAN: usize = 8192;

/// ALSA PCMs that hand audio to a sound server, best first. cpal's Linux host is ALSA and
/// nothing else, so reaching PipeWire is a matter of opening the right PCM: these are the
/// plugins that lead to it, and everything else in the list is a card or a filter.
pub(crate) const SERVER_PCMS: [&str; 3] = ["pipewire", "pulse", "default"];

/// ALSA's `null` PCM accepts and discards every sample, so selecting it looks like playback
/// with no sound. Never use it as a fallback.
pub(crate) const NULL_PCM: &str = "null";

/// The ALSA PCM id - `pipewire`, `pulse`, `default`, `null`. `description().name()` is a
/// human sentence ("PipeWire Sound Server") and no use for matching; the id is the name
/// ALSA knows the device by.
pub(crate) fn pcm_id(device: &cpal::Device) -> String {
    device
        .id()
        .map(|cpal::DeviceId(_, id)| id)
        .unwrap_or_default()
}

/// Prefer named server PCMs over `open_default_sink`, whose fallback can select `null`;
/// reject `null` rather than silently playing nowhere.
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

    // Nothing named: bare ALSA with no server at all, where the default device is the
    // only sensible answer there is - but still never the one that eats what it is given.
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

/// Opens the source once here, so a station that will not answer is an error the mod can
/// be told about, then hands the rest to a feeder.
pub(crate) fn start(
    source: &str,
    player: &rodio::Player,
    state: &Arc<Mutex<AudioState>>,
) -> Result<()> {
    let generation = GENERATION.load(Ordering::SeqCst);
    let title = TitleSink {
        state: Some(state.clone()),
        generation,
    };

    let mut playlist = if Path::new(source).is_dir() {
        Some(Playlist::from_dir(Path::new(source))?)
    } else {
        None
    };
    let first_path = playlist.as_mut().map(|p| {
        p.next()
            .expect("a directory playlist is non-empty by construction")
    });
    let first = match first_path.as_ref() {
        Some(path) => {
            title.set(local_title(path));
            open_file(path)?
        }
        None => open_source(source, &title)?,
    };
    let format = AudioFormat {
        channels: first.channels(),
        rate: first.sample_rate(),
    };

    let (tx, rx) = sync_channel::<Vec<Sample>>(RING);
    let ready = Arc::new(AtomicBool::new(false));
    let queued = Arc::new(AtomicUsize::new(0));
    let prefill_samples =
        format.rate.get() as usize * format.channels.get() as usize * PREFILL_SECS;
    player.append(Ring {
        rx,
        held: Vec::new(),
        at: 0,
        channels: format.channels,
        rate: format.rate,
        generation,
        ready: ready.clone(),
        queued: queued.clone(),
        prefill_samples,
    });

    let source = source.to_string();
    std::thread::Builder::new()
        .name("slopd audio feed".into())
        .spawn(move || {
            feed(
                first, source, playlist, format, tx, generation, title, ready, queued,
            )
        })
        .context("starting the feeder")?;

    Ok(())
}

/// Decodes into the ring until the source runs dry. A file loops, a directory advances through
/// its shuffled bag, and a station that dropped reconnects. A selected source keeps retrying
/// until the run is superseded or the ring's other end has gone.
fn feed(
    first: Box<dyn Source + Send>,
    source: String,
    mut playlist: Option<Playlist>,
    format: AudioFormat,
    tx: SyncSender<Vec<Sample>>,
    generation: u64,
    title: TitleSink,
    ready: Arc<AtomicBool>,
    queued: Arc<AtomicUsize>,
) {
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
                tracing::warn!("audio: decoder for {source} ended; rebuilding after a pause");
                if !super::wait_for_retry(REOPEN_PAUSE, generation, &GENERATION) {
                    return;
                }
                match open_source(&source, &title) {
                    Ok(d) if d.channels() == format.channels && d.sample_rate() == format.rate => d,
                    Ok(_) => {
                        // Keep the ring alive while a station changes encoders or a local file
                        // is replaced. Playing it through the old shape would be wrong, but
                        // dropping the selected source is worse; the next retry may match.
                        tracing::warn!("audio: {source} came back in another format; retrying");
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
            // Blocks on a full ring, which is the whole of the rate limiting: the
            // download runs exactly as fast as the speakers empty it.
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

    if !ready.load(Ordering::Relaxed) {
        if queued.load(Ordering::Acquire) >= prefill_samples {
            ready.store(true, Ordering::Release);
        }
    }
    true
}

struct ReadyOnDrop(Arc<AtomicBool>);

impl Drop for ReadyOnDrop {
    fn drop(&mut self) {
        self.0.store(true, Ordering::Release);
    }
}

/// Where the decoded samples cross to the audio callback. An empty ring answers silence
/// rather than blocking, because the callback is on a deadline the network is not.
pub(crate) struct Ring {
    pub(crate) rx: Receiver<Vec<Sample>>,
    pub(crate) held: Vec<Sample>,
    pub(crate) at: usize,
    pub(crate) channels: ChannelCount,
    pub(crate) rate: SampleRate,
    /// The run this belongs to. Ending on a bump is how a station switch happens: the
    /// player finishes this source and moves to the one appended behind it.
    pub(crate) generation: u64,
    /// Playback waits here while the feeder builds a small amount of live headroom.
    pub(crate) ready: Arc<AtomicBool>,
    /// Samples still in the channel, excluding `held`; used only at chunk boundaries so the audio
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
                    // Direct test senders do not publish a count; production always does. Keep
                    // the accounting saturating without adding work to every sample callback.
                    let _ = self
                        .queued
                        .fetch_update(Ordering::AcqRel, Ordering::Acquire, |n| {
                            Some(n.saturating_sub(len))
                        });
                    self.held = next;
                    self.at = 0;
                }
                // An underrun. The feeder is still there and will catch up; a None here
                // would end the source and the music with it.
                Err(TryRecvError::Empty) => {
                    // Stop chasing the feeder one chunk at a time. Rebuild the original headroom
                    // before resuming, otherwise a single long reconnect leaves playback riding
                    // the underrun edge indefinitely.
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
    /// Finite and frame-aligned. Rodio rebuilds channel/rate converters only at span
    /// boundaries; `None` would preserve the first station's shape across every switch,
    /// playing later mono or 22050 Hz streams at 2x or 4x. Alignment preserves channel
    /// sides, and `SPAN` remains below rodio's 32768-sample clamp.
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
