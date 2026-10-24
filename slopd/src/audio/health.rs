//! Callback-safe underrun counters; the command worker owns throttled diagnostic logging.

use std::sync::Arc;
use std::sync::Mutex;
use std::sync::atomic::{AtomicBool, AtomicUsize, Ordering};
use std::time::{Duration, Instant};

pub(super) struct OutputHealth {
    pub(super) failed: AtomicBool,
    pub(super) backend_underruns: AtomicUsize,
    pub(super) source_underruns: Arc<AtomicUsize>,
    last_report: Mutex<Instant>,
}

impl OutputHealth {
    pub(super) fn new() -> Self {
        Self {
            failed: AtomicBool::new(false),
            backend_underruns: AtomicUsize::new(0),
            source_underruns: Arc::new(AtomicUsize::new(0)),
            last_report: Mutex::new(Instant::now()),
        }
    }

    pub(super) fn record(&self, error: &rodio::cpal::StreamError) {
        if matches!(error, rodio::cpal::StreamError::BufferUnderrun) {
            self.backend_underruns.fetch_add(1, Ordering::Relaxed);
        } else {
            self.failed.store(true, Ordering::Release);
        }
    }

    /// Called only by the command worker, never by an audio callback.
    pub(super) fn report(&self) {
        let mut last = self.last_report.lock().unwrap_or_else(|p| p.into_inner());
        if last.elapsed() < Duration::from_secs(5) {
            return;
        }
        *last = Instant::now();
        let backend_underruns = self.backend_underruns.swap(0, Ordering::Relaxed);
        let source_underruns = self.source_underruns.swap(0, Ordering::Relaxed);
        if backend_underruns != 0 || source_underruns != 0 {
            tracing::warn!(
                backend_underruns,
                source_underruns,
                "audio: playback underruns"
            );
        }
    }
}

#[cfg(test)]
#[path = "health_tests.rs"]
mod tests;
