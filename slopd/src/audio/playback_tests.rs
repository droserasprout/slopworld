use super::*;
use crate::audio::test_support::{FakeOutput, FakeOutputState};
use std::time::Duration;

fn candidate() -> OpenedSource {
    let source = Path::new(env!("CARGO_MANIFEST_DIR")).join("../mod/Sounds/SlopWorld/silent.ogg");
    open(&source.to_string_lossy(), TitleSink::pending(None, 0)).unwrap()
}

fn output() -> FakeOutput {
    FakeOutput(Arc::new(FakeOutputState {
        opens: AtomicUsize::new(0),
        appends: AtomicUsize::new(0),
        volumes: std::sync::Mutex::new(Vec::new()),
    }))
}

#[test]
fn feeder_start_failure_does_not_change_output() {
    let output = output();
    let error = commit_with_spawn(candidate(), &output, 0.7, |_| {
        Err(std::io::Error::other("synthetic spawn failure"))
    })
    .err()
    .expect("startup fails");
    assert!(error.to_string().contains("starting the feeder"));
    assert_eq!(output.0.appends.load(Ordering::Acquire), 0);
    assert!(output.0.volumes.lock().unwrap().is_empty());
}

#[test]
fn cancellation_during_startup_discards_the_gated_feeder() {
    let output = output();
    let opened = candidate();
    let title = opened.title.clone();
    let (done_tx, done_rx) = std::sync::mpsc::channel();
    let committed = commit_with_spawn(opened, &output, 0.7, |feed| {
        std::thread::spawn(move || {
            feed();
            done_tx.send(()).unwrap();
        });
        title.cancel();
        Ok(())
    })
    .unwrap();
    assert!(committed.is_none());
    done_rx.recv_timeout(Duration::from_secs(2)).unwrap();
    assert_eq!(output.0.appends.load(Ordering::Acquire), 0);
    assert!(output.0.volumes.lock().unwrap().is_empty());
}

#[test]
fn backend_failure_is_latched_but_recoverable_underruns_do_not_retire_output() {
    let health = OutputHealth::new();
    health.record(&cpal::StreamError::BufferUnderrun);
    assert!(!health.failed.load(Ordering::Acquire));
    for _ in 0..100 {
        health.record(&cpal::StreamError::BackendSpecific {
            err: cpal::BackendSpecificError {
                description: "POLLERR".into(),
            },
        });
    }
    health.record(&cpal::StreamError::BufferUnderrun);
    assert!(health.failed.load(Ordering::Acquire));
    assert_eq!(health.backend_underruns.load(Ordering::Relaxed), 2);
}

#[test]
fn unavailable_preferred_devices_fall_back_in_order() {
    let mut attempted = Vec::new();
    let (id, ()) = open_first_available(
        [("pipewire".into(), 1), ("pulse".into(), 2)],
        || Some(("default".into(), 3)),
        |device| {
            attempted.push(device);
            if device < 3 {
                anyhow::bail!("server unavailable");
            }
            Ok(())
        },
    )
    .unwrap();
    assert_eq!(id, "default");
    assert_eq!(attempted, [1, 2, 3]);
}

#[test]
fn successful_preferred_device_does_not_probe_default() {
    let mut attempted = Vec::new();
    let (id, ()) = open_first_available(
        [("pipewire".into(), 1), ("pulse".into(), 2)],
        || panic!("default must not be queried after success"),
        |device| {
            attempted.push(device);
            if device == 1 {
                anyhow::bail!("pipewire unavailable");
            }
            Ok(())
        },
    )
    .unwrap();
    assert_eq!(id, "pulse");
    assert_eq!(attempted, [1, 2]);
}

#[test]
fn failed_devices_report_each_error_without_retrying_default_alias() {
    let mut attempted = Vec::new();
    let error = open_first_available(
        [("pipewire".into(), 1), ("pulse".into(), 2)],
        || Some(("pulse".into(), 2)),
        |device| -> Result<()> {
            attempted.push(device);
            anyhow::bail!("failure {device}")
        },
    )
    .unwrap_err();
    assert_eq!(attempted, [1, 2]);
    let message = error.to_string();
    assert!(message.contains("pipewire"));
    assert!(message.contains("failure 1"));
    assert!(message.contains("pulse"));
    assert!(message.contains("failure 2"));
}

#[test]
fn null_and_absent_default_devices_are_rejected() {
    for default in [None, Some(("null".into(), ()))] {
        let result = open_first_available(
            std::iter::empty::<(String, ())>(),
            || default,
            |()| -> Result<()> { panic!("null must never be opened") },
        );
        assert!(
            result
                .unwrap_err()
                .to_string()
                .contains("no usable output device")
        );
    }
}

#[test]
fn committed_ring_and_feeder_retire_only_with_their_own_player() {
    use crate::audio::control::Control;
    use std::sync::Mutex;
    #[derive(Default)]
    struct HeldOutput(Mutex<Option<Box<dyn Source + Send>>>);
    impl AudioOutput for HeldOutput {
        fn append(&self, source: Box<dyn Source + Send>) {
            *self.0.lock().unwrap() = Some(source);
        }
        fn set_volume(&self, _: f32) {}
        fn play(&self) {}
    }
    let control = Arc::new(Control::new());
    let source = Path::new(env!("CARGO_MANIFEST_DIR")).join("../mod/Sounds/SlopWorld/silent.ogg");
    let generation = control.play(source.to_str().unwrap());
    let title = TitleSink::pending_for(None, generation, control.clone());
    let opened = open(source.to_str().unwrap(), title).unwrap();
    let output = HeldOutput::default();
    let mut feeder = None;
    let committed = commit_with_spawn(opened, &output, 1.0, |work| {
        let other = Control::new();
        other.play("second");
        other.stop();
        feeder = Some(work);
        Ok(())
    })
    .unwrap();
    assert!(committed.is_some());
    assert_eq!(output.0.lock().unwrap().as_mut().unwrap().next(), Some(0.0));
    control.stop();
    assert_eq!(output.0.lock().unwrap().as_mut().unwrap().next(), None);
    // Release the accepted feeder after retirement; it must finish without decoding.
    feeder.unwrap()();
}

fn samples(data: Vec<f32>, rate: u32) -> Box<dyn Source + Send> {
    Box::new(rodio::buffer::SamplesBuffer::new(
        rodio::ChannelCount::new(1).unwrap(),
        rodio::SampleRate::new(rate).unwrap(),
        data,
    ))
}

fn feed_fixture(
    first: Box<dyn Source + Send>,
    title: TitleSink,
) -> (FeedArgs, crate::audio::queue::Consumer) {
    let (tx, rx) = queue::channel(16);
    let format = AudioFormat {
        channels: first.channels(),
        rate: first.sample_rate(),
    };
    (
        FeedArgs {
            first,
            source: "test source".into(),
            playlist: None,
            format,
            tx,
            generation: title.generation,
            title,
            ready: Arc::new(AtomicBool::new(false)),
            queued: Arc::new(AtomicUsize::new(0)),
        },
        rx,
    )
}

#[test]
fn feeder_preserves_short_tails_across_reopen_errors_and_format_changes() {
    let (args, mut rx) = feed_fixture(
        samples(vec![0.25; CHUNK + 3], 8000),
        TitleSink::pending(None, 0),
    );
    let mut attempts = 0;
    feed_with_reopen(args, |_, _| {
        attempts += 1;
        match attempts {
            1 => Some(Err(anyhow!("temporarily unavailable"))),
            2 => Some(Ok(samples(vec![0.9; 7], 16000))),
            3 => Some(Ok(samples(vec![0.5; 5], 8000))),
            4 => None,
            _ => panic!("retry loop did not terminate"),
        }
    });
    assert_eq!(attempts, 4);
    let chunks = rx.drain_chunks();
    assert_eq!(
        chunks.iter().map(Vec::len).collect::<Vec<_>>(),
        [CHUNK, 3, 5]
    );
    assert_eq!(chunks[0], vec![0.25; CHUNK]);
    assert_eq!(chunks[1], vec![0.25; 3]);
    assert_eq!(chunks[2], vec![0.5; 5]);
}

#[test]
fn cancellation_or_replacement_during_reopen_prevents_new_samples() {
    for replace in [false, true] {
        let title = TitleSink::pending(None, 0);
        let (args, mut rx) = feed_fixture(samples(vec![0.25; 3], 8000), title);
        let mut attempts = 0;
        feed_with_reopen(args, |_, title| {
            attempts += 1;
            if replace {
                title.current_generation.fetch_add(1, Ordering::SeqCst);
            } else {
                title.cancel();
            }
            Some(Ok(samples(vec![0.9; 7], 8000)))
        });
        assert_eq!(attempts, 1);
        assert_eq!(rx.drain_chunks(), vec![vec![0.25; 3]]);
    }
}

#[test]
fn feeder_advances_playlist_then_finishes_when_no_playable_tracks_remain() {
    let path = Path::new(env!("CARGO_MANIFEST_DIR")).join("../mod/Sounds/SlopWorld/silent.ogg");
    let decoded = open_file(&path).unwrap();
    let format = AudioFormat {
        channels: decoded.channels(),
        rate: decoded.sample_rate(),
    };
    let title = TitleSink::pending(None, 0);
    let (mut args, mut rx) = feed_fixture(
        Box::new(rodio::buffer::SamplesBuffer::new(
            format.channels,
            format.rate,
            vec![0.25; 3],
        )),
        title,
    );
    // A different format is skipped; the exhausted playlist must end, not reopen as a station.
    args.playlist = Some(Playlist {
        files: vec![path.with_extension("missing"), path],
        next: 0,
        last: None,
    });
    args.format.rate = rodio::SampleRate::new(format.rate.get() + 1).unwrap();
    feed_with_reopen(args, |_, _| {
        panic!("playlist must not use station recovery")
    });
    assert_eq!(rx.drain_chunks(), vec![vec![0.25; 3]]);
}

#[test]
fn feeder_delivers_next_playlist_track_after_a_short_first_track() {
    let path = Path::new(env!("CARGO_MANIFEST_DIR")).join("../mod/Sounds/SlopWorld/silent.ogg");
    let decoded = open_file(&path).unwrap();
    let format = AudioFormat {
        channels: decoded.channels(),
        rate: decoded.sample_rate(),
    };
    let title = TitleSink::pending(None, 0);
    let (mut args, mut rx) = feed_fixture(
        Box::new(rodio::buffer::SamplesBuffer::new(
            format.channels,
            format.rate,
            vec![0.25; 3],
        )),
        title.clone(),
    );
    args.playlist = Some(Playlist {
        files: vec![path],
        next: 0,
        last: None,
    });
    let feeder = std::thread::spawn(move || {
        feed_with_reopen(args, |_, _| panic!("playlist must not reopen as a station"))
    });
    let first = rx.recv_timeout(Duration::from_secs(2));
    let second = rx.recv_timeout(Duration::from_secs(2));
    title.cancel();
    drop(rx);
    feeder.join().unwrap();
    assert_eq!(first.unwrap(), vec![0.25; 3]);
    assert!(!second.unwrap().is_empty());
}

#[test]
fn retirement_during_output_io_discards_ring_and_releases_gated_feeder() {
    struct RetiringOutput {
        title: TitleSink,
        replace: bool,
        during_append: bool,
        ring: std::sync::Mutex<Option<Box<dyn Source + Send>>>,
    }
    impl RetiringOutput {
        fn retire(&self) {
            if self.replace {
                self.title.current_generation.fetch_add(1, Ordering::SeqCst);
            } else {
                self.title.cancel();
            }
        }
    }
    impl AudioOutput for RetiringOutput {
        fn set_volume(&self, _: f32) {}
        fn append(&self, source: Box<dyn Source + Send>) {
            *self.ring.lock().unwrap() = Some(source);
            if self.during_append {
                self.retire();
            }
        }
        fn play(&self) {
            if !self.during_append {
                self.retire();
            }
        }
    }
    for replace in [false, true] {
        for during_append in [false, true] {
            let opened = candidate();
            let output = RetiringOutput {
                title: opened.title.clone(),
                replace,
                during_append,
                ring: std::sync::Mutex::new(None),
            };
            let (done_tx, done_rx) = std::sync::mpsc::channel();
            let result = commit_with_spawn(opened, &output, 0.5, |feed| {
                std::thread::spawn(move || {
                    feed();
                    done_tx.send(()).unwrap();
                });
                Ok(())
            })
            .unwrap();
            assert!(result.is_none());
            done_rx.recv_timeout(Duration::from_secs(2)).unwrap();
            assert_eq!(output.ring.lock().unwrap().as_mut().unwrap().next(), None);
        }
    }
}

/// Exercise both Linux sound-server bridges with the production decoder/queue/mixer.
/// Run explicitly with pw-profiler alongside it: graph xruns need not reach CPAL.
/// Muted output keeps this diagnostic from playing a second audible soundtrack.
#[test]
#[ignore = "requires live sound servers; observe their graph errors with pw-profiler"]
fn compares_sound_server_bridges() {
    let host = cpal::default_host();
    let devices: Vec<_> = host.output_devices().unwrap().collect();
    let source = Path::new(env!("CARGO_MANIFEST_DIR")).join("../mod/Sounds/SlopWorld/OST");
    let mut runs = Vec::new();
    for id in ["pipewire", "pulse"] {
        let device = devices.iter().find(|d| pcm_id(d) == id).unwrap().clone();
        let health = Arc::new(OutputHealth::new());
        let callback_health = health.clone();
        let sink = rodio::DeviceSinkBuilder::from_device(device)
            .unwrap()
            .with_error_callback(move |error| callback_health.record(&error))
            .open_stream()
            .unwrap();
        let output = RodioOutput {
            id: "test".into(),
            _sink: sink,
            volume: Arc::new(AtomicU32::new(0.0f32.to_bits())),
            health,
        };
        let title = TitleSink::pending(None, 0);
        let opened = open(source.to_str().unwrap(), title.clone()).unwrap();
        assert!(commit(opened, &output, 0.0).unwrap().is_some());
        runs.push((id, output, title));
    }
    println!("bridge diagnostic ready: pid={}", std::process::id());
    std::thread::sleep(Duration::from_secs(60));
    for (id, output, title) in &runs {
        title.cancel();
        println!(
            "{id}: backend_underruns={} source_underruns={} failed={}",
            output.health.backend_underruns.load(Ordering::Relaxed),
            output.health.source_underruns.load(Ordering::Relaxed),
            output.failed(),
        );
    }
}

#[test]
fn output_preferences_fall_back_without_using_null_or_repeating_devices() {
    use crate::config::{AudioBackend, AudioOutputSettings};
    let settings = AudioOutputSettings {
        backend: AudioBackend::Pulse,
        device: "headphones".into(),
    };
    assert_eq!(
        preferred_ids(&settings),
        ["headphones", "pulse", "pipewire", "default"]
    );
    let mut attempts = Vec::new();
    let (id, ()) = open_first_available(
        preferred_ids(&settings)
            .into_iter()
            .map(|id| (id.clone(), id)),
        || Some(("default".into(), "default".into())),
        |id| {
            attempts.push(id.clone());
            if id == "pipewire" {
                Ok(())
            } else {
                Err(anyhow!("unavailable"))
            }
        },
    )
    .unwrap();
    assert_eq!(id, "pipewire");
    assert_eq!(attempts, ["headphones", "pulse", "pipewire"]);
    let settings = AudioOutputSettings {
        backend: AudioBackend::Default,
        device: "null".into(),
    };
    assert_eq!(preferred_ids(&settings), ["default", "pipewire", "pulse"]);
    assert_eq!(preferred_ids(&AudioOutputSettings::default()), SERVER_PCMS);
}
