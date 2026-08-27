//! Host playback keeps station networking out of Unity because FMOD lacks TLS/AAC and requires Icecast's omitted `Content-Length`; a feeder decodes URLs/files into a callback-safe ring.

use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::mpsc::Receiver;
use std::sync::{Arc, Mutex};
use std::time::Duration;

use serde::Serialize;

mod playback;
mod station;

pub(crate) const CONNECT: Duration = Duration::from_secs(15);
pub(crate) const REOPEN_PAUSE: Duration = Duration::from_secs(2);
pub(crate) const QUEUE_POLL: Duration = Duration::from_millis(5);

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
        if crate::runtime::is_slopcar() {
            self.reject(
                "audio playback is unavailable in slopcar; playback stays in the native game",
            );
            return;
        }
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
                // A reconnecting game replays its selection after the daemon has already
                // accepted it. Keep the existing feeder and playlist in that case: opening
                // the same directory or stream again would reset its position.
                if let Some((_, player)) = device.as_ref() {
                    if active_source(&state, &source) {
                        player.set_volume(volume);
                        state.lock().unwrap_or_else(|p| p.into_inner()).volume = volume;
                        continue;
                    }
                }
                GENERATION.fetch_add(1, Ordering::SeqCst);

                if device.is_none() {
                    match playback::open_device() {
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

                match playback::start(&source, player, &state) {
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

fn active_source(state: &Arc<Mutex<AudioState>>, source: &str) -> bool {
    let s = state.lock().unwrap_or_else(|p| p.into_inner());
    s.playing && s.source.as_deref() == Some(source)
}

fn fail(state: &Arc<Mutex<AudioState>>, why: String) {
    tracing::warn!("audio: {why}");
    let mut s = state.lock().unwrap_or_else(|p| p.into_inner());
    s.playing = false;
    s.source = None;
    s.title = None;
    s.error = Some(why);
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
mod tests {
    use super::{
        enqueue_chunk, local_title, open_device, pcm_id, stream_title, supported_file,
        wait_for_retry, AudioState, Feed, Icy, Playlist, Reconnect, Ring, StreamBody,
        StreamConnector, TitleSink, CONNECT, GENERATION, NULL_PCM, RING, SERVER_PCMS,
    };
    use rodio::cpal::traits::{DeviceTrait, HostTrait};
    use rodio::{cpal, ChannelCount, Sample, SampleRate, Source};
    use std::collections::VecDeque;
    use std::io::{self, BufRead, BufReader, Read, Seek, SeekFrom, Write};
    use std::net::TcpListener;
    use std::path::{Path, PathBuf};
    use std::sync::atomic::{AtomicBool, AtomicU64, AtomicUsize, Ordering};
    use std::sync::mpsc::{sync_channel, SyncSender};
    use std::sync::{Arc, Mutex};
    use std::time::Duration;

    #[test]
    fn active_source_only_matches_the_current_playing_source() {
        let state = Arc::new(Mutex::new(AudioState {
            playing: true,
            source: Some("same".to_string()),
            ..Default::default()
        }));

        assert!(super::active_source(&state, "same"));
        assert!(!super::active_source(&state, "other"));

        state.lock().unwrap().playing = false;
        assert!(!super::active_source(&state, "same"));
    }

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

        // Exercise different source shapes - stereo first, so it is the one that would have
        // done the latching. The final source is the only one that resamples *down*.
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
        let encoded = include_bytes!("../../../mod/Sounds/SlopWorld/silent.ogg");
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

    #[test]
    fn an_active_title_updates_clears_and_ignores_duplicates() {
        let state = Arc::new(Mutex::new(AudioState::default()));
        let sink = TitleSink {
            state: Some(state.clone()),
            generation: GENERATION.load(Ordering::SeqCst),
        };

        sink.set(Some("Now Playing".to_string()));
        assert_eq!(state.lock().unwrap().title.as_deref(), Some("Now Playing"));
        sink.set(Some("Now Playing".to_string()));
        sink.set(None);
        assert_eq!(state.lock().unwrap().title, None);

        TitleSink {
            state: None,
            generation: sink.generation,
        }
        .set(Some("Nowhere".to_string()));
    }

    #[test]
    fn playlist_directories_filter_supported_files_and_reject_empty_ones() {
        let dir = std::env::temp_dir().join(format!(
            "slopd-audio-playlist-{}-{}",
            std::process::id(),
            uuid::Uuid::new_v4()
        ));
        std::fs::create_dir_all(dir.join("nested.ogg")).unwrap();
        std::fs::write(dir.join("notes.txt"), b"not audio").unwrap();
        assert!(Playlist::from_dir(&dir).is_err());

        for name in ["one.mp3", "two.OGG", "ignored.flac"] {
            std::fs::write(dir.join(name), b"fixture").unwrap();
        }
        let playlist = Playlist::from_dir(&dir).unwrap();
        let mut names = playlist
            .files
            .iter()
            .map(|path| path.file_name().unwrap().to_string_lossy().into_owned())
            .collect::<Vec<_>>();
        names.sort();
        assert_eq!(names, ["one.mp3", "two.OGG"]);
        assert_eq!(
            local_title(Path::new("/music/One Track.ogg")).as_deref(),
            Some("One Track")
        );
        assert_eq!(local_title(Path::new("/")), None);

        std::fs::remove_dir_all(dir).unwrap();
    }

    struct ReadError(io::ErrorKind);

    impl Read for ReadError {
        fn read(&mut self, _: &mut [u8]) -> io::Result<usize> {
            Err(io::Error::new(self.0, "synthetic read failure"))
        }
    }

    #[test]
    fn reconnect_preserves_invalid_data_and_empty_reads_do_nothing() {
        let mut reopened = false;
        let mut stream = Reconnect {
            inner: Box::new(ReadError(io::ErrorKind::InvalidData)),
            reopen: || {
                reopened = true;
                Ok(Box::new(io::Cursor::new(Vec::new())) as StreamBody)
            },
            source: "synthetic fixture".to_string(),
            generation: GENERATION.load(Ordering::SeqCst),
            read_since_open: false,
        };

        assert_eq!(stream.read(&mut []).unwrap(), 0);
        assert_eq!(
            stream.read(&mut [0; 1]).unwrap_err().kind(),
            io::ErrorKind::InvalidData
        );
        drop(stream);
        assert!(!reopened);
    }

    #[test]
    fn icy_reports_truncated_metadata_and_skips_it_for_empty_reads() {
        let mut icy = Icy {
            inner: io::Cursor::new(vec![1, b'x']),
            metaint: 4,
            left: 0,
            title: TitleSink {
                state: None,
                generation: GENERATION.load(Ordering::SeqCst),
            },
        };

        assert_eq!(icy.read(&mut []).unwrap(), 0);
        assert_eq!(
            icy.read(&mut [0; 1]).unwrap_err().kind(),
            io::ErrorKind::UnexpectedEof
        );
    }

    #[test]
    fn live_feed_reads_but_never_seeks() {
        let mut feed = Feed(Mutex::new(io::Cursor::new(b"audio".to_vec())));
        let mut bytes = Vec::new();
        feed.read_to_end(&mut bytes).unwrap();
        assert_eq!(bytes, b"audio");
        assert_eq!(
            feed.seek(SeekFrom::Start(0)).unwrap_err().kind(),
            io::ErrorKind::Unsupported
        );
    }

    #[test]
    fn enqueue_stops_for_a_missing_consumer_or_superseded_full_ring() {
        let (tx, rx) = sync_channel(1);
        drop(rx);
        let ready = AtomicBool::new(false);
        let queued = AtomicUsize::new(0);
        assert!(!enqueue_chunk(
            &tx,
            vec![0.1, 0.2],
            &ready,
            &queued,
            2,
            GENERATION.load(Ordering::SeqCst),
        ));
        assert_eq!(queued.load(Ordering::Acquire), 0);

        let (tx, _rx) = sync_channel(1);
        tx.send(vec![1.0]).unwrap();
        assert!(!enqueue_chunk(
            &tx,
            vec![0.3],
            &ready,
            &queued,
            2,
            GENERATION.load(Ordering::SeqCst).wrapping_sub(1),
        ));
        assert_eq!(queued.load(Ordering::Acquire), 0);
    }

    #[test]
    fn ring_reports_frame_aligned_spans_and_its_audio_shape() {
        let (_tx, mut ring) = ring();
        ring.channels = ChannelCount::new(3).unwrap();
        ring.rate = SampleRate::new(22050).unwrap();

        assert_eq!(ring.current_span_len(), Some(8193));
        assert_eq!(ring.channels().get(), 3);
        assert_eq!(ring.sample_rate().get(), 22050);
        assert_eq!(ring.total_duration(), None);
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
