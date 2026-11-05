//! Request identity, cancellation, and state publication. Playback I/O belongs to the worker.

use std::sync::atomic::{AtomicBool, AtomicU32, AtomicU64, Ordering};
use std::sync::{Arc, Mutex};

use super::{AudioState, fail_state};

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

    fn replace(&mut self, source: Option<String>, current: &AtomicU64) -> u64 {
        self.cancel();
        self.generation = current.fetch_add(1, Ordering::SeqCst).wrapping_add(1);
        self.source = source;
        self.generation
    }
}

#[derive(Clone, Copy)]
pub(super) enum SourceRequirement<'a> {
    Active,
    Exact(&'a str),
    None,
}

#[derive(Clone, Copy)]
pub(super) enum PlayDisposition {
    AlreadyPending,
    AlreadyPlaying,
    Open,
}

pub(super) struct Control {
    request: Mutex<RequestState>,
    // Shared only with this player's openers, decoders, and audio callbacks.
    pub(super) current_generation: Arc<AtomicU64>,
    pub(super) volume: AtomicU32,
}

impl Control {
    pub(super) fn new() -> Self {
        Self {
            request: Mutex::new(RequestState {
                source: None,
                generation: 0,
                cancel: None,
            }),
            current_generation: Arc::new(AtomicU64::new(0)),
            volume: AtomicU32::new(1.0f32.to_bits()),
        }
    }

    pub(super) fn volume(&self) -> f32 {
        f32::from_bits(self.volume.load(Ordering::Acquire))
    }

    pub(super) fn play(&self, source: &str) -> u64 {
        let mut request = self.request.lock().unwrap_or_else(|p| p.into_inner());
        if request.source.as_deref() != Some(source) {
            request.replace(Some(source.to_string()), &self.current_generation);
        }
        request.generation
    }

    pub(super) fn stop(&self) -> u64 {
        let mut request = self.request.lock().unwrap_or_else(|p| p.into_inner());
        request.replace(None, &self.current_generation)
    }

    /// Retire this worker's resources without changing another player's active generation.
    /// There are no public handles left to submit commands after this boundary.
    pub(super) fn shutdown(&self) {
        let mut request = self.request.lock().unwrap_or_else(|p| p.into_inner());
        request.retire();
    }

    /// Publish a short state change while the request identity is locked. Lock order is request
    /// then audio state. Callers must finish playback and other I/O before entering this boundary.
    fn with_current<R>(
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
            || generation != self.current_generation.load(Ordering::SeqCst)
            || !source_matches
        {
            return None;
        }

        let mut current = state.lock().unwrap_or_else(|p| p.into_inner());
        Some(publish(&mut request, &mut current))
    }

    /// Worker phase is only a hint: published playback must also match before reusing a ring.
    /// The cancellation handle is registered under request identity before the opener starts,
    /// so a replacement can interrupt it without waiting for the worker's next command.
    pub(super) fn prepare_play(
        &self,
        state: &Arc<Mutex<AudioState>>,
        source: &str,
        generation: u64,
        cancel: Arc<AtomicBool>,
        disposition: PlayDisposition,
    ) -> Option<PlayDisposition> {
        self.with_current(
            state,
            generation,
            SourceRequirement::Exact(source),
            |request, current| {
                match disposition {
                    PlayDisposition::AlreadyPending => return PlayDisposition::AlreadyPending,
                    PlayDisposition::AlreadyPlaying
                        if current.playing && current.source.as_deref() == Some(source) =>
                    {
                        current.volume = self.volume();
                        return PlayDisposition::AlreadyPlaying;
                    }
                    PlayDisposition::AlreadyPlaying | PlayDisposition::Open => {}
                }
                current.playing = false;
                current.source = None;
                current.title = None;
                current.error = None;
                request.cancel = Some(cancel);
                PlayDisposition::Open
            },
        )
    }

    pub(super) fn publish_playing(
        &self,
        state: &Arc<Mutex<AudioState>>,
        source: &str,
        generation: u64,
        cancel: Arc<AtomicBool>,
    ) -> bool {
        self.with_current(
            state,
            generation,
            SourceRequirement::Exact(source),
            |request, current| {
                request.cancel = Some(cancel);
                current.playing = true;
                current.source = Some(source.to_owned());
                current.volume = self.volume();
                current.error = None;
            },
        )
        .is_some()
    }

    pub(super) fn publish_stopped(&self, state: &Arc<Mutex<AudioState>>, generation: u64) -> bool {
        self.with_current(state, generation, SourceRequirement::None, |_, current| {
            current.playing = false;
            current.source = None;
            current.title = None;
        })
        .is_some()
    }

    pub(super) fn publish_recovery(
        &self,
        state: &Arc<Mutex<AudioState>>,
        source: &str,
        generation: u64,
        why: &str,
    ) {
        self.with_current(
            state,
            generation,
            SourceRequirement::Exact(source),
            |_, current| {
                current.playing = false;
                current.error = Some(format!("{why}; recovering audio output"));
            },
        );
    }

    pub(super) fn publish_title(
        &self,
        state: &Arc<Mutex<AudioState>>,
        generation: u64,
        title: Option<String>,
    ) -> bool {
        self.with_current(
            state,
            generation,
            SourceRequirement::Active,
            |_, current| {
                if current.title == title {
                    false
                } else {
                    current.title = title;
                    true
                }
            },
        )
        .unwrap_or(false)
    }

    // Fixtures can arrange otherwise unreachable identities without exposing mutable request
    // publication to production owners. All production callers use the operations above.
    #[cfg(test)]
    pub(super) fn publish_current<R>(
        &self,
        state: &Arc<Mutex<AudioState>>,
        generation: u64,
        source: SourceRequirement<'_>,
        publish: impl FnOnce(&mut RequestState, &mut AudioState) -> R,
    ) -> Option<R> {
        self.with_current(state, generation, source, publish)
    }

    pub(super) fn is_current(&self, source: &str, generation: u64) -> bool {
        let request = self.request.lock().unwrap_or_else(|p| p.into_inner());
        request.generation == generation
            && request.source.as_deref() == Some(source)
            && generation == self.current_generation.load(Ordering::SeqCst)
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
            .with_current(state, generation, requirement, |request, current| {
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
            request.replace(None, &self.current_generation);
            let mut current = state.lock().unwrap_or_else(|p| p.into_inner());
            fail_state(&mut current, why);
        }
        tracing::warn!("audio: {log_why}");
    }
}

#[cfg(test)]
#[path = "control_tests.rs"]
mod tests;
