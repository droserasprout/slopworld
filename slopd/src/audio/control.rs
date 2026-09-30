//! Request identity, cancellation, and state publication. Playback I/O belongs to the worker.

use std::sync::atomic::{AtomicU32, Ordering};
use std::sync::{Arc, Mutex};

use super::{AudioState, GENERATION, fail_state};

pub(super) struct RequestState {
    pub(super) source: Option<String>,
    pub(super) generation: u64,
    pub(super) cancel: Option<Arc<std::sync::atomic::AtomicBool>>,
}

impl RequestState {
    fn cancel(&mut self) {
        if let Some(cancel) = self.cancel.take() {
            cancel.store(true, Ordering::Release);
        }
    }

    fn retire(&mut self) {
        self.cancel();
        self.source = None;
    }

    fn replace(&mut self, source: Option<String>) -> u64 {
        self.cancel();
        self.generation = GENERATION.fetch_add(1, Ordering::SeqCst).wrapping_add(1);
        self.source = source;
        self.generation
    }
}

pub(super) enum SourceRequirement<'a> {
    Active,
    Exact(&'a str),
    None,
}

pub(super) struct Control {
    request: Mutex<RequestState>,
    pub(super) volume: AtomicU32,
}

impl Control {
    pub(super) fn new() -> Self {
        Self {
            request: Mutex::new(RequestState {
                source: None,
                generation: GENERATION.load(Ordering::SeqCst),
                cancel: None,
            }),
            volume: AtomicU32::new(1.0f32.to_bits()),
        }
    }

    pub(super) fn volume(&self) -> f32 {
        f32::from_bits(self.volume.load(Ordering::Acquire))
    }

    pub(super) fn play(&self, source: &str) -> u64 {
        let mut request = self.request.lock().unwrap_or_else(|p| p.into_inner());
        if request.source.as_deref() != Some(source) {
            request.replace(Some(source.to_string()));
        }
        request.generation
    }

    pub(super) fn stop(&self) -> u64 {
        let mut request = self.request.lock().unwrap_or_else(|p| p.into_inner());
        request.replace(None)
    }

    /// Retire this worker's resources without changing another player's active generation.
    /// There are no public handles left to submit commands after this boundary.
    pub(super) fn shutdown(&self) {
        let mut request = self.request.lock().unwrap_or_else(|p| p.into_inner());
        request.retire();
    }

    /// Publish a short state change while the request identity is locked. Lock order is request
    /// then audio state. Callers must finish playback and other I/O before entering this boundary.
    pub(super) fn publish_current<R>(
        &self,
        state: &Arc<Mutex<AudioState>>,
        generation: u64,
        source: SourceRequirement<'_>,
        publish: impl FnOnce(&mut RequestState, &mut AudioState) -> R,
    ) -> Option<R> {
        let mut request = self.request.lock().unwrap_or_else(|p| p.into_inner());
        let source_matches = match source {
            SourceRequirement::Active => request.source.is_some(),
            SourceRequirement::Exact(source) => request.source.as_deref() == Some(source),
            SourceRequirement::None => request.source.is_none(),
        };
        if request.generation != generation
            || generation != GENERATION.load(Ordering::SeqCst)
            || !source_matches
        {
            return None;
        }

        let mut current = state.lock().unwrap_or_else(|p| p.into_inner());
        Some(publish(&mut request, &mut current))
    }

    pub(super) fn is_current(&self, source: &str, generation: u64) -> bool {
        let request = self.request.lock().unwrap_or_else(|p| p.into_inner());
        request.generation == generation
            && request.source.as_deref() == Some(source)
            && generation == GENERATION.load(Ordering::SeqCst)
    }

    pub(super) fn snapshot(&self) -> (Option<String>, u64) {
        let request = self.request.lock().unwrap_or_else(|p| p.into_inner());
        (request.source.clone(), request.generation)
    }

    pub(super) fn fail_request(
        &self,
        state: &Arc<Mutex<AudioState>>,
        source: Option<&str>,
        generation: u64,
        why: String,
    ) {
        let requirement = source.map_or(SourceRequirement::None, SourceRequirement::Exact);
        let log_why = why.clone();
        let published = self
            .publish_current(state, generation, requirement, |request, current| {
                request.retire();
                fail_state(current, why);
            })
            .is_some();
        if published {
            tracing::warn!("audio: {log_why}");
        }
    }

    pub(super) fn fail_current(
        &self,
        state: &Arc<Mutex<AudioState>>,
        source: &str,
        generation: u64,
        why: String,
    ) {
        self.fail_request(state, Some(source), generation, why);
    }

    pub(super) fn reject(&self, state: &Arc<Mutex<AudioState>>, why: String) {
        let log_why = why.clone();
        {
            let mut request = self.request.lock().unwrap_or_else(|p| p.into_inner());
            request.replace(None);
            let mut current = state.lock().unwrap_or_else(|p| p.into_inner());
            fail_state(&mut current, why);
        }
        tracing::warn!("audio: {log_why}");
    }
}

#[cfg(test)]
#[path = "control_tests.rs"]
mod tests;
