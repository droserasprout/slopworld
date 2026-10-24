//! Exercise public requests through the real command worker, opener, and stream decoder.
use super::test_support::{FakeOutputFactory, FakeOutputState};
use super::*;
use std::io::{BufRead, BufReader, Write};
use std::sync::atomic::AtomicUsize;
use std::time::Instant;

fn wait_state(audio: &Audio, predicate: impl Fn(&AudioState) -> bool) -> AudioState {
    let deadline = Instant::now() + Duration::from_secs(5);
    loop {
        let state = audio.state();
        if predicate(&state) {
            return state;
        }
        assert!(
            Instant::now() < deadline,
            "audio did not reach expected state: {state:?}"
        );
        std::thread::sleep(Duration::from_millis(5));
    }
}

fn fixture() -> String {
    std::path::Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("../mod/Sounds/SlopWorld/silent.ogg")
        .to_string_lossy()
        .into_owned()
}

#[test]
fn public_stream_play_publishes_then_replaces_adjusts_volume_and_stops() {
    let Some(_) = crate::test_support::isolated_with_env(|command, _| {
        command.env_remove("SLOPD_RUNTIME");
    }) else {
        return;
    };
    let listener = std::net::TcpListener::bind("127.0.0.1:0").unwrap();
    let url = format!("http://{}/radio", listener.local_addr().unwrap());
    let server = std::thread::spawn(move || {
        let (mut stream, _) = listener.accept().unwrap();
        stream
            .set_read_timeout(Some(Duration::from_secs(5)))
            .unwrap();
        let mut request = BufReader::new(stream.try_clone().unwrap());
        let mut line = String::new();
        request.read_line(&mut line).unwrap();
        assert!(line.starts_with("GET /radio "), "{line}");
        loop {
            line.clear();
            assert!(request.read_line(&mut line).unwrap() > 0);
            if line == "\r\n" {
                break;
            }
        }
        let body = include_bytes!("../../../mod/Sounds/SlopWorld/silent.ogg");
        write!(stream, "HTTP/1.1 200 OK\r\nContent-Type: audio/ogg\r\nContent-Length: {}\r\nConnection: close\r\n\r\n", body.len()).unwrap();
        stream.write_all(body).unwrap();
    });
    let output = Arc::new(FakeOutputState {
        opens: AtomicUsize::new(0),
        appends: AtomicUsize::new(0),
        volumes: Mutex::new(Vec::new()),
    });
    let audio = Audio::with_factory(Arc::new(FakeOutputFactory(output.clone())));
    audio.play(&url, 0.35);
    let first = wait_state(&audio, |s| s.playing && s.source.as_deref() == Some(&url));
    assert_eq!(first.volume, 0.35);
    assert!(first.error.is_none());
    server.join().unwrap();
    assert_eq!(output.appends.load(Ordering::Acquire), 1);
    let generation = audio.inner.control.snapshot().1;
    let file = fixture();
    audio.play(&file, 0.7);
    let replacement = wait_state(&audio, |s| s.playing && s.source.as_deref() == Some(&file));
    assert_eq!(replacement.volume, 0.7);
    assert!(replacement.error.is_none());
    assert!(audio.inner.control.snapshot().1 > generation);
    assert_eq!(output.appends.load(Ordering::Acquire), 2);
    assert_eq!(
        output.opens.load(Ordering::Acquire),
        1,
        "replacement should reuse output"
    );
    audio.set_volume(2.0);
    wait_state(&audio, |s| (s.volume - 1.0).abs() < f32::EPSILON);
    assert_eq!(output.volumes.lock().unwrap().last(), Some(&1.0));
    audio.stop();
    let stopped = wait_state(&audio, |s| !s.playing && s.source.is_none());
    assert!(stopped.title.is_none());
    assert!(stopped.error.is_none());
    // Repeated stop must leave a player restartable, even for the same source.
    audio.stop();
    audio.play(&file, 0.2);
    wait_state(&audio, |s| {
        s.playing && (s.volume - 0.2).abs() < f32::EPSILON
    });
    assert_eq!(output.appends.load(Ordering::Acquire), 3);
    audio.stop();
    wait_state(&audio, |s| !s.playing && s.source.is_none());
}

struct PanicOutput(Arc<AtomicBool>);
impl playback::AudioOutput for PanicOutput {
    fn append(&self, _: Box<dyn rodio::Source + Send>) {}
    fn set_volume(&self, _: f32) {
        assert!(
            !self.0.load(Ordering::Acquire),
            "injected audio worker panic"
        );
    }
    fn play(&self) {}
}
struct PanicFactory(Arc<AtomicBool>);
impl OutputFactory for PanicFactory {
    fn open(&self) -> anyhow::Result<Box<dyn playback::AudioOutput>> {
        Ok(Box::new(PanicOutput(self.0.clone())))
    }
}

#[test]
fn worker_panic_clears_playback_and_later_requests_report_the_closed_channel() {
    let Some(_) = crate::test_support::isolated_with_env(|command, _| {
        command.env_remove("SLOPD_RUNTIME");
    }) else {
        return;
    };
    let panic = Arc::new(AtomicBool::new(false));
    let audio = Audio::with_factory(Arc::new(PanicFactory(panic.clone())));
    audio.play(&fixture(), 0.5);
    wait_state(&audio, |s| s.playing);
    panic.store(true, Ordering::Release);
    audio.set_volume(0.6);
    let failed = wait_state(&audio, |s| {
        s.error.as_deref() == Some("audio worker terminated unexpectedly")
    });
    assert!(!failed.playing);
    assert!(failed.source.is_none());
    assert!(failed.title.is_none());
    audio.play(&fixture(), 0.5);
    let unavailable = wait_state(&audio, |s| {
        s.error
            .as_deref()
            .is_some_and(|e| e.contains("audio worker unavailable during play"))
    });
    assert!(!unavailable.playing);
    assert!(unavailable.source.is_none());
}
