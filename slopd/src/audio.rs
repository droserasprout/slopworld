//! Host playback keeps station networking out of Unity because FMOD lacks TLS/AAC and requires Icecast's omitted `Content-Length`; a feeder decodes URLs/files into a callback-safe ring.

use std::io::{self, BufReader, Read, Seek, SeekFrom};
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, AtomicU64, AtomicUsize, Ordering};
use std::sync::mpsc::{sync_channel, Receiver, SyncSender, TryRecvError, TrySendError};
use std::sync::{Arc, Mutex};
use std::time::Duration;

use anyhow::{anyhow, Context, Result};
use rand::seq::SliceRandom;
use rodio::cpal::traits::{DeviceTrait, HostTrait};
use rodio::{cpal, ChannelCount, Sample, SampleRate, Source};
use serde::Serialize;

/// Samples per feeder hop; small enough for prompt stops without making the channel the work.
const CHUNK: usize = 4096;

/// Ring capacity in hops: about twelve seconds at 44.1kHz stereo, pacing downloads to playback.
const RING: usize = 256;

/// Do not consume a live source until the feeder has a second of decoded audio ahead of us.
const PREFILL_SECS: usize = 1;

/// Samples per mixer span before frame alignment; see `Ring::current_span_len`.
const SPAN: usize = 8192;

/// How long to wait on a station before calling the connection failed. The body itself
/// is never timed out: it is not supposed to end.
const CONNECT: Duration = Duration::from_secs(15);

/// Delay before reopening a dry file or reconnecting a dropped station.
const REOPEN_PAUSE: Duration = Duration::from_secs(2);

/// How often a feeder checks whether a full ring has been retired by a station switch.
const QUEUE_POLL: Duration = Duration::from_millis(5);

/// State sent to the mod; errors persist for reporting, and directory playback reports the
/// current local file stem as its title.
#[derive(Clone, Debug, Default, PartialEq, Serialize)]
pub struct AudioState {
    pub playing: bool,
    pub source: Option<String>,
    pub volume: f32,
    pub error: Option<String>,
    pub title: Option<String>,
}

enum Cmd {
    Play { source: String, volume: f32 },
    Volume(f32),
    Stop,
}

/// The handle. Cheap to clone and safe to call from anywhere; every method is a message.
#[derive(Clone)]
pub struct Audio {
    tx: std::sync::mpsc::Sender<Cmd>,
    state: Arc<Mutex<AudioState>>,
}

impl Audio {
    pub fn new() -> Audio {
        let (tx, rx) = std::sync::mpsc::channel();
        let state = Arc::new(Mutex::new(AudioState {
            volume: 1.0,
            ..Default::default()
        }));

        let worker_state = state.clone();
        // Detached: shutdown only needs the worker to observe its closed channel.
        let spawned = std::thread::Builder::new()
            .name("slopd audio".into())
            .spawn(move || run(rx, worker_state));
        if let Err(e) = spawned {
            tracing::error!("audio thread would not start: {e}");
        }

        Audio { tx, state }
    }

    /// `source` is a URL, file path, or directory; `volume` is 0..1.
    pub fn play(&self, source: &str, volume: f32) {
        let _ = self.tx.send(Cmd::Play {
            source: source.to_string(),
            volume: volume.clamp(0.0, 1.0),
        });
    }

    pub fn set_volume(&self, volume: f32) {
        let _ = self.tx.send(Cmd::Volume(volume.clamp(0.0, 1.0)));
    }

    pub fn stop(&self) {
        let _ = self.tx.send(Cmd::Stop);
    }

    /// Publishes a request-side error without opening a source, stopping stale playback first.
    pub fn reject(&self, why: impl Into<String>) {
        GENERATION.fetch_add(1, Ordering::SeqCst);
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
            let now = m.audio.state();
            if last.as_ref() == Some(&now) {
                continue;
            }
            last = Some(now.clone());
            let _ = m.events.send(crate::session::Event::Audio { audio: now });
        }
    })
}

/// Generation of the active source; stale feeders stop before writing to the new ring.
static GENERATION: AtomicU64 = AtomicU64::new(0);

fn run(rx: Receiver<Cmd>, state: Arc<Mutex<AudioState>>) {
    // Open lazily so a missing sink is reported when playback is requested.
    let mut device: Option<(rodio::MixerDeviceSink, rodio::Player)> = None;

    while let Ok(cmd) = rx.recv() {
        match cmd {
            Cmd::Play { source, volume } => {
                GENERATION.fetch_add(1, Ordering::SeqCst);

                if device.is_none() {
                    match open_device() {
                        Ok(d) => device = Some(d),
                        Err(e) => {
                            fail(&state, format!("no audio device: {e:#}"));
                            continue;
                        }
                    }
                }
                let (_sink, player) = device.as_ref().unwrap();

                // Clear stale metadata before opening; a fast station may publish a new title.
                state.lock().unwrap_or_else(|p| p.into_inner()).title = None;

                player.set_volume(volume);
                // Do not call `Player::clear`: it pauses playback. The generation bump retires
                // the old ring, whose next callback returns `None`.
                player.play();

                match start(&source, player, &state) {
                    Ok(()) => {
                        let mut s = state.lock().unwrap_or_else(|p| p.into_inner());
                        s.playing = true;
                        s.source = Some(source);
                        s.volume = volume;
                        s.error = None;
                        tracing::info!("audio: playing {}", s.source.as_deref().unwrap_or(""));
                    }
                    // Nothing was appended - `start` opens the source before it queues
                    // anything - so the generation bump is the whole of the cleanup.
                    Err(e) => fail(&state, format!("{source}: {e:#}")),
                }
            }

            Cmd::Volume(volume) => {
                if let Some((_, player)) = device.as_ref() {
                    player.set_volume(volume);
                }
                state.lock().unwrap_or_else(|p| p.into_inner()).volume = volume;
            }

            Cmd::Stop => {
                // The bump is the stop: every Ring carrying an older generation ends
                // itself, and the player runs out of sources and goes quiet.
                GENERATION.fetch_add(1, Ordering::SeqCst);
                let mut s = state.lock().unwrap_or_else(|p| p.into_inner());
                s.playing = false;
                s.source = None;
                s.title = None;
            }
        }
    }
}

fn fail(state: &Arc<Mutex<AudioState>>, why: String) {
    tracing::warn!("audio: {why}");
    let mut s = state.lock().unwrap_or_else(|p| p.into_inner());
    s.playing = false;
    s.source = None;
    s.title = None;
    s.error = Some(why);
}

/// ALSA PCMs that hand audio to a sound server, best first. cpal's Linux host is ALSA and
/// nothing else, so reaching PipeWire is a matter of opening the right PCM: these are the
/// plugins that lead to it, and everything else in the list is a card or a filter.
const SERVER_PCMS: [&str; 3] = ["pipewire", "pulse", "default"];

/// ALSA's `null` PCM accepts and discards every sample, so selecting it looks like playback
/// with no sound. Never use it as a fallback.
const NULL_PCM: &str = "null";

/// The ALSA PCM id - `pipewire`, `pulse`, `default`, `null`. `description().name()` is a
/// human sentence ("PipeWire Sound Server") and no use for matching; the id is the name
/// ALSA knows the device by.
fn pcm_id(device: &cpal::Device) -> String {
    device
        .id()
        .map(|cpal::DeviceId(_, id)| id)
        .unwrap_or_default()
}

/// Prefer named server PCMs over `open_default_sink`, whose fallback can select `null`;
/// reject `null` rather than silently playing nowhere.
fn open_device() -> Result<(rodio::MixerDeviceSink, rodio::Player)> {
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

/// Receives delayed station metadata. The generation prevents an old station naming its
/// replacement; `None` omits titles for files and tests.
#[derive(Clone)]
struct TitleSink {
    state: Option<Arc<Mutex<AudioState>>>,
    generation: u64,
}

impl TitleSink {
    fn set(&self, title: Option<String>) {
        let state = match self.state.as_ref() {
            Some(s) => s,
            None => return,
        };
        if self.generation != GENERATION.load(Ordering::SeqCst) {
            return;
        }
        let mut s = state.lock().unwrap_or_else(|p| p.into_inner());
        if s.title == title {
            return;
        }
        tracing::info!("audio: now playing {}", title.as_deref().unwrap_or("-"));
        s.title = title;
    }
}

/// Opens the source once here, so a station that will not answer is an error the mod can
/// be told about, then hands the rest to a feeder.
fn start(source: &str, player: &rodio::Player, state: &Arc<Mutex<AudioState>>) -> Result<()> {
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
                if !wait_for_retry(REOPEN_PAUSE, generation, &GENERATION) {
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

fn enqueue_chunk(
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

#[derive(Clone, Copy)]
struct AudioFormat {
    channels: ChannelCount,
    rate: SampleRate,
}

fn open_source(source: &str, title: &TitleSink) -> Result<Box<dyn Source + Send>> {
    if source.starts_with("http://") || source.starts_with("https://") {
        open_stream(source, title)
    } else {
        open_file(Path::new(source))
    }
}

fn open_file(path: &Path) -> Result<Box<dyn Source + Send>> {
    let file = std::fs::File::open(path).with_context(|| format!("opening {}", path.display()))?;
    let len = file.metadata().map(|m| m.len()).ok();

    let mut builder = rodio::Decoder::builder()
        .with_data(BufReader::new(file))
        .with_seekable(true)
        .with_hint(&path.to_string_lossy());
    if let Some(len) = len {
        builder = builder.with_byte_len(len);
    }

    Ok(Box::new(builder.build().context("decoding")?))
}

/// A directory selection is a non-repeating shuffled bag. It reshuffles only after every
/// file has been used, and swaps the first entry when needed so a bag boundary cannot repeat
/// the track that just ended.
struct Playlist {
    files: Vec<PathBuf>,
    next: usize,
    last: Option<PathBuf>,
}

impl Playlist {
    fn from_dir(path: &Path) -> Result<Self> {
        let mut files = std::fs::read_dir(path)
            .with_context(|| format!("reading audio directory {}", path.display()))?
            .filter_map(|entry| entry.ok().map(|entry| entry.path()))
            .filter(|entry| entry.is_file() && supported_file(entry))
            .collect::<Vec<_>>();
        files.sort();
        if files.is_empty() {
            return Err(anyhow!("audio directory {} is empty", path.display()));
        }

        files.shuffle(&mut rand::thread_rng());
        Ok(Self {
            files,
            next: 0,
            last: None,
        })
    }

    fn len(&self) -> usize {
        self.files.len()
    }

    fn next(&mut self) -> Option<PathBuf> {
        if self.next == self.files.len() {
            self.files.shuffle(&mut rand::thread_rng());
            if self.files.len() > 1 && self.last.as_ref() == self.files.first() {
                self.files.swap(0, 1);
            }
            self.next = 0;
        }

        let path = self.files.get(self.next)?.clone();
        self.next += 1;
        self.last = Some(path.clone());
        Some(path)
    }
}

fn supported_file(path: &Path) -> bool {
    matches!(
        path.extension()
            .and_then(|ext| ext.to_str())
            .map(|ext| ext.to_ascii_lowercase())
            .as_deref(),
        Some("mp3") | Some("ogg")
    )
}

fn local_title(path: &Path) -> Option<String> {
    path.file_stem()
        .and_then(|stem| stem.to_str())
        .map(str::to_string)
}

fn next_playlist_source(
    playlist: &mut Playlist,
    format: AudioFormat,
    title: &TitleSink,
) -> Option<Box<dyn Source + Send>> {
    for _ in 0..playlist.len() {
        let path = playlist.next()?;
        match open_file(&path) {
            Ok(decoded)
                if decoded.channels() == format.channels
                    && decoded.sample_rate() == format.rate =>
            {
                title.set(local_title(&path));
                return Some(decoded);
            }
            Ok(decoded) => {
                tracing::warn!(
                    "audio: skipping {}: format changes within a directory playlist",
                    path.display()
                );
                drop(decoded);
            }
            Err(e) => tracing::warn!("audio: skipping {}: {e:#}", path.display()),
        }
    }
    None
}

fn open_stream(url: &str, title: &TitleSink) -> Result<Box<dyn Source + Send>> {
    let mut connector = StreamConnector::new(url, title.clone());
    let first = connector
        .connect()
        .with_context(|| format!("connecting to {url}"))?;
    let mime = connector
        .mime
        .clone()
        .expect("a successful stream connection records its MIME type");
    let generation = title.generation;
    let source = url.to_string();
    let live = Reconnect {
        inner: first,
        reopen: move || connector.connect(),
        source,
        generation,
        read_since_open: false,
    };
    let body = BufReader::with_capacity(64 * 1024, live);

    let decoded = rodio::Decoder::builder()
        .with_data(Feed(Mutex::new(body)))
        .with_mime_type(&mime)
        .with_seekable(false)
        .build()
        .with_context(|| format!("decoding {mime} from {url}"))?;

    Ok(Box::new(decoded))
}

type StreamBody = Box<dyn Read + Send>;

/// Makes one HTTP response reader. Reconnects reuse the same agent and must retain the MIME type
/// the decoder was probed with. ICY state is per response because every response begins at a new
/// metadata interval.
struct StreamConnector {
    agent: ureq::Agent,
    url: String,
    title: TitleSink,
    mime: Option<String>,
}

impl StreamConnector {
    fn new(url: &str, title: TitleSink) -> Self {
        // A live response has no response/body/global deadline. In ureq 3.3, `RecvBody` checks
        // the preceding `RecvResponse` timer too, despite the latter being documented as headers
        // only; setting it to `CONNECT` killed every stream body after exactly fifteen seconds.
        // The socket/TLS connection itself remains bounded.
        let agent = ureq::Agent::config_builder()
            .timeout_connect(Some(CONNECT))
            .timeout_recv_response(None)
            .timeout_recv_body(None)
            .timeout_global(None)
            .build()
            .into();
        Self {
            agent,
            url: url.to_string(),
            title,
            mime: None,
        }
    }

    fn connect(&mut self) -> io::Result<StreamBody> {
        // `Icy-MetaData: 1` asks for titles inside the body. Those bytes must be removed before
        // buffering or decoding, and a fresh `Icy` starts its count at each HTTP response.
        let response = self
            .agent
            .get(&self.url)
            .header("Icy-MetaData", "1")
            .call()
            .map_err(|e| io::Error::other(format!("connecting to {}: {e}", self.url)))?;

        let metaint = response
            .headers()
            .get("icy-metaint")
            .and_then(|v| v.to_str().ok())
            .and_then(|v| v.trim().parse::<usize>().ok())
            .filter(|n| *n > 0);
        let mime = response
            .headers()
            .get("content-type")
            .and_then(|v| v.to_str().ok())
            .unwrap_or("audio/mpeg")
            .split(';')
            .next()
            .unwrap_or("audio/mpeg")
            .trim()
            .to_string();

        match self.mime.as_ref() {
            Some(expected) if !expected.eq_ignore_ascii_case(&mime) => {
                return Err(io::Error::new(
                    io::ErrorKind::InvalidData,
                    format!("stream changed MIME type from {expected} to {mime}"),
                ));
            }
            None => self.mime = Some(mime),
            _ => {}
        }

        let raw: StreamBody = match metaint {
            Some(n) => {
                tracing::debug!("audio: {} splices titles every {n} bytes", self.url);
                Box::new(Icy {
                    inner: response.into_body().into_reader(),
                    metaint: n,
                    left: n,
                    title: self.title.clone(),
                })
            }
            None => Box::new(response.into_body().into_reader()),
        };
        Ok(raw)
    }
}

/// Keeps transport boundaries below the decoder. Rebuilding a decoder for every short HTTP body
/// loses its compressed-audio history and can replay the relay's connection-opening buffer. A
/// body that carried audio is replaced immediately; an empty/rejected response waits so a broken
/// endpoint cannot be hammered. The decoded ring supplies headroom while this blocks.
struct Reconnect<F> {
    inner: StreamBody,
    reopen: F,
    source: String,
    generation: u64,
    read_since_open: bool,
}

impl<F> Reconnect<F>
where
    F: FnMut() -> io::Result<StreamBody>,
{
    fn replace(&mut self, why: &str) -> io::Result<bool> {
        tracing::info!("audio: stream body {why}; reconnecting {}", self.source);
        let mut wait = !self.read_since_open;

        loop {
            if wait && !wait_for_retry(REOPEN_PAUSE, self.generation, &GENERATION) {
                return Ok(false);
            }
            if self.generation != GENERATION.load(Ordering::SeqCst) {
                return Ok(false);
            }

            match (self.reopen)() {
                Ok(next) => {
                    self.inner = next;
                    self.read_since_open = false;
                    return Ok(true);
                }
                Err(e) => {
                    tracing::debug!("audio: reconnecting {}: {e}", self.source);
                    wait = true;
                }
            }
        }
    }
}

impl<F> Read for Reconnect<F>
where
    F: FnMut() -> io::Result<StreamBody>,
{
    fn read(&mut self, buf: &mut [u8]) -> io::Result<usize> {
        if buf.is_empty() {
            return Ok(0);
        }

        loop {
            match self.inner.read(buf) {
                Ok(0) => {
                    if !self.replace("ended")? {
                        return Ok(0);
                    }
                }
                Ok(n) => {
                    self.read_since_open = true;
                    return Ok(n);
                }
                Err(e) if e.kind() == io::ErrorKind::Interrupted => continue,
                // The existing decoder cannot consume a new codec. Let it end so the outer
                // feeder can probe a fresh decoder and validate its sample shape.
                Err(e) if e.kind() == io::ErrorKind::InvalidData => return Err(e),
                Err(e) => {
                    let why = format!("failed ({e})");
                    if !self.replace(&why)? {
                        return Ok(0);
                    }
                }
            }
        }
    }
}

/// Removes ICY metadata blocks from a station stream while passing audio bytes through.
/// Each block has `icy-metaint` audio bytes, a 16-byte-unit length, and padded metadata;
/// zero length means no title change.
struct Icy<R: Read> {
    inner: R,
    metaint: usize,
    /// Audio bytes still owed before the next block.
    left: usize,
    title: TitleSink,
}

impl<R: Read> Icy<R> {
    /// One metadata block, read whole: it is at most 4080 bytes and it is in the way of the
    /// audio behind it, so there is nothing to be gained by handing it back in pieces.
    fn block(&mut self) -> io::Result<()> {
        let mut len = [0u8; 1];
        self.inner.read_exact(&mut len)?;
        let n = len[0] as usize * 16;
        if n == 0 {
            return Ok(());
        }

        let mut raw = vec![0u8; n];
        self.inner.read_exact(&mut raw)?;
        if let Some(title) = stream_title(&raw) {
            self.title.set(title);
        }
        Ok(())
    }
}

impl<R: Read> Read for Icy<R> {
    fn read(&mut self, buf: &mut [u8]) -> io::Result<usize> {
        if buf.is_empty() {
            return Ok(0);
        }
        if self.left == 0 {
            self.block()?;
            self.left = self.metaint;
        }
        // Never read past the metadata boundary: bytes beyond it are not audio.
        let want = buf.len().min(self.left);
        let n = self.inner.read(&mut buf[..want])?;
        self.left -= n;
        Ok(n)
    }
}

/// Extracts `StreamTitle`; `Some(None)` clears it, while `None` means no field was present.
/// Decode lossily because streams use both Latin-1 and UTF-8 in practice.
fn stream_title(raw: &[u8]) -> Option<Option<String>> {
    let text = String::from_utf8_lossy(raw);
    let rest = text.split("StreamTitle=").nth(1)?.strip_prefix('\'')?;
    // `';` and not `'`, because an apostrophe in a song title is ordinary and a semicolon
    // right behind one is not. The fallback is for a block where StreamTitle is the last
    // field and the quote is followed by padding rather than by another key.
    let end = match rest.find("';") {
        Some(i) => i,
        None => rest.find('\'')?,
    };

    let title = rest[..end].trim();
    Some(if title.is_empty() {
        None
    } else {
        Some(title.to_string())
    })
}

/// The decoder's input has to be `Read + Seek + Send + Sync`, and a live stream is
/// neither seekable nor shared. The seek is a stub - the decoder is built with
/// `is_seekable(false)` and never calls it - and the mutex is only there to make a `Send`
/// reader `Sync`; nothing ever contends on it, because every use holds `&mut self`.
struct Feed<R: Read + Send>(Mutex<R>);

impl<R: Read + Send> Read for Feed<R> {
    fn read(&mut self, buf: &mut [u8]) -> io::Result<usize> {
        self.0
            .get_mut()
            .unwrap_or_else(|p| p.into_inner())
            .read(buf)
    }
}

impl<R: Read + Send> Seek for Feed<R> {
    fn seek(&mut self, _: SeekFrom) -> io::Result<u64> {
        Err(io::Error::new(
            io::ErrorKind::Unsupported,
            "a live stream does not seek",
        ))
    }
}

/// Where the decoded samples cross to the audio callback. An empty ring answers silence
/// rather than blocking, because the callback is on a deadline the network is not.
struct Ring {
    rx: Receiver<Vec<Sample>>,
    held: Vec<Sample>,
    at: usize,
    channels: ChannelCount,
    rate: SampleRate,
    /// The run this belongs to. Ending on a bump is how a station switch happens: the
    /// player finishes this source and moves to the one appended behind it.
    generation: u64,
    /// Playback waits here while the feeder builds a small amount of live headroom.
    ready: Arc<AtomicBool>,
    /// Samples still in the channel, excluding `held`; used only at chunk boundaries so the audio
    /// callback does not perform an atomic operation per sample.
    queued: Arc<AtomicUsize>,
    prefill_samples: usize,
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

#[cfg(test)]
mod tests {
    use super::*;
    use std::collections::VecDeque;
    use std::io::{BufRead, Write};
    use std::net::TcpListener;

    fn ring() -> (SyncSender<Vec<Sample>>, Ring) {
        let (tx, rx) = sync_channel(RING);
        (
            tx,
            Ring {
                rx,
                held: Vec::new(),
                at: 0,
                channels: ChannelCount::new(2).unwrap(),
                rate: SampleRate::new(44100).unwrap(),
                // Whatever run the suite is on: only the ignored tests move it, and
                // those do not run beside these.
                generation: GENERATION.load(Ordering::SeqCst),
                ready: Arc::new(AtomicBool::new(true)),
                queued: Arc::new(AtomicUsize::new(0)),
                prefill_samples: 1,
            },
        )
    }

    /// Exercises the real path: one player with differently shaped stations appended.
    /// A ramp exposes playback position directly without counting a converter tail, and
    /// needs neither network nor audio device.
    #[test]
    fn a_station_of_any_shape_plays_at_its_own_speed() {
        // Frames in, per station. The ramp runs 0.0 to 1.0 across them.
        const N: usize = 8192;

        let (mixer, mut out) = rodio::mixer::mixer(
            ChannelCount::new(2).unwrap(),
            SampleRate::new(44100).unwrap(),
        );
        let player = rodio::Player::connect_new(&mixer);

        // The shapes the stations actually come in - RP at 128k, WeFunk at 64k, RP at 32k,
        // WALM at 320k, and back - stereo first, so it is the one that would have done the
        // latching. WALM's 48000 is the only one that resamples *down*.
        let stations = [
            (2u16, 44100u32),
            (1, 44100),
            (1, 22050),
            (2, 48000),
            (2, 44100),
        ];

        for (channels, rate) in stations {
            let (tx, rx) = sync_channel::<Vec<Sample>>(RING);
            // The global generation is left alone: this runs beside the other ring tests,
            // and a bump here would retire their rings out from under them.
            player.append(Ring {
                rx,
                held: Vec::new(),
                at: 0,
                channels: ChannelCount::new(channels).unwrap(),
                rate: SampleRate::new(rate).unwrap(),
                generation: GENERATION.load(Ordering::SeqCst),
                ready: Arc::new(AtomicBool::new(true)),
                queued: Arc::new(AtomicUsize::new(0)),
                prefill_samples: 1,
            });

            // The ramp starts at FLOOR rather than at zero, so its first sample can be told
            // from the silence the mixer leads in with - which is what marks where the
            // station begins. Position is then read off the value: FLOOR at the start,
            // 1.0 at the end.
            const FLOOR: Sample = 0.2;
            let mut ramp = Vec::with_capacity(N * channels as usize);
            for frame in 0..N {
                let at = FLOOR + (1.0 - FLOOR) * (frame as Sample / N as Sample);
                ramp.extend(std::iter::repeat_n(at, channels as usize));
            }
            tx.send(ramp).unwrap();
            // Ending it is what moves the player on to the next station.
            drop(tx);

            // Long enough to drain this station and its tail, so the next one starts clean.
            let got: Vec<Sample> = (&mut out).take(N * 8).collect();

            // The distance between two points on the ramp, rather than the position of
            // either: how much silence the mixer leads in with then does not matter, and
            // neither does anything the tail does. A quarter of the way in to three
            // quarters of the way in is half the station.
            let crossing = |v: Sample| {
                got.iter()
                    .position(|s| *s > v)
                    .unwrap_or_else(|| panic!("{channels}ch {rate}Hz never reached {v}"))
            };
            let half =
                crossing(FLOOR + (1.0 - FLOOR) * 0.75) - crossing(FLOOR + (1.0 - FLOOR) * 0.25);

            // Half the station's frames, resampled to the mixer's 44100, two samples each.
            let want = N / 2 * 44100 / rate as usize * 2;
            let ratio = want as f64 / half as f64;

            // At 2x half the station goes by in half the samples; at 4x, a quarter of them.
            assert!(
                (0.95..=1.05).contains(&ratio),
                "{channels}ch {rate}Hz: half the station took {half} samples where {want} \
                 was due - {ratio:.2}x speed"
            );
        }
    }

    /// The retirement that replaces `Player::clear`: a source whose run has been
    /// superseded ends, and the player moves on to the one appended behind it.
    #[test]
    fn a_superseded_ring_ends() {
        let (tx, mut r) = ring();
        tx.send(vec![1.0, 1.0]).unwrap();
        assert_eq!(r.next(), Some(1.0));
        // An older run. Wrapping because the suite may well be on run zero.
        r.generation = r.generation.wrapping_sub(1);
        assert_eq!(r.next(), None);
    }

    #[test]
    fn ring_hands_over_what_it_is_given() {
        let (tx, mut r) = ring();
        tx.send(vec![0.25, -0.5]).unwrap();
        assert_eq!(r.next(), Some(0.25));
        assert_eq!(r.next(), Some(-0.5));
    }

    /// The callback is on a deadline the network is not: an empty ring is a moment of
    /// silence, never a stop. Ending the source here would end the music for good.
    #[test]
    fn an_empty_ring_is_silence_and_not_the_end() {
        let (_tx, mut r) = ring();
        assert_eq!(r.next(), Some(0.0));
        assert_eq!(r.next(), Some(0.0));
    }

    #[test]
    fn ring_does_not_consume_until_prefill_is_ready() {
        let (tx, mut r) = ring();
        r.ready.store(false, Ordering::Release);
        tx.send(vec![0.75]).unwrap();

        assert_eq!(r.next(), Some(0.0));
        r.ready.store(true, Ordering::Release);
        assert_eq!(r.next(), Some(0.75));
    }

    #[test]
    fn queued_prefill_releases_playback_at_the_threshold() {
        let (tx, rx) = sync_channel(2);
        let ready = AtomicBool::new(false);
        let queued = AtomicUsize::new(0);
        let generation = GENERATION.load(Ordering::SeqCst);

        assert!(enqueue_chunk(
            &tx,
            vec![0.1; 4],
            &ready,
            &queued,
            5,
            generation,
        ));
        assert!(!ready.load(Ordering::Acquire));
        assert!(enqueue_chunk(
            &tx,
            vec![0.2; 1],
            &ready,
            &queued,
            5,
            generation,
        ));
        assert!(ready.load(Ordering::Acquire));
        assert_eq!(rx.try_iter().flatten().count(), 5);
    }

    #[test]
    fn a_live_body_has_no_inherited_response_deadline() {
        let connector = StreamConnector::new(
            "http://127.0.0.1/unused-local-fixture",
            TitleSink {
                state: None,
                generation: GENERATION.load(Ordering::SeqCst),
            },
        );
        let timeouts = connector.agent.config().timeouts();

        assert_eq!(timeouts.connect, Some(CONNECT));
        assert_eq!(timeouts.recv_response, None);
        assert_eq!(timeouts.recv_body, None);
        assert_eq!(timeouts.global, None);
    }

    #[test]
    fn an_underrun_rebuilds_headroom_before_resuming() {
        let (tx, rx) = sync_channel(4);
        let ready = Arc::new(AtomicBool::new(true));
        let queued = Arc::new(AtomicUsize::new(0));
        let generation = GENERATION.load(Ordering::SeqCst);
        let mut ring = Ring {
            rx,
            held: Vec::new(),
            at: 0,
            channels: ChannelCount::new(1).unwrap(),
            rate: SampleRate::new(5).unwrap(),
            generation,
            ready: ready.clone(),
            queued: queued.clone(),
            prefill_samples: 5,
        };

        assert!(enqueue_chunk(
            &tx,
            vec![0.1],
            &ready,
            &queued,
            5,
            generation,
        ));
        assert_eq!(ring.next(), Some(0.1));
        assert_eq!(ring.next(), Some(0.0));
        assert!(!ready.load(Ordering::Acquire));

        assert!(enqueue_chunk(
            &tx,
            vec![0.2; 4],
            &ready,
            &queued,
            5,
            generation,
        ));
        assert_eq!(ring.next(), Some(0.0));
        assert!(enqueue_chunk(
            &tx,
            vec![0.3],
            &ready,
            &queued,
            5,
            generation,
        ));
        assert!(ready.load(Ordering::Acquire));
        assert_eq!(ring.next(), Some(0.2));
    }

    #[test]
    fn retry_wait_can_be_cancelled_by_a_new_selection() {
        let generation = GENERATION.load(Ordering::SeqCst);
        let active = AtomicU64::new(generation.wrapping_add(1));
        assert!(!wait_for_retry(
            Duration::from_secs(30),
            generation,
            &active
        ));
    }

    #[test]
    fn a_stream_body_break_is_hidden_from_its_consumer() {
        let mut rest: VecDeque<StreamBody> = [b"def".as_slice(), b"ghi".as_slice()]
            .into_iter()
            .map(|bytes| Box::new(io::Cursor::new(bytes.to_vec())) as StreamBody)
            .collect();
        let mut stream = Reconnect {
            inner: Box::new(io::Cursor::new(b"abc".to_vec())),
            reopen: move || {
                rest.pop_front()
                    .ok_or_else(|| io::Error::other("synthetic stream exhausted"))
            },
            source: "synthetic fixture".to_string(),
            generation: GENERATION.load(Ordering::SeqCst),
            read_since_open: false,
        };

        let mut out = [0u8; 9];
        stream.read_exact(&mut out).unwrap();
        assert_eq!(&out, b"abcdefghi");
    }

    struct FailsAfterBytes {
        bytes: io::Cursor<Vec<u8>>,
    }

    impl Read for FailsAfterBytes {
        fn read(&mut self, buf: &mut [u8]) -> io::Result<usize> {
            let n = self.bytes.read(buf)?;
            if n == 0 {
                Err(io::Error::new(
                    io::ErrorKind::ConnectionReset,
                    "synthetic reset",
                ))
            } else {
                Ok(n)
            }
        }
    }

    #[test]
    fn a_stream_read_failure_is_hidden_from_its_consumer() {
        let mut next = Some(Box::new(io::Cursor::new(b"after".to_vec())) as StreamBody);
        let mut stream = Reconnect {
            inner: Box::new(FailsAfterBytes {
                bytes: io::Cursor::new(b"before".to_vec()),
            }),
            reopen: move || {
                next.take()
                    .ok_or_else(|| io::Error::other("synthetic stream exhausted"))
            },
            source: "synthetic fixture".to_string(),
            generation: GENERATION.load(Ordering::SeqCst),
            read_since_open: false,
        };

        let mut out = [0u8; 11];
        stream.read_exact(&mut out).unwrap();
        assert_eq!(&out, b"beforeafter");
    }

    /// This is the complete HTTP/ICY reconnect path, deliberately bound to loopback. Each finite
    /// response has its own metadata interval; the consumer sees one uninterrupted audio reader.
    #[test]
    fn loopback_http_responses_share_one_audio_reader() {
        let listener = TcpListener::bind(("127.0.0.1", 0)).unwrap();
        let address = listener.local_addr().unwrap();
        assert!(address.ip().is_loopback());

        let server = std::thread::spawn(move || {
            for audio in [b"abc", b"def"] {
                let (mut socket, peer) = listener.accept().unwrap();
                assert!(peer.ip().is_loopback());
                let mut request = BufReader::new(socket.try_clone().unwrap());
                let mut headers = String::new();
                loop {
                    let mut line = String::new();
                    request.read_line(&mut line).unwrap();
                    if line == "\r\n" {
                        break;
                    }
                    headers.push_str(&line);
                }
                assert!(headers.to_ascii_lowercase().contains("icy-metadata: 1\r\n"));

                socket
                    .write_all(
                        b"HTTP/1.1 200 OK\r\n\
                          Content-Type: audio/mpeg\r\n\
                          icy-metaint: 3\r\n\
                          Content-Length: 4\r\n\
                          Connection: close\r\n\r\n",
                    )
                    .unwrap();
                socket.write_all(audio).unwrap();
                socket.write_all(&[0]).unwrap();
            }
        });

        let title = TitleSink {
            state: None,
            generation: GENERATION.load(Ordering::SeqCst),
        };
        let mut connector = StreamConnector::new(&format!("http://{address}"), title);
        let first = connector.connect().unwrap();
        let mut stream = Reconnect {
            inner: first,
            reopen: move || connector.connect(),
            source: "loopback fixture".to_string(),
            generation: GENERATION.load(Ordering::SeqCst),
            read_since_open: false,
        };

        let mut out = [0u8; 6];
        stream.read_exact(&mut out).unwrap();
        assert_eq!(&out, b"abcdef");
        server.join().unwrap();
    }

    /// The transport fix is below the real decoder, not merely a byte-concatenation trick. Split
    /// a repository OGG at awkward offsets and require the decoder to produce the same number of
    /// samples as the uninterrupted file. This fixture and every reader above are local-only.
    #[test]
    fn decoder_survives_synthetic_body_breaks() {
        let encoded = include_bytes!("../../mod/Sounds/SlopWorld/silent.ogg");
        let uninterrupted = rodio::Decoder::builder()
            .with_data(io::Cursor::new(encoded.as_slice()))
            .with_mime_type("audio/ogg")
            .build()
            .unwrap()
            .count();
        assert!(uninterrupted > 0);

        let cuts = [731, 2_017];
        let mut rest: VecDeque<StreamBody> = [&encoded[cuts[0]..cuts[1]], &encoded[cuts[1]..]]
            .into_iter()
            .map(|bytes| Box::new(io::Cursor::new(bytes.to_vec())) as StreamBody)
            .collect();
        let stream = Reconnect {
            inner: Box::new(io::Cursor::new(encoded[..cuts[0]].to_vec())),
            reopen: move || {
                rest.pop_front()
                    .ok_or_else(|| io::Error::other("synthetic stream exhausted"))
            },
            source: "local OGG fixture".to_string(),
            generation: GENERATION.load(Ordering::SeqCst),
            read_since_open: false,
        };
        let decoded = rodio::Decoder::builder()
            .with_data(Feed(Mutex::new(stream)))
            .with_mime_type("audio/ogg")
            .with_seekable(false)
            .build()
            .unwrap();

        assert_eq!(decoded.take(uninterrupted).count(), uninterrupted);
    }

    /// A feeder that has gone is the one thing that does end it.
    #[test]
    fn a_dropped_feeder_ends_the_source() {
        let (tx, mut r) = ring();
        drop(tx);
        assert_eq!(r.next(), None);
    }

    /// Whatever is left in hand is played out before the feeder is missed.
    #[test]
    fn held_samples_outlive_the_feeder() {
        let (tx, mut r) = ring();
        tx.send(vec![1.0]).unwrap();
        drop(tx);
        assert_eq!(r.next(), Some(1.0));
        assert_eq!(r.next(), None);
    }

    #[test]
    fn playlist_uses_each_file_once_before_reshuffling() {
        let files = (0..4)
            .map(|n| PathBuf::from(format!("track-{n}.ogg")))
            .collect();
        let mut playlist = Playlist {
            files,
            next: 0,
            last: None,
        };

        let first: Vec<_> = (0..4).map(|_| playlist.next().unwrap()).collect();
        let second: Vec<_> = (0..4).map(|_| playlist.next().unwrap()).collect();

        let mut first_unique = first.clone();
        first_unique.sort();
        first_unique.dedup();
        let mut second_unique = second.clone();
        second_unique.sort();
        second_unique.dedup();
        assert_eq!(first_unique.len(), 4);
        assert_eq!(second_unique.len(), 4);
        assert_ne!(first.last(), second.first());
    }

    #[test]
    fn supported_directory_extensions_are_limited_to_stream_codecs() {
        assert!(supported_file(Path::new("pace.ogg")));
        assert!(supported_file(Path::new("dive.MP3")));
        assert!(!supported_file(Path::new("hime.flac")));
        assert!(!supported_file(Path::new("notes.txt")));
    }

    /// The ordinary block, and the two shapes that used to be got wrong: an apostrophe in
    /// the title, and a block with nothing after the title but padding.
    #[test]
    fn reads_a_stream_title() {
        let block = |s: &str| {
            let mut b = s.as_bytes().to_vec();
            b.resize(b.len().div_ceil(16) * 16, 0);
            b
        };

        assert_eq!(
            stream_title(&block(
                "StreamTitle='Boards of Canada - Roygbiv';StreamUrl='';"
            )),
            Some(Some("Boards of Canada - Roygbiv".to_string()))
        );
        assert_eq!(
            stream_title(&block("StreamTitle='Don't Stop';StreamUrl='';")),
            Some(Some("Don't Stop".to_string()))
        );
        assert_eq!(
            stream_title(&block("StreamTitle='Alone At Last'")),
            Some(Some("Alone At Last".to_string()))
        );
        // The station saying it is playing nothing in particular, which is not the same
        // as a block that never mentioned a title.
        assert_eq!(
            stream_title(&block("StreamTitle='';StreamUrl='';")),
            Some(None)
        );
        assert_eq!(stream_title(&block("StreamUrl='http://example';")), None);
        assert_eq!(stream_title(&[0u8; 16]), None);
    }

    /// The whole point of the reader: what comes out is the audio and nothing but, whatever
    /// size the reads happen to be. A byte of metadata handed to the decoder is a click.
    #[test]
    fn icy_keeps_the_metadata_out_of_the_audio() {
        // Two spans of audio with a titled block between them and an empty one behind it.
        let mut raw = vec![b'a'; 8];
        raw.push(2); // 32 bytes of text
        let mut meta = b"StreamTitle='One - Two';".to_vec();
        meta.resize(32, 0);
        raw.extend_from_slice(&meta);
        raw.extend_from_slice(&[b'b'; 8]);
        raw.push(0); // nothing has changed
        raw.extend_from_slice(&[b'c'; 3]);

        let state = Arc::new(Mutex::new(AudioState::default()));
        let mut icy = Icy {
            inner: io::Cursor::new(raw),
            metaint: 8,
            left: 8,
            title: TitleSink {
                state: Some(state.clone()),
                generation: GENERATION.load(Ordering::SeqCst),
            },
        };

        // Read in threes, so a block boundary lands mid-request rather than on one.
        let mut out = Vec::new();
        let mut buf = [0u8; 3];
        loop {
            match icy.read(&mut buf) {
                Ok(0) | Err(_) => break,
                Ok(n) => out.extend_from_slice(&buf[..n]),
            }
        }

        assert_eq!(out, b"aaaaaaaabbbbbbbbccc");
        assert_eq!(state.lock().unwrap().title.as_deref(), Some("One - Two"));
    }

    /// A run that has been superseded says nothing: the feeder is still decoding the
    /// station that was switched away from, and its title is not the answer any more.
    #[test]
    fn a_superseded_title_is_dropped() {
        let state = Arc::new(Mutex::new(AudioState::default()));
        let sink = TitleSink {
            state: Some(state.clone()),
            generation: GENERATION.load(Ordering::SeqCst).wrapping_sub(1),
        };
        sink.set(Some("Too Late".to_string()));
        assert_eq!(state.lock().unwrap().title, None);
    }

    /// What cpal can see, and which of them this would pick. Needs no network and makes
    /// no noise; it is the first thing to run when audio reports itself playing and
    /// nothing comes out, because the answer is usually that the chosen device is a card
    /// rather than the sound server.
    #[test]
    #[ignore]
    fn lists_output_devices() {
        let host = cpal::default_host();
        println!("host: {}", host.id().name());
        match host.default_output_device() {
            Some(d) => println!("default: {:?}", pcm_id(&d)),
            None => println!("default: none"),
        }
        match host.output_devices() {
            Ok(devices) => {
                for d in devices {
                    let id = pcm_id(&d);
                    let mark = match id.as_str() {
                        NULL_PCM => "<- the trap",
                        i if SERVER_PCMS.contains(&i) => "<- server",
                        _ => "",
                    };
                    println!(
                        "  {id:20} {mark:12} opens={}  {}",
                        d.default_output_config().is_ok(),
                        d.description()
                            .map(|x| x.name().to_string())
                            .unwrap_or_default()
                    );
                }
            }
            Err(e) => println!("output_devices: {e}"),
        }
        println!(
            "open_device: {:?}",
            open_device().map(|_| "ok").map_err(|e| format!("{e:#}"))
        );
    }
}
