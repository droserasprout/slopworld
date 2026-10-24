//! Callback-safe sample delivery, prefill pacing, and terminal feeder completion.

use super::queue::{CHUNK, Consumer, Producer, SendError};
use super::title::TitleSink;
use rodio::{ChannelCount, Sample, SampleRate, Source};
use std::sync::Arc;
use std::sync::atomic::{AtomicBool, AtomicU64, AtomicUsize, Ordering};
use std::sync::mpsc::TryRecvError;
use std::time::Duration;

use super::QUEUE_POLL;

const SPAN: usize = 8192;

pub(super) fn enqueue_chunk(
    tx: &mut Producer,
    chunk: &[Sample],
    ready: &AtomicBool,
    queued: &AtomicUsize,
    prefill_samples: usize,
    title: &TitleSink,
) -> bool {
    let len = chunk.len();
    loop {
        if title.cancelled.load(Ordering::Acquire)
            || title.generation != title.current_generation.load(Ordering::SeqCst)
        {
            return false;
        }
        // Publish the count before the chunk: an active consumer may receive immediately after a
        // successful send. Failed sends roll it back before retrying.
        queued.fetch_add(len, Ordering::AcqRel);
        match tx.try_send(chunk) {
            Ok(()) => break,
            Err(SendError::Disconnected | SendError::InvalidBlock) => {
                queued.fetch_sub(len, Ordering::AcqRel);
                return false;
            }
            Err(SendError::Full) => {
                queued.fetch_sub(len, Ordering::AcqRel);
                std::thread::sleep(QUEUE_POLL);
            }
        }
    }

    if !ready.load(Ordering::Relaxed) && queued.load(Ordering::Acquire) >= prefill_samples {
        ready.store(true, Ordering::Release);
    }
    true
}

pub(super) struct FinishedOnDrop(pub(super) Arc<AtomicBool>);

impl Drop for FinishedOnDrop {
    fn drop(&mut self) {
        self.0.store(true, Ordering::Release);
    }
}

/// Supply decoded samples to the audio callback.
/// Return silence when the ring buffer is empty so network delays do not block the callback.
pub(super) struct Ring {
    pub(super) rx: Consumer,
    pub(super) held: [Sample; CHUNK],
    pub(super) held_len: usize,
    pub(super) underruns: Arc<AtomicUsize>,
    pub(super) at: usize,
    pub(super) channels: ChannelCount,
    pub(super) rate: SampleRate,
    /// The source generation for this run.
    /// A generation change retires this mixer input.
    pub(super) generation: u64,
    pub(super) current_generation: Arc<AtomicU64>,
    pub(super) cancelled: Arc<AtomicBool>,
    /// Playback waits here while the feeder builds a small amount of live headroom.
    pub(super) ready: Arc<AtomicBool>,
    /// Completion is terminal and must survive underrun readiness resets.
    pub(super) finished: Arc<AtomicBool>,
    /// Samples still in the channel, excluding `held`. Used only at chunk boundaries so the audio
    /// callback does not perform an atomic operation per sample.
    pub(super) queued: Arc<AtomicUsize>,
    pub(super) prefill_samples: usize,
}

impl Ring {
    pub(super) fn rebuffer(&self) {
        self.ready.store(false, Ordering::Release);
        if self.queued.load(Ordering::Acquire) >= self.prefill_samples {
            self.ready.store(true, Ordering::Release);
        }
    }
}

impl Iterator for Ring {
    type Item = Sample;

    fn next(&mut self) -> Option<Sample> {
        // Retire at the callback boundary so a stopped or superseded run cannot keep playing.
        if self.generation != self.current_generation.load(Ordering::SeqCst)
            || self.cancelled.load(Ordering::Acquire)
        {
            return None;
        }
        if !self.ready.load(Ordering::Acquire) && !self.finished.load(Ordering::Acquire) {
            return Some(0.0);
        }
        if self.at >= self.held_len {
            match self.rx.try_recv(&mut self.held) {
                Ok(len) => {
                    // Direct test senders do not publish a count. Production always does. Keep
                    // the accounting saturating without adding work to every sample callback.
                    match self
                        .queued
                        .try_update(Ordering::AcqRel, Ordering::Acquire, |n| {
                            Some(n.saturating_sub(len))
                        }) {
                        Ok(_) | Err(_) => {}
                    }
                    self.held_len = len;
                    self.at = 0;
                }
                // The buffer is empty, but the feeder remains active.
                // Do not return None because that would end playback.
                Err(TryRecvError::Empty) => {
                    // Restore the initial buffer level before resuming playback.
                    // Otherwise, a long reconnection can cause repeated buffer underruns.
                    self.underruns.fetch_add(1, Ordering::Relaxed);
                    self.rebuffer();
                    return Some(0.0);
                }
                Err(TryRecvError::Disconnected) => return None,
            }
        }
        let Some(sample) = self.held.get(self.at).copied() else {
            return Some(0.0);
        };
        self.at += 1;
        Some(sample)
    }
}

impl Source for Ring {
    /// Keep generic concatenation frame-aligned and below Rodio's 32768-sample span limit.
    /// Production wraps each fixed-format run in OutputSource, which disables span rebuilding.
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
#[path = "ring_tests.rs"]
mod tests;
