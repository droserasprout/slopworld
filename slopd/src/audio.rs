//! The jukebox's sound, played on the host.
//!
//! It lives here rather than in the game because the game cannot play it. Unity 2022
//! refuses to send a cleartext request at all, FMOD - which is what actually fetches the
//! audio once a clip is streamed - has no TLS and no AAC decoder on desktop, and it will
//! not begin playback on a response with no `Content-Length`, which is every Icecast
//! stream there is. Four runs of the game found four walls. Out here it is a socket, a
//! decoder and an output device.
//!
//! The mod says what to play and how loud; it holds no opinion about how. A source is a
//! URL for the station or an absolute path for the built-in track - anything that is not
//! `http://` or `https://` is a file, which is also why the mod sends a path rather than a
//! `file://` URL: there is then nothing to percent-decode and no way to get it wrong.
//!
//! Threads. The command loop owns the device and the mixer. Opening a source blocks it,
//! deliberately: an error belongs to the pick that caused it, and the mod is told. Once a
//! source is open a feeder thread decodes it into a ring, because the audio callback must
//! never wait on a socket - an empty ring is silence for a moment, not a stop.

use std::io::{self, BufReader, Read, Seek, SeekFrom};
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::mpsc::{sync_channel, Receiver, SyncSender, TryRecvError};
use std::sync::{Arc, Mutex};
use std::time::Duration;

use anyhow::{anyhow, Context, Result};
use rodio::cpal::traits::{DeviceTrait, HostTrait};
use rodio::{cpal, ChannelCount, Sample, SampleRate, Source};
use serde::Serialize;

/// Samples per hop between the feeder and the callback. Small enough that a stop is
/// noticed promptly, large enough that the channel is not the work.
const CHUNK: usize = 4096;

/// Hops the ring holds. At 44.1kHz stereo this is about six seconds of audio, which is
/// what a station's hiccup costs and what the feeder is allowed to run ahead by - the
/// send blocking on a full ring is what paces the download to playback.
const RING: usize = 64;

/// Samples a ring calls a span, before frame alignment. This is not a buffer size: it is
/// how often the mixer is made to look at what it is playing. See `Ring::current_span_len`,
/// which is where the reason lives.
const SPAN: usize = 8192;

/// How long to wait on a station before calling the connection failed. The body itself
/// is never timed out: it is not supposed to end.
const CONNECT: Duration = Duration::from_secs(15);

/// A source that runs dry is opened again - a file loops, a dropped station reconnects -
/// but not instantly and not forever, or a station that has gone becomes a spin.
const REOPEN_PAUSE: Duration = Duration::from_secs(2);
const REOPEN_TRIES: u32 = 20;

/// What the mod is told. `error` is the last thing that went wrong, kept after the fact
/// so a pick that failed can be reported rather than just leaving silence. `title` is what
/// the station says it is playing, which arrives spliced into the audio and so is empty for
/// a while after every pick - and forever for a file, which says nothing about itself.
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
        // Detached on purpose: the daemon outlives it in every case that matters, and a
        // join on shutdown would only wait for a socket to notice it has been closed.
        let spawned = std::thread::Builder::new()
            .name("slopd audio".into())
            .spawn(move || run(rx, worker_state));
        if let Err(e) = spawned {
            tracing::error!("audio thread would not start: {e}");
        }

        Audio { tx, state }
    }

    /// `source` is a URL or an absolute path; `volume` is 0..1.
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

    pub fn state(&self) -> AudioState {
        self.state.lock().unwrap_or_else(|p| p.into_inner()).clone()
    }
}

impl Default for Audio {
    fn default() -> Self {
        Audio::new()
    }
}

/// Watches the player and broadcasts what it is doing. A poll rather than a callback out
/// of the audio thread: what changes without being asked for is a station dropping or a
/// device that was not there, and neither is worth a millisecond. The mod hears about its
/// own picks on the same beat as everything else.
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

/// Which run of the player a thread belongs to. Every Play bumps it, and a feeder
/// carrying an older one puts itself down - which is what keeps the last source from
/// pouring into a ring nobody is reading.
static GENERATION: AtomicU64 = AtomicU64::new(0);

fn run(rx: Receiver<Cmd>, state: Arc<Mutex<AudioState>>) {
    // Opened on the first Play rather than here: a machine with no sink should say so
    // when somebody asks for music, not on the daemon's way up.
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

                // Whatever was named is not what is about to play. Cleared before the
                // source is opened rather than after, so a station that names itself
                // promptly is not cleared by the pick that asked for it.
                state.lock().unwrap_or_else(|p| p.into_inner()).title = None;

                player.set_volume(volume);
                // Not `Player::clear`: that pauses the player - and nothing appended to a
                // paused player is ever pulled, which is silence with no sign of itself -
                // and it blocks until the current source ends, which a station does not.
                // The generation bump above is what retires the old source: its `Ring`
                // answers `None` on the next callback and the player moves on to this one.
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

/// The one PCM that must never be chosen. It accepts every sample and discards it, opens
/// without complaint on any machine, and sorts first in ALSA's enumeration - so it is what
/// a "try them all until one opens" fallback lands on. Playing to it looks exactly like
/// working: no error, no sound, and nothing in `pavucontrol`, because it never goes near
/// the sound server.
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

/// Named rather than default, and never `open_default_sink`. That helper falls back by
/// walking every output device and taking the first that opens - and on a machine whose
/// `default` PCM is not reachable, the first that opens is `null`. See `NULL_PCM`: this is
/// what "playing, no sound, not in pavucontrol" was. A device that is not the one in use
/// is not a fallback, it is a wrong answer, so a failure here stays a failure.
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

/// Where a station's `StreamTitle` goes. Held by the reader, which is on the far side of the
/// decoder and two threads from anyone who asks: a title arrives spliced into the audio,
/// minutes after the source it belongs to was opened. Hence the generation, checked on the
/// way in - a station being switched away from must not name the one that replaced it.
/// `state` is an Option so a source nobody wants the titles of - a file, which has none, or
/// a test - has somewhere to send them.
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

    let first = open_source(source, &title)?;
    let channels = first.channels();
    let rate = first.sample_rate();

    let (tx, rx) = sync_channel::<Vec<Sample>>(RING);
    player.append(Ring {
        rx,
        held: Vec::new(),
        at: 0,
        channels,
        rate,
        generation,
    });

    let source = source.to_string();
    std::thread::Builder::new()
        .name("slopd audio feed".into())
        .spawn(move || feed(first, source, channels, rate, tx, generation, title))
        .context("starting the feeder")?;

    Ok(())
}

/// Decodes into the ring until the source runs dry, then opens it again: a file loops,
/// and a station that dropped reconnects. Stops for good when the run it belongs to has
/// been superseded, when the ring's other end has gone, or when the source will not come
/// back.
fn feed(
    first: Box<dyn Source + Send>,
    source: String,
    channels: ChannelCount,
    rate: SampleRate,
    tx: SyncSender<Vec<Sample>>,
    generation: u64,
    title: TitleSink,
) {
    let mut current = Some(first);
    let mut tries = 0;

    loop {
        let decoded = match current.take() {
            Some(d) => d,
            None => {
                if tries >= REOPEN_TRIES {
                    tracing::warn!("audio: {source} would not come back");
                    return;
                }
                tries += 1;
                std::thread::sleep(REOPEN_PAUSE);
                if generation != GENERATION.load(Ordering::SeqCst) {
                    return;
                }
                match open_source(&source, &title) {
                    Ok(d) if d.channels() == channels && d.sample_rate() == rate => d,
                    // A file that came back as something else would play at the wrong
                    // speed, which is worse than stopping and saying so.
                    Ok(_) => {
                        tracing::warn!("audio: {source} came back in another format");
                        return;
                    }
                    Err(e) => {
                        tracing::debug!("audio: reopening {source}: {e:#}");
                        continue;
                    }
                }
            }
        };

        // A source that played is a source worth waiting for again.
        tries = 0;

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
            if tx
                .send(std::mem::replace(&mut chunk, Vec::with_capacity(CHUNK)))
                .is_err()
            {
                return;
            }
        }
    }
}

fn open_source(source: &str, title: &TitleSink) -> Result<Box<dyn Source + Send>> {
    if source.starts_with("http://") || source.starts_with("https://") {
        open_stream(source, title)
    } else {
        open_file(source)
    }
}

fn open_file(path: &str) -> Result<Box<dyn Source + Send>> {
    let file = std::fs::File::open(path).with_context(|| format!("opening {path}"))?;
    let len = file.metadata().map(|m| m.len()).ok();

    let mut builder = rodio::Decoder::builder()
        .with_data(BufReader::new(file))
        .with_seekable(true)
        .with_hint(path);
    if let Some(len) = len {
        builder = builder.with_byte_len(len);
    }

    Ok(Box::new(builder.build().context("decoding")?))
}

fn open_stream(url: &str, title: &TitleSink) -> Result<Box<dyn Source + Send>> {
    // `Icy-MetaData: 1` asks for the titles, which arrive *inside* the body - see `Icy`.
    // Nothing else may see those bytes: handed to the decoder they are audio, and they are
    // not. No body timeout either - the body never ends.
    let agent: ureq::Agent = ureq::Agent::config_builder()
        .timeout_connect(Some(CONNECT))
        .timeout_recv_response(Some(CONNECT))
        .timeout_recv_body(None)
        .timeout_global(None)
        .build()
        .into();

    let response = agent
        .get(url)
        .header("User-Agent", "SlopWorld")
        .header("Icy-MetaData", "1")
        .call()
        .with_context(|| format!("connecting to {url}"))?;

    // Absent, or nonsense, means the station is not splicing anything in and the body is
    // audio from end to end. Which is also what happens when it ignores the request header.
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

    // The unpicking happens on the raw body, before any buffering: `Icy` counts the bytes
    // it hands on, and a reader that had already swallowed some would put it out of step.
    let raw: Box<dyn Read + Send> = match metaint {
        Some(n) => {
            tracing::debug!("audio: {url} splices titles every {n} bytes");
            Box::new(Icy {
                inner: response.into_body().into_reader(),
                metaint: n,
                left: n,
                title: title.clone(),
            })
        }
        None => Box::new(response.into_body().into_reader()),
    };
    let body = BufReader::with_capacity(64 * 1024, raw);

    let decoded = rodio::Decoder::builder()
        .with_data(Feed(Mutex::new(body)))
        .with_mime_type(&mime)
        .with_seekable(false)
        .build()
        .with_context(|| format!("decoding {mime} from {url}"))?;

    Ok(Box::new(decoded))
}

/// Takes the station's titles back out of its audio.
///
/// With `Icy-MetaData: 1` on the request the server answers an `icy-metaint`, and from then
/// on the body is that many bytes of audio, one length byte, that many *sixteens* of text,
/// and around again. The text is `StreamTitle='Artist - Song';StreamUrl='...';` padded with
/// NULs, and a length of zero - which is nearly all of them - means nothing has changed.
///
/// This hands on the audio and keeps the text, so what reaches the decoder is sound and only
/// sound. Getting that wrong is not a missing title, it is a click every few seconds.
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
        // Never past the block boundary, however much was asked for: what is on the other
        // side of it is not audio.
        let want = buf.len().min(self.left);
        let n = self.inner.read(&mut buf[..want])?;
        self.left -= n;
        Ok(n)
    }
}

/// The `StreamTitle` out of one block, or `None` when it carries none - which is nothing to
/// say rather than nothing playing. `Some(None)` is the station clearing it, which is what
/// an advertisement break looks like.
///
/// Latin-1 by the specification and UTF-8 in practice, so it is read lossily rather than
/// refused: a mangled dash beats no title.
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
}

impl Iterator for Ring {
    type Item = Sample;

    fn next(&mut self) -> Option<Sample> {
        // Superseded, or stopped. This is the retirement `Player::clear` could not do:
        // clear pauses the player and blocks on a source that never ends.
        if self.generation != GENERATION.load(Ordering::SeqCst) {
            return None;
        }
        if self.at >= self.held.len() {
            match self.rx.try_recv() {
                Ok(next) => {
                    self.held = next;
                    self.at = 0;
                }
                // An underrun. The feeder is still there and will catch up; a None here
                // would end the source and the music with it.
                Err(TryRecvError::Empty) => return Some(0.0),
                Err(TryRecvError::Disconnected) => return None,
            }
        }
        let sample = self.held[self.at];
        self.at += 1;
        Some(sample)
    }
}

impl Source for Ring {
    /// **Finite, and never `None`.** The mixer wraps what it is given in rodio's
    /// `UniformSourceIterator`, which builds the channel and rate converters from the
    /// source's own `channels()` and `sample_rate()` - and rebuilds them only at a span
    /// boundary. `None` means "one span, for ever", so the converters built for the *first*
    /// stream of the session stay bolted on to every stream appended after it: the player
    /// is opened once and outlives every station switch.
    ///
    /// That is a station playing at the wrong speed. Mono read as stereo is 2x - WeFunk's
    /// 64k - and RP's 32k, which is 22050 mono, read as 44100 stereo is 4x. Stereo 44100
    /// was every stream on the list until it was not, which is why nothing showed it.
    ///
    /// Frame-aligned, or the channel converter loses which sample belongs to which side.
    /// `SPAN` samples is a fraction of a second and well under the 32768 the iterator
    /// clamps to; the shape never actually changes within one ring, so the rebuild costs a
    /// converter and nothing else.
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
            },
        )
    }

    /// **One player, several stations in a row, which is the real path**: the device is
    /// opened once and every switch appends behind it. A ring that called itself one endless
    /// span left rodio's converters built for whichever stream came first, so a mono station
    /// after a stereo one played at 2x - WeFunk's 64k - and RP's 32k, mono at 22050, at 4x.
    /// Stereo 44100 was every stream on the list until it was not.
    ///
    /// Measured with a ramp rather than by counting a tone: the value of a sample says
    /// *where* in the station it came from, so reading one at a known moment is reading the
    /// speed directly. Counting instead would measure the tail, where ending a ring early
    /// leaves the converters a few hundred samples to flush - true of the fixed player as
    /// well as the broken one. Needs neither network nor device.
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

    /// The presets whose shapes differ, with what each decodes to. Kept in step with
    /// `Radio.Stations` by hand: this is a diagnostic pointed at the open web, not a second
    /// copy of the menu. **Not all of them are stereo 44100**, which is the whole of why
    /// `start` takes the channel count and rate off the source and why a ring declares a
    /// finite span - see `Ring::current_span_len`. RP's 32k is mono at 22050 and WeFunk
    /// serves one 64k stream, mono; WALM serves one at 320k and at **48000**, the only thing
    /// on the list that is not 44100. Everything else matches RP's 128.
    const STATIONS: &[(&str, u16, u32)] = &[
        ("https://stream.radioparadise.com/mp3-128", 2, 44100),
        ("https://stream.radioparadise.com/mp3-32", 1, 22050),
        ("https://s-00.wefunkradio.com:8443/wefunk64.mp3", 1, 44100),
        ("https://icecast.walmradio.com:8443/classic", 2, 48000),
    ];

    /// Everything but the speakers: connect, demux, decode, for every station on the list.
    /// Needs the network, so it is not part of `cargo test`, but it needs no audio device
    /// and so runs anywhere - which is what makes it the one to reach for when a station
    /// has gone quiet.
    #[test]
    #[ignore]
    fn decodes_the_station() {
        for &(url, channels, rate) in STATIONS {
            let nowhere = TitleSink {
                state: None,
                generation: 0,
            };
            let decoded = open_source(url, &nowhere).expect("the station should open");

            assert_eq!(decoded.channels().get(), channels, "{url}");
            assert_eq!(decoded.sample_rate().get(), rate, "{url}");

            // A second of it. Digital silence would mean the bytes arrived and meant
            // nothing. A second is `rate` frames, which is `rate * channels` samples.
            let want = rate as usize * channels as usize;
            let samples: Vec<Sample> = decoded.take(want).collect();
            assert_eq!(samples.len(), want, "{url}");
            assert!(
                samples.iter().any(|s| s.abs() > 0.001),
                "a second of {url} decoded to silence"
            );
        }
    }

    /// The other half of that, and the one that proves a station is still splicing titles
    /// in at all: `icy-metaint` is a header it is free to stop sending. Needs the network
    /// and no speakers. A block arrives every `metaint` bytes of *encoded* audio - about a
    /// second at 128k, two at 64k - so five seconds of decoding is more than enough for the
    /// first one either way.
    #[test]
    #[ignore]
    fn names_what_the_station_is_playing() {
        for &(url, channels, rate) in STATIONS {
            let state = Arc::new(Mutex::new(AudioState::default()));
            let sink = TitleSink {
                state: Some(state.clone()),
                generation: GENERATION.load(Ordering::SeqCst),
            };

            let decoded = open_source(url, &sink).expect("the station should open");
            let want = rate as usize * channels as usize * 5;
            let samples: Vec<Sample> = decoded.take(want).collect();
            assert_eq!(samples.len(), want, "{url}");

            let title = state.lock().unwrap().title.clone();
            println!("{url} title: {title:?}");
            assert!(title.is_some(), "five seconds of {url} named nothing");
        }
    }

    /// Makes noise and needs an output device as well as the network, so it is ignored
    /// twice over: `cargo test -- --ignored --nocapture` on a machine with speakers.
    #[test]
    #[ignore]
    fn plays_the_station() {
        let audio = Audio::new();
        audio.play("https://stream.radioparadise.com/mp3-128", 0.3);

        // Connect, decode and fill: this is a real station over a real line.
        std::thread::sleep(Duration::from_secs(8));

        let state = audio.state();
        println!("state: {state:?}");
        assert!(state.playing, "not playing: {:?}", state.error);
        assert_eq!(state.error, None);

        audio.stop();
        std::thread::sleep(Duration::from_millis(200));
        assert!(!audio.state().playing);
    }
}
