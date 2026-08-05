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

/// How long to wait on a station before calling the connection failed. The body itself
/// is never timed out: it is not supposed to end.
const CONNECT: Duration = Duration::from_secs(15);

/// A source that runs dry is opened again - a file loops, a dropped station reconnects -
/// but not instantly and not forever, or a station that has gone becomes a spin.
const REOPEN_PAUSE: Duration = Duration::from_secs(2);
const REOPEN_TRIES: u32 = 20;

/// What the mod is told. `error` is the last thing that went wrong, kept after the fact
/// so a pick that failed can be reported rather than just leaving silence.
#[derive(Clone, Debug, Default, PartialEq, Serialize)]
pub struct AudioState {
    pub playing: bool,
    pub source: Option<String>,
    pub volume: f32,
    pub error: Option<String>,
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

                player.set_volume(volume);
                // Not `Player::clear`: that pauses the player - and nothing appended to a
                // paused player is ever pulled, which is silence with no sign of itself -
                // and it blocks until the current source ends, which a station does not.
                // The generation bump above is what retires the old source: its `Ring`
                // answers `None` on the next callback and the player moves on to this one.
                player.play();

                match start(&source, player) {
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
            }
        }
    }
}

fn fail(state: &Arc<Mutex<AudioState>>, why: String) {
    tracing::warn!("audio: {why}");
    let mut s = state.lock().unwrap_or_else(|p| p.into_inner());
    s.playing = false;
    s.source = None;
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

/// Opens the source once here, so a station that will not answer is an error the mod can
/// be told about, then hands the rest to a feeder.
fn start(source: &str, player: &rodio::Player) -> Result<()> {
    let generation = GENERATION.load(Ordering::SeqCst);

    let first = open_source(source)?;
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
        .spawn(move || feed(first, source, channels, rate, tx, generation))
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
                match open_source(&source) {
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

fn open_source(source: &str) -> Result<Box<dyn Source + Send>> {
    if source.starts_with("http://") || source.starts_with("https://") {
        open_stream(source)
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

fn open_stream(url: &str) -> Result<Box<dyn Source + Send>> {
    // No Icy-MetaData header, so the station sends none: title bytes spliced into the
    // audio would be decoded as audio. No body timeout either - the body never ends.
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
        .call()
        .with_context(|| format!("connecting to {url}"))?;

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

    let body = BufReader::with_capacity(64 * 1024, response.into_body().into_reader());

    let decoded = rodio::Decoder::builder()
        .with_data(Feed(Mutex::new(body)))
        .with_mime_type(&mime)
        .with_seekable(false)
        .build()
        .with_context(|| format!("decoding {mime} from {url}"))?;

    Ok(Box::new(decoded))
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
    fn current_span_len(&self) -> Option<usize> {
        None
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

    /// Everything but the speakers: connect, demux, decode. Needs the network, so it is
    /// not part of `cargo test`, but it needs no audio device and so runs anywhere -
    /// which is what makes it the one to reach for when a station has gone quiet.
    #[test]
    #[ignore]
    fn decodes_the_station() {
        let decoded = open_source("https://stream.radioparadise.com/mp3-128")
            .expect("the station should open");

        assert_eq!(decoded.channels().get(), 2);
        assert_eq!(decoded.sample_rate().get(), 44100);

        // A second of it. Digital silence would mean the bytes arrived and meant nothing.
        let samples: Vec<Sample> = decoded.take(44100 * 2).collect();
        assert_eq!(samples.len(), 44100 * 2);
        assert!(
            samples.iter().any(|s| s.abs() > 0.001),
            "a second of the station decoded to silence"
        );
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
