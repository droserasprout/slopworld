//! Game-free output and ring fixtures shared by the audio owner tests.

use super::playback::{AudioOutput, OutputFactory};
use super::ring::{Ring, RING};
use super::GENERATION;
use rodio::{ChannelCount, Sample, SampleRate, Source};
use std::sync::atomic::{AtomicBool, AtomicUsize, Ordering};
use std::sync::mpsc::{sync_channel, SyncSender};
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

pub(super) fn ring() -> (SyncSender<Vec<Sample>>, Ring) {
    let (tx, rx) = sync_channel(RING);
    (
        tx,
        Ring {
            rx,
            held: Vec::new(),
            at: 0,
            channels: ChannelCount::new(2).unwrap(),
            rate: SampleRate::new(44100).unwrap(),
            // Use the current generation. Only ignored tests change it, and they run separately.
            generation: GENERATION.load(Ordering::SeqCst),
            cancelled: Arc::new(AtomicBool::new(false)),
            ready: Arc::new(AtomicBool::new(true)),
            finished: Arc::new(AtomicBool::new(false)),
            queued: Arc::new(AtomicUsize::new(0)),
            prefill_samples: 1,
        },
    )
}
