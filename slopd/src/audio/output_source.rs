//! One fixed-format playback run per mixer input, with lock-free volume control.
//! Playback validates formats before feeding a run; a replacement gets its own converter.

use rodio::{ChannelCount, Sample, SampleRate, Source};
use std::sync::Arc;
use std::sync::atomic::{AtomicU32, Ordering};
use std::time::Duration;

pub(super) struct OutputSource {
    source: Box<dyn Source + Send>,
    volume: Arc<AtomicU32>,
}

impl OutputSource {
    pub(super) fn new(source: Box<dyn Source + Send>, volume: Arc<AtomicU32>) -> Self {
        Self { source, volume }
    }
}

impl Iterator for OutputSource {
    type Item = Sample;

    fn next(&mut self) -> Option<Sample> {
        self.source
            .next()
            .map(|sample| sample * f32::from_bits(self.volume.load(Ordering::Relaxed)))
    }
}

impl Source for OutputSource {
    fn current_span_len(&self) -> Option<usize> {
        // This source never changes format. Finite spans cause Rodio to repeatedly destroy and
        // allocate resampler buffers on its callback. Attach runs separately instead of queuing
        // changing formats through a Player, so converters are constructed on the worker once.
        None
    }

    fn channels(&self) -> ChannelCount {
        self.source.channels()
    }

    fn sample_rate(&self) -> SampleRate {
        self.source.sample_rate()
    }

    fn total_duration(&self) -> Option<Duration> {
        None
    }
}
