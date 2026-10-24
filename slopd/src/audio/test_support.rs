//! Game-free output and ring fixtures shared by the audio owner tests.

use super::playback::{AudioOutput, OutputFactory};
use super::queue::{self, CHUNK, Consumer, Producer, RING, SendError};
use super::ring::Ring;
use rodio::{ChannelCount, Sample, SampleRate, Source};
use std::sync::atomic::{AtomicBool, AtomicU64, AtomicUsize, Ordering};
use std::sync::mpsc::{RecvTimeoutError, TryRecvError};
use std::sync::{Arc, Mutex};

pub(super) struct FakeOutputState {
    pub(super) opens: AtomicUsize,
    pub(super) appends: AtomicUsize,
    pub(super) volumes: Mutex<Vec<f32>>,
}

pub(super) struct FakeOutputFactory(pub(super) Arc<FakeOutputState>);

pub(super) struct FakeOutput(pub(super) Arc<FakeOutputState>);

impl OutputFactory for FakeOutputFactory {
    fn open(&self) -> anyhow::Result<Box<dyn AudioOutput>> {
        self.0.opens.fetch_add(1, Ordering::AcqRel);
        Ok(Box::new(FakeOutput(self.0.clone())))
    }
}

impl AudioOutput for FakeOutput {
    fn append(&self, _source: Box<dyn Source + Send>) {
        self.0.appends.fetch_add(1, Ordering::AcqRel);
    }

    fn set_volume(&self, volume: f32) {
        self.0.volumes.lock().unwrap().push(volume);
    }

    fn play(&self) {}
}

pub(super) fn ring() -> (Producer, Ring) {
    let (tx, rx) = queue::channel(RING);
    (
        tx,
        Ring {
            rx,
            held: [0.0; CHUNK],
            held_len: 0,
            underruns: Arc::new(AtomicUsize::new(0)),
            at: 0,
            channels: ChannelCount::new(2).unwrap(),
            rate: SampleRate::new(44100).unwrap(),
            // This fixture owns its generation domain independently of other players.
            generation: 0,
            current_generation: Arc::new(AtomicU64::new(0)),
            cancelled: Arc::new(AtomicBool::new(false)),
            ready: Arc::new(AtomicBool::new(true)),
            finished: Arc::new(AtomicBool::new(false)),
            queued: Arc::new(AtomicUsize::new(0)),
            prefill_samples: 1,
        },
    )
}

// Convenience adapters allocate only in tests; production handoff uses caller-owned arrays.
impl Producer {
    pub(super) fn send(&mut self, samples: &[Sample]) -> Result<(), SendError> {
        for chunk in samples.chunks(CHUNK) {
            self.try_send(chunk)?;
        }
        Ok(())
    }
}

impl Consumer {
    pub(super) fn drain_chunks(&mut self) -> Vec<Vec<Sample>> {
        let mut result = Vec::new();
        let mut samples = [0.0; CHUNK];
        while let Ok(len) = self.try_recv(&mut samples) {
            result.push(samples[..len].to_vec());
        }
        result
    }

    pub(super) fn recv_timeout(
        &mut self,
        timeout: std::time::Duration,
    ) -> Result<Vec<Sample>, RecvTimeoutError> {
        let deadline = std::time::Instant::now() + timeout;
        let mut samples = [0.0; CHUNK];
        loop {
            match self.try_recv(&mut samples) {
                Ok(len) => return Ok(samples[..len].to_vec()),
                Err(TryRecvError::Disconnected) => return Err(RecvTimeoutError::Disconnected),
                Err(TryRecvError::Empty) => {}
            }
            if std::time::Instant::now() >= deadline {
                return Err(RecvTimeoutError::Timeout);
            }
            std::thread::sleep(std::time::Duration::from_millis(1));
        }
    }
}
