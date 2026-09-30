//! Callback-safe sample delivery, prefill pacing, and terminal feeder completion.

use rodio::{ChannelCount, Sample, SampleRate, Source};
use std::sync::Arc;
use std::sync::atomic::{AtomicBool, AtomicUsize, Ordering};
use std::sync::mpsc::{Receiver, SyncSender, TryRecvError, TrySendError};
use std::time::Duration;

use super::{GENERATION, QUEUE_POLL};

pub(crate) const RING: usize = 256;
const SPAN: usize = 8192;

pub(crate) fn enqueue_chunk(
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
pub(crate) struct Ring {
    pub(crate) rx: Receiver<Vec<Sample>>,
    pub(crate) held: Vec<Sample>,
    pub(crate) at: usize,
    pub(crate) channels: ChannelCount,
    pub(crate) rate: SampleRate,
    /// The source generation for this run.
    /// A generation change ends this source so the player advances to the next source.
    pub(crate) generation: u64,
    pub(crate) cancelled: Arc<AtomicBool>,
    /// Playback waits here while the feeder builds a small amount of live headroom.
    pub(crate) ready: Arc<AtomicBool>,
    /// Completion is terminal and must survive underrun readiness resets.
    pub(crate) finished: Arc<AtomicBool>,
    /// Samples still in the channel, excluding `held`. Used only at chunk boundaries so the audio
    /// callback does not perform an atomic operation per sample.
    pub(crate) queued: Arc<AtomicUsize>,
    pub(crate) prefill_samples: usize,
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
        // Superseded, or stopped. This is the retirement `Player::clear` could not do:
        // clear pauses the player and blocks on a source that never ends.
        if self.generation != GENERATION.load(Ordering::SeqCst)
            || self.cancelled.load(Ordering::Acquire)
        {
            return None;
        }
        if !self.ready.load(Ordering::Acquire) && !self.finished.load(Ordering::Acquire) {
            return Some(0.0);
        }
        if self.at >= self.held.len() {
            match self.rx.try_recv() {
                Ok(next) => {
                    let len = next.len();
                    // Direct test senders do not publish a count. Production always does. Keep
                    // the accounting saturating without adding work to every sample callback.
                    match self
                        .queued
                        .fetch_update(Ordering::AcqRel, Ordering::Acquire, |n| {
                            Some(n.saturating_sub(len))
                        }) {
                        Ok(_) | Err(_) => {}
                    }
                    self.held = next;
                    self.at = 0;
                }
                // The buffer is empty, but the feeder remains active.
                // Do not return None because that would end playback.
                Err(TryRecvError::Empty) => {
                    // Restore the initial buffer level before resuming playback.
                    // Otherwise, a long reconnection can cause repeated buffer underruns.
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
    /// Return a finite span aligned to complete audio frames.
    /// Rodio rebuilds channel and sample-rate converters only at span boundaries.
    /// None would preserve the first station's format and play later streams at incorrect speeds.
    /// Frame alignment preserves channel positions. SPAN remains below rodio's 32768-sample limit.
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
