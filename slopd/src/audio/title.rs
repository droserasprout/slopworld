//! Candidate metadata activation and identity-checked title publication; also owns open deadlines.

use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::{Arc, Mutex};
use std::time::Instant;

use super::control::{Control, SourceRequirement};
use super::{AudioState, GENERATION, OPEN};

/// Receive delayed station metadata. The generation prevents old metadata from changing a replacement station's title.
/// `None` omits titles for files and tests.
#[derive(Clone)]
pub(crate) struct TitleSink {
    pub(crate) state: Option<Arc<Mutex<AudioState>>>,
    pub(super) control: Option<Arc<Control>>,
    pub(crate) generation: u64,
    /// Opening and decoder probing happen before the worker commits a source. Metadata seen
    /// during that phase belongs to a candidate, not to the audible selection yet.
    phase: Arc<Mutex<TitlePhase>>,
    /// Shared with the HTTP transport so replacing a source closes a blocked open/read instead
    /// of merely abandoning the thread that owns it.
    pub(crate) cancelled: Arc<AtomicBool>,
    /// Monotonic deadline for one header or open operation.
    /// Zero removes this deadline from the live body. Only the idle timeout then limits body reads.
    pub(crate) opening_deadline: Arc<AtomicU64>,
}

// One lock serializes activation, pending updates (including clears), and publication.
// Title phase is acquired before Control's request lock and AudioState; no inverse path exists.
enum TitlePhase {
    Pending(Option<Option<String>>),
    Committed,
}

impl TitleSink {
    fn with_phase(
        state: Option<Arc<Mutex<AudioState>>>,
        generation: u64,
        control: Option<Arc<Control>>,
        phase: TitlePhase,
    ) -> Self {
        Self {
            state,
            control,
            generation,
            phase: Arc::new(Mutex::new(phase)),
            cancelled: Arc::new(AtomicBool::new(false)),
            opening_deadline: Arc::new(AtomicU64::new(0)),
        }
    }

    #[cfg(test)]
    pub(crate) fn new(state: Option<Arc<Mutex<AudioState>>>, generation: u64) -> Self {
        Self::with_phase(state, generation, None, TitlePhase::Committed)
    }

    #[cfg(test)]
    pub(crate) fn pending(state: Option<Arc<Mutex<AudioState>>>, generation: u64) -> Self {
        Self::with_phase(state, generation, None, TitlePhase::Pending(None))
    }

    pub(super) fn pending_for(
        state: Option<Arc<Mutex<AudioState>>>,
        generation: u64,
        control: Arc<Control>,
    ) -> Self {
        Self::with_phase(state, generation, Some(control), TitlePhase::Pending(None))
    }

    pub(crate) fn set(&self, title: Option<String>) {
        if self.cancelled.load(Ordering::Acquire) {
            return;
        }
        let mut phase = self.phase.lock().unwrap_or_else(|p| p.into_inner());
        match &mut *phase {
            TitlePhase::Pending(pending) => *pending = Some(title),
            TitlePhase::Committed => self.publish_title(title),
        }
    }

    fn publish_title(&self, title: Option<String>) {
        if self.cancelled.load(Ordering::Acquire) {
            return;
        }
        let Some(state) = self.state.as_ref() else {
            return;
        };
        let label = title.as_deref().unwrap_or("-").to_string();
        let changed = if let Some(control) = self.control.as_ref() {
            control
                .publish_current(state, self.generation, SourceRequirement::Active, |_, s| {
                    if s.title == title {
                        false
                    } else {
                        s.title = title;
                        true
                    }
                })
                .unwrap_or(false)
        } else if self.generation == GENERATION.load(Ordering::SeqCst) {
            let mut s = state.lock().unwrap_or_else(|p| p.into_inner());
            if s.title == title {
                false
            } else {
                s.title = title;
                true
            }
        } else {
            false
        };
        if changed {
            tracing::info!("audio: now playing {label}");
        }
    }

    pub(crate) fn commit(&self) {
        let mut phase = self.phase.lock().unwrap_or_else(|p| p.into_inner());
        if let TitlePhase::Pending(Some(title)) =
            std::mem::replace(&mut *phase, TitlePhase::Committed)
        {
            self.publish_title(title);
        }
    }

    pub(crate) fn cancel(&self) {
        self.cancelled.store(true, Ordering::Release);
    }

    pub(crate) fn begin_opening(&self) {
        let deadline = monotonic_millis().saturating_add(crate::clock::duration_ms(OPEN));
        self.opening_deadline.store(deadline, Ordering::Release);
    }

    pub(crate) fn finish_opening(&self) {
        self.opening_deadline.store(0, Ordering::Release);
    }
}

pub(super) fn monotonic_millis() -> u64 {
    static START: std::sync::OnceLock<Instant> = std::sync::OnceLock::new();
    START
        .get_or_init(Instant::now)
        .elapsed()
        .as_millis()
        .try_into()
        .unwrap_or(u64::MAX)
}

#[cfg(test)]
#[path = "title_tests.rs"]
mod tests;
