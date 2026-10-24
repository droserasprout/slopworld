//! Preallocated single-producer/single-consumer sample blocks. Ring owns playback readiness.
//! Neither endpoint allocates, frees, locks, spins, or wakes another thread during handoff.

use rodio::Sample;
use std::sync::Arc;
use std::sync::atomic::{AtomicBool, AtomicU32, AtomicUsize, Ordering};
use std::sync::mpsc::TryRecvError;

pub(super) const CHUNK: usize = 4096;
pub(super) const RING: usize = 256;

struct Slot {
    samples: [AtomicU32; CHUNK],
    // Zero grants the producer ownership; a published length grants the consumer ownership.
    len: AtomicUsize,
}

struct Shared {
    slots: Box<[Slot]>,
    producer_alive: AtomicBool,
    consumer_alive: AtomicBool,
}

// Endpoints are deliberately not Clone. Mutable access keeps each cursor single-owner.
pub(super) struct Producer {
    shared: Arc<Shared>,
    at: usize,
}

pub(super) struct Consumer {
    shared: Arc<Shared>,
    at: usize,
}

#[derive(Debug, PartialEq, Eq)]
pub(super) enum SendError {
    InvalidBlock,
    Full,
    Disconnected,
}

pub(super) fn channel(capacity: usize) -> (Producer, Consumer) {
    assert!(capacity > 0);
    let shared = Arc::new(Shared {
        slots: (0..capacity)
            .map(|_| Slot {
                samples: std::array::from_fn(|_| AtomicU32::new(0)),
                len: AtomicUsize::new(0),
            })
            .collect(),
        producer_alive: AtomicBool::new(true),
        consumer_alive: AtomicBool::new(true),
    });
    (
        Producer {
            shared: shared.clone(),
            at: 0,
        },
        Consumer { shared, at: 0 },
    )
}

impl Producer {
    #[expect(
        clippy::indexing_slicing,
        reason = "cursor is bounded by the nonempty slot array"
    )]
    pub(super) fn try_send(&mut self, samples: &[Sample]) -> Result<(), SendError> {
        if samples.is_empty() || samples.len() > CHUNK {
            return Err(SendError::InvalidBlock);
        }
        if !self.shared.consumer_alive.load(Ordering::Acquire) {
            return Err(SendError::Disconnected);
        }
        let slot = &self.shared.slots[self.at];
        if slot.len.load(Ordering::Acquire) != 0 {
            return Err(SendError::Full);
        }
        // Acquire of zero follows the consumer's last read. Release of the length publishes
        // the entire block. Atomic samples avoid unsafe shared mutable storage; relaxed accesses
        // need no per-sample fences because the length transfers exclusive slot ownership.
        for (cell, sample) in slot.samples.iter().zip(samples) {
            cell.store(sample.to_bits(), Ordering::Relaxed);
        }
        slot.len.store(samples.len(), Ordering::Release);
        self.at = (self.at + 1) % self.shared.slots.len();
        Ok(())
    }
}

impl Consumer {
    #[expect(
        clippy::indexing_slicing,
        reason = "cursor is bounded by the nonempty slot array"
    )]
    pub(super) fn try_recv(
        &mut self,
        samples: &mut [Sample; CHUNK],
    ) -> Result<usize, TryRecvError> {
        let slot = &self.shared.slots[self.at];
        let mut len = slot.len.load(Ordering::Acquire);
        if len == 0 {
            if self.shared.producer_alive.load(Ordering::Acquire) {
                return Err(TryRecvError::Empty);
            }
            // Completion can race the first length read. Acquire completion before rechecking
            // so the final published block always drains before Disconnected is returned.
            len = slot.len.load(Ordering::Acquire);
            if len == 0 {
                return Err(TryRecvError::Disconnected);
            }
        }
        for (sample, cell) in samples.iter_mut().zip(&slot.samples).take(len) {
            *sample = Sample::from_bits(cell.load(Ordering::Relaxed));
        }
        slot.len.store(0, Ordering::Release);
        self.at = (self.at + 1) % self.shared.slots.len();
        Ok(len)
    }
}

impl Drop for Producer {
    fn drop(&mut self) {
        self.shared.producer_alive.store(false, Ordering::Release);
    }
}

impl Drop for Consumer {
    fn drop(&mut self) {
        self.shared.consumer_alive.store(false, Ordering::Release);
    }
}

#[cfg(test)]
#[path = "queue_tests.rs"]
mod tests;
